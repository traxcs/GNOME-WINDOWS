using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Settings;
using GnomeWin.Services.Wallpaper;
using GnomeWin.Shell.ApplicationGrid;
using GnomeWin.Shell.Search;
using GnomeWin.Shell.WorkspaceView;
using GnomeWin.UI;
using GnomeWin.UI.Components;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace GnomeWin.Shell.Overview;

public sealed class OverviewWindow : ShellWindow
{
    private readonly Image _bgSharp = new() { Stretch = Stretch.UniformToFill };
    private readonly Image _bgBlur = new() { Stretch = Stretch.UniformToFill, Opacity = 0 };
    private readonly WpfRectangle _bgSolid = new();
    private readonly WpfRectangle _bgDim = new() { Opacity = 0 };
    private readonly Grid _content = new();
    private readonly StackPanel _top = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly TranslateTransform _topSlide = new();
    private readonly TranslateTransform _dockSlide = new();
    private OverviewBackground _bgMode;
    private RECT _physical;

    public MonitorInfo Monitor { get; private set; }
    public bool IsPrimarySurface { get; }
    public Canvas PreviewCanvas { get; } = new() { Background = Brushes.Transparent };
    public TextBox SearchBox { get; } = new() { Width = 380 };
    public WorkspaceStrip Strip { get; } = new();
    public Grid MainArea { get; } = new();
    public AppGridView AppGrid { get; } = new() { Visibility = Visibility.Collapsed };
    public SearchResultsView Results { get; } = new() { Visibility = Visibility.Collapsed };
    public Border DockHost { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };

    public event Action? BackgroundClicked;

    public OverviewWindow(MonitorInfo monitor, bool primary) : base(noActivate: !primary, transparent: false)
    {
        Monitor = monitor;
        IsPrimarySurface = primary;
        PreferSoftwareRendering = false; // full-screen animations: GPU rendering
        Title = "GnomeWin Overview";
        Background = Brushes.Black;

        _bgSolid.SetResourceReference(Shape_Fill, "Brush.OverviewSolid");
        _bgDim.SetResourceReference(Shape_Fill, "Brush.OverviewDim");
        RenderOptions.SetBitmapScalingMode(_bgBlur, BitmapScalingMode.Linear);

        var root = new Grid();
        root.Children.Add(_bgSolid);
        root.Children.Add(_bgSharp);
        root.Children.Add(_bgBlur);
        root.Children.Add(_bgDim);
        root.Children.Add(PreviewCanvas);

        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_top, 1);
        Grid.SetColumn(MainArea, 1);
        if (primary)
        {
            SearchBox.SetResourceReference(StyleProperty, "SearchBox");
            SearchBox.Tag = Loc.T("Search");
            SearchBox.HorizontalAlignment = HorizontalAlignment.Center;
            SearchBox.Margin = new Thickness(0, 18, 0, 0);
            _top.Children.Add(SearchBox);
            _top.Children.Add(Strip);
            _top.RenderTransform = _topSlide;
            _content.Children.Add(_top);

            MainArea.Children.Add(AppGrid);
            MainArea.Children.Add(Results);
            Grid.SetRow(MainArea, 1);
            _content.Children.Add(MainArea);

            DockHost.RenderTransform = _dockSlide;
            Grid.SetRow(DockHost, 2);
            _content.Children.Add(DockHost);
        }
        else
        {
            Grid.SetRow(MainArea, 1);
            _content.Children.Add(MainArea);
        }
        root.Children.Add(_content);
        Content = root;

        PreviewCanvas.MouseLeftButtonUp += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, PreviewCanvas)) BackgroundClicked?.Invoke();
        };
        MainArea.Background = null;
    }

    private static readonly DependencyProperty Shape_Fill = System.Windows.Shapes.Shape.FillProperty;

    public double Scale => DpiScale;

    public void Place(MonitorInfo monitor, RECT insets)
    {
        Monitor = monitor;
        var b = monitor.Bounds;
        _physical = new RECT(b.Left + insets.Left, b.Top + insets.Top, b.Right - insets.Right, b.Bottom - insets.Bottom);
        EnsureHandle();
        PlacePhysical(_physical);
    }

    public void ConfigureDock(DockPosition? position)
    {
        if (!IsPrimarySurface) return;
        if (position == null) { DockHost.Visibility = Visibility.Collapsed; return; }
        DockHost.Visibility = Visibility.Visible;
        switch (position)
        {
            case DockPosition.Left:
            case DockPosition.Right:
                Grid.SetRow(DockHost, 0);
                Grid.SetRowSpan(DockHost, 3);
                Grid.SetColumn(DockHost, position == DockPosition.Left ? 0 : 2);
                Grid.SetColumnSpan(DockHost, 1);
                DockHost.HorizontalAlignment = HorizontalAlignment.Center;
                DockHost.VerticalAlignment = VerticalAlignment.Center;
                DockHost.Margin = position == DockPosition.Left ? new Thickness(12, 0, 0, 0) : new Thickness(0, 0, 12, 0);
                break;
            default:
                Grid.SetRow(DockHost, 2);
                Grid.SetRowSpan(DockHost, 1);
                Grid.SetColumn(DockHost, 0);
                Grid.SetColumnSpan(DockHost, 3);
                DockHost.HorizontalAlignment = HorizontalAlignment.Center;
                DockHost.VerticalAlignment = VerticalAlignment.Bottom;
                DockHost.Margin = new Thickness(0, 0, 0, 12);
                break;
        }
    }

    public RECT PhysicalBounds => _physical;

    public void SetBackground(WallpaperImages images, OverviewBackground mode)
    {
        _bgMode = mode;
        _bgSharp.Source = images.Sharp;
        _bgSharp.Stretch = images.Stretch == Stretch.None ? Stretch.None : images.Stretch;
        _bgBlur.Source = images.Blurred;
        _bgBlur.Stretch = Stretch.UniformToFill;
        _bgSolid.Fill = new SolidColorBrush(images.Background);
        double s = Math.Max(0.5, Monitor.Scale);
        var m = Monitor.Bounds;
        _bgSharp.Margin = _bgBlur.Margin = new Thickness(
            -(_physical.Left - m.Left) / s, -(_physical.Top - m.Top) / s, -(m.Right - _physical.Right) / s, -(m.Bottom - _physical.Bottom) / s);
    }

    public void SetProgress(double p)
    {
        double e = UI.Animations.Anim.EaseOutCubic(p);
        switch (_bgMode)
        {
            case OverviewBackground.BlurredWallpaper:
                _bgBlur.Opacity = e;
                _bgDim.Opacity = e;
                break;
            case OverviewBackground.Wallpaper:
                _bgBlur.Opacity = 0;
                _bgDim.Opacity = e * 0.8;
                break;
            default:
                _bgBlur.Opacity = 0;
                _bgSharp.Opacity = 1 - e;
                _bgDim.Opacity = 0;
                break;
        }
        if (_bgMode != OverviewBackground.Solid) _bgSharp.Opacity = 1;
        _top.Opacity = e;
        _topSlide.Y = (1 - e) * -30;
        DockHost.Opacity = e;
        if (Grid.GetRow(DockHost) == 2) { _dockSlide.X = 0; _dockSlide.Y = (1 - e) * 60; }
        else { _dockSlide.Y = 0; _dockSlide.X = (1 - e) * (Grid.GetColumn(DockHost) == 0 ? -60 : 60); }
    }

    public Rect WindowsArea()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            w = _physical.Width / Math.Max(0.5, Scale);
            h = _physical.Height / Math.Max(0.5, Scale);
        }
        double left = 0, right = w, top = 24, bottom = h - 24;
        if (IsPrimarySurface && MainArea.ActualWidth > 0)
        {
            var tl = MainArea.TranslatePoint(new Point(0, 0), this);
            left = tl.X;
            right = tl.X + MainArea.ActualWidth;
            top = tl.Y + 12;
            bottom = tl.Y + MainArea.ActualHeight - 8;
        }
        double sideMargin = Math.Max(28, (right - left) * 0.035);
        return new Rect(left + sideMargin, top + WindowPreview.PadTop,
            Math.Max(10, right - left - sideMargin * 2), Math.Max(10, bottom - top - WindowPreview.PadTop - WindowPreview.LabelHeight));
    }

    public Rect ToLocal(RECT screen)
    {
        double s = Math.Max(0.5, Scale);
        return new Rect((screen.Left - _physical.Left) / s, (screen.Top - _physical.Top) / s, screen.Width / s, screen.Height / s);
    }

    public POINT ToScreen(Point local)
    {
        double s = Math.Max(0.5, Scale);
        return new POINT((int)(_physical.Left + local.X * s), (int)(_physical.Top + local.Y * s));
    }

    public void SetStripVisible(bool visible) => Strip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        WheelScrolled?.Invoke(e);
    }

    public event Action<MouseWheelEventArgs>? WheelScrolled;
}
