using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GnomeWin.Services.Logging;
using static GnomeWin.Platform.Win32.NativeMethods;

namespace GnomeWin.Platform.Win32;

public static class IconHelper
{
    public static ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        IntPtr h = IntPtr.Zero;
        foreach (int type in new[] { ICON_BIG, ICON_SMALL2, ICON_SMALL })
        {
            if (SendMessageTimeout(hwnd, WM_GETICON, new IntPtr(type), IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out h) != IntPtr.Zero && h != IntPtr.Zero)
                break;
            h = IntPtr.Zero;
        }
        if (h == IntPtr.Zero) h = GetClassLongPtr(hwnd, GCLP_HICON);
        if (h == IntPtr.Zero) h = GetClassLongPtr(hwnd, GCLP_HICONSM);
        return h == IntPtr.Zero ? null : FromHIcon(h, destroy: false);
    }

    public static ImageSource? FromHIcon(IntPtr hIcon, bool destroy)
    {
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { if (destroy) DestroyIcon(hIcon); }
    }

    public static ImageSource? GetShellItemImage(IShellItem item, int size, bool iconOnly = true)
    {
        IShellItemImageFactory? factory = item as IShellItemImageFactory;
        if (factory == null) return null;
        int flags = ShellApi.SIIGBF_RESIZETOFIT | (iconOnly ? ShellApi.SIIGBF_ICONONLY : 0);
        if (factory.GetImage(new SIZE(size, size), flags, out IntPtr hbmp) != 0 || hbmp == IntPtr.Zero)
            return null;
        try { return FromHBitmapWithAlpha(hbmp); }
        finally { DeleteObject(hbmp); }
    }

    public static ImageSource? GetFileImage(string path, int size)
    {
        try
        {
            Guid iid = ShellApi.IID_IShellItem;
            if (ShellApi.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItem item) != 0 || item == null) return null;
            try { return GetShellItemImage(item, size); }
            finally { Marshal.ReleaseComObject(item); }
        }
        catch (Exception ex)
        {
            Log.Debug($"GetFileImage({path}) failed: {ex.Message}");
            return null;
        }
    }

    public static unsafe BitmapSource? FromHBitmapWithAlpha(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out BITMAP bmp) == 0) return null;
        if (bmp.bmBitsPixel != 32 || bmp.bmBits == IntPtr.Zero)
        {
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight), stride = bmp.bmWidthBytes;
        var pixels = new byte[stride * h];
        byte* src = (byte*)bmp.bmBits;
        bool hasAlpha = false;
        for (int y = 0; y < h; y++)
        {
            int srcRow = bmp.bmHeight > 0 ? h - 1 - y : y;
            Marshal.Copy((IntPtr)(src + srcRow * stride), pixels, y * stride, stride);
        }
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
