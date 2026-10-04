using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GnomeWin.Core;
using GnomeWin.Platform.Dwm;
using GnomeWin.Platform.Win32;
using GnomeWin.UI.Components;

namespace GnomeWin.Shell.Dock;

public sealed class WindowPickerWindow : ShellWindow
{
    private const double ThumbWidth = 220, ThumbHeight = 140;
    private readonly List<(WindowInfo Window, Border Slot, DwmThumbnail? Thumb)> _items = new();
    private readonly Action<WindowInfo> _activate;
    private readonly Action<WindowInfo> _close;
    private bool _closing;

    public WindowPickerWindow(IReadOnlyList<WindowInfo> windows, Action<WindowInfo> activate, Action<WindowInfo> close)
        : base(noActivate: false, transparent: false)
    {
        _activate = activate;
        _close = close;
        Title = "GnomeWin Windows";
        SizeToContent = SizeToContent.WidthAndHeight;
        SetResourceReference(BackgroundProperty, "Brush.PopupBg");

        var panel = new WrapPanel { Margin = new Thickness(10), MaxWidth = (ThumbWidth + 16) * 4 };
        foreach (var w in windows)
        {
            var slot = new Border { Width = ThumbWidth, Height = ThumbHeight, Background = Brushes.Transparent };
            var title = new TextBlock
            {
                Text = w.DisplayTitle,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 6, 4, 0),
                MaxWidth = ThumbWidth,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var closeBtn = new Button
            {
                Content = "",
                Width = 24,
                Height = 24,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = UI.Loc.T("Close"),
            };
            closeBtn.SetResourceReference(StyleProperty, "RoundIconButton");
            closeBtn.Click += (_, _) => { _close(w); CloseSoon(); };

            var stack = new StackPanel();
            stack.Children.Add(closeBtn);
            stack.Children.Add(slot);
            stack.Children.Add(title);
            var card = new Border
            {
                Child = stack,
                Padding = new Thickness(8),
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "Brush.ItemHover");
            card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
            card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) { _activate(w); CloseSoon(); } };
            panel.Children.Add(card);
            _items.Add((w, slot, null));
        }
        Content = panel;
        Deactivated += (_, _) => CloseSoon();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseSoon(); };
        LayoutUpdated += (_, _) => UpdateThumbnails();
        Closed += (_, _) => { foreach (var it in _items) it.Thumb?.Dispose(); };
    }

    public void ShowAt(POINT anchor, Core.MonitorInfo monitor)
    {
        EnsureHandle();
        SetCornerPreference(Platform.Win32.NativeMethods.DWMWCP_ROUND);
        Show();
        UpdateLayout();
        var (w, h) = PhysicalSize(new Size(ActualWidth, ActualHeight));
        int x = Math.Clamp(anchor.X - w / 2, monitor.WorkArea.Left + 8, monitor.WorkArea.Right - w - 8);
        int y = Math.Clamp(anchor.Y - h - 12, monitor.WorkArea.Top + 8, monitor.WorkArea.Bottom - h - 8);
        MovePhysical(x, y);
        ForceActivate();
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            _items[i] = (it.Window, it.Slot, DwmThumbnail.Register(Handle, it.Window.Handle));
        }
        UpdateThumbnails();
    }

    private void UpdateThumbnails()
    {
        if (Handle == IntPtr.Zero) return;
        double s = DpiScale;
        foreach (var (window, slot, thumb) in _items)
        {
            if (thumb == null || !slot.IsLoaded) continue;
            var origin = slot.TranslatePoint(new Point(0, 0), this);
            var src = DwmThumbnail.VisibleSourceRect(window.Handle);
            var size = src is RECT r ? new Size(r.Width, r.Height) : new Size(Math.Max(1, window.Bounds.Width), Math.Max(1, window.Bounds.Height));
            double k = Math.Min(ThumbWidth / size.Width, ThumbHeight / size.Height);
            double tw = size.Width * k, th = size.Height * k;
            double x = origin.X + (ThumbWidth - tw) / 2, y = origin.Y + (ThumbHeight - th) / 2;
            thumb.Update(new RECT((int)(x * s), (int)(y * s), (int)((x + tw) * s), (int)((y + th) * s)), 255, true, src);
        }
    }

    private void CloseSoon()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.BeginInvoke(Close);
    }
}
