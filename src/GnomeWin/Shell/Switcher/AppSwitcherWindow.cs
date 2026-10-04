using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.UI.Animations;
using GnomeWin.UI.Components;

namespace GnomeWin.Shell.Switcher;

public sealed class AppSwitcherWindow : ShellWindow
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _label = new() { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 700 };
    private List<(AppEntry App, List<WindowInfo> Windows)> _groups = new();
    private int _index, _windowIndex;

    public AppSwitcherWindow() : base(noActivate: true, transparent: true)
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "GnomeWin Switcher";
        var card = new Border
        {
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(16),
            Margin = new Thickness(20),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.PopupBg");
        var stack = new StackPanel();
        stack.Children.Add(_row);
        stack.Children.Add(_label);
        card.Child = stack;
        Content = card;
    }

    public bool IsSwitching { get; private set; }

    public bool Begin(IEnumerable<WindowInfo> mru, MonitorInfo monitor, bool reverse)
    {
        if (IsSwitching) { Step(reverse ? -1 : 1); return true; }
        _groups = mru.Where(w => w.App != null)
            .GroupBy(w => w.App!.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().App!, g.ToList()))
            .ToList();
        if (_groups.Count == 0) return false;
        IsSwitching = true;
        _row.Children.Clear();
        foreach (var (app, windows) in _groups)
        {
            var img = new Image { Source = app.Icon ?? windows[0].Icon, Width = 72, Height = 72 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var cell = new Border { Child = img, Padding = new Thickness(12), Margin = new Thickness(4), CornerRadius = new CornerRadius(16), Background = Brushes.Transparent };
            _row.Children.Add(cell);
        }
        _index = _groups.Count > 1 ? 1 : 0;
        if (reverse) _index = _groups.Count - 1;
        _windowIndex = 0;
        Highlight();

        EnsureHandle();
        Opacity = 0;
        Show();
        UpdateLayout();
        var (w, h) = PhysicalSize(new Size(ActualWidth, ActualHeight));
        MovePhysical(monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2, monitor.Bounds.Top + (monitor.Bounds.Height - h) / 2);
        BringToTopmost();
        Anim.Fade(this, 1, 90);
        return true;
    }

    public void Step(int delta)
    {
        if (!IsSwitching || _groups.Count == 0) return;
        _index = (_index + delta + _groups.Count) % _groups.Count;
        _windowIndex = 0;
        Highlight();
    }

    public void NextWindow()
    {
        if (!IsSwitching || _groups.Count == 0) return;
        var wins = _groups[_index].Windows;
        _windowIndex = (_windowIndex + 1) % wins.Count;
        Highlight();
    }

    private void Highlight()
    {
        for (int i = 0; i < _row.Children.Count; i++)
        {
            var b = (Border)_row.Children[i];
            if (i == _index) b.SetResourceReference(Border.BackgroundProperty, "Brush.ItemSelected");
            else b.Background = Brushes.Transparent;
        }
        var (app, windows) = _groups[_index];
        _label.Text = windows.Count > 1 ? $"{app.Name} — {windows[_windowIndex].DisplayTitle}" : app.Name;
    }

    public void Commit()
    {
        if (!IsSwitching) return;
        var target = _groups.Count > 0 ? _groups[_index].Windows[Math.Min(_windowIndex, _groups[_index].Windows.Count - 1)] : null;
        Cancel();
        if (target != null) WindowActions.Activate(target.Handle);
    }

    public void Cancel()
    {
        IsSwitching = false;
        Hide();
        _groups.Clear();
    }
}
