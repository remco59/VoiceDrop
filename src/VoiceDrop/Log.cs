using System;
using System.IO;

namespace VoiceDrop;

/// <summary>Tiny file log (%LOCALAPPDATA%\VoiceDrop\log.txt, trimmed at 512 KB) so silent failures can be diagnosed.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppSettings.DataDir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 512 * 1024) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
