using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MeyertecPrint;

/// <summary>Отрисовка одной физической этикетки 60 × 30 мм без рамок редактора.</summary>
public static class LabelPrintRenderer
{
    public static Bitmap Render(LabelLayout layout, PrintSettings settings, float dpiX, float dpiY, bool validateForPrint = false)
    {
        if (!float.IsFinite(dpiX) || !float.IsFinite(dpiY) || dpiX < 72 || dpiY < 72 || dpiX > 2400 || dpiY > 2400)
            throw new InvalidDataException(L10n.T("Print.InvalidResolution"));
        var plan = LabelPrintLayout.Create(layout);
        settings ??= new PrintSettings();
        plan.EnsureFits(settings.OffsetXMm, settings.OffsetYMm, layout.MediaWidthUm, layout.MediaHeightUm);
        var width = checked((int)Math.Round(layout.MediaWidthUm * dpiX / 25_400d));
        var height = checked((int)Math.Round(layout.MediaHeightUm * dpiY / 25_400d));
        if (width < 1 || height < 1 || (long)width * height > 80_000_000)
            throw new InvalidDataException(L10n.T("Print.RasterTooLarge"));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            bitmap.SetResolution(dpiX, dpiY);
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            foreach (var position in plan.Cells)
            {
                var content = layout.Rows[position.Row].Cells[position.Column].Content;
                // Округляем координаты границ, а не ширину каждой ячейки отдельно.
                var left = Dot(position.X + settings.OffsetXMm * 1000, dpiX);
                var right = Dot(position.X + position.Width + settings.OffsetXMm * 1000, dpiX);
                var top = Dot(position.Y + settings.OffsetYMm * 1000, dpiY);
                var bottom = Dot(position.Y + position.Height + settings.OffsetYMm * 1000, dpiY);
                var rect = Rectangle.FromLTRB(left, top, right, bottom);
                if (rect.Width <= 0 || rect.Height <= 0)
                    throw new InvalidDataException(L10n.T("Print.CellTooSmall"));
                DrawContent(g, content, rect, dpiY, validateForPrint);
            }
            foreach (var item in layout.Objects ?? new List<LabelObject>())
            {
                if (item is null || item.Content is null || item.WidthUm <= 0 || item.HeightUm <= 0 ||
                    item.XUm + settings.OffsetXMm * 1000d < 0 || item.YUm + settings.OffsetYMm * 1000d < 0 ||
                    item.XUm + (double)item.WidthUm + settings.OffsetXMm * 1000d > layout.MediaWidthUm ||
                    item.YUm + (double)item.HeightUm + settings.OffsetYMm * 1000d > layout.MediaHeightUm)
                    throw new InvalidDataException(L10n.T("Print.DoesNotFit"));
                var left = Dot(item.XUm + settings.OffsetXMm * 1000, dpiX);
                var top = Dot(item.YUm + settings.OffsetYMm * 1000, dpiY);
                var right = Dot(item.XUm + (double)item.WidthUm + settings.OffsetXMm * 1000, dpiX);
                var bottom = Dot(item.YUm + (double)item.HeightUm + settings.OffsetYMm * 1000, dpiY);
                var rect = Rectangle.FromLTRB(left, top, right, bottom);
                if (rect.Width <= 0 || rect.Height <= 0)
                    throw new InvalidDataException(L10n.T("Print.CellTooSmall"));
                DrawContent(g, item.Content, rect, dpiY, validateForPrint);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
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

    private static int Dot(double um, float dpi) => checked((int)Math.Round(um * dpi / 25_400d));

    private static void DrawContent(Graphics g, CellContent content, Rectangle rect, float dpiY, bool validateForPrint)
    {
        switch (content.Type)
        {
            case CellContentType.Empty:
                return;
            case CellContentType.Text:
                DrawText(g, content, rect, dpiY, validateForPrint);
                return;
            case CellContentType.Qr:
                if (!string.IsNullOrEmpty(content.Text)) DrawQr(g, content.Text, rect);
                return;
            case CellContentType.Barcode:
                if (!string.IsNullOrEmpty(content.BarcodeData)) DrawBarcode(g, content.BarcodeSymbol, content.BarcodeData, rect);
                return;
            case CellContentType.Image:
                if (!string.IsNullOrEmpty(content.ImageBase64)) DrawImage(g, content.ImageBase64, rect);
                return;
            default:
                throw new InvalidDataException(L10n.T("Print.UnsupportedContent"));
        }
    }

    private static void DrawText(Graphics g, CellContent content, Rectangle rect, float dpiY, bool validateForPrint)
    {
        if (string.IsNullOrEmpty(content.Text)) return;
        var automaticSize = Math.Max(6f, rect.Height * 0.35f * 72f / dpiY);
        var size = content.FontSizePt ?? automaticSize;
        if (!float.IsFinite(size) || size <= 0 || size > 500)
            throw new InvalidDataException(L10n.T("Print.InvalidFont"));
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
            if (validateForPrint)
            {
                g.MeasureString(content.Text, font, rect.Size, format, out var fitted, out _);
                if (fitted < content.Text.Length)
                    throw new InvalidDataException(string.Format(L10n.T("Print.TextOverflow"), content.Text.Length > 40 ? content.Text[..40] + "…" : content.Text));
            }
            var state = g.Save();
            try { g.SetClip(rect); g.DrawString(content.Text, font, Brushes.Black, rect, format); }
            finally { g.Restore(state); }
        }
    }

    private static void DrawQr(Graphics g, string text, Rectangle rect)
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
                if (matrix[row, column]) g.FillRectangle(Brushes.Black, left + column * module, top + row * module, module, module);
    }

    private static void DrawBarcode(Graphics g, BarcodeSymbol symbol, string data, Rectangle rect)
    {
        var modules = BarcodeEncoder.Encode(symbol, data);
        const int quiet = 12;
        var module = rect.Width * 8 / 10 / (modules.Length + quiet * 2);
        if (module < 1) throw new InvalidDataException(L10n.T("Print.BarcodeTooSmall"));
        var left = rect.Left + (rect.Width - (modules.Length + quiet * 2) * module) / 2 + quiet * module;
        var height = Math.Max(1, rect.Height * 8 / 10);
        var top = rect.Top + (rect.Height - height) / 2;
        for (var i = 0; i < modules.Length; i++)
            if (modules[i]) g.FillRectangle(Brushes.Black, left + i * module, top, module, height);
    }

    private static void DrawImage(Graphics g, string encoded, Rectangle rect)
    {
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(encoded));
            using var image = Image.FromStream(stream);
            var scale = Math.Min(rect.Width / (double)image.Width, rect.Height / (double)image.Height);
            var width = (float)(image.Width * scale);
            var height = (float)(image.Height * scale);
            var destination = new RectangleF(rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2, width, height);
            var state = g.Save();
            try { g.SetClip(rect); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(image, destination); }
            finally { g.Restore(state); }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new InvalidDataException(L10n.T("Print.InvalidImage"), ex);
        }
    }
}
