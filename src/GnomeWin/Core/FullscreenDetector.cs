using GnomeWin.Platform.Win32;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Core;

public sealed class FullscreenDetector
{
    private readonly MonitorManager _monitors;
    private readonly HashSet<IntPtr> _fullscreen = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public FullscreenDetector(MonitorManager monitors) => _monitors = monitors;

    public event Action<IntPtr, bool>? Changed;

    public bool AnyFullscreen => _fullscreen.Count > 0;
    public bool IsFullscreen(IntPtr monitor) => _fullscreen.Contains(monitor);

    public void Evaluate(IntPtr foreground)
    {
        IntPtr monitor = IntPtr.Zero;
        bool fs = false;
        if (foreground != IntPtr.Zero && IsWindowVisible(foreground))
        {
            GetWindowThreadProcessId(foreground, out uint pid);
            string cls = GetClassNameOf(foreground);
            if (pid != _ownPid && !WindowFilter.IsShellOrTrayClass(cls) && cls != "Windows.UI.Core.CoreWindow")
            {
                var mon = _monitors.FromWindow(foreground);
                monitor = mon.Handle;
                GetWindowRect(foreground, out RECT r);
                fs = r.Covers(mon.Bounds) && !IsZoomed(foreground);
                if (!fs && SHQueryUserNotificationState(out int state) == 0)
                    fs = state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
            }
        }

        foreach (var m in _fullscreen.ToList())
        {
            if (m == monitor && fs) continue;
            _fullscreen.Remove(m);
            Changed?.Invoke(m, false);
        }
        if (fs && monitor != IntPtr.Zero && _fullscreen.Add(monitor)) Changed?.Invoke(monitor, true);
    }

    public void Hint(IntPtr monitor, bool fullscreen)
    {
        if (fullscreen ? _fullscreen.Add(monitor) : _fullscreen.Remove(monitor)) Changed?.Invoke(monitor, fullscreen);
    }
}
