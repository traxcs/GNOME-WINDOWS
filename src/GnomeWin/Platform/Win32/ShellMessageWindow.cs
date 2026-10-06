using System.Runtime.InteropServices;
using System.Windows.Interop;
using GnomeWin.Services.Logging;
using GnomeWin.Services.Recovery;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Win32;

public sealed class ShellMessageWindow : IDisposable
{
    private static readonly Guid GUID_BATTERY_PERCENTAGE_REMAINING = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    private static readonly Guid GUID_ACDC_POWER_SOURCE = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

    private readonly HwndSource _source;
    private readonly int _taskbarCreatedMsg;
    private readonly List<IntPtr> _powerNotifications = new();

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action<string?>? SettingChanged;
    public event Action? PowerStatusChanged;
    public event Action? Suspending;
    public event Action? Resumed;
    public event Action? SessionLocked;
    public event Action? SessionUnlocked;
    public event Action? SessionEnding;
    public event Action? SessionEnded;
    public event Action? SessionEndCancelled;
    public event Action<string>? CommandReceived;

    public IntPtr Handle => _source.Handle;

    public ShellMessageWindow()
    {
        var p = new HwndSourceParameters(Watchdog.MessageWindowTitle)
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

        WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION);
        foreach (var g in new[] { GUID_BATTERY_PERCENTAGE_REMAINING, GUID_ACDC_POWER_SOURCE })
        {
            Guid guid = g;
            IntPtr h = RegisterPowerSettingNotification(Handle, ref guid, 0);
            if (h != IntPtr.Zero) _powerNotifications.Add(h);
        }
    }

    private void Post(Action a) => _source.Dispatcher.BeginInvoke(a);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == _taskbarCreatedMsg)
            {
                Log.Warn("TaskbarCreated received: explorer (re)started.");
                Post(() => ExplorerRestarted?.Invoke());
                return IntPtr.Zero;
            }
            switch (msg)
            {
                case WM_DISPLAYCHANGE:
                case WM_DPICHANGED:
                    Post(() => DisplayChanged?.Invoke());
                    break;
                case WM_SETTINGCHANGE:
                    string? area = lParam != IntPtr.Zero ? Marshal.PtrToStringUni(lParam) : null;
                    if (wParam.ToInt32() == (int)SPI_SETWORKAREA) area = "WorkArea";
                    else if (wParam.ToInt32() == (int)SPI_SETDESKWALLPAPER) area = "Wallpaper";
                    Post(() => SettingChanged?.Invoke(area));
                    break;
                case WM_POWERBROADCAST:
                    int evt = wParam.ToInt32();
                    if (evt == PBT_APMSUSPEND) Post(() => Suspending?.Invoke());
                    else if (evt is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND) Post(() => Resumed?.Invoke());
                    else if (evt is PBT_APMPOWERSTATUSCHANGE or PBT_POWERSETTINGCHANGE) Post(() => PowerStatusChanged?.Invoke());
                    handled = true;
                    return new IntPtr(1);
                case WM_WTSSESSION_CHANGE:
                    if (wParam.ToInt32() == WTS_SESSION_LOCK) Post(() => SessionLocked?.Invoke());
                    else if (wParam.ToInt32() == WTS_SESSION_UNLOCK) Post(() => SessionUnlocked?.Invoke());
                    break;
                case WM_QUERYENDSESSION:
                    SessionEnding?.Invoke();
                    handled = true;
                    return new IntPtr(1);
                case WM_ENDSESSION:
                    if (wParam != IntPtr.Zero) SessionEnded?.Invoke();
                    else Post(() => SessionEndCancelled?.Invoke());
                    handled = true;
                    return IntPtr.Zero;
                case WM_COPYDATA:
                    var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                    if (cds.lpData != IntPtr.Zero && cds.cbData > 0)
                    {
                        string cmd = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2).TrimEnd('\0');
                        Log.Info($"Command from another instance: {cmd}");
                        Post(() => CommandReceived?.Invoke(cmd));
                    }
                    handled = true;
                    return new IntPtr(1);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"ShellMessageWindow message 0x{msg:X} failed", ex);
        }
        return IntPtr.Zero;
    }

    public static bool SendToRunningInstance(string command)
    {
        IntPtr hwnd = FindWindow(null, Watchdog.MessageWindowTitle);
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0) AllowSetForegroundWindow((int)pid);
        IntPtr buffer = Marshal.StringToHGlobalUni(command + "\0");
        try
        {
            var cds = new COPYDATASTRUCT { dwData = new IntPtr(1), cbData = (command.Length + 1) * 2, lpData = buffer };
            SendMessage(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        foreach (var h in _powerNotifications) UnregisterPowerSettingNotification(h);
        WTSUnRegisterSessionNotification(Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
