using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MeyertecPrint;

/// <summary>Отрисовка одной физической этикетки без рамок редактора.</summary>
public static class LabelPrintRenderer
{
    public static Bitmap Render(LabelLayout layout, PrintSettings settings, float dpiX, float dpiY,
        bool validateForPrint = false, bool showMedia = false)
    {
        var plan = LabelRasterPlan.Create(layout, settings ?? new PrintSettings(), dpiX, dpiY);
        var bitmap = new Bitmap(plan.Width, plan.Height, PixelFormat.Format32bppArgb);
        try
        {
            bitmap.SetResolution(dpiX, dpiY);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            foreach (var element in plan.Elements)
                DrawContent(g, element.Content, element.Bounds, dpiY, validateForPrint);
            if (plan.Cutout is { Width: > 0, Height: > 0 } cutout)
                DrawCutout(g, cutout, dpiX / dpiY, showMedia);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawCutout(Graphics graphics, Rectangle bounds, float horizontalScale, bool showMedia)
    {
        var curveHeight = Math.Min(2f * bounds.Width / horizontalScale, bounds.Height);
        var curveWidth = curveHeight * horizontalScale;
        var top = bounds.Top + (bounds.Height - curveHeight) / 2;
        var middle = bounds.Left + curveWidth / 2;
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, top, curveWidth, curveHeight, 90, 180);
        path.AddLine(middle, top, bounds.Right, top);
        path.AddLine(bounds.Right, top, bounds.Right, top + curveHeight);
        path.CloseFigure();

        using var fill = new SolidBrush(!showMedia ? Color.White :
            SystemInformation.HighContrast ? SystemColors.WindowText : Theme.Canvas);
        graphics.FillPath(fill, path);
    }

    /// <summary>Рисует готовую этикетку в точках устройства с учётом аппаратного поля.</summary>
    public static void DrawToDevice(Graphics graphics, Bitmap bitmap, float hardMarginX, float hardMarginY)
    {
        var state = graphics.Save();
        try
        {
            graphics.PageUnit = GraphicsUnit.Pixel;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            // HardMargin задаётся в сотых долях дюйма.
            var x = (float)-Math.Round(hardMarginX * graphics.DpiX / 100f);
            var y = (float)-Math.Round(hardMarginY * graphics.DpiY / 100f);
            var destination = new RectangleF(x, y, bitmap.Width, bitmap.Height);
            graphics.DrawImage(bitmap, destination, new RectangleF(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
        }
        finally { graphics.Restore(state); }
    }

    private static void DrawContent(Graphics g, CellContent content, Rectangle rect, float dpiY, bool validateForPrint)
    {
        switch (content.Type)
        {
            case CellContentType.Empty:
            case CellContentType.Text or CellContentType.Qr when string.IsNullOrEmpty(content.Text):
            case CellContentType.Barcode when string.IsNullOrEmpty(content.BarcodeData):
            case CellContentType.Image when string.IsNullOrEmpty(content.ImageBase64):
                return;
        }

        var foreground = content.InvertColors ? Brushes.White : Brushes.Black;
        if (content.InvertColors) g.FillRectangle(Brushes.Black, rect);
        switch (content.Type)
        {
            case CellContentType.Text:
                DrawText(g, content, rect, dpiY, foreground, validateForPrint);
                break;
            case CellContentType.Qr:
                DrawQr(g, content.Text, rect, foreground);
                break;
            case CellContentType.Barcode:
                DrawBarcode(g, content.BarcodeSymbol, content.BarcodeData, rect, foreground);
                break;
            case CellContentType.Image:
                DrawImage(g, content.ImageBase64!, rect, content.InvertColors);
                break;
            default:
                throw new InvalidDataException(L10n.T("Print.UnsupportedContent"));
        }
    }

    private static void DrawText(Graphics g, CellContent content, Rectangle rect, float dpiY, Brush foreground, bool validateForPrint)
    {
        var size = LabelTextFit.GetRequestedPoints(content, rect.Height, dpiY);
        var style = content.Bold ? FontStyle.Bold : FontStyle.Regular;
        Font font;
        try { font = new Font(string.IsNullOrEmpty(content.FontName) ? Theme.FontName : content.FontName, size, style, GraphicsUnit.Point); }
        catch (ArgumentException) { font = new Font(Theme.FontName, size, style, GraphicsUnit.Point); }
        using (font)
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.LineLimit })
        {
            if (validateForPrint && !string.IsNullOrWhiteSpace(content.FontName) &&
                !string.Equals(font.Name, content.FontName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(string.Format(L10n.T("Print.FontMissing"), content.FontName));

            bool Fits(Font testedFont)
            {
                g.MeasureString(content.Text, testedFont, rect.Size, format, out var fitted, out _);
                return fitted >= content.Text.Length;
            }

            var fittedSize = content.AutoFitText || !content.FontSizePt.HasValue
                ? LabelTextFit.FindSize(size, points =>
                {
                    using var trial = new Font(font.FontFamily, points, font.Style, GraphicsUnit.Point);
                    return Fits(trial);
                })
                : size;
            using var adjusted = fittedSize != size ? new Font(font.FontFamily, fittedSize, font.Style, GraphicsUnit.Point) : null;
            var selected = adjusted ?? font;
            if (validateForPrint && !Fits(selected)) throw LabelTextFit.Overflow(content.Text);

            var state = g.Save();
            try { g.SetClip(rect); g.DrawString(content.Text, selected, foreground, rect, format); }
            finally { g.Restore(state); }
        }
    }

    private static void DrawQr(Graphics g, string text, Rectangle rect, Brush foreground)
    {
        var matrix = QrEncoder.Encode(text);
        const int quiet = 4;
        var modules = matrix.GetLength(0) + quiet * 2;
        var module = Math.Min(rect.Width, rect.Height) * 8 / 10 / modules;
        if (module < 1) throw new InvalidDataException(L10n.T("Print.QrTooSmall"));
        var left = rect.Left + (rect.Width - modules * module) / 2 + quiet * module;
        var top = rect.Top + (rect.Height - modules * module) / 2 + quiet * module;
        for (var row = 0; row < matrix.GetLength(0); row++)
            for (var column = 0; column < matrix.GetLength(1); column++)
                if (matrix[row, column]) g.FillRectangle(foreground, left + column * module, top + row * module, module, module);
    }

    private static void DrawBarcode(Graphics g, BarcodeSymbol symbol, string data, Rectangle rect, Brush foreground)
    {
        var modules = BarcodeEncoder.Encode(symbol, data);
        const int quiet = 12;
        var module = rect.Width * 8 / 10 / (modules.Length + quiet * 2);
        if (module < 1) throw new InvalidDataException(L10n.T("Print.BarcodeTooSmall"));
        var left = rect.Left + (rect.Width - (modules.Length + quiet * 2) * module) / 2 + quiet * module;
        var height = Math.Max(1, rect.Height * 8 / 10);
        var top = rect.Top + (rect.Height - height) / 2;
        for (var i = 0; i < modules.Length; i++)
            if (modules[i]) g.FillRectangle(foreground, left + i * module, top, module, height);
    }

    private static void DrawImage(Graphics g, string encoded, Rectangle rect, bool invert)
    {
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(encoded));
            using var image = Image.FromStream(stream);
            var scale = Math.Min(rect.Width / (double)image.Width, rect.Height / (double)image.Height);
            var width = (float)(image.Width * scale);
            var height = (float)(image.Height * scale);
            var destination = new RectangleF(rect.X + (rect.Width - width) / 2,
                rect.Y + (rect.Height - height) / 2, width, height);
            var state = g.Save();
            try
            {
                g.SetClip(rect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (!invert)
                {
                    g.DrawImage(image, destination);
                    return;
                }

                using var attributes = new ImageAttributes();
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                attributes.SetColorMatrix(new ColorMatrix(new[]
                {
                    new[] { -1f, 0, 0, 0, 0 }, new[] { 0, -1f, 0, 0, 0 }, new[] { 0, 0, -1f, 0, 0 },
                    new[] { 0, 0, 0, 1f, 0 }, new[] { 1f, 1f, 1f, 0, 1f }
                }));
                g.DrawImage(image,
                    [destination.Location, new PointF(destination.Right, destination.Top), new PointF(destination.Left, destination.Bottom)],
                    new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, attributes);
            }
            finally { g.Restore(state); }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new InvalidDataException(L10n.T("Print.InvalidImage"), ex);
        }
    }
}
