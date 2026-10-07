using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VoiceDrop;

public partial class SettingsPage : UserControl
{
    private readonly MainWindow _owner;
    private bool _capturing;

    internal SettingsPage(MainWindow owner)
    {
        _owner = owner;
        InitializeComponent();
        KeyCap.Text = KeyNames.Name(AppSettings.Current.HotkeyVk);

        ThemeDark.IsChecked = AppSettings.Current.DarkTheme;
        ThemeLight.IsChecked = !AppSettings.Current.DarkTheme;

        var s = AppSettings.Current;
        AddToggle("Show overlay", "Floating pill with waveform and live text while you speak.", () => s.ShowOverlay, v => s.ShowOverlay = v);
        AddToggle("Sounds", "Short beep when recording starts.", () => s.PlaySounds, v => s.PlaySounds = v);
        AddToggle("Add trailing space", "Put a space after inserted text so you can keep talking.", () => s.TrailingSpace, v => s.TrailingSpace = v);
        AddToggle("Save history", "Keep transcripts on this PC for the History and Stats pages.", () => s.SaveHistory, v => s.SaveHistory = v);
        AddToggle("Start with Windows", "Launch VoiceDrop in the tray at login.", () => s.StartWithWindows, v => s.StartWithWindows = v);
        AddToggle("Start minimized", "Do not open this window on launch.", () => s.StartMinimized, v => s.StartMinimized = v);
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
            Toggles.Children.Add(new System.Windows.Controls.Border { Height = 1, Background = (System.Windows.Media.Brush)FindResource("Separator") });
        Toggles.Children.Add(grid);
    }

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && ThemeDark.IsChecked != true && ThemeLight.IsChecked != true) return;
        bool dark = ThemeDark.IsChecked == true;
        if (dark == AppSettings.Current.DarkTheme && !IsLoaded) return;
        AppSettings.Current.DarkTheme = dark;
        AppSettings.Current.Save();
        Theme.Apply(dark);
        _owner.ApplyTitleBarTheme();
    }

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        ChangeKey.Content = "Press a key...";
        ChangeKey.Focus();
    }

    private void ChangeKey_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); return; }
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        // Ctrl/Shift/Alt arrive as generic keys; recover left/right variant from the key itself
        vk = key switch
        {
            Key.LeftCtrl => 0xA2, Key.RightCtrl => 0xA3,
            Key.LeftShift => 0xA0, Key.RightShift => 0xA1,
            Key.LeftAlt => 0xA4, Key.RightAlt => 0xA5,
            _ => vk
        };
        AppSettings.Current.HotkeyVk = vk;
        AppSettings.Current.Save();
        KeyCap.Text = KeyNames.Name(vk);
        ((App)Application.Current).RebindHotkey();
        EndCapture();
    }

    private void ChangeKey_LostFocus(object sender, RoutedEventArgs e) => EndCapture();

    private void EndCapture()
    {
        _capturing = false;
        ChangeKey.Content = "Change";
    }
}
