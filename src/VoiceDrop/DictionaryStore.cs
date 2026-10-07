using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiceDrop;

/// <summary>A word or phrase you want transcribed exactly, plus optional "misheard as" variants that get replaced.</summary>
internal sealed class DictEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Term { get; set; } = "";
    public List<string> Variants { get; set; } = new();
}

/// <summary>
/// Custom dictionary. Two effects: (1) every term is fed to Whisper as a vocabulary hint (initial prompt),
/// (2) every variant is replaced with its term after transcription.
/// </summary>
internal static class DictionaryStore
{
    private const int MaxPromptChars = 400; // Whisper prompts are limited (about 224 tokens)
    private static readonly string FilePath = Path.Combine(AppSettings.DataDir, "dictionary.json");
    private static List<DictEntry>? _items;
    private static List<(Regex Rx, string Term)>? _rules;

    public static event Action? Changed;

    public static IReadOnlyList<DictEntry> Items => _items ??= Load();

    private static List<DictEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<DictEntry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void Persist()
    {
        _rules = null;
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        Changed?.Invoke();
    }

    public static void Add(DictEntry e) { _ = Items; _items!.Insert(0, e); Persist(); }
    public static void Remove(DictEntry e) { _ = Items; _items!.Remove(e); Persist(); }

    public static List<string> ParseVariants(string text) =>
        text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Comma separated vocabulary for Whisper's initial prompt (empty when the dictionary is empty).</summary>
    public static string PromptText()
    {
        var sb = new StringBuilder();
        foreach (var t in Items.Select(i => i.Term).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (sb.Length + t.Length + 2 > MaxPromptChars) break;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(t);
        }
        return sb.ToString();
    }

    /// <summary>Replace "misheard as" variants with their term (whole words, case-insensitive).</summary>
    public static string Apply(string text)
    {
        if (text.Length == 0) return text;
        _rules ??= BuildRules();
        foreach (var (rx, term) in _rules) text = rx.Replace(text, term);
        return text;
    }

    private static List<(Regex, string)> BuildRules()
    {
        var rules = new List<(Regex, string)>();
        foreach (var e in Items)
        {
            var variants = e.Variants.Where(v => v.Length > 0).OrderByDescending(v => v.Length).Select(Regex.Escape).ToList();
            if (variants.Count == 0 || e.Term.Length == 0) continue;
            var rx = new Regex(@"(?<![\p{L}\p{N}])(" + string.Join("|", variants) + @")(?![\p{L}\p{N}])",
                               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            rules.Add((rx, e.Term));
        }
        return rules;
    }
}
