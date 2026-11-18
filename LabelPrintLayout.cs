namespace MeyertecPrint;

/// <summary>Координаты ячеек в микронах от верхнего левого угла материала.</summary>
public readonly record struct LabelCellPosition(int Row, int Column, long X, long Y, long Width, long Height);

public sealed class LabelPrintLayout
{
    public const long PaperWidthUm = 60_000;
    public const long PaperHeightUm = 30_000;
    public IReadOnlyList<LabelCellPosition> Cells { get; }
    public long WidthUm { get; }
    public long HeightUm { get; }

    private LabelPrintLayout(List<LabelCellPosition> cells, long width, long height)
    {
        Cells = cells;
        WidthUm = width;
        HeightUm = height;
    }

    public static LabelPrintLayout Create(LabelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var geometry = layout.Geometry ?? throw new InvalidDataException(L10n.T("Print.InvalidGeometry"));
        if (layout.Rows is null || layout.Rows.Count == 0 || geometry.LabelsInRow < 1 ||
            geometry.WidthUm <= 0 || geometry.LeftUm < 0 || geometry.TopUm < 0 ||
            geometry.RightUm < 0 || geometry.GapBetweenUm < 0 || geometry.GapBottomUm < 0)
            throw new InvalidDataException(L10n.T("Print.InvalidGeometry"));

        var cells = new List<LabelCellPosition>();
        long y = geometry.TopUm, maxWidth = 0;
        for (var rowIndex = 0; rowIndex < layout.Rows.Count; rowIndex++)
        {
            var row = layout.Rows[rowIndex];
            if (row is null || row.HeightUm <= 0 || row.Cells is null || row.Cells.Count == 0)
                throw new InvalidDataException(L10n.T("Print.InvalidGeometry"));
            long consumed = 0;
            var passedBoundaries = 0;
            // При разбиении ширина в мкм округляется вниз; на каждую часть возможна потеря до 1 мкм.
            var tolerance = (long)row.Cells.Count;
            for (var column = 0; column < row.Cells.Count; column++)
            {
                var cell = row.Cells[column];
                if (cell is null || cell.WidthUm <= 0 || cell.Content is null)
                    throw new InvalidDataException(L10n.T("Print.InvalidGeometry"));
                var x = checked(geometry.LeftUm + consumed + geometry.GapBetweenUm * passedBoundaries);
                cells.Add(new LabelCellPosition(rowIndex, column, x, y, cell.WidthUm, row.HeightUm));
                consumed = checked(consumed + cell.WidthUm);

                if (geometry.GapBetweenUm == 0 || column == row.Cells.Count - 1 ||
                    passedBoundaries >= geometry.LabelsInRow - 1) continue;
                var boundary = checked(geometry.WidthUm * (passedBoundaries + 1L));
                if (Math.Abs(consumed - boundary) <= tolerance)
                    passedBoundaries++;
                else if (consumed > boundary + tolerance)
                    throw new InvalidDataException(L10n.T("Print.AmbiguousGap"));
            }
            if (geometry.GapBetweenUm > 0 && passedBoundaries != geometry.LabelsInRow - 1)
                throw new InvalidDataException(L10n.T("Print.AmbiguousGap"));
            maxWidth = Math.Max(maxWidth, checked(consumed + geometry.GapBetweenUm * passedBoundaries));
            y = checked(y + row.HeightUm);
        }
        return new LabelPrintLayout(cells,
            checked(geometry.LeftUm + maxWidth + geometry.RightUm),
            checked(y + geometry.GapBottomUm));
    }

    public static void EnsurePrintWidth(LabelLayout layout, double maxWidthMm)
    {
        if (maxWidthMm <= 0) return;
        var spans = new List<(double Left, double Right)>();
        foreach (var cell in Create(layout).Cells)
            if (layout.Rows[cell.Row].Cells[cell.Column].Content.Type != CellContentType.Empty)
                spans.Add((cell.X, cell.X + (double)cell.Width));
        foreach (var item in layout.Objects ?? new List<LabelObject>())
            if (item.Content.Type != CellContentType.Empty) spans.Add((item.XUm, item.XUm + (double)item.WidthUm));
        if (spans.Count > 0 && spans.Max(x => x.Right) - spans.Min(x => x.Left) > maxWidthMm * 1000d)
            throw new InvalidDataException(string.Format(L10n.T("Print.HeadWidth"), maxWidthMm));
    }

    /// <summary>Проверяет область с содержимым, включая пользовательское смещение.</summary>
    public void EnsureFits(double offsetXMm, double offsetYMm, long mediaWidthUm = PaperWidthUm, long mediaHeightUm = PaperHeightUm)
    {
        if (!double.IsFinite(offsetXMm) || !double.IsFinite(offsetYMm))
            throw new InvalidDataException(L10n.T("Print.InvalidGeometry"));
        if (mediaWidthUm <= 0 || mediaHeightUm <= 0 || mediaWidthUm > 300_000 || mediaHeightUm > 762_000)
            throw new InvalidDataException(L10n.T("Print.InvalidMediaSize"));
        var x = offsetXMm * 1000d;
        var y = offsetYMm * 1000d;
        if (Cells.Any(c => c.X + x < 0 || c.Y + y < 0 ||
            c.X + (double)c.Width + x > mediaWidthUm || c.Y + (double)c.Height + y > mediaHeightUm))
            throw new InvalidDataException(L10n.T("Print.DoesNotFit"));
    }
}
