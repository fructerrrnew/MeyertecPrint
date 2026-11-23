using System.Drawing;
using SkiaSharp;

namespace MeyertecPrint;

/// <summary>Renders the project geometry to printer dots on non-Windows systems.</summary>
internal static class LabelRaster
{
    public static SKBitmap Render(LabelLayout layout, PrintSettings settings, int dpi, bool validateForPrint = false)
    {
        var plan = LabelRasterPlan.Create(layout, settings, dpi, dpi);
        var bitmap = new SKBitmap(plan.Width, plan.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            foreach (var (content, bounds) in plan.Elements)
                DrawElement(canvas, content, bounds, dpi, validateForPrint);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawElement(SKCanvas canvas, CellContent content, Rectangle bounds, int dpi, bool validateForPrint)
    {
        switch (content.Type)
        {
            case CellContentType.Empty:
            case CellContentType.Text or CellContentType.Qr when string.IsNullOrEmpty(content.Text):
            case CellContentType.Barcode when string.IsNullOrEmpty(content.BarcodeData):
            case CellContentType.Image when string.IsNullOrEmpty(content.ImageBase64):
                return;
        }

        var area = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        canvas.Save();
        try
        {
            canvas.ClipRect(area);
            if (content.InvertColors)
            {
                canvas.DrawRect(area, paint);
                paint.Color = SKColors.White;
            }

            switch (content.Type)
            {
                case CellContentType.Text:
                    DrawText(canvas, content, area, paint, dpi, validateForPrint);
                    break;
                case CellContentType.Qr:
                    DrawQr(canvas, content.Text, bounds, paint);
                    break;
                case CellContentType.Barcode:
                    DrawBarcode(canvas, content.BarcodeSymbol, content.BarcodeData, bounds, paint);
                    break;
                case CellContentType.Image:
                    DrawImage(canvas, content.ImageBase64!, area, paint, content.InvertColors);
                    break;
                default:
                    throw new InvalidDataException(L10n.T("Print.UnsupportedContent"));
            }
        }
        finally { canvas.Restore(); }
    }

    private static void DrawText(SKCanvas canvas, CellContent content, SKRect area, SKPaint paint, int dpi, bool validateForPrint)
    {
        var family = string.IsNullOrWhiteSpace(content.FontName) ? "DejaVu Sans" : content.FontName;
        using var face = SKTypeface.FromFamilyName(family, content.Bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        if (validateForPrint && !string.IsNullOrWhiteSpace(content.FontName) &&
            !string.Equals(face?.FamilyName, content.FontName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(L10n.T("Print.FontMissing"), content.FontName));
        paint.Typeface = face ?? SKTypeface.Default;
        paint.TextAlign = SKTextAlign.Center;
        var paragraphs = content.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Split('\n');
        var lines = new List<string>();
        bool Layout(float points)
        {
            paint.TextSize = points * dpi / 72f;
            lines.Clear();
            return Wrap(paragraphs, area, paint, lines);
        }

        var requested = LabelTextFit.GetRequestedPoints(content, (int)area.Height, dpi);
        var selected = content.AutoFitText || !content.FontSizePt.HasValue ? LabelTextFit.FindSize(requested, Layout) : requested;
        if (!Layout(selected) && validateForPrint) throw LabelTextFit.Overflow(content.Text);
        if (lines.Count == 0) return;

        var metrics = paint.FontMetrics;
        var spacing = paint.FontSpacing;
        var textHeight = metrics.Descent - metrics.Ascent + (lines.Count - 1) * spacing;
        var y = area.MidY - textHeight / 2 - metrics.Ascent;
        foreach (var line in lines)
        {
            canvas.DrawText(line, area.MidX, y, paint);
            y += spacing;
        }
    }

    private static bool Wrap(string[] paragraphs, SKRect area, SKPaint paint, List<string> lines)
    {
        var metrics = paint.FontMetrics;
        var lineHeight = metrics.Descent - metrics.Ascent;
        var spacing = paint.FontSpacing;
        if (spacing <= 0 || lineHeight > area.Height) return false;
        var maxLines = (int)((area.Height - lineHeight) / spacing) + 1;

        foreach (var paragraph in paragraphs)
        {
            var remaining = paragraph.AsSpan();
            do
            {
                if (lines.Count >= maxLines) return false;
                var count = remaining.Length == 0 ? 0 : (int)paint.BreakText(remaining, area.Width);
                if (remaining.Length != 0 && count <= 0) return false;
                if (count < remaining.Length)
                {
                    if (char.IsHighSurrogate(remaining[count - 1])) count--;
                    if (count == 0) return false;
                    var boundary = remaining[..count].LastIndexOf(' ');
                    if (boundary > 0) count = boundary;
                }
                lines.Add(remaining[..count].ToString());
                remaining = remaining[count..].TrimStart(' ');
            }
            while (!remaining.IsEmpty);
        }
        return true;
    }

    private static void DrawQr(SKCanvas canvas, string text, Rectangle bounds, SKPaint paint)
    {
        var matrix = QrEncoder.Encode(text);
        const int quiet = 4;
        var modules = matrix.GetLength(0) + 2 * quiet;
        var module = Math.Min(bounds.Width, bounds.Height) * 8 / 10 / modules;
        if (module < 1) throw new InvalidDataException(L10n.T("Print.QrTooSmall"));
        var x = bounds.Left + (bounds.Width - modules * module) / 2 + quiet * module;
        var y = bounds.Top + (bounds.Height - modules * module) / 2 + quiet * module;
        for (var row = 0; row < matrix.GetLength(0); row++)
            for (var column = 0; column < matrix.GetLength(1); column++)
                if (matrix[row, column])
                    canvas.DrawRect(x + column * module, y + row * module, module, module, paint);
    }

    private static void DrawBarcode(SKCanvas canvas, BarcodeSymbol symbol, string data, Rectangle bounds, SKPaint paint)
    {
        var modules = BarcodeEncoder.Encode(symbol, data);
        const int quiet = 12;
        var module = bounds.Width * 8 / 10 / (modules.Length + 2 * quiet);
        if (module < 1) throw new InvalidDataException(L10n.T("Print.BarcodeTooSmall"));
        var height = Math.Max(1, bounds.Height * 8 / 10);
        var x = bounds.Left + (bounds.Width - (modules.Length + 2 * quiet) * module) / 2 + quiet * module;
        var y = bounds.Top + (bounds.Height - height) / 2;
        for (var i = 0; i < modules.Length; i++)
            if (modules[i]) canvas.DrawRect(x + i * module, y, module, height, paint);
    }

    private static void DrawImage(SKCanvas canvas, string encoded, SKRect area, SKPaint paint, bool invert)
    {
        try
        {
            using var image = SKBitmap.Decode(Convert.FromBase64String(encoded));
            if (image is null) throw new InvalidDataException(L10n.T("Print.InvalidImage"));
            var scale = Math.Min(area.Width / image.Width, area.Height / image.Height);
            var halfWidth = image.Width * scale / 2;
            var halfHeight = image.Height * scale / 2;
            using var filter = invert ? SKColorFilter.CreateColorMatrix([
                -1f, 0, 0, 0, 255,
                0, -1f, 0, 0, 255,
                0, 0, -1f, 0, 255,
                0, 0, 0, 1f, 0
            ]) : null;
            paint.ColorFilter = filter;
            paint.FilterQuality = SKFilterQuality.High;
            canvas.DrawBitmap(image, new SKRect(area.MidX - halfWidth, area.MidY - halfHeight,
                area.MidX + halfWidth, area.MidY + halfHeight), paint);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(L10n.T("Print.InvalidImage"), ex);
        }
    }
}
