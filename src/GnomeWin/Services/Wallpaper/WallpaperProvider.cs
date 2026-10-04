using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using GnomeWin.Core;
using GnomeWin.Platform.Win32;
using GnomeWin.Services.Logging;

namespace GnomeWin.Services.Wallpaper;

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string path);
    [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out RECT rect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(int position);
    [PreserveSig] int GetPosition(out int position);
}

[ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperCom { }

public sealed record WallpaperImages(ImageSource? Sharp, ImageSource? Blurred, Stretch Stretch, Color Background);

public sealed class WallpaperProvider
{
    private readonly Dictionary<string, WallpaperImages> _cache = new();

    public void Invalidate() => _cache.Clear();

    public WallpaperImages Get(MonitorInfo monitor)
    {
        if (_cache.TryGetValue(monitor.Key, out var cached)) return cached;
        var images = Load(monitor);
        _cache[monitor.Key] = images;
        return images;
    }

    private static WallpaperImages Load(MonitorInfo monitor)
    {
        string? path = null;
        int position = 4;
        Color bg = Color.FromRgb(0x1D, 0x1D, 0x21);
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperCom();
            try
            {
                if (dw.GetMonitorDevicePathCount(out uint count) == 0)
                {
                    for (uint i = 0; i < count; i++)
                    {
                        if (dw.GetMonitorDevicePathAt(i, out string id) != 0) continue;
                        if (dw.GetMonitorRECT(id, out RECT r) == 0 && r == monitor.Bounds)
                        {
                            if (dw.GetWallpaper(id, out string p) == 0 && !string.IsNullOrEmpty(p)) path = p;
                            break;
                        }
                    }
                }
                dw.GetPosition(out position);
                if (dw.GetBackgroundColor(out uint c) == 0) bg = Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch (Exception ex) { Log.Debug("IDesktopWallpaper failed: " + ex.Message); }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            var sb = new StringBuilder(520);
            if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) && File.Exists(sb.ToString()))
                path = sb.ToString();
        }
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            string transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded)) path = transcoded;
        }

        Stretch stretch = position switch { 0 => Stretch.None, 2 => Stretch.Fill, 3 => Stretch.Uniform, _ => Stretch.UniformToFill };
        if (string.IsNullOrEmpty(path)) return new WallpaperImages(null, null, stretch, bg);

        try
        {
            BitmapSource sharp;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.DecodePixelWidth = Math.Min(monitor.Bounds.Width, 3840);
                bmp.EndInit();
                bmp.Freeze();
                sharp = bmp;
            }
            return new WallpaperImages(sharp, Blur(sharp), stretch, bg);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot load wallpaper '{path}'", ex);
            return new WallpaperImages(null, null, stretch, bg);
        }
    }

    private static ImageSource? Blur(BitmapSource source)
    {
        try
        {
            const int width = 480;
            int height = Math.Max(1, (int)(source.PixelHeight * (width / (double)source.PixelWidth)));
            var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill, Effect = new BlurEffect { Radius = 18, KernelType = KernelType.Gaussian } };
            var host = new Grid { Width = width, Height = height, ClipToBounds = true };
            image.Margin = new Thickness(-24);
            image.Width = width + 48;
            image.Height = height + 48;
            host.Children.Add(image);
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);
            rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            Log.Debug("Blur failed: " + ex.Message);
            return source;
        }
    }
}
