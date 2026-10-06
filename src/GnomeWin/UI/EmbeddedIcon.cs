using System.Windows.Media.Imaging;

namespace GnomeWin.UI;

public static class EmbeddedIcon
{
    private static readonly Dictionary<(string Name, int Size), BitmapFrame> Cache = new();

    public static BitmapFrame Load(string fileName, int size)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue((fileName, size), out var cached)) return cached;
            var decoder = new IconBitmapDecoder(
                new Uri($"pack://application:,,,/GnomeWin;component/Assets/{fileName}"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Where(f => f.PixelWidth >= size).OrderBy(f => f.PixelWidth).FirstOrDefault()
                        ?? decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            Cache[(fileName, size)] = frame;
            return frame;
        }
    }

    public static BitmapFrame Window(string fileName) => Load(fileName, 256);
}
