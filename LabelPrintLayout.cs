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
        if (layout.AlternateNotch is { } notch && !notch.Fits(layout.MediaWidthUm, layout.MediaHeightUm))
            throw new InvalidDataException(L10n.T("Print.InvalidNotch"));

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

internal sealed class LabelRasterPlan
{
    public int Width { get; }
    public int Height { get; }
    public System.Drawing.Rectangle? Cutout { get; }
    public IReadOnlyList<(CellContent Content, System.Drawing.Rectangle Bounds)> Elements { get; }

    private LabelRasterPlan(int width, int height, List<(CellContent, System.Drawing.Rectangle)> elements,
        System.Drawing.Rectangle? cutout)
    {
        Width = width;
        Height = height;
        Elements = elements;
        Cutout = cutout;
    }

    public static LabelRasterPlan Create(LabelLayout layout, PrintSettings settings, float dpiX, float dpiY)
    {
        if (!float.IsFinite(dpiX) || !float.IsFinite(dpiY) || dpiX is < 72 or > 2400 || dpiY is < 72 or > 2400)
            throw new InvalidDataException(L10n.T("Print.InvalidResolution"));
        var layoutPlan = LabelPrintLayout.Create(layout);
        var offsetX = settings.OffsetXMm * 1000d;
        var offsetY = settings.OffsetYMm * 1000d;
        layoutPlan.EnsureFits(settings.OffsetXMm, settings.OffsetYMm, layout.MediaWidthUm, layout.MediaHeightUm);
        var width = Dot(layout.MediaWidthUm, dpiX);
        var height = Dot(layout.MediaHeightUm, dpiY);
        if (width < 1 || height < 1 || (long)width * height > 80_000_000)
            throw new InvalidDataException(L10n.T("Print.RasterTooLarge"));

        // Round both boundaries so adjacent cells still meet on the same printer dot.
        System.Drawing.Rectangle Rectangle(long x, long y, long w, long h)
        {
            var left = Dot(x + offsetX, dpiX);
            var top = Dot(y + offsetY, dpiY);
            var right = Dot(x + (double)w + offsetX, dpiX);
            var bottom = Dot(y + (double)h + offsetY, dpiY);
            if (right <= left || bottom <= top)
                throw new InvalidDataException(L10n.T("Print.CellTooSmall"));
            return System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
        }

        var elements = new List<(CellContent, System.Drawing.Rectangle)>();
        foreach (var position in layoutPlan.Cells)
        {
            var content = layout.Rows[position.Row].Cells[position.Column].Content;
            elements.Add((content, Rectangle(position.X, position.Y, position.Width, position.Height)));
        }
        foreach (var item in layout.Objects ?? [])
        {
            if (item is null || item.Content is null || item.WidthUm <= 0 || item.HeightUm <= 0 ||
                item.XUm + offsetX < 0 || item.YUm + offsetY < 0 ||
                item.XUm + (double)item.WidthUm + offsetX > layout.MediaWidthUm ||
                item.YUm + (double)item.HeightUm + offsetY > layout.MediaHeightUm)
                throw new InvalidDataException(L10n.T("Print.DoesNotFit"));
            elements.Add((item.Content, Rectangle(item.XUm, item.YUm, item.WidthUm, item.HeightUm)));
        }
        System.Drawing.Rectangle? cutout = null;
        if (layout.ActiveNotch is { } notch)
        {
            // The recess belongs to the stock. Printer X/Y adjustments move the artwork only.
            var top = notch.CenterYUm - notch.HeightUm / 2;
            cutout = System.Drawing.Rectangle.FromLTRB(Dot(layout.MediaWidthUm - notch.DepthUm, dpiX),
                Dot(top, dpiY), width, Dot(top + notch.HeightUm, dpiY));
        }
        return new LabelRasterPlan(width, height, elements, cutout);
    }

    private static int Dot(double positionUm, float dpi) => checked((int)Math.Round(positionUm * dpi / 25_400d));
}

internal static class LabelTextFit
{
    public static float GetRequestedPoints(CellContent content, int heightDots, float dpi)
    {
        var size = content.FontSizePt ?? Math.Max(6f, heightDots * 0.35f * 72f / dpi);
        if (!float.IsFinite(size) || size <= 0 || size > 500)
            throw new InvalidDataException(L10n.T("Print.InvalidFont"));
        return size;
    }

    public static float FindSize(float maximum, Func<float, bool> fits)
    {
        if (fits(maximum)) return maximum;

        // A single printer dot still has to be able to represent part of a glyph.
        var minimum = Math.Min(1f, maximum);
        if (!fits(minimum)) return minimum;
        for (var i = 0; i < 12 && maximum - minimum > 0.025f; i++)
        {
            var middle = (minimum + maximum) / 2;
            if (fits(middle)) minimum = middle;
            else maximum = middle;
        }
        return minimum;
    }

    public static InvalidDataException Overflow(string text)
        => new(string.Format(L10n.T("Print.TextOverflow"), text.Length > 40 ? text[..40] + "…" : text));
}
