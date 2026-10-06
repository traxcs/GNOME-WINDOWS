using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GnomeWin.Platform.Win32;

namespace GnomeWin.Setup;

public static class ShortcutHelper
{
    public static void Create(string lnkPath, string target, string arguments, string description, string? workingDir = null, string? appId = null, string? iconPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetWorkingDirectory(workingDir ?? Path.GetDirectoryName(target)!);
            link.SetIconLocation(iconPath ?? target, 0);
            if (appId != null && link is IPropertyStore store)
            {
                var key = ShellApi.PKEY_AppUserModel_ID;
                var pv = new PROPVARIANT { vt = 31, p = Marshal.StringToCoTaskMemUni(appId) };
                try { store.SetValue(ref key, ref pv); store.Commit(); }
                finally { Marshal.FreeCoTaskMem(pv.p); }
            }
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
