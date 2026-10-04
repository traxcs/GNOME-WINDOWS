using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.UI.Animations;

namespace GnomeWin.UI.Components;

public enum PopupAnchor { Left, Center, Right }

public class PopupPanelWindow : ShellWindow
{
    private const double ShadowMargin = 16;
    private readonly Border _card;
    private readonly TranslateTransform _slide = new();
    private bool _closing;
    private DateTime _shownAt;

    public PopupPanelWindow(double width) : base(noActivate: false, transparent: true)
    {
        SizeToContent = SizeToContent.Height;
        Width = width + ShadowMargin * 2;
        _card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            Margin = new Thickness(ShadowMargin),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.45, Color = Colors.Black },
            RenderTransform = _slide,
        };
        _card.SetResourceReference(Border.BackgroundProperty, "Brush.PopupBg");
        _card.SetResourceReference(Border.BorderBrushProperty, "Brush.PopupBorder");
        Content = _card;
        Deactivated += (_, _) =>
        {
            if ((DateTime.Now - _shownAt).TotalMilliseconds > 150) CloseAnimated();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
    }

    protected UIElement? Body { get => _card.Child; set => _card.Child = value; }

    public void ShowAnchored(int anchorX, int top, PopupAnchor anchor, MonitorInfo monitor)
    {
        EnsureHandle();
        _shownAt = DateTime.Now;
        Opacity = 0;
        Show();
        UpdateLayout();
        var (w, _) = PhysicalSize(new Size(ActualWidth, ActualHeight));
        int margin = (int)(ShadowMargin * monitor.Scale);
        int x = anchor switch
        {
            PopupAnchor.Left => anchorX - margin,
            PopupAnchor.Right => anchorX - w + margin,
            _ => anchorX - w / 2,
        };
        x = Math.Clamp(x, monitor.Bounds.Left, monitor.Bounds.Right - w);
        MovePhysical(x, top - margin + (int)(4 * monitor.Scale));
        ForceActivate();
        _slide.Y = -8;
        Anim.Fade(this, 1, 160);
        Anim.Animate(_slide, TranslateTransform.YProperty, 0, 180);
    }

    public bool IsClosing => _closing;

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        Anim.Fade(this, 0, 120, () => Close());
    }
}
