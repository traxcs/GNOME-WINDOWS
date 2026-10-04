using System.Diagnostics;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services;

public static class ShellLauncher
{
    public static bool Open(string target, string? args = null)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (args != null) psi.Arguments = args;
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {target}", ex);
            return false;
        }
    }
}
