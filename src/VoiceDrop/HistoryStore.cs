using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VoiceDrop;

internal sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Time { get; set; } = DateTime.Now;
    public string Text { get; set; } = "";
    public string Language { get; set; } = "";
    public double Seconds { get; set; }
    public bool Pinned { get; set; }
    public int Words => Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

internal static class HistoryStore
{
    private static readonly string FilePath = Path.Combine(AppSettings.DataDir, "history.json");
    private static List<HistoryEntry>? _items;

    public static event Action? Changed;

    public static IReadOnlyList<HistoryEntry> Items => _items ??= Load();

    private static List<HistoryEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch { }
        Changed?.Invoke();
    }

    public static void Add(HistoryEntry e)
    {
        _ = Items;
        _items!.Insert(0, e);
        Persist();
    }

    public static void Remove(HistoryEntry e) { _ = Items; _items!.Remove(e); Persist(); }
    public static void TogglePin(HistoryEntry e) { e.Pinned = !e.Pinned; Persist(); }
    public static void Clear() { _ = Items; _items!.RemoveAll(x => !x.Pinned); Persist(); }

    public static IEnumerable<HistoryEntry> Today => Items.Where(x => x.Time.Date == DateTime.Today);
}
