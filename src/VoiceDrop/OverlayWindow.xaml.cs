using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VoiceDrop;

/// <summary>
/// Floating glass card with live waveform and transcript. Never takes focus and is always click-through.
/// The window is sized and positioned by us in one SetWindowPos per frame, anchored at the bottom, so it never jumps.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int BarCount = 25, Mid = BarCount / 2;
    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20;
    private const double WindowWidthDip = 616;       // card (560) + 2 * 28 margin for the shadow
    private const double CardLiftDip = 12;           // gap between card and the bottom of the work area
    private const double ShadowMarginDip = 28;
    private const double CollapsedHeight = 52;       // 2 lines at 26 px
    private const double PreviewGap = 16;            // space between transcript and waveform row

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hMon, int type, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

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
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float _target;
    private bool _processing, _wasDown, _wantVisible, _expanded, _frameHooked;
    private double _phase, _lastFrame, _restDip;

    // screen anchor (physical pixels), fixed for the whole recording
    private int _anchorCenterX, _anchorBottom;
    private double _scale = 1;
    private int _lastX = int.MinValue, _lastY, _lastW, _lastH;

    // intro animation: small dot rises from the bottom edge, then grows into the card
    private const double IntroSeconds = 0.65, DotSize = 26, RiseDip = 70;
    private double _introStart = -1, _naturalCardHeight = 120;

    // transcript animation state
    private string _fullText = "";
    private int _shownLen;
    private double _curBox, _curTop;

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
        _poll.Tick += (_, _) => PollCursor();
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        };
    }

    // ---------- show / hide ----------

    public void ShowListening()
    {
        _processing = false;
        _wantVisible = true;
        _expanded = false;
        _fullText = "";
        _shownLen = 0;
        _curBox = _curTop = 0;
        Preview.Text = "";
        PreviewBox.Height = 0;
        PreviewBox.Visibility = Visibility.Collapsed;
        PreviewBox.OpacityMask = null;
        LangChip.Visibility = Visibility.Collapsed;
        StatusText.Text = AppSettings.Current.TapToToggle ? "Listening · release to stop" : "Listening";
        Array.Clear(_levels);

        // base height of the card without transcript (measured once per show, before the intro shrinks it)
        PreviewBox.Visibility = Visibility.Collapsed;
        Pill.Width = 560; Pill.Height = double.NaN; Pill.CornerRadius = new CornerRadius(28);
        Pill.Measure(new Size(WindowWidthDip, double.PositiveInfinity));
        _restDip = Pill.DesiredSize.Height;
        _naturalCardHeight = _restDip - 2 * ShadowMarginDip;
        _introStart = _clock.Elapsed.TotalSeconds;
        ApplyIntro(0);

        // pick the monitor once; the card stays put for the whole recording
        var mon = TargetMonitor();
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(mon, ref mi))
        {
            _anchorCenterX = (mi.rcWork.Left + mi.rcWork.Right) / 2;
            _anchorBottom = mi.rcWork.Bottom + (int)Math.Round((ShadowMarginDip - CardLiftDip) * ScaleOf(mon));
            _scale = ScaleOf(mon);
        }
        _lastX = int.MinValue;

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
        ApplyWindowRect(force: true);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        _anim.Start();
        _poll.Start();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        if (!_frameHooked) { CompositionTarget.Rendering += OnFrame; _frameHooked = true; }
    }

    public void ShowTranscribing()
    {
        _processing = true;
        StatusText.Text = "Transcribing";
    }

    public void HideOverlay()
    {
        if (!_wantVisible) return;
        _wantVisible = false;
        _expanded = false;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) =>
        {
            if (_wantVisible) return;
            _anim.Stop();
            _poll.Stop();
            if (_frameHooked) { CompositionTarget.Rendering -= OnFrame; _frameHooked = false; }
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    public void SetHint(string text) => StatusText.Text = text;
    public void PushLevel(float level) => _target = level;

    public void SetLanguage(string code)
    {
        if (string.IsNullOrEmpty(code)) { LangChip.Visibility = Visibility.Collapsed; return; }
        LangText.Text = code.ToUpperInvariant();
        LangChip.Visibility = Visibility.Visible;
    }

    public void SetPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // text revisions: keep what is already on screen up to the first difference, then type the rest
        int common = 0, max = Math.Min(Math.Min(_shownLen, _fullText.Length), text.Length);
        while (common < max && _fullText[common] == text[common]) common++;
        _shownLen = Math.Min(_shownLen, common);
        _fullText = text;
    }

    // ---------- per-frame animation ----------

    private static readonly LinearGradientBrush FadeMask = MakeFade();

    private static LinearGradientBrush MakeFade()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        b.GradientStops.Add(new GradientStop(Colors.Black, 0.4));
        b.Freeze();
        return b;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastFrame, 0.001, 0.05);
        _lastFrame = now;
        double k = 1 - Math.Exp(-dt * 11); // exponential smoothing, frame-rate independent

        // typewriter: reveal faster when far behind, always at least a couple of characters per frame
        if (_shownLen < _fullText.Length)
        {
            int remaining = _fullText.Length - _shownLen;
            _shownLen += Math.Max(1, (int)Math.Ceiling(remaining * (1 - Math.Exp(-dt * 7))));
            _shownLen = Math.Min(_shownLen, _fullText.Length);
        }
        string shown = _fullText.Substring(0, _shownLen);

        if (shown.Length > 0)
        {
            PreviewBox.Visibility = Visibility.Visible;
            if (Preview.Text != shown) Preview.Text = shown;

            double maxH = CollapsedHeight;
            if (_expanded) maxH = Math.Max(CollapsedHeight, SystemParameters.WorkArea.Height - 220);

            Preview.Measure(new Size(PreviewBox.Width, double.PositiveInfinity));
            double h = Preview.DesiredSize.Height;
            double targetBox = Math.Min(h, maxH);
            double targetTop = targetBox - h;

            _curBox += (targetBox - _curBox) * k;
            _curTop += (targetTop - _curTop) * k;
            if (Math.Abs(targetBox - _curBox) < 0.3) _curBox = targetBox;
            if (Math.Abs(targetTop - _curTop) < 0.3) _curTop = targetTop;

            PreviewBox.Height = Math.Max(0, _curBox);
            Canvas.SetTop(Preview, _curTop);
            PreviewBox.OpacityMask = _curTop < -0.5 ? FadeMask : null;
        }

        if (_introStart >= 0) ApplyIntro((now - _introStart) / IntroSeconds);

        ApplyWindowRect(force: false);
    }

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>p = 0..1: first the dot rises from the bottom edge, overlapping with it growing into the full card.</summary>
    private void ApplyIntro(double p)
    {
        if (p >= 1)
        {
            _introStart = -1;
            Pill.Width = 560; Pill.Height = double.NaN; Pill.CornerRadius = new CornerRadius(28);
            CardContent.Opacity = 1; Lift.Y = 0;
            return;
        }
        p = Math.Max(0, p);
        double rise = EaseOut(Math.Min(1, p / 0.5));
        double grow = EaseInOut(Math.Clamp((p - 0.3) / 0.7, 0, 1));
        double w = DotSize + (560 - DotSize) * grow;
        double natural = _naturalCardHeight + (PreviewBox.Visibility == Visibility.Visible ? PreviewBox.Height + PreviewGap : 0);
        double h = DotSize + (natural - DotSize) * grow;
        Pill.Width = w;
        Pill.Height = h;
        Pill.CornerRadius = new CornerRadius(Math.Min(28, h / 2));
        Lift.Y = (1 - rise) * RiseDip;
        CardContent.Opacity = Math.Clamp((grow - 0.55) / 0.45, 0, 1);
    }

    /// <summary>One SetWindowPos for size and position, bottom-anchored in physical pixels.</summary>
    private void ApplyWindowRect(bool force)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || _anchorBottom == 0) return;

        double hDip = _restDip + (PreviewBox.Visibility == Visibility.Visible ? PreviewBox.Height + PreviewGap : 0);
        int w = (int)Math.Round(WindowWidthDip * _scale);
        int h = (int)Math.Round(hDip * _scale);
        int x = _anchorCenterX - w / 2;
        int y = _anchorBottom - h;
        if (!force && x == _lastX && y == _lastY && w == _lastW && h == _lastH) return;
        _lastX = x; _lastY = y; _lastW = w; _lastH = h;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static double ScaleOf(IntPtr monitor)
    {
        try { if (GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 && dx > 0) return dx / 96.0; } catch { }
        return 1.0;
    }

    // ---------- hover / click expansion ----------
    // The overlay is always click-through and never activatable, so "focus follows mouse" can not
    // steal the active window. Hover/click on the card is detected by polling the cursor instead.

    private bool CursorOverCard()
    {
        if (!GetCursorPos(out var c)) return false;
        var tl = Pill.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        return c.X >= tl.X && c.X <= tl.X + Pill.ActualWidth * dpi.DpiScaleX
            && c.Y >= tl.Y && c.Y <= tl.Y + Pill.ActualHeight * dpi.DpiScaleY;
    }

    private void PollCursor()
    {
        var mode = AppSettings.Current.FullTextMode;
        bool want = _expanded;
        if (mode == "off") want = false;
        else if (mode == "hover") want = CursorOverCard() && _fullText.Length > 0;
        else // click: toggles
        {
            bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (CursorOverCard() && down && !_wasDown && _fullText.Length > 0) want = !_expanded;
            _wasDown = down;
        }
        _expanded = want;
    }

    // ---------- waveform ----------

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
