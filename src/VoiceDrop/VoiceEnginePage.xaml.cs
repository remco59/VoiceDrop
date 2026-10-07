using System;
using System.Windows;
using System.Windows.Controls;

namespace VoiceDrop;

public partial class VoiceEnginePage : UserControl
{
    private static readonly string[] Languages = ["auto", "nl", "en", "de", "fr", "es", "it", "pt"];
    private readonly DictationController _dictation;

    internal VoiceEnginePage(DictationController dictation)
    {
        _dictation = dictation;
        InitializeComponent();

        foreach (var code in Languages)
        {
            var chip = new RadioButton { Content = KeyNames.LanguageName(code), Style = (Style)FindResource("Chip"), GroupName = "lang",
                                         IsChecked = AppSettings.Current.Language == code };
            chip.Checked += async (_, _) =>
            {
                AppSettings.Current.Language = code;
                AppSettings.Current.Save();
                await _dictation.LoadModelAsync();
            };
            Langs.Children.Add(chip);
        }

        LiveToggle.IsChecked = AppSettings.Current.LivePreview;
        LiveToggle.Click += (_, _) => { AppSettings.Current.LivePreview = LiveToggle.IsChecked == true; AppSettings.Current.Save(); };

        _dictation.DownloadProgress += OnProgress;
        Unloaded += (_, _) => _dictation.DownloadProgress -= OnProgress;
        RenderModels();
    }

    private void OnProgress(double p)
    {
        Progress.Visibility = Visibility.Visible;
        Progress.Value = p;
        ModelStatus.Text = $"Downloading... {p:P0}";
        if (p >= 1) Progress.Visibility = Visibility.Collapsed;
    }

    private void RenderModels()
    {
        Models.Children.Clear();
        foreach (var m in ModelCatalog.All)
        {
            bool active = AppSettings.Current.ModelId == m.Id;
            bool have = ModelCatalog.IsDownloaded(m);

            var card = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource(active ? "AccentSoft" : "CardElevated"),
                BorderBrush = (System.Windows.Media.Brush)FindResource(active ? "Accent" : "CardBorder"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 11, 12, 11), Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = m.Title, FontWeight = FontWeights.Medium });
            info.Children.Add(new TextBlock { Text = m.Detail, Style = (Style)FindResource("Secondary"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
            grid.Children.Add(info);

            UIElement action;
            if (active)
                action = new TextBlock { Text = "Active", Foreground = (System.Windows.Media.Brush)FindResource("Accent"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            else
            {
                var btn = new Button { Content = have ? "Use" : "Download", Style = have ? (Style)FindResource("AccentButton") : (Style)FindResource(typeof(Button)), VerticalAlignment = VerticalAlignment.Center };
                btn.Click += async (_, _) =>
                {
                    btn.IsEnabled = false;
                    AppSettings.Current.ModelId = m.Id;
                    AppSettings.Current.Save();
                    await _dictation.LoadModelAsync();
                    ModelStatus.Text = _dictation.StatusText;
                    RenderModels();
                };
                action = btn;
            }
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
            card.Child = grid;
            Models.Children.Add(card);
        }
    }
}
