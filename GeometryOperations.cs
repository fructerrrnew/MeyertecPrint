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
}
