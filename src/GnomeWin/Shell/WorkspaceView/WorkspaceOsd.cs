using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GnomeWin.Core;
using GnomeWin.UI.Animations;
using GnomeWin.UI.Components;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace GnomeWin.Shell.WorkspaceView;

public sealed class WorkspaceOsd : ShellWindow
{
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _hide;

    public WorkspaceOsd() : base(noActivate: true, transparent: true)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "GnomeWin Workspace OSD";
        IsHitTestVisible = false;
        var card = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(18, 14, 18, 14), Child = _dots, Margin = new Thickness(10) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.PopupBg");
        Content = card;
        _hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _hide.Tick += (_, _) => { _hide.Stop(); Anim.Fade(this, 0, 200, Hide); };
    }

    public void ShowFor(int count, int current, MonitorInfo monitor)
    {
        _dots.Children.Clear();
        for (int i = 0; i < count; i++)
        {
            var r = new WpfRectangle { Height = 10, Width = i == current ? 40 : 10, RadiusX = 5, RadiusY = 5, Margin = new Thickness(4, 0, 4, 0), Opacity = i == current ? 1 : 0.45 };
            r.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Fg");
            _dots.Children.Add(r);
        }
        EnsureHandle();
        if (!IsVisible) { Opacity = 0; Show(); }
        UpdateLayout();
        var (w, h) = PhysicalSize(new Size(ActualWidth, ActualHeight));
        MovePhysical(monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2, monitor.Bounds.Bottom - h - (int)(140 * monitor.Scale));
        BringToTopmost();
        Anim.Fade(this, 1, 100);
        _hide.Stop();
        _hide.Start();
    }
}
