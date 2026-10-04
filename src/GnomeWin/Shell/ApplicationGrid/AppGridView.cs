using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.Shell.Dock;
using GnomeWin.UI;

namespace GnomeWin.Shell.ApplicationGrid;

public sealed class AppGridView : Grid
{
    public const double TileWidth = 132, TileHeight = 132;

    private readonly ScrollViewer _scroll;
    private readonly WrapPanel _panel;
    private readonly List<(AppEntry App, Border Tile)> _tiles = new();
    private int _selected = -1;
    private Point _press;
    private AppEntry? _pressedApp;

    public event Action<AppEntry>? LaunchRequested;
    public event Action<AppEntry, FrameworkElement>? ContextRequested;

    public AppGridView()
    {
        _panel = new WrapPanel { ItemWidth = TileWidth, ItemHeight = TileHeight, HorizontalAlignment = HorizontalAlignment.Center };
        _scroll = new ScrollViewer
        {
            Content = _panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Padding = new Thickness(40, 10, 40, 10),
        };
        Children.Add(_scroll);
        SizeChanged += (_, _) => _panel.MaxWidth = Math.Max(TileWidth, Math.Floor((ActualWidth - 80) / TileWidth) * TileWidth);
    }

    public int Count => _tiles.Count;

    public void SetApps(IReadOnlyList<AppEntry> apps)
    {
        _panel.Children.Clear();
        _tiles.Clear();
        foreach (var app in apps)
        {
            var tile = BuildTile(app);
            _tiles.Add((app, tile));
            _panel.Children.Add(tile);
        }
        _selected = -1;
    }

    private Border BuildTile(AppEntry app)
    {
        var icon = new Image { Width = 64, Height = 64, Margin = new Thickness(0, 10, 0, 8) };
        icon.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(AppEntry.Icon)) { Source = app });
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var label = new TextBlock
        {
            Text = app.Name, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36, FontSize = 12.5, Margin = new Thickness(6, 0, 6, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OverviewFg");
        var stack = new StackPanel();
        stack.Children.Add(icon);
        stack.Children.Add(label);
        var tile = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = app.Name,
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
        var tile = _tiles[index].Tile;
        tile.Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        tile.BringIntoView();
    }

    public void MoveSelection(Key key)
    {
        int perRow = Math.Max(1, (int)(_panel.ActualWidth / TileWidth));
        int i = _selected < 0 ? 0 : _selected;
        if (_selected >= 0)
        {
            i += key switch
            {
                Key.Left => -1,
                Key.Right => 1,
                Key.Up => -perRow,
                Key.Down => perRow,
                Key.PageDown => perRow * 3,
                Key.PageUp => -perRow * 3,
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

    public void ScrollToTop() => _scroll.ScrollToTop();
}
