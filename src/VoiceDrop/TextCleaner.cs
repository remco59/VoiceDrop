using System;
using System.Text.RegularExpressions;

namespace VoiceDrop;

/// <summary>
/// Instant rule-based cleanup (no AI): spoken commands such as "new line", filler words such as "um",
/// and a few tidy-ups. Works for English and Dutch.
/// </summary>
internal static class TextCleaner
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // spoken layout commands (Whisper usually adds punctuation around them: "Hello. New line. Next")
    private static readonly Regex NewParagraph = new(
        @"([.!?])?[\s,;:]*\b(?:new paragraph|nieuwe alinea|nieuw alinea)\b[\s,.;:!?]*(\p{L})?", Opt);
    private static readonly Regex NewLine = new(
        @"([.!?])?[\s,;:]*\b(?:new line|newline|nieuwe regel|nieuwe lijn|volgende regel)\b[\s,.;:!?]*(\p{L})?", Opt);

    // spoken punctuation (only unambiguous words; "punt" and "komma" are common words and are left alone)
    private static readonly Regex QuestionMark = new(@"\s*\b(?:question mark|vraagteken)\b[,.]?", Opt);
    private static readonly Regex Exclamation = new(@"\s*\b(?:exclamation mark|exclamation point|uitroepteken)\b[,.]?", Opt);

    // filler words, with their trailing comma/period and spacing
    private const string Filler = @"(?:u+h+m*|u+m+|e+h+m*|e+u+h+m*|erm|h+m{2,}|m+h?m+)";
    private static readonly Regex FillerAtSentenceStart = new(
        @"(^|[.!?]\s+|\n)" + Filler + @"\b[,.…]*\s*(\p{L})?", Opt);
    private static readonly Regex FillerMidSentence = new(
        @"\s*,?\s*\b" + Filler + @"\b[,…]*(?=\s|$|[.!?])", Opt);

    public static string Apply(string text, bool voiceCommands, bool fillers)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        if (fillers)
        {
            text = FillerAtSentenceStart.Replace(text, m => m.Groups[1].Value + (m.Groups[2].Success ? m.Groups[2].Value.ToUpperInvariant() : ""));
            text = FillerMidSentence.Replace(text, "");
        }

        if (voiceCommands)
        {
            text = NewParagraph.Replace(text, m => m.Groups[1].Value + "\n\n" + Upper(m.Groups[2]));
            text = NewLine.Replace(text, m => m.Groups[1].Value + "\n" + Upper(m.Groups[2]));
            text = QuestionMark.Replace(text, "?");
            text = Exclamation.Replace(text, "!");
        }

        text = Regex.Replace(text, @"[ \t]{2,}", " ");          // collapse double spaces
        text = Regex.Replace(text, @"[ \t]+([,.!?;:])", "$1");   // no space before punctuation
        text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");     // no spaces around line breaks
        return text.Trim(' ', '\t');
    }

    private static string Upper(Group g) => g.Success ? g.Value.ToUpperInvariant() : "";
}
