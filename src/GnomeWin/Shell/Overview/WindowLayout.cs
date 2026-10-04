using System.Windows;
using GnomeWin.Services.Settings;

namespace GnomeWin.Shell.Overview;

public readonly record struct LayoutWindow(Size Size, Point Center);

public static class WindowLayout
{
    public static Rect[] Compute(IReadOnlyList<LayoutWindow> windows, Rect area, double spacingX, double spacingY,
                                 OverviewLayout mode = OverviewLayout.Natural, double maxScale = 1.0)
    {
        int n = windows.Count;
        var result = new Rect[n];
        if (n == 0 || area.Width <= 1 || area.Height <= 1) return result;
        var sizes = windows.Select(w => new Size(Math.Max(1, w.Size.Width), Math.Max(1, w.Size.Height))).ToArray();
        return mode == OverviewLayout.Grid
            ? Grid(sizes, area, spacingX, spacingY, maxScale)
            : Natural(windows, sizes, area, spacingX, spacingY, maxScale);
    }

    private static Rect[] Natural(IReadOnlyList<LayoutWindow> windows, Size[] sizes, Rect area, double sx, double sy, double maxScale)
    {
        int n = sizes.Length;
        int[] byY = Enumerable.Range(0, n).OrderBy(i => windows[i].Center.Y).ThenBy(i => windows[i].Center.X).ToArray();
        double totalWidth = sizes.Sum(s => s.Width);

        List<List<int>>? bestRows = null;
        double bestScale = -1;
        for (int rowCount = 1; rowCount <= n; rowCount++)
        {
            var rows = SplitRows(byY, sizes, totalWidth / rowCount, rowCount);
            double scale = ScaleFor(rows, sizes, area, sx, sy, maxScale);
            if (scale > bestScale * 1.02)
            {
                bestScale = scale;
                bestRows = rows;
            }
            if (rows.Count < rowCount) break;
        }

        var result = new Rect[n];
        var finalRows = bestRows!;
        double s = bestScale;
        double totalHeight = finalRows.Sum(r => r.Max(i => sizes[i].Height) * s) + sy * (finalRows.Count - 1);
        double y = area.Top + (area.Height - totalHeight) / 2;
        foreach (var row in finalRows)
        {
            var ordered = row.OrderBy(i => windows[i].Center.X).ToList();
            double rowHeight = ordered.Max(i => sizes[i].Height) * s;
            double rowWidth = ordered.Sum(i => sizes[i].Width * s) + sx * (ordered.Count - 1);
            double x = area.Left + (area.Width - rowWidth) / 2;
            foreach (int i in ordered)
            {
                double w = sizes[i].Width * s, h = sizes[i].Height * s;
                result[i] = new Rect(x, y + (rowHeight - h) / 2, w, h);
                x += w + sx;
            }
            y += rowHeight + sy;
        }
        return result;
    }

    private static List<List<int>> SplitRows(int[] order, Size[] sizes, double idealRowWidth, int rowCount)
    {
        var rows = new List<List<int>> { new() };
        double acc = 0;
        for (int k = 0; k < order.Length; k++)
        {
            int i = order[k];
            var row = rows[^1];
            int remainingWindows = order.Length - k;
            int remainingRows = rowCount - rows.Count;
            if (row.Count > 0 && remainingRows > 0 &&
                (acc + sizes[i].Width / 2 > idealRowWidth || remainingWindows <= remainingRows))
            {
                rows.Add(new List<int>());
                row = rows[^1];
                acc = 0;
            }
            row.Add(i);
            acc += sizes[i].Width;
        }
        return rows;
    }

    private static double ScaleFor(List<List<int>> rows, Size[] sizes, Rect area, double sx, double sy, double maxScale)
    {
        double scale = maxScale;
        double rawHeight = 0;
        foreach (var row in rows)
        {
            double rawWidth = row.Sum(i => sizes[i].Width);
            double available = area.Width - sx * (row.Count - 1);
            if (available <= 0) return 0;
            scale = Math.Min(scale, available / rawWidth);
            rawHeight += row.Max(i => sizes[i].Height);
        }
        double availableH = area.Height - sy * (rows.Count - 1);
        if (availableH <= 0) return 0;
        return Math.Min(scale, availableH / rawHeight);
    }

    private static Rect[] Grid(Size[] sizes, Rect area, double sx, double sy, double maxScale)
    {
        int n = sizes.Length;
        double best = -1;
        int bestCols = 1;
        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (int)Math.Ceiling(n / (double)cols);
            double cw = (area.Width - sx * (cols - 1)) / cols, ch = (area.Height - sy * (rows - 1)) / rows;
            if (cw <= 0 || ch <= 0) continue;
            double fill = sizes.Average(s => Math.Min(Math.Min(cw / s.Width, ch / s.Height), maxScale) * s.Width * s.Height / (cw * ch));
            double score = fill * cw * ch;
            if (score > best) { best = score; bestCols = cols; }
        }
        int rowsCount = (int)Math.Ceiling(n / (double)bestCols);
        double cellW = (area.Width - sx * (bestCols - 1)) / bestCols, cellH = (area.Height - sy * (rowsCount - 1)) / rowsCount;
        var result = new Rect[n];
        for (int i = 0; i < n; i++)
        {
            int r = i / bestCols, c = i % bestCols;
            int inRow = Math.Min(bestCols, n - r * bestCols);
            double rowOffset = (area.Width - (inRow * cellW + (inRow - 1) * sx)) / 2;
            double k = Math.Min(Math.Min(cellW / sizes[i].Width, cellH / sizes[i].Height), maxScale);
            double w = sizes[i].Width * k, h = sizes[i].Height * k;
            double cx = area.Left + rowOffset + c * (cellW + sx) + (cellW - w) / 2;
            double cy = area.Top + r * (cellH + sy) + (cellH - h) / 2;
            result[i] = new Rect(cx, cy, w, h);
        }
        return result;
    }
}
