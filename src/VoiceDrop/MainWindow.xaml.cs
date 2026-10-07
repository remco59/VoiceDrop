using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace VoiceDrop;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly DictationController _dictation;
    private UserControl? _page;

    internal MainWindow(DictationController dictation)
    {
        _dictation = dictation;
        InitializeComponent();

        NavDashboard.Checked += (_, _) => Show(new DashboardPage(_dictation));
        NavHistory.Checked += (_, _) => Show(new HistoryPage());
        NavCleanup.Checked += (_, _) => Show(new CleanupPage(_dictation));
        NavDictionary.Checked += (_, _) => Show(new DictionaryPage(_dictation));
        NavStats.Checked += (_, _) => Show(new StatsPage());
        NavEngine.Checked += (_, _) => Show(new VoiceEnginePage(_dictation));
        NavSettings.Checked += (_, _) => Show(new SettingsPage(this));

        _dictation.StateChanged += UpdateStatus;
        UpdateStatus();
        Show(new DashboardPage(_dictation));

        SourceInitialized += (_, _) => ApplyTitleBarTheme();
    }

    private void Show(UserControl page)
    {
        _page = page;
        PageHost.Content = page;
    }

    public void ApplyTitleBarTheme()
    {
        int dark = AppSettings.Current.DarkTheme ? 1 : 0;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }

    private void UpdateStatus()
    {
        StatusText.Text = _dictation.StatusText;
        string key = _dictation.State switch
        {
            DictationState.Idle => "Accent",
            DictationState.Listening => "Danger",
            _ => "Warning"
        };
        StatusDot.Fill = (Brush)FindResource(key);
        StatusSub.Text = _dictation.State == DictationState.Idle
            ? $"Hold {KeyNames.Name(AppSettings.Current.HotkeyVk)} to talk" : "Vulkan GPU";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // keep running in the tray
        e.Cancel = true;
        Hide();
    }
}
