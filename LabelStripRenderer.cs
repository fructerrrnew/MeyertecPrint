using System.Runtime.CompilerServices;

namespace MeyertecPrint;

/// <summary>
/// Отрисовка полосы этикетки: полоса (белый фон + рамка), ячейки (рамки),
/// содержимое (текст / QR / штрихкод / изображение), выделение — фирменный
/// жёлтый. Используется холстом редактора и окном предпросмотра.
/// </summary>
public static class LabelStripRenderer
{
    // Rendered bitmaps live as long as the input string is in a project or undo history.
    private static readonly ConditionalWeakTable<string, Bitmap> QrImages = new();
    private static readonly ConditionalWeakTable<string, Bitmap> Code128Images = new();
    private static readonly ConditionalWeakTable<string, Bitmap> Ean13Images = new();
    private static readonly ConditionalWeakTable<string, Bitmap> ImportedImages = new();

    /// <summary>
    /// Рисует макет в области area (масштаб подбирается под область).
    /// Возвращает расчётные прямоугольники ячеек (для hit-test в редакторе).
    /// </summary>
    public static List<(int Row, int Col, RectangleF Rect)> Draw(
        Graphics g, LabelLayout layout, RectangleF area,
        int? selectedRow = null, int? selectedCol = null)
    {
        var rects = new List<(int, int, RectangleF)>();
        if (layout.Rows.Count == 0 || area.Width <= 24 || area.Height <= 24)
            return rects;

        LabelPrintLayout plan;
        try { plan = LabelPrintLayout.Create(layout); }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException) { return rects; }
        var margin = 12f;
        var totalWidth = plan.WidthUm;
        var totalHeight = plan.HeightUm;
        if (totalWidth <= 0 || totalHeight <= 0) return rects;
        var scale = Math.Min(
            (area.Width - 2 * margin) / (float)totalWidth,
            (area.Height - 2 * margin) / (float)totalHeight);
        var originX = area.X + margin + (area.Width - 2 * margin - totalWidth * scale) / 2;
        var originY = area.Y + margin + (area.Height - 2 * margin - totalHeight * scale) / 2;
        var stripRect = new RectangleF(
            originX + layout.Geometry.LeftUm * scale, originY + layout.Geometry.TopUm * scale,
            (totalWidth - layout.Geometry.LeftUm - layout.Geometry.RightUm) * scale,
            (totalHeight - layout.Geometry.TopUm - layout.Geometry.GapBottomUm) * scale);
        using (var stripBrush = new SolidBrush(Color.White)) g.FillRectangle(stripBrush, stripRect);
        using (var stripPen = new Pen(Theme.Dark, 2f)) g.DrawRectangle(stripPen, stripRect);
        using var cellPen = new Pen(Theme.Dark, 1f);
        using var selPen = new Pen(Theme.Yellow, 3f);
        using var selBrush = new SolidBrush(Color.FromArgb(90, Theme.Yellow));
        foreach (var position in plan.Cells)
        {
            var rect = new RectangleF(originX + position.X * scale, originY + position.Y * scale,
                position.Width * scale, position.Height * scale);
            rects.Add((position.Row, position.Column, rect));
            if (position.Row == selectedRow && position.Column == selectedCol)
            {
                g.FillRectangle(selBrush, rect);
                g.DrawRectangle(selPen, rect);
            }
            else g.DrawRectangle(cellPen, rect);
            DrawContent(g, layout.Rows[position.Row].Cells[position.Column].Content, rect);
        }
        return rects;
    }

    private static void DrawContent(Graphics g, CellContent content, RectangleF rect)
    {
        switch (content.Type)
        {
            case CellContentType.Text:
            {
                if (string.IsNullOrEmpty(content.Text)) break;
                var fontPt = content.FontSizePt ?? Math.Max(6f, rect.Height * 0.35f / 96f * 72f);
                var fontStyle = content.Bold ? FontStyle.Bold : FontStyle.Regular;
                Font font;
                try
                {
                    font = new Font(string.IsNullOrEmpty(content.FontName)
                        ? Theme.FontName : content.FontName,
                        fontPt, fontStyle, GraphicsUnit.Point);
                }
                catch
                {
                    // Шрифта нет в системе — дефолтный
                    font = new Font(Theme.FontName, fontPt, fontStyle, GraphicsUnit.Point);
                }
                var state = g.Save();
                g.SetClip(rect);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(content.Text, font, Brushes.Black,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
                    format);
                g.Restore(state);
                font.Dispose();
                break;
            }
            case CellContentType.Qr:
            {
                if (string.IsNullOrEmpty(content.Text)) break;
                var qr = GetQrBitmap(content.Text);
                DrawFitted(g, qr, rect, 0.8f);
                break;
            }
            case CellContentType.Barcode:
            {
                if (string.IsNullOrEmpty(content.BarcodeData)) break;
                var bc = GetBarcodeBitmap(content.BarcodeSymbol, content.BarcodeData);
                DrawFitted(g, bc, rect, 0.8f);
                break;
            }
            case CellContentType.Image:
            {
                var img = GetImage(content);
                if (img is not null)
                {
                    DrawFitted(g, img, rect, 1.0f);
                }
                break;
            }
        }
    }

    private static void DrawFitted(Graphics g, Bitmap source, RectangleF rect, float fill)
    {
        var targetW = rect.Width * fill;
        var targetH = rect.Height * fill;
        var ratio = Math.Min(targetW / source.Width, targetH / source.Height);
        var w = source.Width * ratio;
        var h = source.Height * ratio;
        var x = rect.X + (rect.Width - w) / 2;
        var y = rect.Y + (rect.Height - h) / 2;
        g.DrawImage(source, new RectangleF(x, y, w, h));
    }

    private static Bitmap GetQrBitmap(string data)
        => QrImages.GetValue(data, text =>
        {
            var matrix = QrEncoder.Encode(text);
            const int module = 4;
            const int quiet = 4;
            var size = matrix.GetLength(0) + quiet * 2;
            var bmp = new Bitmap(size * module, size * module);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var black = new SolidBrush(Color.Black);
            for (var r = 0; r < matrix.GetLength(0); r++)
            for (var c = 0; c < matrix.GetLength(1); c++)
            {
                if (matrix[r, c])
                    g.FillRectangle(black, (c + quiet) * module, (r + quiet) * module, module, module);
            }
            return bmp;
        });

    private static Bitmap GetBarcodeBitmap(BarcodeSymbol symbol, string data)
    {
        var cache = symbol == BarcodeSymbol.Ean13 ? Ean13Images : Code128Images;
        return cache.GetValue(data, text =>
        {
            var modules = BarcodeEncoder.Encode(symbol, text);
            const int module = 3;
            const int quiet = 10;
            const int height = 60;
            var width = (modules.Length + quiet * 2) * module;
            var bmp = new Bitmap(width, height);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            using var black = new SolidBrush(Color.Black);
            for (var i = 0; i < modules.Length; i++)
            {
                if (modules[i])
                    g.FillRectangle(black, (i + quiet) * module, 0, module, height);
            }
            return bmp;
        });
    }

    private static Bitmap? GetImage(CellContent content)
    {
        if (string.IsNullOrEmpty(content.ImageBase64)) return null;
        return ImportedImages.GetValue(content.ImageBase64, encoded =>
        {
            try
            {
                var bytes = Convert.FromBase64String(encoded);
                using var ms = new MemoryStream(bytes);
                using var original = new Bitmap(ms);
                return new Bitmap(original);
            }
            catch
            {
                return new Bitmap(1, 1);
            }
        });
    }
}
