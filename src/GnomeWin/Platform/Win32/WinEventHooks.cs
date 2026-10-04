using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Win32;

public sealed class WinEventHooks : IDisposable
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    private readonly WinEventProc _proc;
    private readonly List<IntPtr> _hooks = new();

    public event Action<uint, IntPtr>? WindowEvent;

    public WinEventHooks()
    {
        _proc = Callback;
    }

    public void Install()
    {
        (uint, uint)[] ranges =
        {
            (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
            (EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND),
            (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
            (EVENT_OBJECT_CREATE, EVENT_OBJECT_HIDE),
            (EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE),
            (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
        };
        foreach (var (min, max) in ranges)
        {
            IntPtr h = SetWinEventHook(min, max, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (h == IntPtr.Zero) Log.Warn($"SetWinEventHook {min:X}-{max:X} failed");
            else _hooks.Add(h);
        }
    }

    private IntPtr _locationHook;

    public void SetLocationTracking(bool enabled)
    {
        if (enabled && _locationHook == IntPtr.Zero)
            _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        else if (!enabled && _locationHook != IntPtr.Zero)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }

    public bool IsTrackingLocation => _locationHook != IntPtr.Zero;

    public int RawCount;
    public readonly Dictionary<uint, int> Counts = new();

    private void Callback(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        RawCount++;
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF) return;
        Counts[evt] = Counts.TryGetValue(evt, out int n) ? n + 1 : 1;
        try { WindowEvent?.Invoke(evt, hwnd); }
        catch (Exception ex) { Log.Error($"WinEvent handler failed for event {evt:X}", ex); }
    }

    public void Dispose()
    {
        foreach (var h in _hooks) UnhookWinEvent(h);
        _hooks.Clear();
        SetLocationTracking(false);
    }
}
