using Microsoft.Win32;
using GnomeWin.Services;
using GnomeWin.Services.Logging;

namespace GnomeWin.Platform.Startup;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GnomeWin";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, $"\"{AppPaths.ExecutablePath}\" --autostart");
            else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
            Log.Info($"Launch at startup: {enabled}");
        }
        catch (Exception ex)
        {
            Log.Error("Cannot change startup registration", ex);
        }
    }
}
