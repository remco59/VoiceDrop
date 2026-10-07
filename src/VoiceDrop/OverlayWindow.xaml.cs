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
    private const int BarCount = 25, Mid = BarCount / 2;
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
    private readonly float[] _levels = new float[Mid + 1]; // [0] = newest, mirrored outwards from the centre
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private float _target;
    private bool _processing;
    private double _phase;

    public OverlayWindow()
    {
        InitializeComponent();
        for (int i = 0; i < BarCount; i++)
        {
            double t = i / (double)(BarCount - 1);
            var color = Color.FromRgb((byte)(79 + 45 * t), (byte)(140 - 48 * t), 255); // blue -> indigo
            var r = new Rectangle { Width = 3.2, Height = 4, RadiusX = 1.6, RadiusY = 1.6, Margin = new Thickness(0.9, 0, 0.9, 0),
                                    Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
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
        // the window has a 28 DIP transparent margin for the shadow; put the card itself 12 DIP above the work area edge
        int y = mi.rcWork.Bottom - h + (int)Math.Round(16 * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY);
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
        StatusText.Text = AppSettings.Current.TapToToggle ? "Listening · release to stop" : "Listening";
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

    private static readonly LinearGradientBrush FadeMask = MakeFade();

    private static LinearGradientBrush MakeFade()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.4));
        b.Freeze();
        return b;
    }

    private const int PreviewWords = 60;     // cap for very long dictations
    private const double MaxPreviewHeight = 78; // 3 lines at 26 px

    public void SetPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        FullText.Text = text;
        FullScroll.ScrollToEnd();

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Preview.Text = words.Length > PreviewWords ? string.Join(' ', words[^PreviewWords..]) : text;
        PreviewBox.Visibility = Visibility.Visible;

        // grow up to 3 lines; beyond that the newest line stays at the bottom and the top fades out
        Preview.Measure(new Size(PreviewBox.Width, double.PositiveInfinity));
        double h = Preview.DesiredSize.Height;
        bool overflow = h > MaxPreviewHeight;
        PreviewBox.Height = Math.Min(h, MaxPreviewHeight);
        Canvas.SetTop(Preview, PreviewBox.Height - h);
        PreviewBox.OpacityMask = overflow ? FadeMask : null;
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
        _phase += 0.3;
        for (int i = _levels.Length - 1; i > 0; i--) _levels[i] = _levels[i - 1];
        float v = _processing ? (float)(0.3 + 0.25 * Math.Sin(_phase)) : _target;
        _levels[0] = _levels[0] * 0.35f + v * 0.65f;

        for (int i = 0; i < BarCount; i++)
        {
            int d = Math.Abs(i - Mid);
            double env = Math.Exp(-Math.Pow(d / (Mid * 0.62), 2));           // tall in the centre, dots at the edges
            double lvl = _processing ? 0.5 + 0.5 * Math.Sin(_phase - d * 0.55) : _levels[d];
            double h = 4 + lvl * 32 * env;
            if (_processing) h = 4 + 14 * lvl * env;
            _bars[i].Height = Math.Min(34, Math.Max(3.2, h));
            _bars[i].Opacity = 0.45 + 0.55 * env;
        }
    }
}
