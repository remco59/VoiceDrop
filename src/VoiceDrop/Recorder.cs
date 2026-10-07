using System;
using System.IO;
using NAudio.Wave;

namespace VoiceDrop;

/// <summary>Records the default microphone as 16 kHz mono 16-bit PCM (what Whisper expects).</summary>
internal sealed class Recorder : IDisposable
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private readonly object _lock = new();
    private WaveIn? _in;
    private MemoryStream? _pcm;

    public bool IsRecording => _in != null;
    public double Seconds { get { lock (_lock) return (_pcm?.Length ?? 0) / (double)Format.AverageBytesPerSecond; } }

    /// <summary>Raised from the audio thread with a 0..1 loudness value.</summary>
    public event Action<float>? Level;

    public void Start()
    {
        if (_in != null) return;
        lock (_lock) _pcm = new MemoryStream();
        _in = new WaveIn { WaveFormat = Format, BufferMilliseconds = 50 };
        _in.DataAvailable += OnData;
        _in.StartRecording();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_lock) _pcm?.Write(e.Buffer, 0, e.BytesRecorded);

        double sum = 0;
        int count = e.BytesRecorded / 2;
        for (int i = 0; i < count; i++)
        {
            short v = BitConverter.ToInt16(e.Buffer, i * 2);
            sum += v * (double)v;
        }
        double rms = count > 0 ? Math.Sqrt(sum / count) / 32768.0 : 0;
        Level?.Invoke((float)Math.Min(1.0, Math.Sqrt(rms) * 1.6)); // sqrt: lift quiet speech
    }

    /// <summary>WAV of the last <paramref name="maxSeconds"/> recorded so far (for live preview), or null if too short.</summary>
    public MemoryStream? Snapshot(double maxSeconds, double minSeconds = 0.6)
    {
        byte[] data;
        lock (_lock)
        {
            if (_pcm == null) return null;
            int total = (int)_pcm.Length;
            int want = Math.Min(total, (int)(maxSeconds * Format.AverageBytesPerSecond)) & ~1;
            if (want < minSeconds * Format.AverageBytesPerSecond) return null;
            data = new byte[want];
            Buffer.BlockCopy(_pcm.GetBuffer(), total - want, data, 0, want);
        }
        return ToWav(data, data.Length);
    }

    /// <summary>Stops recording and returns the full WAV, or null if nothing usable was captured.</summary>
    public MemoryStream? Stop()
    {
        if (_in == null) return null;
        _in.StopRecording();
        _in.Dispose();
        _in = null;

        MemoryStream? pcm;
        lock (_lock) { pcm = _pcm; _pcm = null; }
        if (pcm == null || pcm.Length < Format.AverageBytesPerSecond / 4) return null; // under 0.25 s: accidental tap
        return ToWav(pcm.GetBuffer(), (int)pcm.Length);
    }

    private static MemoryStream ToWav(byte[] pcm, int length)
    {
        var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreCloseStream(wav), Format))
            writer.Write(pcm, 0, length);
        wav.Position = 0;
        return wav;
    }

    public void Dispose() { _in?.Dispose(); }

    private sealed class IgnoreCloseStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin s) => inner.Seek(o, s);
        public override void SetLength(long v) => inner.SetLength(v);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        protected override void Dispose(bool disposing) { }
    }
}
