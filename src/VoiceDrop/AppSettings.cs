using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace VoiceDrop;

internal sealed class AppSettings
{
    public uint HotkeyVk { get; set; } = 0xA3;          // Right Ctrl
    public string Language { get; set; } = "auto";      // "auto", "nl", "en", ...
    public string ModelId { get; set; } = "turbo";      // see ModelCatalog
    public bool DarkTheme { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public bool LivePreview { get; set; } = true;
    public bool SaveHistory { get; set; } = true;
    public bool TrailingSpace { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }

    public static AppSettings Current { get; private set; } = new();

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceDrop");
    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Current = new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are best effort */ }
        ApplyAutostart();
    }

    private void ApplyAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            if (StartWithWindows && Environment.ProcessPath is { } exe) key.SetValue("VoiceDrop", $"\"{exe}\"");
            else key.DeleteValue("VoiceDrop", false);
        }
        catch { }
    }
}
