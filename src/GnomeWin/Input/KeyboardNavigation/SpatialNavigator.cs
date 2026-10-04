using System.Windows;

namespace GnomeWin.Input.KeyboardNavigation;

public enum NavDirection { Left, Right, Up, Down }

public static class SpatialNavigator
{
    public static int Next(IReadOnlyList<Rect> rects, int current, NavDirection direction)
    {
        if (rects.Count == 0) return -1;
        if (current < 0 || current >= rects.Count) return 0;
        Rect from = rects[current];
        Point c = Center(from);
        int best = current;
        double bestScore = double.MaxValue;
        for (int i = 0; i < rects.Count; i++)
        {
            if (i == current) continue;
            Point p = Center(rects[i]);
            double dx = p.X - c.X, dy = p.Y - c.Y;
            double primary, secondary;
            switch (direction)
            {
                case NavDirection.Left: primary = -dx; secondary = Math.Abs(dy); break;
                case NavDirection.Right: primary = dx; secondary = Math.Abs(dy); break;
                case NavDirection.Up: primary = -dy; secondary = Math.Abs(dx); break;
                default: primary = dy; secondary = Math.Abs(dx); break;
            }
            if (primary <= 1) continue;
            double score = primary + secondary * 2.5;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    private static Point Center(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
