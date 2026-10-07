using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoiceDrop;

public partial class HistoryPage : UserControl
{
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { HistoryStore.Changed += Render; Render(); };
        Unloaded += (_, _) => HistoryStore.Changed -= Render;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => Render();
    private void Clear_Click(object sender, RoutedEventArgs e) => HistoryStore.Clear();

    private void Render()
    {
        if (List == null) return;
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var q = Search.Text.Trim();
        var items = HistoryStore.Items
            .Where(x => FilterAll.IsChecked == true || x.Pinned)
            .Where(x => q.Length == 0 || x.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        List.Children.Clear();
        foreach (var e in items) List.Children.Add(Row(e));
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = HistoryStore.Items.Count == 0 ? "No transcriptions yet." : "Nothing matches.";
    }

    private UIElement Row(HistoryEntry e)
    {
        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 12, 10, 12), Margin = new Thickness(0, 0, 0, 10) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = e.Text, TextWrapping = TextWrapping.Wrap });
        string meta = e.Time.ToString("ddd d MMM, HH:mm") + $"  ·  {e.Words} words";
        if (e.Language.Length > 0) meta += "  ·  " + e.Language.ToUpperInvariant();
        left.Children.Add(new TextBlock { Text = meta, Style = (Style)FindResource("Secondary"), FontSize = 11, Margin = new Thickness(0, 6, 0, 0) });
        grid.Children.Add(left);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(Icon("", "Copy", () => { try { Clipboard.SetText(e.Text); } catch { } }));
        buttons.Children.Add(Icon(e.Pinned ? "" : "", e.Pinned ? "Unpin" : "Pin", () => HistoryStore.TogglePin(e), e.Pinned));
        buttons.Children.Add(Icon("", "Delete", () => HistoryStore.Remove(e)));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    private Button Icon(string glyph, string tip, Action click, bool accent = false)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Style = (Style)FindResource("IconButton") };
        if (accent) b.Foreground = (Brush)FindResource("Accent");
        b.Click += (_, _) => click();
        return b;
    }
}
