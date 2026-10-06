using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GnomeWin.Core;
using GnomeWin.Shell.Dock;
using GnomeWin.UI;
using GnomeWin.UI.Animations;

namespace GnomeWin.Shell.ApplicationGrid;

public sealed class AppGridView : Grid
{
    private static readonly (int Rows, int Columns)[] GridModes = { (8, 3), (6, 4), (4, 6), (3, 8) };

    private const double IconSize = 64;
    private const double TilePadding = 12;
    private const double TileRadius = 24;
    private const double PageIndicatorSize = 10;
    private const double MaxSpacing = 36;
    private const double TileWidth = 128, TileHeight = 128;
    private const double SideMargin = 60;

    private readonly Grid _viewport = new() { ClipToBounds = true };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal };
    private readonly TranslateTransform _slide = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) };
    private readonly List<(AppEntry App, Border Tile)> _tiles = new();

    private IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    private (int Rows, int Columns) _mode = (3, 8);
    private int _page, _pageCount = 1, _selected = -1;
    private double _pageWidth;
    private Point _press;
    private AppEntry? _pressedApp;

    public event Action<AppEntry>? LaunchRequested;
    public event Action<AppEntry, FrameworkElement>? ContextRequested;

    public AppGridView()
    {
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _strip.RenderTransform = _slide;
        _viewport.Children.Add(_strip);
        Children.Add(_viewport);
        SetRow(_dots, 1);
        Children.Add(_dots);

        SizeChanged += (_, _) => Rebuild();
        MouseWheel += (_, e) => { ShowPage(_page + (e.Delta > 0 ? -1 : 1)); e.Handled = true; };
    }

    public int Count => _tiles.Count;
    public int PerPage => _mode.Rows * _mode.Columns;

    public void SetApps(IReadOnlyList<AppEntry> apps)
    {
        _apps = apps;
        _selected = -1;
        _page = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        double w = ActualWidth - SideMargin * 2, h = ActualHeight - 40;
        if (w <= 0 || h <= 0) return;
        var mode = GridModes.OrderBy(m => Math.Abs((double)m.Columns / m.Rows - w / h)).First();
        int perPage = mode.Rows * mode.Columns;
        int pages = Math.Max(1, (int)Math.Ceiling(_apps.Count / (double)perPage));
        bool sameLayout = mode == _mode && pages == _pageCount && Math.Abs(w - _pageWidth) < 0.5 && _tiles.Count == _apps.Count;
        if (sameLayout) return;

        _mode = mode;
        _pageCount = pages;
        _pageWidth = w;
        _tiles.Clear();
        _strip.Children.Clear();

        double cellW = Math.Min(w / mode.Columns, TileWidth + MaxSpacing);
        double cellH = Math.Min(h / mode.Rows, TileHeight + MaxSpacing);
        for (int p = 0; p < pages; p++)
        {
            var grid = new UniformGrid
            {
                Rows = mode.Rows, Columns = mode.Columns,
                Width = cellW * mode.Columns, Height = cellH * mode.Rows,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            for (int i = p * perPage; i < Math.Min((p + 1) * perPage, _apps.Count); i++)
            {
                var tile = BuildTile(_apps[i], cellW, cellH);
                _tiles.Add((_apps[i], tile));
                grid.Children.Add(tile);
            }
            _strip.Children.Add(new Grid { Children = { grid }, Width = w, Height = h, Margin = new Thickness(SideMargin, 0, SideMargin, 0) });
        }
        BuildDots();
        ShowPage(Math.Min(_page, pages - 1), animate: false);
    }

    private void BuildDots()
    {
        _dots.Children.Clear();
        if (_pageCount < 2) return;
        for (int i = 0; i < _pageCount; i++)
        {
            int index = i;
            var dot = new Ellipse
            {
                Width = PageIndicatorSize, Height = PageIndicatorSize,
                Margin = new Thickness(5, 0, 5, 0),
                Cursor = Cursors.Hand,
                Opacity = i == _page ? 1 : 0.35,
            };
            dot.SetResourceReference(Shape.FillProperty, "Brush.OverviewFg");
            dot.MouseLeftButtonUp += (_, _) => ShowPage(index);
            _dots.Children.Add(dot);
        }
    }

    public void ShowPage(int page, bool animate = true)
    {
        page = Math.Clamp(page, 0, _pageCount - 1);
        _page = page;
        double to = -page * (_pageWidth + SideMargin * 2);
        if (animate) Anim.Animate(_slide, TranslateTransform.XProperty, to, 250, Anim.EaseInOut);
        else { _slide.BeginAnimation(TranslateTransform.XProperty, null); _slide.X = to; }
        for (int i = 0; i < _dots.Children.Count; i++)
            ((UIElement)_dots.Children[i]).Opacity = i == page ? 1 : 0.35;
    }

    private Border BuildTile(AppEntry app, double cellW, double cellH)
    {
        var icon = new Image { Width = IconSize, Height = IconSize, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(AppEntry.Icon)) { Source = app });
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = app.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, FontSize = 12.5,
            Margin = new Thickness(0, 6, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var tile = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(TileRadius),
            Padding = new Thickness(TilePadding),
            Margin = new Thickness(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = Math.Max(80, cellW - 8),
            MaxHeight = Math.Max(80, cellH - 8),
        };
        tile.MouseEnter += (_, _) => { if (!ReferenceEquals(SelectedApp, app)) tile.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)); };
        tile.MouseLeave += (_, _) => { if (!ReferenceEquals(SelectedApp, app)) tile.Background = Brushes.Transparent; };
        tile.MouseLeftButtonDown += (_, e) => { _pressedApp = app; _press = e.GetPosition(this); };
        tile.MouseMove += (_, e) =>
        {
            if (_pressedApp != app || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(this) - _press;
            if (Math.Abs(d.X) < 10 && Math.Abs(d.Y) < 10) return;
            _pressedApp = null;
            DragDrop.DoDragDrop(tile, new DataObject(DockView.DragFormat, app.Id), DragDropEffects.Move | DragDropEffects.Copy);
        };
        tile.MouseLeftButtonUp += (_, _) => { if (_pressedApp == app) LaunchRequested?.Invoke(app); _pressedApp = null; };
        tile.MouseRightButtonUp += (_, e) => { ContextRequested?.Invoke(app, tile); e.Handled = true; };
        return tile;
    }

    public AppEntry? SelectedApp => _selected >= 0 && _selected < _tiles.Count ? _tiles[_selected].App : null;

    public void Select(int index)
    {
        if (_tiles.Count == 0) return;
        index = Math.Clamp(index, 0, _tiles.Count - 1);
        if (_selected >= 0 && _selected < _tiles.Count) _tiles[_selected].Tile.Background = Brushes.Transparent;
        _selected = index;
        _tiles[index].Tile.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        ShowPage(index / PerPage);
    }

    public void MoveSelection(Key key)
    {
        int columns = _mode.Columns;
        int i = _selected < 0 ? 0 : _selected;
        if (_selected >= 0)
        {
            i += key switch
            {
                Key.Left => -1,
                Key.Right => 1,
                Key.Up => -columns,
                Key.Down => columns,
                Key.PageDown => PerPage,
                Key.PageUp => -PerPage,
                _ => 0,
            };
            if (key == Key.Home) i = 0;
            if (key == Key.End) i = _tiles.Count - 1;
        }
        Select(i);
    }

    public void ActivateSelected()
    {
        if (SelectedApp is AppEntry app) LaunchRequested?.Invoke(app);
    }

    public void ScrollToTop() => ShowPage(0, animate: false);
}
