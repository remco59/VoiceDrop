using System.Runtime.InteropServices;

namespace VoiceDrop;

/// <summary>Types text into the focused window via SendInput (Unicode), without touching the clipboard.</summary>
internal static class TextInjector
{
    public static void Type(string text)
    {
        var inputs = new NativeMethods.INPUT[text.Length * 2];
        int n = 0;
        foreach (char c in text)
        {
            inputs[n++] = Key(c, 0);
            inputs[n++] = Key(c, NativeMethods.KEYEVENTF_KEYUP);
        }
        NativeMethods.SendInput((uint)n, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(char c, uint extraFlags) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wScan = c, dwFlags = NativeMethods.KEYEVENTF_UNICODE | extraFlags }
        }
    };
}
