using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace VoiceDrop;

/// <summary>Local Whisper (whisper.cpp) with Vulkan GPU acceleration and automatic language detection.</summary>
internal sealed class Transcriber : IDisposable
{
    private static readonly string ModelDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceDrop", "models");
    private static readonly string ModelPath = Path.Combine(ModelDir, "ggml-large-v3-turbo-q5_0.bin");

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public async Task InitializeAsync(Action<string> status)
    {
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

        if (!File.Exists(ModelPath))
        {
            status("downloading model (one time, ~600 MB)...");
            Directory.CreateDirectory(ModelDir);
            var tmp = ModelPath + ".part";
            using (var src = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.LargeV3Turbo, QuantizationType.Q5_0))
            using (var dst = File.Create(tmp))
                await src.CopyToAsync(dst);
            File.Move(tmp, ModelPath, overwrite: true);
        }

        status("loading model...");
        _factory = WhisperFactory.FromPath(ModelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("auto") // detects Dutch / English / etc. per recording
            .Build();
        status("ready");
    }

    public async Task<string> TranscribeAsync(Stream wav)
    {
        if (_processor == null) return "";
        var sb = new StringBuilder();
        await foreach (var seg in _processor.ProcessAsync(wav))
            sb.Append(seg.Text);
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
