using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GnomeWin.Shell.Dock;

public partial class DockView : UserControl
{
    public const string DragFormat = "GnomeWin.AppId";

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(DockView), new PropertyMetadata(Orientation.Horizontal));
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(DockView), new PropertyMetadata(48.0,
            (d, e) => ((DockView)d).ItemSize = (double)e.NewValue + 12));
    public static readonly DependencyProperty ItemSizeProperty =
        DependencyProperty.Register(nameof(ItemSize), typeof(double), typeof(DockView), new PropertyMetadata(52.0));
    public static readonly DependencyProperty IndicatorSideProperty =
        DependencyProperty.Register(nameof(IndicatorSide), typeof(string), typeof(DockView), new PropertyMetadata("Bottom"));
    public static readonly DependencyProperty ZoomOnHoverProperty =
        DependencyProperty.Register(nameof(ZoomOnHover), typeof(bool), typeof(DockView), new PropertyMetadata(true));
    public static readonly DependencyProperty DockBackgroundProperty =
        DependencyProperty.Register(nameof(DockBackground), typeof(Brush), typeof(DockView), new PropertyMetadata(Brushes.Transparent));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double ItemSize { get => (double)GetValue(ItemSizeProperty); set => SetValue(ItemSizeProperty, value); }
    public string IndicatorSide { get => (string)GetValue(IndicatorSideProperty); set => SetValue(IndicatorSideProperty, value); }
    public bool ShowAppsVisible { get => ShowApps.Visibility == Visibility.Visible; set => ShowApps.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

    public event Action? ShowAppsClicked;

    public void ConfigureShape(GnomeWin.Services.Settings.DockPosition position, bool extended)
    {
        bool vertical = position != GnomeWin.Services.Settings.DockPosition.Bottom;
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        DockPanel.SetDock(ShowApps, vertical ? System.Windows.Controls.Dock.Bottom : System.Windows.Controls.Dock.Right);
        IndicatorSide = position.ToString();
        if (extended)
        {
            Panel.CornerRadius = new CornerRadius(0);
            Panel.BorderThickness = position switch
            {
                GnomeWin.Services.Settings.DockPosition.Left => new Thickness(0, 0, 1, 0),
                GnomeWin.Services.Settings.DockPosition.Right => new Thickness(1, 0, 0, 0),
                _ => new Thickness(0, 1, 0, 0),
            };
            Panel.Padding = vertical ? new Thickness(2, 4, 2, 4) : new Thickness(4, 2, 4, 2);
            ItemsHost.VerticalAlignment = VerticalAlignment.Top;
            ItemsHost.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
        else
        {
            Panel.CornerRadius = new CornerRadius(18);
            Panel.BorderThickness = new Thickness(1);
            Panel.Padding = new Thickness(3);
            ItemsHost.VerticalAlignment = VerticalAlignment.Stretch;
            ItemsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }
    public bool ZoomOnHover { get => (bool)GetValue(ZoomOnHoverProperty); set => SetValue(ZoomOnHoverProperty, value); }
    public Brush DockBackground { get => (Brush)GetValue(DockBackgroundProperty); set => SetValue(DockBackgroundProperty, value); }

    public event Action<DockItemViewModel, FrameworkElement>? ItemClicked;
    public event Action<DockItemViewModel, FrameworkElement>? ItemMiddleClicked;
    public event Action<DockItemViewModel, FrameworkElement>? ItemContextRequested;
    public event Action<DockItemViewModel, int>? ItemScrolled;
    public event Action<string, int>? AppDropped;

    private Point _pressPoint;
    private DockItemViewModel? _pressed;

    public DockView()
    {
        InitializeComponent();
        ItemsHost.PreviewMouseLeftButtonDown += OnPreviewDown;
        ItemsHost.PreviewMouseMove += OnPreviewMove;
        ItemsHost.PreviewMouseLeftButtonUp += OnPreviewUp;
        ItemsHost.PreviewMouseUp += OnMiddleUp;
        ItemsHost.PreviewMouseRightButtonUp += OnRightUp;
        ItemsHost.PreviewMouseWheel += OnWheel;
        DragOver += OnDragOver;
        DragLeave += (_, _) => ClearDropMarkers();
        Drop += OnDrop;
        ShowApps.ToolTip = GnomeWin.UI.Loc.T("ShowApplications");
        ShowApps.MouseEnter += (_, _) => ShowAppsHighlight.Background = (Brush)Resources["DockHover"];
        ShowApps.MouseLeave += (_, _) => ShowAppsHighlight.Background = Brushes.Transparent;
        ShowApps.MouseLeftButtonUp += (_, e) => { ShowAppsClicked?.Invoke(); e.Handled = true; };
    }

    public IEnumerable? ItemsSource { get => ItemsHost.ItemsSource; set => ItemsHost.ItemsSource = value; }

    public FrameworkElement PanelElement => Panel;

    public FrameworkElement? ContainerFor(DockItemViewModel item) =>
        ItemsHost.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;

    private static DockItemViewModel? ItemFrom(object source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.DataContext is DockItemViewModel vm) return vm;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = ItemFrom(e.OriginalSource);
        _pressPoint = e.GetPosition(this);
    }

    private void OnPreviewMove(object sender, MouseEventArgs e)
    {
        if (_pressed == null || e.LeftButton != MouseButtonState.Pressed || _pressed.IsShowApps || !_pressed.IsPinned) return;
        Vector delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;
        var item = _pressed;
        _pressed = null;
        var data = new DataObject(DragFormat, item.Id);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        ClearDropMarkers();
    }

    private void OnPreviewUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemFrom(e.OriginalSource);
        if (item != null && ReferenceEquals(item, _pressed) && ContainerFor(item) is FrameworkElement fe)
            ItemClicked?.Invoke(item, fe);
        _pressed = null;
    }

    private void OnMiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        var item = ItemFrom(e.OriginalSource);
        if (item != null && ContainerFor(item) is FrameworkElement fe) { ItemMiddleClicked?.Invoke(item, fe); e.Handled = true; }
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemFrom(e.OriginalSource);
        if (item != null && ContainerFor(item) is FrameworkElement fe) { ItemContextRequested?.Invoke(item, fe); e.Handled = true; }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var item = ItemFrom(e.OriginalSource);
        if (item != null) { ItemScrolled?.Invoke(item, e.Delta > 0 ? -1 : 1); e.Handled = true; }
    }

    private int DropIndex(DragEventArgs e, out DockItemViewModel? markerItem)
    {
        markerItem = null;
        if (ItemsHost.ItemsSource is not IEnumerable<DockItemViewModel> items) return -1;
        int pinIndex = 0;
        foreach (var it in items)
        {
            if (!it.IsPinned) break;
            if (ContainerFor(it) is FrameworkElement fe)
            {
                Point p = e.GetPosition(fe);
                bool before = Orientation == Orientation.Horizontal ? p.X < fe.ActualWidth / 2 : p.Y < fe.ActualHeight / 2;
                if (before) { markerItem = it; return pinIndex; }
            }
            pinIndex++;
        }
        markerItem = items.FirstOrDefault(i => !i.IsPinned);
        return pinIndex;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Move;
        DropIndex(e, out var marker);
        ClearDropMarkers();
        if (marker != null) marker.IsDropTarget = true;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ClearDropMarkers();
        if (e.Data.GetData(DragFormat) is not string id) return;
        int index = DropIndex(e, out _);
        AppDropped?.Invoke(id, index);
        e.Handled = true;
    }

    private void ClearDropMarkers()
    {
        if (ItemsHost.ItemsSource is IEnumerable<DockItemViewModel> items)
            foreach (var it in items) it.IsDropTarget = false;
    }
}
