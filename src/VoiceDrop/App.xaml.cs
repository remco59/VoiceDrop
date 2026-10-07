using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace VoiceDrop;

public partial class App : Application
{
    private Forms.NotifyIcon? _tray;
    private PushToTalkHook? _hook;
    private DictationController? _dictation;
    private OverlayWindow? _overlay;
    private MainWindow? _main;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Write("app start");
        DispatcherUnhandledException += (_, ex) => { Log.Write("unhandled: " + ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("fatal: " + ex.ExceptionObject);
        AppSettings.Load();
        Theme.Apply(AppSettings.Current.DarkTheme);

        _dictation = new DictationController(Dispatcher);
        _overlay = new OverlayWindow();
        _main = new MainWindow(_dictation);

        // a finished model download becomes the active polish model
        LlmDownloads.Completed += _ => Dispatcher.BeginInvoke(() => { _dictation.Llm.Unload(); _dictation.WarmUpLlm(); });
        _dictation.StateChanged += OnStateChanged;
        _dictation.Level += l => Dispatcher.BeginInvoke(() => _overlay.PushLevel(l));
        _dictation.Partial += text => { _overlay.SetPreview(text); _overlay.SetLanguage(_dictation.DetectedLanguage); };
        _dictation.Error += msg => _tray?.ShowBalloonTip(4000, "VoiceDrop", msg, Forms.ToolTipIcon.Error);

        BuildTray();
        if (!AppSettings.Current.StartMinimized || Array.IndexOf(e.Args, "--show") >= 0) _main.Show();

        try
        {
            RebindHotkey();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "VoiceDrop: keyboard hook failed");
        }

        await _dictation.LoadModelAsync();
    }

    public void RebindHotkey()
    {
        _hook?.Dispose();
        _hook = new PushToTalkHook(AppSettings.Current.HotkeyVk);
        _hook.Pressed += () => Dispatcher.BeginInvoke(OnHotkeyDown);
        _hook.Released += () => Dispatcher.BeginInvoke(OnHotkeyUp);
    }

    private static readonly TimeSpan DoubleTapWindow = TimeSpan.FromMilliseconds(450);
    private DateTime _pressedAt = DateTime.MinValue;
    private bool _latched;
    private System.Threading.CancellationTokenSource? _pendingStop;

    private async void OnHotkeyDown()
    {
        var d = _dictation!;
        Log.Write($"hotkey down (state {d.State}, latched {_latched})");
        var now = DateTime.UtcNow;

        if (d.State == DictationState.Listening && _latched)
        {
            // second tap within the window (also while a stop is pending) = double tap = cancel
            if (_pendingStop != null || now - _pressedAt < DoubleTapWindow)
            {
                _pendingStop?.Cancel();
                _pendingStop = null;
                _latched = false;
                await d.CancelListeningAsync();
                return;
            }

            // single tap: stop and insert, after a short wait to see whether a second tap turns it into a cancel
            _pressedAt = now;
            var cts = _pendingStop = new System.Threading.CancellationTokenSource();
            try { await System.Threading.Tasks.Task.Delay(DoubleTapWindow, cts.Token); }
            catch (OperationCanceledException) { return; }
            if (!ReferenceEquals(_pendingStop, cts)) return;
            _pendingStop = null;
            _latched = false;
            await d.EndListeningAsync();
            return;
        }

        _pressedAt = now;
        _latched = false;
        d.BeginListening();
    }

    private async void OnHotkeyUp()
    {
        if (_dictation!.State != DictationState.Listening || _latched) return;
        // quick tap: keep listening until the next press
        if (AppSettings.Current.TapToToggle && DateTime.UtcNow - _pressedAt < TimeSpan.FromSeconds(1))
        {
            _latched = true;
            _overlay?.SetHint("Listening · tap to stop");
            return;
        }
        await _dictation.EndListeningAsync();
    }

    private void OnStateChanged()
    {
        if (_dictation == null || _overlay == null) return;
        switch (_dictation.State)
        {
            case DictationState.Listening:
                if (AppSettings.Current.ShowOverlay) _overlay.ShowListening();
                break;
            case DictationState.Transcribing:
                if (AppSettings.Current.ShowOverlay) _overlay.ShowTranscribing(_dictation.StatusText);
                break;
            default:
                _overlay.HideOverlay();
                break;
        }
        if (_tray != null)
        {
            var text = "VoiceDrop - " + _dictation.StatusText;
            _tray.Text = text.Length > 63 ? text[..63] : text;
        }
    }

    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open VoiceDrop", null, (_, _) => ShowMain());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "VoiceDrop", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, ev) => { if (ev.Button == Forms.MouseButtons.Left) ShowMain(); };
    }

    private void ShowMain()
    {
        if (_main == null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    /// <summary>Rounded dark square with a green waveform, drawn at runtime so no image asset is needed.</summary>
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(255, 18, 18, 18));
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(0, 0, 10, 10, 180, 90); path.AddArc(21, 0, 10, 10, 270, 90);
            path.AddArc(21, 21, 10, 10, 0, 90); path.AddArc(0, 21, 10, 10, 90, 90);
            path.CloseFigure();
            g.FillPath(bg, path);
            using var green = new SolidBrush(Color.FromArgb(255, 52, 199, 120));
            int[] h = [8, 16, 24, 14, 20, 10];
            for (int i = 0; i < h.Length; i++)
                g.FillRectangle(green, 4 + i * 4.4f, (32 - h[i]) / 2f, 2.8f, h[i]);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _dictation?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
