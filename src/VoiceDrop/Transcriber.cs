using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace VoiceDrop;

internal sealed record ModelInfo(string Id, string Title, string Detail, string FileName, GgmlType Type, QuantizationType Quant);

internal static class ModelCatalog
{
    public static readonly ModelInfo[] All =
    [
        new("turbo", "Whisper Large v3 Turbo", "Best accuracy, 99 languages, ~570 MB", "ggml-large-v3-turbo-q5_0.bin", GgmlType.LargeV3Turbo, QuantizationType.Q5_0),
        new("small", "Whisper Small", "Faster, lower accuracy, ~180 MB", "ggml-small-q5_1.bin", GgmlType.Small, QuantizationType.Q5_1),
        new("base", "Whisper Base", "Fastest, basic accuracy, ~60 MB", "ggml-base-q5_1.bin", GgmlType.Base, QuantizationType.Q5_1),
    ];

    public static ModelInfo Get(string id) => All.FirstOrDefault(m => m.Id == id) ?? All[0];
    public static string PathOf(ModelInfo m) => Path.Combine(AppSettings.DataDir, "models", m.FileName);
    public static bool IsDownloaded(ModelInfo m) => File.Exists(PathOf(m));
}

internal sealed record TranscribeResult(string Text, string Language);

/// <summary>Local Whisper (whisper.cpp) with Vulkan GPU acceleration. One shared processor, calls are serialized.</summary>
internal sealed class Transcriber : IDisposable
{
    private static readonly Regex Noise = new(@"\[[^\]]*\]|\([^)]*\)|♪+", RegexOptions.Compiled);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _loadedModel = "";
    private string _loadedLanguage = "", _loadedPrompt = "";

    public bool IsLoaded => _processor != null;

    /// <summary>Loads (downloading if needed) the model and builds the processor for the current settings.</summary>
    public async Task LoadAsync(Action<string> status, Action<double>? progress = null)
    {
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        var s = AppSettings.Current;
        var model = ModelCatalog.Get(s.ModelId);

        await _gate.WaitAsync();
        try
        {
            var prompt = DictionaryStore.PromptText();
            if (_processor != null && _loadedModel == model.Id && _loadedLanguage == s.Language && _loadedPrompt == prompt) return;

            if (!ModelCatalog.IsDownloaded(model))
                await DownloadAsync(model, status, progress);

            status("Loading model...");
            if (_loadedModel != model.Id || _factory == null)
            {
                _processor?.Dispose();
                _processor = null;
                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(ModelCatalog.PathOf(model));
                _loadedModel = model.Id;
            }
            _processor?.Dispose();
            var builder = _factory.CreateBuilder().WithLanguage(s.Language);
            if (prompt.Length > 0) builder = builder.WithPrompt(prompt); // custom dictionary as vocabulary hint
            _processor = builder.Build();
            _loadedLanguage = s.Language;
            _loadedPrompt = prompt;
            status("Ready");
        }
        finally { _gate.Release(); }
    }

    public static async Task DownloadAsync(ModelInfo model, Action<string> status, Action<double>? progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelCatalog.PathOf(model))!);
        var path = ModelCatalog.PathOf(model);
        var tmp = path + ".part";
        status($"Downloading {model.Title}...");
        using (var src = await new WhisperGgmlDownloader(new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan })
                   .GetGgmlModelAsync(model.Type, model.Quant))
        using (var dst = File.Create(tmp))
        {
            var buf = new byte[1 << 20];
            long total = 0, expected = src.CanSeek ? src.Length : 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                total += n;
                if (expected > 0) progress?.Invoke((double)total / expected);
                else status($"Downloading {model.Title}... {total / (1024 * 1024)} MB");
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<TranscribeResult> TranscribeAsync(Stream wav, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_processor == null) return new("", "");
            var sb = new StringBuilder();
            string lang = "";
            await foreach (var seg in _processor.ProcessAsync(wav, ct))
            {
                sb.Append(seg.Text);
                if (lang.Length == 0) lang = seg.Language ?? "";
            }
            var text = Regex.Replace(Noise.Replace(sb.ToString(), ""), @"\s+", " ").Trim();
            return new(DictionaryStore.Apply(text), lang);
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
