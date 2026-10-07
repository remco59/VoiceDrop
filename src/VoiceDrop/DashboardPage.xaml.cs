using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoiceDrop;

public partial class DashboardPage : UserControl
{
    private readonly DictationController _dictation;

    internal DashboardPage(DictationController dictation)
    {
        _dictation = dictation;
        InitializeComponent();
        TryBox.TextChanged += (_, _) => TryHint.Visibility = TryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => { HistoryStore.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => HistoryStore.Changed -= Refresh;
    }

    private void Refresh()
    {
        KeyCap.Text = KeyNames.Name(AppSettings.Current.HotkeyVk);
        HeroSub.Text = "Hold " + KeyNames.Name(AppSettings.Current.HotkeyVk) + " and speak. Release to insert the text at your cursor.";

        var today = HistoryStore.Today.ToList();
        int words = today.Sum(x => x.Words);
        double mins = today.Sum(x => x.Seconds) / 60.0;
        WordsToday.Text = words.ToString("N0");
        CountToday.Text = today.Count.ToString("N0");
        WpmToday.Text = mins > 0.01 ? Math.Round(words / mins).ToString("N0") : "-";

        Recent.Children.Clear();
        foreach (var e in HistoryStore.Items.Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBlock { Text = e.Text, TextTrimming = TextTrimming.CharacterEllipsis };
            var time = new TextBlock { Text = e.Time.ToString("HH:mm"), Style = (Style)FindResource("Secondary"), Margin = new Thickness(14, 0, 0, 0) };
            Grid.SetColumn(time, 1);
            row.Children.Add(text);
            row.Children.Add(time);
            Recent.Children.Add(row);
        }
        RecentEmpty.Visibility = Recent.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
