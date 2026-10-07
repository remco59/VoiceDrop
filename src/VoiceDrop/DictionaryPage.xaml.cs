using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VoiceDrop;

public partial class DictionaryPage : UserControl
{
    private readonly DictationController _dictation;

    internal DictionaryPage(DictationController dictation)
    {
        _dictation = dictation;
        InitializeComponent();
        TermBox.TextChanged += (_, _) => TermHint.Visibility = TermBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        VariantBox.TextChanged += (_, _) => VariantHint.Visibility = VariantBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Add_Click(sender, e);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        var term = TermBox.Text.Trim();
        if (term.Length == 0) { TermBox.Focus(); return; }

        // same term again: merge the variants instead of creating a duplicate
        var variants = DictionaryStore.ParseVariants(VariantBox.Text);
        var existing = DictionaryStore.Items.FirstOrDefault(x => x.Term.Equals(term, StringComparison.OrdinalIgnoreCase));
        if (existing != null) DictionaryStore.Remove(existing);
        var entry = new DictEntry { Term = term, Variants = variants };
        if (existing != null)
            entry.Variants = existing.Variants.Concat(variants).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        DictionaryStore.Add(entry);

        TermBox.Clear();
        VariantBox.Clear();
        TermBox.Focus();
        Render();
        await Refresh();
    }

    private async System.Threading.Tasks.Task Refresh()
    {
        try { await _dictation.Transcriber.LoadAsync(_ => { }); } catch { }
    }

    private void Render()
    {
        List.Children.Clear();
        var items = DictionaryStore.Items;
        CountText.Text = items.Count == 1 ? "1 word" : $"{items.Count} words";
        Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LimitNote.Visibility = DictionaryStore.PromptText().Split(',').Length < items.Count ? Visibility.Visible : Visibility.Collapsed;

        foreach (var entry in items)
        {
            var row = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("CardElevated"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorder"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 8, 10), Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = entry.Term, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap });
            if (entry.Variants.Count > 0)
                text.Children.Add(new TextBlock
                {
                    Text = "heard as: " + string.Join(", ", entry.Variants),
                    Style = (Style)FindResource("Secondary"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0)
                });
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(IconBtn("", "Edit", () =>
            {
                TermBox.Text = entry.Term;
                VariantBox.Text = string.Join(", ", entry.Variants);
                DictionaryStore.Remove(entry);
                Render();
                TermBox.Focus();
                TermBox.CaretIndex = TermBox.Text.Length;
            }));
            buttons.Children.Add(IconBtn("", "Delete", async () => { DictionaryStore.Remove(entry); Render(); await Refresh(); }));
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            row.Child = grid;
            List.Children.Add(row);
        }
    }

    private Button IconBtn(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, ToolTip = tip, Style = (Style)FindResource("IconButton") };
        b.Click += (_, _) => click();
        return b;
    }
}
