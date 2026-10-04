using System.Runtime.InteropServices;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Core;

public sealed record MonitorInfo(IntPtr Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;
    public string Key => DeviceName;
}

public sealed class MonitorManager
{
    private List<MonitorInfo> _monitors = new();
    private string _signature = string.Empty;

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;
    public MonitorInfo Primary => _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors.First();

    public event Action? MonitorsChanged;
    public event Action? WorkAreasChanged;

    public MonitorManager() => Refresh(raise: false);

    public void Refresh(bool raise = true)
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(h, ref mi))
            {
                uint dpi = GetDpiForMonitor(h, 0, out uint dx, out _) == 0 ? dx : 96;
                list.Add(new MonitorInfo(h, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0, dpi));
            }
            return true;
        }, IntPtr.Zero);

        if (list.Count == 0)
        {
            Log.Warn("No monitor enumerated; keeping previous topology.");
            return;
        }
        list = list.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();

        string geometry = string.Join("|", list.Select(m => $"{m.DeviceName}:{m.Bounds}:{m.Dpi}:{m.IsPrimary}"));
        string work = string.Join("|", list.Select(m => m.WorkArea.ToString()));
        bool topologyChanged = geometry != _signature.Split('#')[0];
        bool workChanged = !topologyChanged && _signature.EndsWith("#" + work) == false;
        _signature = geometry + "#" + work;
        _monitors = list;

        if (!raise) return;
        if (topologyChanged)
        {
            Log.Info("Monitors changed: " + geometry);
            MonitorsChanged?.Invoke();
        }
        else if (workChanged) WorkAreasChanged?.Invoke();
    }

    public MonitorInfo FromHandle(IntPtr hMonitor) =>
        _monitors.FirstOrDefault(m => m.Handle == hMonitor) ?? Primary;

    public MonitorInfo FromWindow(IntPtr hwnd) => FromHandle(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    public MonitorInfo FromPoint(int x, int y) =>
        FromHandle(MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST));

    public MonitorInfo FromCursor()
    {
        GetCursorPos(out POINT p);
        return FromPoint(p.X, p.Y);
    }
}
