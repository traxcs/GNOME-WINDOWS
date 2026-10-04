using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.UI.Components;

namespace GnomeWin.Shell.SystemPanel;

public sealed class TopBarWindow : ShellWindow
{
    public const double BarHeight = 32;

    private AppBar? _appBar;

    public TopBarView View { get; } = new();
    public MonitorInfo Monitor { get; private set; }
    public event Action<TopBarWindow, bool>? FullscreenAppChanged;

    public TopBarWindow(MonitorInfo monitor) : base(noActivate: true, transparent: false)
    {
        Monitor = monitor;
        Title = "GnomeWin Top Bar";
        Content = View;
        SetResourceReference(BackgroundProperty, "Brush.PanelBg");
    }

    public int PhysicalHeight => (int)Math.Ceiling(BarHeight * Monitor.Scale);

    public void Place(MonitorInfo monitor)
    {
        Monitor = monitor;
        IntPtr h = EnsureHandle();
        RECT b = monitor.Bounds;
        PlacePhysical(new RECT(b.Left, b.Top, b.Right, b.Top + PhysicalHeight));
        if (_appBar == null)
        {
            _appBar = new AppBar(h);
            _appBar.PositionAssigned += r => PlacePhysical(r);
            _appBar.FullscreenAppChanged += fs => FullscreenAppChanged?.Invoke(this, fs);
        }
        _appBar.Register(b, PhysicalHeight);
    }

    public void ReRegister()
    {
        _appBar?.Dispose();
        _appBar = null;
        Place(Monitor);
    }

    protected override void OnClosed(EventArgs e)
    {
        _appBar?.Dispose();
        _appBar = null;
        base.OnClosed(e);
    }
}
