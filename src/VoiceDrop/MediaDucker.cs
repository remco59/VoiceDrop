using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace VoiceDrop;

/// <summary>
/// Lowers the volume of every other app's audio session while you dictate, so a video or music in the background is not
/// picked up by the microphone, and fades it back afterwards. VoiceDrop's own sounds are left alone.
/// Original volumes are written to disk while ducked so a crash can not leave your PC quiet (see RecoverAsync).
/// </summary>
internal sealed class MediaDucker
{
    private sealed class Saved
    {
        public string Id { get; set; } = "";
        public float Original { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public AudioSessionControl? Session { get; set; }
    }

    private static readonly string StatePath = Path.Combine(AppSettings.DataDir, "ducked.json");
    private const int DownMs = 90, UpMs = 320, StepMs = 15;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<Saved> _saved = new();
    private Task _last = Task.CompletedTask;

    /// <summary>Queue a duck. Returns immediately; operations run in order on a background thread.</summary>
    public void Duck(int levelPercent) => Enqueue(() => DuckCore(Math.Clamp(levelPercent, 0, 100) / 100f));

    /// <summary>Queue a restore (no-op when nothing is ducked).</summary>
    public void Restore() => Enqueue(RestoreCore);

    /// <summary>Restore and wait; used when the app exits.</summary>
    public void RestoreNow()
    {
        Restore();
        try { _last.Wait(2000); } catch { }
    }

    /// <summary>At startup: undo a duck that was left behind by a crash or kill.</summary>
    public void RecoverFromCrash() => Enqueue(() =>
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var left = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(StatePath)) ?? new();
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                var match = left.Find(x => x.Id == s.GetSessionInstanceIdentifier);
                if (match != null) s.SimpleAudioVolume.Volume = match.Original;
            }
            File.Delete(StatePath);
            Log.Write($"restored {left.Count} audio sessions after an unclean exit");
        }
        catch (Exception ex) { Log.Write("duck recovery failed: " + ex.Message); }
    });

    private void Enqueue(Action work)
    {
        lock (_gate)
        {
            _last = _last.ContinueWith(_ =>
            {
                try { work(); } catch (Exception ex) { Log.Write("media duck: " + ex.Message); }
            }, TaskScheduler.Default);
        }
    }

    private void DuckCore(float factor)
    {
        if (_saved.Count > 0) return; // already ducked

        int myPid = Environment.ProcessId;
        // keep the device alive until restore: the session objects depend on it
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioSessionManager.RefreshSessions();
        var sessions = _device.AudioSessionManager.Sessions;

        var list = new List<Saved>();
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                if (s.GetProcessID == (uint)myPid) continue;
                float original = s.SimpleAudioVolume.Volume;
                list.Add(new Saved { Id = s.GetSessionInstanceIdentifier, Original = original, Session = s });
            }
            catch { /* a session can disappear while we enumerate */ }
        }
        if (list.Count == 0) { ReleaseDevice(); return; }
        _saved = list;
        _currentFactor = 1f;

        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(list));
        }
        catch { }

        Ramp(1f, factor, DownMs);
        Log.Write($"ducked {list.Count} audio sessions to {factor:P0}");
    }

    private void RestoreCore()
    {
        if (_saved.Count == 0) return;
        var list = _saved;
        _saved = new();
        RampList(list, _currentFactor, 1f, UpMs);
        try { File.Delete(StatePath); } catch { }
        ReleaseDevice();
        Log.Write($"restored {list.Count} audio sessions");
    }

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private float _currentFactor = 1f;

    private void ReleaseDevice()
    {
        try { _device?.Dispose(); } catch { }
        try { _enumerator?.Dispose(); } catch { }
        _device = null;
        _enumerator = null;
    }

    private void Ramp(float from, float to, int ms) => RampList(_saved, from, to, ms);

    /// <summary>Fades every session from original*from to original*to.</summary>
    private void RampList(List<Saved> list, float from, float to, int ms)
    {
        int steps = Math.Max(1, ms / StepMs);
        for (int step = 1; step <= steps; step++)
        {
            float t = step / (float)steps;
            t = t * t * (3 - 2 * t); // smoothstep
            float f = from + (to - from) * t;
            foreach (var s in list)
            {
                try { s.Session!.SimpleAudioVolume.Volume = Math.Clamp(s.Original * f, 0f, 1f); }
                catch { /* session ended */ }
            }
            _currentFactor = f;
            if (step < steps) Thread.Sleep(StepMs);
        }
        if (to >= 1f)
        {
            foreach (var s in list)
            {
                try { s.Session!.SimpleAudioVolume.Volume = s.Original; } catch { }
            }
            _currentFactor = 1f;
        }
    }
}
