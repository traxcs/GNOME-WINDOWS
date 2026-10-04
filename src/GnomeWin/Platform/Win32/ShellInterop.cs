using System.Runtime.InteropServices;
using System.Text;

namespace GnomeWin.Platform.Win32;


[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
    [PreserveSig] int Commit();
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem parent);
    [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
    [PreserveSig] int GetAttributes(uint mask, out uint attribs);
    [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
}

[ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumShellItems e);
}

[ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellLinkW
{
    [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr pfd, uint flags);
    [PreserveSig] int GetIDList(out IntPtr ppidl);
    [PreserveSig] int SetIDList(IntPtr pidl);
    [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    [PreserveSig] int GetHotkey(out short hotkey);
    [PreserveSig] int SetHotkey(short hotkey);
    [PreserveSig] int GetShowCmd(out int cmd);
    [PreserveSig] int SetShowCmd(int cmd);
    [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
public class CShellLink { }

public static class ShellApi
{
    public static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    public static readonly Guid IID_IEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    public static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    public static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    public static readonly Guid BHID_EnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    public static readonly Guid BHID_PropertyStore = new("0384E1A4-1523-439C-A4C8-AB911052F586");
    public static readonly Guid FOLDERID_AppsFolder = new("1E87508D-89C2-42F0-8A7E-645A0F50CA58");

    public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchCommand = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 2);
    public static readonly PROPERTYKEY PKEY_Link_TargetParsingPath = new(new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);
    public static readonly PROPERTYKEY PKEY_AppUserModel_PackageInstallPath = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 15);

    public const uint SIGDN_NORMALDISPLAY = 0x00000000;
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    public const uint SIGDN_FILESYSPATH = 0x80058000;

    public const int SIIGBF_RESIZETOFIT = 0x0;
    public const int SIIGBF_BIGGERSIZEOK = 0x1;
    public const int SIIGBF_ICONONLY = 0x4;
    public const int SIIGBF_SCALEUP = 0x100;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [DllImport("shell32.dll", PreserveSig = true)]
    public static extern int SHGetKnownFolderItem(ref Guid rfid, uint flags, IntPtr token, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    public static string? GetDisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { NativeMethods.CoTaskMemFree(p); }
    }

    public static IPropertyStore? GetPropertyStore(IShellItem item)
    {
        Guid bhid = BHID_PropertyStore, iid = IID_IPropertyStore;
        if (item.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
        try { return (IPropertyStore)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    public static string? GetStringProperty(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out PROPVARIANT pv) != 0) return null;
        try { return pv.GetString(); }
        finally { NativeMethods.PropVariantClear(ref pv); }
    }

    public static string? GetWindowAumid(IntPtr hwnd)
    {
        try
        {
            Guid iid = IID_IPropertyStore;
            if (NativeMethods.SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store) != 0 || store == null) return null;
            try { return GetStringProperty(store, PKEY_AppUserModel_ID); }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch { return null; }
    }
}
