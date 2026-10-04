using System.Diagnostics;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Services.Recovery;

public static class Watchdog
{
    public const string MessageWindowTitle = "GnomeWin.MessageWindow";
    private const int EmergencyHotkeyId = 0x4757;
    private const int HangRestoreSeconds = 15;
    private const int HangKillSeconds = 60;

    public static int Run(int pid)
    {
        Log.Initialize(AppPaths.Logs, "watchdog", verbose: false);
        Log.Info($"Watchdog started for pid {pid}.");

        IntPtr process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            Log.Warn("Main process not found; restoring defensively.");
            if (TaskbarController.NeedsRecovery()) TaskbarController.Restore();
            return 1;
        }

        ProcessPower.SetEfficiencyMode(true);
        uint mainThread = GetCurrentThreadId();
        bool hotkey = RegisterHotKey(IntPtr.Zero, EmergencyHotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, (uint)VK_F12);
        if (!hotkey) Log.Warn("Emergency hotkey Ctrl+Alt+Shift+F12 unavailable (already registered?)");

        var monitor = new Thread(() => MonitorLoop(process, pid, mainThread)) { IsBackground = true, Name = "Watchdog.Monitor" };
        monitor.Start();
        GC.Collect();
        MemoryTrimmer.Trim("watchdog started");

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == EmergencyHotkeyId)
            {
                Log.Warn("Emergency shortcut pressed: terminating shell and restoring Windows.");
                TerminateProcess(process, 3);
                WaitForSingleObject(process, 3000);
                RestoreAll();
                break;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (hotkey) UnregisterHotKey(IntPtr.Zero, EmergencyHotkeyId);
        CloseHandle(process);
        Log.Info("Watchdog exiting.");
        Log.Flush();
        return 0;
    }

    private static void MonitorLoop(IntPtr process, int pid, uint mainThread)
    {
        int hungSeconds = 0;
        bool restoredForHang = false;
        while (true)
        {
            uint wait = WaitForSingleObject(process, 3000);
            if (wait == 0)
            {
                if (TaskbarController.NeedsRecovery())
                {
                    Log.Warn("Shell exited without restoring the desktop: restoring now.");
                    RestoreAll();
                }
                else Log.Info("Shell exited cleanly.");
                PostThreadMessage(mainThread, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            if (IsResponsive(pid))
            {
                if (hungSeconds > 0) Log.Info($"Shell responsive again after {hungSeconds}s.");
                hungSeconds = 0;
                continue;
            }

            hungSeconds += 3;
            if (hungSeconds >= HangRestoreSeconds && !restoredForHang)
            {
                Log.Warn($"Shell not responding for {hungSeconds}s: restoring native taskbar.");
                RestoreAll();
                restoredForHang = true;
            }
            if (hungSeconds >= HangKillSeconds)
            {
                Log.Error($"Shell not responding for {hungSeconds}s: terminating it.");
                TerminateProcess(process, 2);
            }
        }
    }

    private static bool IsResponsive(int pid)
    {
        IntPtr hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, null, MessageWindowTitle)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != pid) continue;
            return SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 2000, out _) != IntPtr.Zero;
        }
        return true;
    }

    public static void RestoreAll()
    {
        TaskbarController.Restore();
    }

    public static Process? Launch()
    {
        try
        {
            var psi = new ProcessStartInfo(AppPaths.ExecutablePath, $"--watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            Log.Info($"Watchdog launched (pid {p?.Id}).");
            return p;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the watchdog", ex);
            return null;
        }
    }
}
