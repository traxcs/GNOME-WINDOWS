using System.Globalization;
using GnomeWin.Platform.Taskbar;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.Recovery;

public static class RecoveryService
{
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(10);
    private const int CrashLoopThreshold = 3;

    public static bool OnStartup()
    {
        bool forceSafe = false;
        try
        {
            if (File.Exists(AppPaths.SessionLockFile))
            {
                Log.Warn("Previous session did not end cleanly.");
                var history = ReadHistory();
                history.Add(DateTime.Now);
                history = history.Where(t => DateTime.Now - t < CrashWindow).ToList();
                File.WriteAllLines(AppPaths.CrashHistoryFile, history.Select(t => t.ToString("o", CultureInfo.InvariantCulture)));
                if (history.Count >= CrashLoopThreshold)
                {
                    Log.Error($"{history.Count} abnormal exits in {CrashWindow.TotalMinutes} minutes: starting in safe mode.");
                    forceSafe = true;
                }
            }
            if (TaskbarController.NeedsRecovery())
            {
                Log.Warn("Taskbar was left hidden by a previous session: restoring it.");
                TaskbarController.Restore();
            }
            File.WriteAllText(AppPaths.SessionLockFile, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Warn("Recovery bookkeeping failed", ex);
        }
        return forceSafe;
    }

    public static void OnCleanExit()
    {
        ClearSessionLock();
        try { if (File.Exists(AppPaths.CrashHistoryFile)) File.Delete(AppPaths.CrashHistoryFile); } catch { }
    }

    public static void ClearSessionLock()
    {
        try { if (File.Exists(AppPaths.SessionLockFile)) File.Delete(AppPaths.SessionLockFile); } catch { }
    }

    private static List<DateTime> ReadHistory()
    {
        try
        {
            if (!File.Exists(AppPaths.CrashHistoryFile)) return new();
            return File.ReadAllLines(AppPaths.CrashHistoryFile)
                .Select(l => DateTime.TryParse(l, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTime?)null)
                .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        }
        catch { return new(); }
    }
}
