using System.Windows.Input;

namespace VoiceDrop;

internal static class KeyNames
{
    public static string Name(uint vk) => vk switch
    {
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        0x14 => "Caps Lock",
        0x5B => "Left Win",
        0x5C => "Right Win",
        _ => KeyInterop.KeyFromVirtualKey((int)vk).ToString()
    };

    public static string LanguageName(string code) => code switch
    {
        "auto" => "Auto-detect",
        "nl" => "Nederlands",
        "en" => "English",
        "de" => "Deutsch",
        "fr" => "Français",
        "es" => "Español",
        "it" => "Italiano",
        "pt" => "Português",
        _ => code.ToUpperInvariant()
    };
}
