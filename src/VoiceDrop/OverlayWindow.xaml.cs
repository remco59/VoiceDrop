using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VoiceDrop;

/// <summary>Floating pill (bottom centre) with live waveform and transcript preview. Never takes focus.</summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 11;
    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const uint MONITOR_DEFAULTTONEAREST = 2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize; public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    /// <summary>Monitor that holds the focused text input (falls back to the foreground window, then the mouse).</summary>
    private static IntPtr TargetMonitor()
    {
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(GetWindowThreadProcessId(fg, IntPtr.Zero), ref info))
            {
                var h = info.hwndCaret != IntPtr.Zero ? info.hwndCaret
                      : info.hwndFocus != IntPtr.Zero ? info.hwndFocus : fg;
                return MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            }
            return MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        }
        GetCursorPos(out var pt);
        return MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
    }

    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly float[] _levels = new float[BarCount];
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private float _target;
    private bool _processing;
    private double _phase;

    public OverlayWindow()
    {
        InitializeComponent();
        for (int i = 0; i < BarCount; i++)
        {
            var r = new Rectangle { Width = 4, Height = 4, RadiusX = 2, RadiusY = 2, Margin = new Thickness(2, 0, 2, 0),
                                    Fill = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            _bars[i] = r;
            Bars.Children.Add(r);
        }
        _anim.Tick += (_, _) => Animate();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        };
        SizeChanged += (_, _) => Reposition();
    }

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(TargetMonitor(), ref mi)) return;
        GetWindowRect(hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = mi.rcWork.Left + (mi.rcWork.Right - mi.rcWork.Left - w) / 2;
        int y = mi.rcWork.Bottom - h - 8;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void ShowListening()
    {
        _processing = false;
        Preview.Text = "";
        FullText.Text = "";
        FullPopup.IsOpen = false;
        PreviewBox.Visibility = Visibility.Collapsed;
        SetClickThrough(AppSettings.Current.FullTextMode == "off");
        LangChip.Visibility = Visibility.Collapsed;
        StatusText.Text = "Listening";
        Array.Clear(_levels);
        _anim.Start();
        if (!IsVisible) Show();
        UpdateLayout();
        Reposition();
    }

    public void SetHint(string text) => StatusText.Text = text;

    public void ShowTranscribing()
    {
        _processing = true;
        StatusText.Text = "Transcribing";
    }

    public void HideOverlay()
    {
        _anim.Stop();
        FullPopup.IsOpen = false;
        Hide();
    }

    public void PushLevel(float level) => _target = level;

    private const int PreviewWords = 10;

    public void SetPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        FullText.Text = text;
        FullScroll.ScrollToEnd();

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Preview.Text = words.Length > PreviewWords ? string.Join(' ', words[^PreviewWords..]) : text;
        PreviewBox.Visibility = Visibility.Visible;

        // keep the newest words visible: right-align inside the clipped box
        Preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Preview, Math.Min(0, PreviewBox.Width - Preview.DesiredSize.Width));
    }

    private void SetClickThrough(bool on)
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        SetWindowLong(h, GWL_EXSTYLE, on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
    }

    private void Pill_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (AppSettings.Current.FullTextMode == "hover" && FullText.Text.Length > 0) FullPopup.IsOpen = true;
    }

    private void Pill_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (AppSettings.Current.FullTextMode == "hover") FullPopup.IsOpen = false;
    }

    private void Pill_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AppSettings.Current.FullTextMode == "click" && FullText.Text.Length > 0) FullPopup.IsOpen = !FullPopup.IsOpen;
    }

    public void SetLanguage(string code)
    {
        if (string.IsNullOrEmpty(code)) { LangChip.Visibility = Visibility.Collapsed; return; }
        LangText.Text = code.ToUpperInvariant();
        LangChip.Visibility = Visibility.Visible;
    }

    private void Animate()
    {
        _phase += 0.35;
        for (int i = 0; i < BarCount - 1; i++) _levels[i] = _levels[i + 1];
        float v = _processing ? (float)(0.25 + 0.2 * Math.Sin(_phase)) : _target;
        _levels[^1] = _levels[^1] * 0.4f + v * 0.6f;

        for (int i = 0; i < BarCount; i++)
        {
            double mid = 1.0 - Math.Abs(i - (BarCount - 1) / 2.0) / BarCount;   // taller in the centre
            double h = 4 + _levels[i] * 22 * mid;
            if (_processing) h = 4 + 10 * (0.5 + 0.5 * Math.Sin(_phase + i * 0.7));
            _bars[i].Height = Math.Max(4, h);
            _bars[i].Opacity = _processing ? 0.7 : 0.55 + 0.45 * mid;
        }
    }
}
