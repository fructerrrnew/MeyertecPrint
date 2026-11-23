using System.Xml;

namespace MeyertecPrint;

/// <summary>
/// Чтение и запись формата WAGO LabelDESIGNER (.wstx) — XML, размеры в мкм.
/// Структура: LabelStrip → MetaInfo / StripBlocks → StripBlock → StripRow →
/// StripCell → Content. Mapping по данным исследования (DATA_SEMANTICS):
/// Cell.Width → Geometry.WidthUm, LeftOffset первой ячейки → LeftUm,
/// RightOffset последней → RightUm, TopOffset первой строки → TopUm,
/// BottomOffset последней → GapBottomUm, Height строк → HeightUm (сумма).
/// Тег текстового содержимого в эталоне не фиксировался (видели только Empty),
/// поэтому используется собственная конвенция: TextContent/Value, QrContent/Value,
/// BarcodeContent(Symbol,Value), ImageContent(Format,Data).
/// </summary>
public static class WstxIo
{
    public const string FileExtension = ".wstx";

    /// <summary>Читает .wstx: имя + макет. Устройство (Device) не используется.</summary>
    public static (string Name, LabelLayout Layout) Read(string path)
    {
        var doc = new XmlDocument();
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 30_000_000 });
            doc.XmlResolver = null;
            doc.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        var root = doc.DocumentElement;
        if (root is null || !string.Equals(root.Name, "LabelStrip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L10n.T("Error.WstxRoot"));

        var name = string.Empty;
        var meta = root.SelectSingleNode("MetaInfo");
        if (meta?.SelectSingleNode("Name") is { } nameNode)
            name = nameNode.InnerText.Trim();

        var blocks = root.SelectNodes("StripBlocks/StripBlock");
        if (blocks is null || blocks.Count == 0) throw new InvalidDataException(L10n.T("Error.WstxBlock"));
        // Never silently discard blocks that the application cannot yet model.
        if (blocks.Count != 1) throw new InvalidDataException(L10n.T("Wstx.MultipleBlocks"));
        var block = blocks[0]!;

        var layout = new LabelLayout();
        var rows = new List<LabelRow>();
        var rowNodes = block.SelectNodes("StripRow");
        if (rowNodes is null)
            throw new InvalidDataException(L10n.T("Error.WstxRow"));
        foreach (XmlNode rowNode in rowNodes)
        {
            var row = new LabelRow
            {
                HeightUm = ReadLong(rowNode, "Height"),
            };
            var cellNodes = rowNode.SelectNodes("StripCell");
            if (cellNodes is not null)
            {
                foreach (XmlNode cellNode in cellNodes)
                {
                    var cell = new LabelCell
                    {
                        WidthUm = ReadLong(cellNode, "Width"),
                        Content = ReadContent(cellNode),
                    };
                    row.Cells.Add(cell);
                }
            }
            rows.Add(row);
        }

        if (rows.Count == 0)
            throw new InvalidDataException(L10n.T("Error.WstxRow"));

        // Геометрия из первой/последней строки и ячеек (mapping см. в комментарии класса)
        var firstRow = rows[0];
        var lastRow = rows[^1];
        layout.Geometry.TopUm = ReadLongAttr(firstRowNode(block), "TopOffset");
        layout.Geometry.GapBottomUm = ReadLongAttr(lastRowNode(block), "BottomOffset");
        if (firstRow.Cells.Count > 0)
        {
            layout.Geometry.WidthUm = firstRow.Cells[0].WidthUm;
            layout.Geometry.LabelsInRow = firstRow.Cells.Count;
            layout.Geometry.LeftUm = ReadLongAttr(firstCell(firstRowNode(block)), "LeftOffset");
            layout.Geometry.RightUm = ReadLongAttr(lastCell(lastRowNode(block)), "RightOffset");
        }
        layout.Geometry.HeightUm = rows.Sum(r => r.HeightUm);
        layout.Rows = rows;
        if (root.SelectSingleNode("MeyertecMedia") is { } media)
        {
            layout.MediaWidthUm = ReadLong(media, "Width");
            layout.MediaHeightUm = ReadLong(media, "Height");
            if (layout.MediaWidthUm <= 0 || layout.MediaHeightUm <= 0)
                throw new InvalidDataException(L10n.T("Print.InvalidMediaSize"));
        }
        var objectNodes = root.SelectNodes("MeyertecObjects/Object");
        if (objectNodes is not null)
        {
            foreach (XmlNode item in objectNodes)
            {
                var obj = new LabelObject
                {
                    XUm = ReadLong(item, "X"), YUm = ReadLong(item, "Y"),
                    WidthUm = ReadLong(item, "Width"), HeightUm = ReadLong(item, "Height"),
                    Content = ReadContent(item),
                    CsvColumn = int.TryParse(GetAttr(item, "CsvColumn"), out var column) ? column : null,
                };
                if (obj.WidthUm <= 0 || obj.HeightUm <= 0)
                    throw new InvalidDataException(L10n.T("Error.ObjectDimensions"));
                layout.Objects.Add(obj);
            }
        }
        return (name, layout);
    }

    private static XmlNode firstRowNode(XmlNode block) => block.SelectSingleNode("StripRow")!;
    private static XmlNode lastRowNode(XmlNode block) => block.SelectNodes("StripRow")![^1]!;
    private static XmlNode firstCell(XmlNode rowNode) => rowNode.SelectSingleNode("StripCell")!;
    private static XmlNode lastCell(XmlNode rowNode) => rowNode.SelectNodes("StripCell")![^1]!;

    private static long ReadLong(XmlNode node, string attr)
    {
        var value = GetAttr(node, attr);
        return long.TryParse(value, out var l) ? l : 0;
    }

    private static long ReadLongAttr(XmlNode node, string attr)
        => node is null ? 0 : ReadLong(node, attr);

    private static string GetAttr(XmlNode node, string attr)
        => node.Attributes?[attr]?.Value ?? string.Empty;

    private static CellContent ReadContent(XmlNode cellNode)
    {
        var content = new CellContent();
        var node = cellNode.SelectSingleNode("Content");
        if (node is null) return content;
        content.InvertColors = string.Equals(GetAttr(node, "MeyertecInvert"), "True", StringComparison.OrdinalIgnoreCase);

        if (node.SelectSingleNode("TextContent/Value") is { } text)
        {
            content.Type = CellContentType.Text;
            content.Text = text.InnerText;
            // TextAttributes — собственная конвенция (см. комментарий класса)
            if (node.SelectSingleNode("TextContent/TextAttributes") is { } attrs)
            {
                var font = GetAttr(attrs, "Font");
                if (!string.IsNullOrEmpty(font) &&
                    !string.Equals(GetAttr(attrs, "MeyertecDefaultFont"), "True", StringComparison.OrdinalIgnoreCase))
                    content.FontName = font;
                var size = GetAttr(attrs, "Size");
                if (int.TryParse(size, out var sizeHundredthPt))
                    content.FontSizePt = sizeHundredthPt / 100f;
                if (string.Equals(GetAttr(attrs, "MeyertecAutomaticSize"), "True", StringComparison.OrdinalIgnoreCase))
                    content.FontSizePt = null;
                content.AutoFitText = string.Equals(GetAttr(attrs, "MeyertecAutoFit"), "True", StringComparison.OrdinalIgnoreCase);
                content.Bold = string.Equals(GetAttr(attrs, "Bold"), "True",
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        else if (node.SelectSingleNode("QrContent/Value") is { } qr)
        {
            content.Type = CellContentType.Qr;
            content.Text = qr.InnerText;
        }
        else if (node.SelectSingleNode("BarcodeContent") is { } barcode)
        {
            content.Type = CellContentType.Barcode;
            content.BarcodeData = barcode.SelectSingleNode("Value")?.InnerText ?? string.Empty;
            var symbol = barcode.SelectSingleNode("Symbol")?.InnerText;
            content.BarcodeSymbol = string.Equals(symbol, "Ean13", StringComparison.OrdinalIgnoreCase)
                ? BarcodeSymbol.Ean13
                : BarcodeSymbol.Code128;
        }
        else if (node.SelectSingleNode("ImageContent") is { } image)
        {
            content.Type = CellContentType.Image;
            content.ImageBase64 = image.SelectSingleNode("Data")?.InnerText;
            content.ImageFormat = GetAttr(image, "Format");
        }
        else if (node.ChildNodes.OfType<XmlElement>().Any(element => element.Name != "Empty"))
            throw new InvalidDataException(L10n.T("Wstx.UnknownContent"));
        return content;
    }

    /// <summary>Записывает макет в .wstx (мкм).</summary>
    public static void Write(string name, LabelLayout layout, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try { WriteCore(name, layout, temp); File.Move(temp, fullPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void WriteCore(string name, LabelLayout layout, string path)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = System.Text.Encoding.UTF8 };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartElement("LabelStrip");

        writer.WriteStartElement("MetaInfo");
        writer.WriteElementString("Name", name ?? string.Empty);
        writer.WriteElementString("ModificationTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        writer.WriteEndElement();

        writer.WriteStartElement("StripBlocks");
        writer.WriteStartElement("StripBlock");
        writer.WriteAttributeString("Distance", "0");

        for (var r = 0; r < layout.Rows.Count; r++)
        {
            var row = layout.Rows[r];
            writer.WriteStartElement("StripRow");
            writer.WriteAttributeString("Height", row.HeightUm.ToString());
            if (r == 0)
                writer.WriteAttributeString("TopOffset", layout.Geometry.TopUm.ToString());
            if (r == layout.Rows.Count - 1)
                writer.WriteAttributeString("BottomOffset", layout.Geometry.GapBottomUm.ToString());

            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                writer.WriteStartElement("StripCell");
                writer.WriteAttributeString("Width", cell.WidthUm.ToString());
                if (c == 0)
                    writer.WriteAttributeString("LeftOffset", layout.Geometry.LeftUm.ToString());
                if (c == row.Cells.Count - 1)
                    writer.WriteAttributeString("RightOffset", layout.Geometry.RightUm.ToString());

                WriteContent(writer, cell.Content);
                writer.WriteEndElement(); // StripCell
            }

            writer.WriteEndElement(); // StripRow
        }

        writer.WriteEndElement(); // StripBlock
        writer.WriteEndElement(); // StripBlocks

        if (layout.MediaWidthUm != 60_000 || layout.MediaHeightUm != 30_000)
        {
            writer.WriteStartElement("MeyertecMedia");
            writer.WriteAttributeString("Width", layout.MediaWidthUm.ToString());
            writer.WriteAttributeString("Height", layout.MediaHeightUm.ToString());
            writer.WriteEndElement();
        }

        // Собственное расширение MEYERTEC: позволяет повторно открыть свободно размещённые объекты.
        if (layout.Objects is { Count: > 0 })
        {
            writer.WriteStartElement("MeyertecObjects");
            foreach (var obj in layout.Objects)
            {
                writer.WriteStartElement("Object");
                writer.WriteAttributeString("X", obj.XUm.ToString());
                writer.WriteAttributeString("Y", obj.YUm.ToString());
                writer.WriteAttributeString("Width", obj.WidthUm.ToString());
                writer.WriteAttributeString("Height", obj.HeightUm.ToString());
                if (obj.CsvColumn is int column) writer.WriteAttributeString("CsvColumn", column.ToString());
                WriteContent(writer, obj.Content);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        // Device — минимальный заглушка (устройство в новой реализации не печатает)
        writer.WriteStartElement("Device");
        writer.WriteAttributeString("Type", "Printer");
        writer.WriteAttributeString("DrawContent", "True");
        writer.WriteAttributeString("DeviceID", "None");
        writer.WriteEndElement();

        writer.WriteEndElement(); // LabelStrip
    }

    private static void WriteContent(XmlWriter writer, CellContent content)
    {
        writer.WriteStartElement("Content");
        if (content.InvertColors) writer.WriteAttributeString("MeyertecInvert", "True");
        switch (content.Type)
        {
            case CellContentType.Text:
                writer.WriteStartElement("TextContent");
                writer.WriteElementString("Value", content.Text);
                writer.WriteStartElement("TextAttributes");
                writer.WriteAttributeString("Font",
                    string.IsNullOrEmpty(content.FontName) ? "Arial" : content.FontName);
                if (string.IsNullOrEmpty(content.FontName)) writer.WriteAttributeString("MeyertecDefaultFont", "True");
                // Размер в сотых pt (12 pt = 1200)
                writer.WriteAttributeString("Size",
                    ((int)Math.Round((content.FontSizePt ?? 12f) * 100f)).ToString());
                writer.WriteAttributeString("Bold", content.Bold ? "True" : "False");
                if (content.AutoFitText) writer.WriteAttributeString("MeyertecAutoFit", "True");
                if (!content.FontSizePt.HasValue) writer.WriteAttributeString("MeyertecAutomaticSize", "True");
                writer.WriteAttributeString("Color", "000000");
                writer.WriteAttributeString("Align", "Center");
                writer.WriteEndElement();
                writer.WriteEndElement();
                break;
            case CellContentType.Qr:
                writer.WriteStartElement("QrContent");
                writer.WriteElementString("Value", content.Text);
                writer.WriteEndElement();
                break;
            case CellContentType.Barcode:
                writer.WriteStartElement("BarcodeContent");
                writer.WriteElementString("Symbol",
                    content.BarcodeSymbol == BarcodeSymbol.Ean13 ? "Ean13" : "Code128");
                writer.WriteElementString("Value", content.BarcodeData);
                writer.WriteEndElement();
                break;
            case CellContentType.Image:
                writer.WriteStartElement("ImageContent");
                writer.WriteAttributeString("Format", content.ImageFormat ?? "png");
                writer.WriteStartElement("Data");
                writer.WriteValue(content.ImageBase64 ?? string.Empty);
                writer.WriteEndElement();
                writer.WriteEndElement();
                break;
            default:
                writer.WriteStartElement("Empty");
                writer.WriteEndElement();
                break;
        }
        writer.WriteEndElement(); // Content
    }
}

/// <summary>Вспомогательные расширения для XmlNode.</summary>
public static class XmlNodeExtensions
{
    public static bool GetAttributeLong(this XmlNode node, string name, out long value)
    {
        value = 0;
        return long.TryParse(node.Attributes?[name]?.Value, out value);
    }
}
