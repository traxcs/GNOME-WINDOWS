using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.UI;
using GnomeWin.UI.Animations;

namespace GnomeWin.Shell.WorkspaceView;

public sealed class WorkspaceStrip : StackPanel
{
    private const double CellHeight = 78;
    private readonly List<Border> _cells = new();
    private int _hoverDrop = -1;
    private int _dragFrom = -1;
    private Point _pressPoint;

    public event Action<int>? SwitchRequested;
    public event Action<int>? RemoveRequested;
    public event Action? CreateRequested;
    public event Action<int, int>? ReorderRequested;

    public WorkspaceStrip()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new Thickness(0, 12, 0, 0);
    }

    public int CellCount => _cells.Count;

    public void Build(IReadOnlyList<Workspace> workspaces, MonitorInfo monitor, ImageSource? wallpaper, Stretch stretch,
                      bool canRemove, bool showAdd, bool canReorder)
    {
        Children.Clear();
        _cells.Clear();
        double aspect = monitor.Bounds.Width / (double)Math.Max(1, monitor.Bounds.Height);
        double w = CellHeight * aspect;

        foreach (var ws in workspaces)
        {
            int index = ws.Index;
            var canvas = new Canvas { Width = w, Height = CellHeight, ClipToBounds = true };
            if (wallpaper != null)
                canvas.Children.Add(new Image { Source = wallpaper, Width = w, Height = CellHeight, Stretch = stretch == Stretch.None ? Stretch.UniformToFill : stretch });
            double k = w / monitor.Bounds.Width;
            foreach (var win in ws.Windows.AsEnumerable().Reverse())
            {
                if (win.IsMinimized || win.Monitor != monitor.Handle) continue;
                var r = win.Bounds;
                var outline = new Border
                {
                    Width = Math.Max(4, r.Width * k), Height = Math.Max(4, r.Height * k),
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x30, 0x30, 0x36)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    Child = new Image { Source = win.Icon, MaxWidth = 18, MaxHeight = 18, Margin = new Thickness(2) },
                };
                Canvas.SetLeft(outline, (r.Left - monitor.Bounds.Left) * k);
                Canvas.SetTop(outline, (r.Top - monitor.Bounds.Top) * k);
                canvas.Children.Add(outline);
            }

            var remove = new Button
            {
                Content = "", Width = 20, Height = 20, FontSize = 8,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 3, 0), Visibility = Visibility.Collapsed, Focusable = false,
                ToolTip = Loc.T("RemoveWorkspace"),
            };
            remove.SetResourceReference(StyleProperty, "RoundIconButton");
            remove.Click += (_, e) => { e.Handled = true; RemoveRequested?.Invoke(index); };

            var content = new Grid();
            content.Children.Add(canvas);
            if (canRemove && workspaces.Count > 1) content.Children.Add(remove);

            var cell = new Border
            {
                Child = content,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(3),
                BorderBrush = Brushes.Transparent,
                Margin = new Thickness(5, 0, 5, 0),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Tag = index,
                ToolTip = Loc.F("Workspace", index + 1),
            };
            if (ws.IsCurrent) cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            cell.MouseEnter += (_, _) => { if (canRemove && workspaces.Count > 1) remove.Visibility = Visibility.Visible; if (!ws.IsCurrent) cell.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)); };
            cell.MouseLeave += (_, _) => { remove.Visibility = Visibility.Collapsed; if (!ws.IsCurrent) cell.BorderBrush = Brushes.Transparent; };
            cell.MouseLeftButtonDown += (_, e) => { _dragFrom = index; _pressPoint = e.GetPosition(this); cell.CaptureMouse(); e.Handled = true; };
            cell.MouseMove += (_, e) => OnCellDrag(cell, e, canReorder);
            cell.MouseLeftButtonUp += (_, e) => OnCellUp(cell, e, index);
            _cells.Add(cell);
            Children.Add(cell);
        }

        if (showAdd)
        {
            var add = new Button
            {
                Content = "", Width = 36, Height = 36, FontSize = 14, Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("NewWorkspace"), Focusable = false,
            };
            add.SetResourceReference(StyleProperty, "RoundIconButton");
            add.Click += (_, _) => CreateRequested?.Invoke();
            Children.Add(add);
        }
    }

    private void OnCellDrag(Border cell, MouseEventArgs e, bool canReorder)
    {
        if (!canReorder || _dragFrom < 0 || e.LeftButton != MouseButtonState.Pressed || !cell.IsMouseCaptured) return;
        if (Math.Abs(e.GetPosition(this).X - _pressPoint.X) < 10) return;
        int over = CellIndexAt(e.GetPosition(this));
        HighlightDrop(over != _dragFrom ? over : -1);
    }

    private void OnCellUp(Border cell, MouseButtonEventArgs e, int index)
    {
        cell.ReleaseMouseCapture();
        int over = CellIndexAt(e.GetPosition(this));
        bool moved = Math.Abs(e.GetPosition(this).X - _pressPoint.X) >= 10;
        HighlightDrop(-1);
        int from = _dragFrom;
        _dragFrom = -1;
        if (moved && over >= 0 && over != from) ReorderRequested?.Invoke(from, over);
        else if (!moved) SwitchRequested?.Invoke(index);
        e.Handled = true;
    }

    private int CellIndexAt(Point p)
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var c = _cells[i];
            var tl = c.TranslatePoint(new Point(0, 0), this);
            if (p.X >= tl.X - 5 && p.X <= tl.X + c.ActualWidth + 5) return (int)c.Tag;
        }
        return -1;
    }

    public int HitTestCell(Point pointInStrip)
    {
        foreach (var c in _cells)
        {
            var tl = c.TranslatePoint(new Point(0, 0), this);
            if (new Rect(tl, new Size(c.ActualWidth, c.ActualHeight)).Contains(pointInStrip)) return (int)c.Tag;
        }
        return -1;
    }

    public void HighlightDrop(int index)
    {
        if (index == _hoverDrop) return;
        _hoverDrop = index;
        foreach (var c in _cells)
        {
            bool target = (int)c.Tag == index;
            var st = c.RenderTransform as ScaleTransform;
            if (st == null) { st = new ScaleTransform(1, 1); c.RenderTransform = st; c.RenderTransformOrigin = new Point(0.5, 0.5); }
            Anim.Animate(st, ScaleTransform.ScaleXProperty, target ? 1.08 : 1, 120);
            Anim.Animate(st, ScaleTransform.ScaleYProperty, target ? 1.08 : 1, 120);
        }
    }
}
