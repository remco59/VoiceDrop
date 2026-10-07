using System;
using System.Runtime.InteropServices;
using System.Windows;
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
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - ActualWidth) / 2;
        Top = wa.Bottom - ActualHeight - 8;
    }

    public void ShowListening()
    {
        _processing = false;
        Preview.Text = "";
        Preview.Visibility = Visibility.Collapsed;
        LangChip.Visibility = Visibility.Collapsed;
        StatusText.Text = "Listening";
        Array.Clear(_levels);
        _anim.Start();
        if (!IsVisible) Show();
        Reposition();
    }

    public void ShowTranscribing()
    {
        _processing = true;
        StatusText.Text = "Transcribing";
    }

    public void HideOverlay()
    {
        _anim.Stop();
        Hide();
    }

    public void PushLevel(float level) => _target = level;

    public void SetPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        const int max = 220;
        Preview.Text = text.Length > max ? "…" + text[^max..] : text;
        Preview.Visibility = Visibility.Visible;
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
