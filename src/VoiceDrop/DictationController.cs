using System;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceDrop;

internal enum DictationState { Loading, Idle, Listening, Transcribing }

/// <summary>Owns the record -> live preview -> final transcription -> insert pipeline. Raises events on the UI thread.</summary>
internal sealed class DictationController : IDisposable
{
    private readonly System.Windows.Threading.Dispatcher _ui;
    private readonly Recorder _recorder = new();
    private readonly Transcriber _transcriber = new();
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;

    public DictationState State { get; private set; } = DictationState.Loading;
    public string StatusText { get; private set; } = "Starting...";
    public string DetectedLanguage { get; private set; } = "";
    public Transcriber Transcriber => _transcriber;

    public event Action? StateChanged;
    public event Action<float>? Level;
    public event Action<string>? Partial;
    public event Action<HistoryEntry>? Completed;
    public event Action<string>? Error;
    public event Action<double>? DownloadProgress;

    public DictationController(System.Windows.Threading.Dispatcher ui)
    {
        _ui = ui;
        _recorder.Level += l => Level?.Invoke(l);
    }

    private void Set(DictationState s, string? status = null)
    {
        State = s;
        if (status != null) StatusText = status;
        StateChanged?.Invoke();
    }

    public async Task LoadModelAsync()
    {
        var wasState = State;
        Set(DictationState.Loading, "Loading model...");
        try
        {
            await _transcriber.LoadAsync(s => _ui.Invoke(() => { StatusText = s; StateChanged?.Invoke(); }),
                p => _ui.Invoke(() => DownloadProgress?.Invoke(p)));
            Set(DictationState.Idle, "Ready");
        }
        catch (Exception ex)
        {
            Set(wasState == DictationState.Loading ? DictationState.Loading : DictationState.Idle, "Model failed to load");
            Error?.Invoke(ex.Message);
        }
    }

    public void BeginListening()
    {
        if (State != DictationState.Idle) return;
        DetectedLanguage = "";
        _recorder.Start();
        Set(DictationState.Listening, "Listening");
        if (AppSettings.Current.PlaySounds) Sounds.Start();

        if (AppSettings.Current.LivePreview)
        {
            _liveCts = new CancellationTokenSource();
            _liveTask = LivePreviewLoop(_liveCts.Token);
        }
    }

    private async Task LivePreviewLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(450, ct);
                using var wav = _recorder.Snapshot(25);
                if (wav == null) continue;
                var r = await _transcriber.TranscribeAsync(wav, ct);
                if (ct.IsCancellationRequested) break;
                _ui.Invoke(() =>
                {
                    if (r.Language.Length > 0) DetectedLanguage = r.Language;
                    Partial?.Invoke(r.Text);
                });
            }
        }
        catch (OperationCanceledException) { }
        catch { /* preview is best effort */ }
    }

    public async Task EndListeningAsync()
    {
        if (State != DictationState.Listening) return;

        _liveCts?.Cancel();
        try { if (_liveTask != null) await _liveTask; } catch { }
        _liveCts?.Dispose(); _liveCts = null; _liveTask = null;

        double secs = _recorder.Seconds;
        var wav = _recorder.Stop();
        if (wav == null) { Set(DictationState.Idle, "Ready"); return; }

        Set(DictationState.Transcribing, "Transcribing");
        try
        {
            var r = await Task.Run(() => _transcriber.TranscribeAsync(wav));
            if (!string.IsNullOrWhiteSpace(r.Text))
            {
                TextInjector.Type(AppSettings.Current.TrailingSpace ? r.Text + " " : r.Text);
                var entry = new HistoryEntry { Text = r.Text, Language = r.Language, Seconds = secs };
                if (r.Language.Length > 0) DetectedLanguage = r.Language;
                if (AppSettings.Current.SaveHistory) HistoryStore.Add(entry);
                Completed?.Invoke(entry);
            }
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
        finally
        {
            wav.Dispose();
            Set(DictationState.Idle, "Ready");
        }
    }

    public void Dispose()
    {
        _liveCts?.Cancel();
        _recorder.Dispose();
        _transcriber.Dispose();
    }
}

internal static class Sounds
{
    public static void Start() => Task.Run(() => { try { Console.Beep(880, 50); } catch { } });
}
