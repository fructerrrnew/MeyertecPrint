namespace MeyertecPrint;

/// <summary>Resizes a grid without discarding subdivisions when its column count is unchanged.</summary>
public static class GeometryOperations
{
    private static long[] Scale(IReadOnlyList<long> dimensions, long target)
    {
        if (dimensions.Count == 0 || dimensions.Any(x => x <= 0) || target < dimensions.Count)
            throw new InvalidDataException(L10n.T("Geometry.TooSmall"));
        var total = dimensions.Sum();
        var result = new long[dimensions.Count];
        long consumed = 0, previous = 0;
        for (var i = 0; i < dimensions.Count; i++)
        {
            consumed += dimensions[i];
            var boundary = i == dimensions.Count - 1 ? target : (long)Math.Round((double)consumed * target / total);
            result[i] = boundary - previous;
            if (result[i] <= 0) throw new InvalidDataException(L10n.T("Geometry.TooSmall"));
            previous = boundary;
        }
        return result;
    }

    public static void Resize(LabelLayout layout, bool countChanged)
    {
        var g = layout.Geometry;
        var heights = Scale(layout.Rows.Select(r => r.HeightUm).ToList(), g.HeightUm);
        for (var r = 0; r < layout.Rows.Count; r++)
        {
            var row = layout.Rows[r];
            row.HeightUm = heights[r];
            if (countChanged)
            {
                while (row.Cells.Count > g.LabelsInRow) row.Cells.RemoveAt(row.Cells.Count - 1);
                while (row.Cells.Count < g.LabelsInRow) row.Cells.Add(new LabelCell());
                foreach (var cell in row.Cells) cell.WidthUm = g.WidthUm;
            }
            else
            {
                var widths = Scale(row.Cells.Select(c => c.WidthUm).ToList(), checked(g.WidthUm * g.LabelsInRow));
                for (var c = 0; c < row.Cells.Count; c++) row.Cells[c].WidthUm = widths[c];
            }
        }
    }

    public static void ResizeNotch(LabelLayout layout, long notchHeightUm)
    {
        if (layout.AlternateNotch is not { } notch)
            throw new InvalidDataException(L10n.T("Print.InvalidNotch"));
        var resized = notch with { HeightUm = notchHeightUm };
        if (!resized.Fits(layout.MediaWidthUm, layout.MediaHeightUm))
            throw new InvalidDataException(L10n.T("Print.InvalidNotch"));
        layout.AlternateNotch = resized;
    }

    /// <summary>Changes the feed pitch while retaining the artwork's relative position.</summary>
    public static void ResizeNotchedStockPitch(LabelLayout layout, long newHeightUm, long? notchHeightUm = null)
    {
        if (layout.AlternateNotch is not { } notch || layout.MediaHeightUm <= 0 || newHeightUm is < 4_000 or > 762_000)
            throw new InvalidDataException(L10n.T("Print.InvalidMediaSize"));
        if (newHeightUm == layout.MediaHeightUm)
        {
            if (notchHeightUm is long measuredHeight) ResizeNotch(layout, measuredHeight);
            return;
        }

        long ScaleY(long value) => checked((long)Math.Round(value * (double)newHeightUm / layout.MediaHeightUm));
        var resized = layout.Clone();
        var geometry = resized.Geometry;
        var cursor = geometry.TopUm;
        geometry.TopUm = ScaleY(cursor);
        geometry.HeightUm = ScaleY(geometry.HeightUm);
        foreach (var row in resized.Rows)
        {
            var top = ScaleY(cursor);
            cursor = checked(cursor + row.HeightUm);
            row.HeightUm = ScaleY(cursor) - top;
        }
        geometry.GapBottomUm = ScaleY(checked(cursor + geometry.GapBottomUm)) - ScaleY(cursor);
        foreach (var item in resized.Objects)
        {
            var bottom = ScaleY(checked(item.YUm + item.HeightUm));
            item.YUm = ScaleY(item.YUm);
            item.HeightUm = bottom - item.YUm;
        }

        resized.MediaHeightUm = newHeightUm;
        if (notchHeightUm is long absoluteHeight)
            resized.AlternateNotch = notch with { HeightUm = absoluteHeight, CenterYUm = ScaleY(notch.CenterYUm) };
        else
        {
            var oldTop = notch.CenterYUm - notch.HeightUm / 2;
            var newTop = ScaleY(oldTop);
            var height = ScaleY(oldTop + notch.HeightUm) - newTop;
            resized.AlternateNotch = notch with { HeightUm = height, CenterYUm = newTop + height / 2 };
        }
        _ = LabelPrintLayout.Create(resized);
        if (geometry.HeightUm <= 0 || resized.Objects.Any(item => item.HeightUm <= 0))
            throw new InvalidDataException(L10n.T("Geometry.TooSmall"));

        layout.Geometry = geometry;
        layout.Rows = resized.Rows;
        layout.Objects = resized.Objects;
        layout.AlternateNotch = resized.AlternateNotch;
        layout.MediaHeightUm = newHeightUm;
    }
}
