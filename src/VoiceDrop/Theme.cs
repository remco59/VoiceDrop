using System.Windows;
using System.Windows.Media;

namespace VoiceDrop;

/// <summary>Dark/light palettes modelled on FluidVoice's AppTheme. Brushes are swapped live (DynamicResource).</summary>
internal static class Theme
{
    private static Color C(byte r, byte g, byte b, byte a = 255) => Color.FromArgb(a, r, g, b);

    public static void Apply(bool dark)
    {
        var res = Application.Current.Resources;
        void Set(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
            res[key + "Color"] = c;
        }

        if (dark)
        {
            Set("WindowBg", C(18, 18, 18));
            Set("ContentBg", C(23, 23, 23));
            Set("SidebarBg", C(15, 15, 15));
            Set("CardBg", C(20, 20, 20));
            Set("CardElevated", C(28, 28, 28));
            Set("CardBorder", C(255, 255, 255, 22));
            Set("Separator", C(255, 255, 255, 18));
            Set("TextPrimary", C(240, 240, 240));
            Set("TextSecondary", C(160, 160, 160));
            Set("TextTertiary", C(105, 105, 105));
            Set("Hover", C(255, 255, 255, 14));
            Set("Selected", C(255, 255, 255, 26));
        }
        else
        {
            Set("WindowBg", C(246, 246, 246));
            Set("ContentBg", C(255, 255, 255));
            Set("SidebarBg", C(238, 238, 240));
            Set("CardBg", C(250, 250, 250));
            Set("CardElevated", C(255, 255, 255));
            Set("CardBorder", C(0, 0, 0, 28));
            Set("Separator", C(0, 0, 0, 20));
            Set("TextPrimary", C(28, 28, 30));
            Set("TextSecondary", C(110, 110, 115));
            Set("TextTertiary", C(160, 160, 165));
            Set("Hover", C(0, 0, 0, 12));
            Set("Selected", C(0, 0, 0, 22));
        }
        Set("Accent", C(52, 199, 120)); // FluidVoice green
        Set("AccentSoft", C(52, 199, 120, 40));
        Set("Warning", C(255, 159, 10));
        Set("Danger", C(255, 69, 58));
    }
}
