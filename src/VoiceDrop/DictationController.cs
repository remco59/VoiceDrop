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
    public LocalLlm Llm { get; } = new();

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
        Log.Write($"state {s} ({StatusText})");
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
            WarmUpLlm();
        }
        catch (Exception ex)
        {
            Log.Write("model load failed: " + ex);
            Set(wasState == DictationState.Loading ? DictationState.Loading : DictationState.Idle, "Model failed to load");
            Error?.Invoke(ex.Message);
        }
    }

    /// <summary>Loads the polishing model in the background so the first dictation is not delayed.</summary>
    public void WarmUpLlm()
    {
        if (AppSettings.Current.CleanupMode != "smart" || !LlmCatalog.IsDownloaded(LlmCatalog.Get(AppSettings.Current.LlmId))) return;
        _ = Task.Run(async () => { try { await Llm.EnsureLoadedAsync(); } catch (Exception ex) { Log.Write("llm warm-up failed: " + ex.Message); } });
    }

    /// <summary>Runs the current cleanup settings on a text (used by the Try-it box); does not touch recording state.</summary>
    public Task<string> PolishForPreviewAsync(string text) => PolishAsync(text, announce: false);

    private async Task<string> PolishAsync(string text, bool announce = true)
    {
        var s = AppSettings.Current;
        if (s.CleanupMode != "off") text = TextCleaner.Apply(text, s.VoiceCommands, s.CleanFillers);
        if (s.CleanupMode == "smart" && text.Length >= 12 && LlmCatalog.IsDownloaded(LlmCatalog.Get(s.LlmId)))
        {
            if (announce) Set(DictationState.Transcribing, "Polishing");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                text = await Task.Run(() => Llm.CleanAsync(text, cts.Token));
            }
            catch (Exception ex) { Log.Write("polish skipped: " + ex.Message); }
        }
        return text;
    }

    public void BeginListening()
    {
        // a failed model load (for example no network during the first download) is retried on the next hotkey press
        if (State == DictationState.Loading && StatusText == "Model failed to load") { _ = LoadModelAsync(); return; }
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

    /// <summary>Stops recording and throws the audio away (double tap on the hotkey).</summary>
    public async Task CancelListeningAsync()
    {
        if (State != DictationState.Listening) return;
        _liveCts?.Cancel();
        try { if (_liveTask != null) await _liveTask; } catch { }
        _liveCts?.Dispose(); _liveCts = null; _liveTask = null;
        _recorder.Stop()?.Dispose();
        Log.Write("recording cancelled");
        Set(DictationState.Idle, "Ready");
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
            var finalText = string.IsNullOrWhiteSpace(r.Text) ? "" : await PolishAsync(r.Text);
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                bool endsWithBreak = finalText.EndsWith('\n');
                TextInjector.Type(AppSettings.Current.TrailingSpace && !endsWithBreak ? finalText + " " : finalText);
                var entry = new HistoryEntry { Text = finalText, Language = r.Language, Seconds = secs };
                if (r.Language.Length > 0) DetectedLanguage = r.Language;
                if (AppSettings.Current.SaveHistory) HistoryStore.Add(entry);
                Completed?.Invoke(entry);
            }
        }
        catch (Exception ex) { Log.Write("transcribe failed: " + ex); Error?.Invoke(ex.Message); }
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
        Llm.Dispose();
    }
}

internal static class Sounds
{
    public static readonly (string Id, string Name)[] All = [("glass", "Glass"), ("soft", "Soft"), ("ping", "Ping"), ("tap", "Tap")];

    public static void Start() => Play(AppSettings.Current.StartSound);

    public static void Play(string id)
    {
        Task.Run(() =>
        {
            try
            {
                var asm = typeof(Sounds).Assembly;
                var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith($".{id}.wav", StringComparison.OrdinalIgnoreCase));
                if (name == null) return;
                var reader = new NAudio.Wave.WaveFileReader(asm.GetManifestResourceStream(name)!);
                var output = new NAudio.Wave.WaveOutEvent();
                output.Init(reader);
                output.PlaybackStopped += (_, _) => { output.Dispose(); reader.Dispose(); };
                output.Play();
            }
            catch { /* sound is optional */ }
        });
    }
}
