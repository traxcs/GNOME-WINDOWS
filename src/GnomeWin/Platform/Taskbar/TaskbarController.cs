using System.Runtime.InteropServices;
using System.Text.Json;
using GnomeWin.Platform.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Taskbar;

public static class TaskbarController
{
    private sealed class State
    {
        public bool Hidden { get; set; }
        public bool OriginalAutoHide { get; set; }
        public int OwnerPid { get; set; }
        public DateTime Since { get; set; }
    }

    public static bool IsHiddenByUs { get; private set; }

    public static IReadOnlyList<IntPtr> FindTaskbars()
    {
        var list = new List<IntPtr>();
        IntPtr main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        IntPtr sec = IntPtr.Zero;
        while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(sec);
        return list;
    }

    public static bool IsTaskbarWindow(IntPtr hwnd)
    {
        string cls = GetClassNameOf(hwnd);
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static int GetAutoHideState()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt32();
    }

    private static void SetAutoHide(bool autoHide)
    {
        var abd = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = FindWindow("Shell_TrayWnd", null),
            lParam = new IntPtr(autoHide ? ABS_AUTOHIDE : ABS_ALWAYSONTOP),
        };
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    public static void Hide()
    {
        try
        {
            var state = ReadState();
            if (state == null || !state.Hidden)
            {
                bool original = (GetAutoHideState() & ABS_AUTOHIDE) != 0;
                state = new State { Hidden = true, OriginalAutoHide = original, OwnerPid = Environment.ProcessId, Since = DateTime.Now };
            }
            else
            {
                state.OwnerPid = Environment.ProcessId;
            }
            WriteState(state);
            SetAutoHide(true);
            IsHiddenByUs = true;
            RefreshHandles();
            foreach (var h in _handles) ShowWindow(h, SW_HIDE);
            HideDuringSettling();
            Log.Info($"Native taskbar hidden (original auto-hide: {state.OriginalAutoHide}).");
        }
        catch (Exception ex)
        {
            Log.Error("Hiding the taskbar failed; restoring.", ex);
            Restore();
        }
    }

    private static volatile IntPtr[] _handles = Array.Empty<IntPtr>();

    private static void RefreshHandles() => _handles = FindTaskbars().ToArray();

    public static bool IsKnownTaskbarHandle(IntPtr hwnd)
    {
        foreach (var h in _handles) if (h == hwnd) return true;
        return false;
    }

    private static void HideDuringSettling()
    {
        var t = new Thread(() =>
        {
            for (int i = 0; i < 24 && IsHiddenByUs; i++)
            {
                Thread.Sleep(250);
                EnsureHidden();
            }
        }) { IsBackground = true, Name = "GnomeWin.TaskbarSettle" };
        t.Start();
    }

    public static void EnsureHidden()
    {
        if (!IsHiddenByUs) return;
        var handles = _handles;
        if (handles.Length == 0 || handles.Any(h => !IsWindow(h))) { RefreshHandles(); handles = _handles; }
        foreach (var h in handles)
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    public static void Restore()
    {
        try
        {
            var state = ReadState();
            bool autoHide = state?.OriginalAutoHide ?? (GetAutoHideState() & ABS_AUTOHIDE) != 0;
            if (state?.Hidden == true || IsHiddenByUs) SetAutoHide(autoHide);
            foreach (var h in FindTaskbars()) ShowWindow(h, SW_SHOWNA);
            IsHiddenByUs = false;
            if (state != null) { state.Hidden = false; WriteState(state); }
            Log.Info($"Native taskbar restored (auto-hide: {autoHide}).");
        }
        catch (Exception ex)
        {
            Log.Error("Taskbar restore failed", ex);
            try { foreach (var h in FindTaskbars()) ShowWindow(h, SW_SHOWNA); } catch { }
        }
    }

    public static bool NeedsRecovery() => ReadState()?.Hidden == true;

    private static State? ReadState()
    {
        try
        {
            string f = AppPaths.TaskbarStateFile;
            return File.Exists(f) ? JsonSerializer.Deserialize<State>(File.ReadAllText(f)) : null;
        }
        catch { return null; }
    }

    private static void WriteState(State s)
    {
        try { File.WriteAllText(AppPaths.TaskbarStateFile, JsonSerializer.Serialize(s)); }
        catch (Exception ex) { Log.Warn("Cannot persist taskbar state", ex); }
    }
}
