using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceDrop;

/// <summary>Global low-level keyboard hook: raises Pressed/Released for one key (hold-to-talk).</summary>
internal sealed class PushToTalkHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private readonly uint _vk;
    private IntPtr _hook;
    private bool _down;

    public event Action? Pressed;
    public event Action? Released;

    public PushToTalkHook(uint virtualKey)
    {
        _vk = virtualKey;
        _proc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(module.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Could not install keyboard hook.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == _vk)
            {
                int msg = wParam.ToInt32();
                if ((msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN) && !_down)
                {
                    _down = true;
                    Pressed?.Invoke();
                }
                else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    _down = false;
                    Released?.Invoke();
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
