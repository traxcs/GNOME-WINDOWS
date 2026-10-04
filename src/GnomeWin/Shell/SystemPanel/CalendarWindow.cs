using System.Windows;
using System.Windows.Controls;
using WpfDock = System.Windows.Controls.Dock;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GnomeWin.Services;
using GnomeWin.Services.Notifications;
using GnomeWin.UI;
using GnomeWin.UI.Components;

namespace GnomeWin.Shell.SystemPanel;

public sealed class CalendarWindow : PopupPanelWindow
{
    private readonly NotificationService _notifications;
    private readonly Action<string> _launchApp;
    private readonly StackPanel _list = new();
    private readonly UniformGrid _days = new() { Columns = 7 };
    private readonly TextBlock _monthLabel = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public CalendarWindow(NotificationService notifications, Action<string> launchApp) : base(700)
    {
        _notifications = notifications;
        _launchApp = launchApp;
        Title = "GnomeWin Calendar";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var left = new DockPanel { Margin = new Thickness(4, 4, 14, 4), MinHeight = 320 };
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, WpfDock.Bottom);
        var center = new Button { Content = Loc.T("OpenNotificationCenter"), Style = (Style)FindResource("FlatButton"), FontSize = 12 };
        center.Click += (_, _) => { CloseAnimated(); ShellLauncher.Open("ms-actioncenter:"); };
        var clear = new Button { Content = Loc.T("ClearAll"), Style = (Style)FindResource("FlatButton"), FontSize = 12 };
        clear.Click += (_, _) => { _notifications.ClearAll(); RebuildList(); };
        DockPanel.SetDock(clear, WpfDock.Right);
        footer.Children.Add(clear);
        footer.Children.Add(new Border { Child = center, HorizontalAlignment = HorizontalAlignment.Left });
        left.Children.Add(footer);
        left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 });
        grid.Children.Add(left);

        var sep = new Border();
        sep.SetResourceReference(Border.BackgroundProperty, "Brush.Separator");
        Grid.SetColumn(sep, 1);
        grid.Children.Add(sep);

        var right = new StackPanel { Margin = new Thickness(14, 4, 4, 4) };
        var today = DateTime.Today;
        right.Children.Add(new TextBlock { Text = today.ToString("dddd", Loc.Culture), FontSize = 13, Opacity = 0.75 });
        right.Children.Add(new TextBlock { Text = today.ToString("d MMMM yyyy", Loc.Culture), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var prev = NavButton("", -1);
        var next = NavButton("", +1);
        DockPanel.SetDock(prev, WpfDock.Left);
        DockPanel.SetDock(next, WpfDock.Right);
        header.Children.Add(prev);
        header.Children.Add(next);
        _monthLabel.HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(_monthLabel);
        right.Children.Add(header);
        right.Children.Add(_days);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Body = grid;
        RenderMonth();
        RebuildList();
        _notifications.Changed += RebuildList;
        Closed += (_, _) => _notifications.Changed -= RebuildList;
        _ = _notifications.RefreshAsync();
    }

    private Button NavButton(string glyph, int delta)
    {
        var b = new Button { Content = glyph, Width = 30, Height = 30, FontSize = 11 };
        b.SetResourceReference(StyleProperty, "RoundIconButton");
        b.Click += (_, _) => { _month = _month.AddMonths(delta); RenderMonth(); };
        return b;
    }

    private void RenderMonth()
    {
        _monthLabel.Text = _month.ToString("MMMM yyyy", Loc.Culture);
        _days.Children.Clear();
        var culture = Loc.Culture;
        var firstDow = culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)firstDow + i) % 7);
            _days.Children.Add(new TextBlock
            {
                Text = culture.DateTimeFormat.GetShortestDayName(dow).ToUpper(culture),
                FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6),
            });
        }
        int offset = ((int)_month.DayOfWeek - (int)firstDow + 7) % 7;
        DateTime start = _month.AddDays(-offset);
        for (int i = 0; i < 42; i++)
        {
            DateTime d = start.AddDays(i);
            bool inMonth = d.Month == _month.Month;
            bool isToday = d == DateTime.Today;
            var cell = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(1),
                Child = new TextBlock
                {
                    Text = d.Day.ToString(culture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13, Opacity = inMonth ? 1 : 0.35, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                },
            };
            if (isToday)
            {
                cell.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
                ((TextBlock)cell.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
            }
            _days.Children.Add(cell);
        }
    }

    private void RebuildList()
    {
        _list.Children.Clear();
        if (!_notifications.WindowsAccessGranted)
        {
            _list.Children.Add(new TextBlock
            {
                Text = Loc.T("NotificationsUnavailable"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12, Margin = new Thickness(6, 4, 6, 10),
            });
        }
        if (_notifications.Items.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 0), Opacity = 0.55 };
            var bell = new TextBlock { Text = "", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center };
            bell.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
            empty.Children.Add(bell);
            empty.Children.Add(new TextBlock { Text = Loc.T("NoNotifications"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            _list.Children.Add(empty);
            return;
        }
        foreach (var n in _notifications.Items) _list.Children.Add(BuildCard(n));
    }

    private UIElement BuildCard(ShellNotification n)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 8, 10), Margin = new Thickness(0, 0, 0, 8) };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.CardBg");
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Image { Source = n.Icon, Width = 28, Height = 28, Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        g.Children.Add(icon);
        var texts = new StackPanel();
        var head = new DockPanel();
        var time = new TextBlock { Text = n.TimeText, FontSize = 11, Opacity = 0.6, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(time, WpfDock.Right);
        head.Children.Add(time);
        head.Children.Add(new TextBlock { Text = n.AppName, FontSize = 11.5, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(head);
        if (!string.IsNullOrEmpty(n.Title)) texts.Children.Add(new TextBlock { Text = n.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(n.Body)) texts.Children.Add(new TextBlock { Text = n.Body, TextWrapping = TextWrapping.Wrap, Opacity = 0.85, FontSize = 12.5, MaxHeight = 80 });
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        var close = new Button { Content = "", Width = 26, Height = 26, FontSize = 9, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent };
        close.SetResourceReference(StyleProperty, "RoundIconButton");
        close.Click += (_, e) => { e.Handled = true; _notifications.Dismiss(n); };
        Grid.SetColumn(close, 2);
        g.Children.Add(close);
        card.Child = g;
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
            if (!string.IsNullOrEmpty(n.AppId)) { CloseAnimated(); _launchApp(n.AppId!); }
        };
        return card;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
