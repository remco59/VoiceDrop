using System;
using System.IO;
using NAudio.Wave;

namespace VoiceDrop;

/// <summary>Records the default microphone as 16 kHz mono 16-bit PCM (what Whisper expects).</summary>
internal sealed class Recorder : IDisposable
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private WaveIn? _in;
    private MemoryStream? _pcm;

    public bool IsRecording => _in != null;

    public void Start()
    {
        if (_in != null) return;
        _pcm = new MemoryStream();
        _in = new WaveIn { WaveFormat = Format, BufferMilliseconds = 50 };
        _in.DataAvailable += (_, e) => _pcm?.Write(e.Buffer, 0, e.BytesRecorded);
        _in.StartRecording();
    }

    /// <summary>Stops recording and returns a WAV stream, or null if nothing usable was captured.</summary>
    public MemoryStream? Stop()
    {
        if (_in == null) return null;
        _in.StopRecording();
        _in.Dispose();
        _in = null;

        var pcm = _pcm!;
        _pcm = null;
        if (pcm.Length < Format.AverageBytesPerSecond / 4) return null; // under 0.25 s: accidental tap

        var wav = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreCloseStream(wav), Format))
            writer.Write(pcm.GetBuffer(), 0, (int)pcm.Length);
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
