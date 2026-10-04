using System.Diagnostics;
using System.Runtime.InteropServices;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.Win32;

public static class PowerActions
{
    private const uint EWX_LOGOFF = 0x0, EWX_SHUTDOWN = 0x1, EWX_REBOOT = 0x2, EWX_POWEROFF = 0x8, EWX_HYBRID_SHUTDOWN = 0x00400000;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, uint len, IntPtr prev, IntPtr retLen);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out LUID luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally { NativeMethods.CloseHandle(token); }
    }

    public static void Lock() => NativeMethods.LockWorkStation();

    public static void Suspend()
    {
        EnableShutdownPrivilege();
        if (!NativeMethods.SetSuspendState(false, false, false)) Log.Warn("SetSuspendState failed");
    }

    public static void Restart() => Exit(EWX_REBOOT, "/r /t 0");
    public static void PowerOff() => Exit(EWX_POWEROFF | EWX_SHUTDOWN | EWX_HYBRID_SHUTDOWN, "/s /hybrid /t 0");
    public static void LogOut() => NativeMethods.ExitWindowsEx(EWX_LOGOFF, SHTDN_REASON_FLAG_PLANNED);

    private static void Exit(uint flags, string fallbackArgs)
    {
        EnableShutdownPrivilege();
        if (NativeMethods.ExitWindowsEx(flags, SHTDN_REASON_FLAG_PLANNED)) return;
        Log.Warn($"ExitWindowsEx failed ({Marshal.GetLastWin32Error()}), using shutdown.exe");
        try { Process.Start(new ProcessStartInfo("shutdown.exe", fallbackArgs) { UseShellExecute = false, CreateNoWindow = true })?.Dispose(); }
        catch (Exception ex) { Log.Error("shutdown.exe failed", ex); }
    }
}
