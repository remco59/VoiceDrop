using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoiceDrop;

public partial class StatsPage : UserControl
{
    public StatsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        var all = HistoryStore.Items;
        int words = all.Sum(x => x.Words);
        double mins = all.Sum(x => x.Seconds) / 60.0;
        TotalWords.Text = words.ToString("N0");
        TotalCount.Text = all.Count.ToString("N0");
        AvgWpm.Text = mins > 0.01 ? Math.Round(words / mins).ToString("N0") : "-";
        double savedMin = Math.Max(0, words / 40.0 - mins);
        Saved.Text = savedMin >= 60 ? $"{savedMin / 60:0.0} h" : $"{savedMin:0} min";

        // 7-day bar chart
        Chart.Children.Clear();
        Chart.ColumnDefinitions.Clear();
        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
        var perDay = days.Select(d => all.Where(x => x.Time.Date == d).Sum(x => x.Words)).ToList();
        int max = Math.Max(1, perDay.Max());
        for (int i = 0; i < 7; i++)
        {
            Chart.ColumnDefinitions.Add(new ColumnDefinition());
            var col = new Grid { Margin = new Thickness(6, 0, 6, 0) };
            col.RowDefinitions.Add(new RowDefinition());
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double frac = perDay[i] / (double)max;
            var bar = new Border
            {
                Background = (Brush)FindResource(perDay[i] > 0 ? "Accent" : "Selected"),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = Math.Max(6, 120 * frac),
                ToolTip = $"{perDay[i]} words"
            };
            var label = new TextBlock { Text = days[i].ToString("ddd"), Style = (Style)FindResource("Secondary"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(label, 1);
            col.Children.Add(bar);
            col.Children.Add(label);
            Grid.SetColumn(col, i);
            Chart.Children.Add(col);
        }

        // languages
        Langs.Children.Clear();
        var byLang = all.Where(x => x.Language.Length > 0).GroupBy(x => x.Language).OrderByDescending(g => g.Sum(x => x.Words)).ToList();
        int totalLangWords = Math.Max(1, byLang.Sum(g => g.Sum(x => x.Words)));
        foreach (var g in byLang)
        {
            int w = g.Sum(x => x.Words);
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.Children.Add(new TextBlock { Text = KeyNames.LanguageName(g.Key) });
            var track = new Border { Background = (Brush)FindResource("Selected"), CornerRadius = new CornerRadius(4), Height = 8 };
            var fill = new Border { Background = (Brush)FindResource("Accent"), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left };
            var host = new Grid();
            host.Children.Add(track);
            host.Children.Add(fill);
            host.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * w / totalLangWords;
            Grid.SetColumn(host, 1);
            row.Children.Add(host);
            var count = new TextBlock { Text = $"{w:N0} words", Style = (Style)FindResource("Secondary"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            Langs.Children.Add(row);
        }
        LangsEmpty.Visibility = byLang.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
