using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VoiceDrop;

public partial class CleanupPage : UserControl
{
    private readonly DictationController _dictation;
    private bool _downloading;

    internal CleanupPage(DictationController dictation)
    {
        _dictation = dictation;
        InitializeComponent();
        var s = AppSettings.Current;

        foreach (var (id, name) in new[] { ("off", "Off"), ("basic", "Basic"), ("smart", "Smart (AI)") })
        {
            var chip = new RadioButton { Content = name, Style = (Style)FindResource("Chip"), GroupName = "mode", IsChecked = s.CleanupMode == id };
            chip.Checked += (_, _) =>
            {
                s.CleanupMode = id;
                s.Save();
                UpdateHint();
                if (id == "smart") _dictation.WarmUpLlm();
            };
            ModeChips.Children.Add(chip);
        }

        AddToggle("Spoken commands", "\"New line\", \"new paragraph\", \"question mark\" and their Dutch versions become real line breaks and punctuation.",
                  () => s.VoiceCommands, v => s.VoiceCommands = v);
        AddToggle("Remove filler words", "Drops \"um\", \"uh\", \"euh\", \"ehm\" and similar.", () => s.CleanFillers, v => s.CleanFillers = v);

        UpdateHint();
        RenderModels();
    }

    private void UpdateHint()
    {
        var mode = AppSettings.Current.CleanupMode;
        ModeHint.Text = mode switch
        {
            "off" => "Your words are typed exactly as recognised.",
            "basic" => "Instant rule-based cleanup, no AI model needed.",
            _ => "Basic cleanup plus a local AI pass. Adds about a second after you stop talking."
        };
        SmartCard.Visibility = mode == "smart" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddToggle(string title, string sub, Func<bool> get, Action<bool> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Medium });
        text.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("Secondary"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        var sw = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
        sw.Click += (_, _) => { set(sw.IsChecked == true); AppSettings.Current.Save(); };
        Grid.SetColumn(sw, 1);
        grid.Children.Add(text);
        grid.Children.Add(sw);
        if (Toggles.Children.Count > 0)
            Toggles.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Separator") });
        Toggles.Children.Add(grid);
    }

    private void RenderModels()
    {
        Models.Children.Clear();
        foreach (var m in LlmCatalog.All)
        {
            bool active = AppSettings.Current.LlmId == m.Id;
            bool have = LlmCatalog.IsDownloaded(m);

            var card = new Border
            {
                Background = (Brush)FindResource(active && have ? "AccentSoft" : "CardElevated"),
                BorderBrush = (Brush)FindResource(active && have ? "Accent" : "CardBorder"),
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
            if (active && have)
                action = new TextBlock { Text = "Active", Foreground = (Brush)FindResource("Accent"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            else
            {
                var btn = new Button
                {
                    Content = have ? "Use" : "Download",
                    Style = have ? (Style)FindResource("AccentButton") : (Style)FindResource(typeof(Button)),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = !_downloading
                };
                btn.Click += async (_, _) => await UseOrDownload(m, have);
                action = btn;
            }
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
            card.Child = grid;
            Models.Children.Add(card);
        }
    }

    private async Task UseOrDownload(LlmInfo m, bool have)
    {
        var s = AppSettings.Current;
        if (!have)
        {
            _downloading = true;
            RenderModels();
            Progress.Visibility = Visibility.Visible;
            Progress.Value = 0;
            ModelStatus.Text = $"Downloading {m.Title} from huggingface.co...";
            try
            {
                await LocalLlm.DownloadAsync(m, p => Dispatcher.Invoke(() => { Progress.Value = p; ModelStatus.Text = $"Downloading {m.Title}... {p:P0}"; }));
                ModelStatus.Text = "Downloaded.";
            }
            catch (Exception ex)
            {
                Log.Write("llm download failed: " + ex);
                ModelStatus.Text = "Download failed: " + ex.Message;
                _downloading = false;
                Progress.Visibility = Visibility.Collapsed;
                RenderModels();
                return;
            }
            _downloading = false;
            Progress.Visibility = Visibility.Collapsed;
        }
        s.LlmId = m.Id;
        s.Save();
        _dictation.Llm.Unload();
        RenderModels();
        _dictation.WarmUpLlm();
        if (have) ModelStatus.Text = "";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        RunBtn.IsEnabled = false;
        TryOut.Text = "...";
        try { TryOut.Text = await _dictation.PolishForPreviewAsync(TryIn.Text); }
        catch (Exception ex) { TryOut.Text = "Error: " + ex.Message; }
        RunBtn.IsEnabled = true;
    }
}
