using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace VoiceDrop;

public partial class App : Application
{
    private const uint HoldKey = 0xA3; // Right Ctrl (VK_RCONTROL): hold to talk, release to insert

    private Forms.NotifyIcon? _tray;
    private PushToTalkHook? _hook;
    private readonly Recorder _recorder = new();
    private readonly Transcriber _transcriber = new();
    private bool _ready, _busy;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Quit VoiceDrop", null, (_, _) => Shutdown());
        _tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "VoiceDrop - starting",
            Visible = true,
            ContextMenuStrip = menu
        };

        try
        {
            await _transcriber.InitializeAsync(SetStatus);
            _ready = true;
            _hook = new PushToTalkHook(HoldKey);
            _hook.Pressed += () => Dispatcher.BeginInvoke(OnPressed);
            _hook.Released += () => Dispatcher.BeginInvoke(OnReleased);
            _tray.ShowBalloonTip(2000, "VoiceDrop", "Hold Right Ctrl and speak.", Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "VoiceDrop failed to start");
            Shutdown();
        }
    }

    private void SetStatus(string s)
    {
        if (_tray == null) return;
        var text = "VoiceDrop - " + s;
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon.Text limit
    }

    private void OnPressed()
    {
        if (!_ready || _busy || _recorder.IsRecording) return;
        _recorder.Start();
        SetStatus("listening...");
        Console.Beep(900, 60);
    }

    private async void OnReleased()
    {
        if (!_recorder.IsRecording) return;
        var wav = _recorder.Stop();
        if (wav == null) { SetStatus("ready"); return; }

        _busy = true;
        SetStatus("transcribing...");
        try
        {
            string text = await Task.Run(() => _transcriber.TranscribeAsync(wav));
            if (!string.IsNullOrWhiteSpace(text)) TextInjector.Type(text + " ");
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(3000, "VoiceDrop error", ex.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            wav.Dispose();
            _busy = false;
            SetStatus("ready");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _recorder.Dispose();
        _transcriber.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
