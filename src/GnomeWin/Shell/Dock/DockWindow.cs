using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Settings;
using GnomeWin.UI.Animations;
using GnomeWin.UI.Components;

namespace GnomeWin.Shell.Dock;

public sealed class DockWindow : ShellWindow
{
    private const double FloatingMargin = 8;

    private readonly System.Windows.Controls.Grid _root = new();
    private readonly System.Windows.Controls.Border _hotEdge = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _hideDelay;
    private DockPosition _position;
    private bool _revealed = true;
    private bool _wantRevealed = true;

    public DockView View { get; }
    public MonitorInfo Monitor { get; private set; }
    public bool IsPointerInside { get; private set; }
    public RECT StripRect { get; private set; }
    public event Action<DockWindow>? PointerChanged;

    public DockWindow(MonitorInfo monitor, DockView view) : base(noActivate: true, transparent: true)
    {
        Monitor = monitor;
        View = view;
        Title = "GnomeWin Dock";
        View.RenderTransform = _slide;
        _root.Children.Add(_hotEdge);
        _root.Children.Add(View);
        Content = _root;

        _hideDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _hideDelay.Tick += (_, _) => { _hideDelay.Stop(); ApplyReveal(true); };

        View.MouseEnter += (_, _) => SetPointer(true);
        View.MouseLeave += (_, _) => SetPointer(false);
        _hotEdge.MouseEnter += (_, _) => SetPointer(true);
    }

    private void SetPointer(bool inside)
    {
        IsPointerInside = inside;
        PointerChanged?.Invoke(this);
    }

    public static double ThicknessDip(DockSettings s)
    {
        double item = s.IconSize + 12 + 2;
        double panel = item + (s.Extended ? 4 : 6) + 1;
        if (s.Extended) return panel;
        return panel + FloatingMargin + (s.HoverEffect == DockHoverEffect.Zoom ? 12 : 0);
    }

    public static int ReservedPx(DockSettings s, MonitorInfo m)
    {
        double item = s.IconSize + 12 + 2 + (s.Extended ? 4 : 6) + 1;
        return (int)Math.Ceiling((s.Extended ? item : item + FloatingMargin) * m.Scale);
    }

    public void ApplyLayout(MonitorInfo monitor, DockSettings s, int topInsetPx)
    {
        Monitor = monitor;
        _position = s.Position;
        int t = (int)Math.Ceiling(ThicknessDip(s) * monitor.Scale);
        RECT m = monitor.Bounds;
        int top = m.Top + topInsetPx;
        StripRect = s.Position switch
        {
            DockPosition.Left => new RECT(m.Left, top, m.Left + t, m.Bottom),
            DockPosition.Right => new RECT(m.Right - t, top, m.Right, m.Bottom),
            _ => new RECT(m.Left, m.Bottom - t, m.Right, m.Bottom),
        };
        bool vertical = s.Position != DockPosition.Bottom;
        View.ConfigureShape(s.Position, s.Extended);

        if (s.Extended)
        {
            View.Margin = new Thickness(0);
            View.HorizontalAlignment = vertical ? (s.Position == DockPosition.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Stretch;
            View.VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
        }
        else
        {
            View.HorizontalAlignment = s.Position switch
            {
                DockPosition.Left => HorizontalAlignment.Left,
                DockPosition.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center,
            };
            View.VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;
            View.Margin = s.Position switch
            {
                DockPosition.Left => new Thickness(FloatingMargin, 0, 0, 0),
                DockPosition.Right => new Thickness(0, 0, FloatingMargin, 0),
                _ => new Thickness(0, 0, 0, FloatingMargin),
            };
        }

        _hotEdge.HorizontalAlignment = vertical ? View.HorizontalAlignment : HorizontalAlignment.Center;
        _hotEdge.VerticalAlignment = vertical ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        double h = StripRect.Height / monitor.Scale, w = StripRect.Width / monitor.Scale;
        if (vertical) { _hotEdge.Width = 2; _hotEdge.Height = s.Extended ? h : h * 0.6; }
        else { _hotEdge.Height = 2; _hotEdge.Width = s.Extended ? w : w * 0.5; }

        EnsureHandle();
        PlacePhysical(StripRect);
        UpdateSlide(animate: false);
    }

    public void SetRevealed(bool revealed, bool immediate = false)
    {
        _wantRevealed = revealed;
        if (revealed || immediate)
        {
            _hideDelay.Stop();
            ApplyReveal(animate: !immediate);
        }
        else if (!_hideDelay.IsEnabled && _revealed) _hideDelay.Start();
    }

    private void ApplyReveal(bool animate)
    {
        if (_revealed == _wantRevealed) return;
        _revealed = _wantRevealed;
        UpdateSlide(animate);
    }

    private void UpdateSlide(bool animate)
    {
        bool vertical = _position != DockPosition.Bottom;
        double distance = (vertical ? View.ActualWidth : View.ActualHeight) + FloatingMargin + 4;
        if (distance < 20) distance = 120;
        double target = _revealed ? 0 : distance;
        if (_position == DockPosition.Left) target = -target;
        var dp = vertical ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var other = vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        _slide.BeginAnimation(other, null);
        _slide.SetValue(other, 0.0);
        if (animate) Anim.Animate(_slide, dp, target, 220, Anim.EaseInOut);
        else { _slide.BeginAnimation(dp, null); _slide.SetValue(dp, target); }
        View.IsHitTestVisible = _revealed;
    }

    public bool IsRevealed => _revealed;

    public RECT PanelScreenRect()
    {
        var panel = View.PanelElement;
        if (!panel.IsLoaded || PresentationSource.FromVisual(panel) == null) return default;
        var tl = panel.PointToScreen(new Point(0, 0));
        var br = panel.PointToScreen(new Point(panel.ActualWidth, panel.ActualHeight));
        double s = DpiScale;
        double dx = _slide.X * s, dy = _slide.Y * s;
        return new RECT((int)(tl.X - dx), (int)(tl.Y - dy), (int)(br.X - dx), (int)(br.Y - dy));
    }
}
