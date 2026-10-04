using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GnomeWin.Platform.Win32;

namespace GnomeWin.Setup;

public static class ShortcutHelper
{
    public static void Create(string lnkPath, string target, string arguments, string description, string? workingDir = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(workingDir ?? Path.GetDirectoryName(target)!);
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
