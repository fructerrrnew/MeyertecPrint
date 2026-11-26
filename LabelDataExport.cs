using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MeyertecPrint;

public static class LabelDataExport
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int WorksheetRowLimit = 1_048_576;
    private const int WorksheetColumnLimit = 16_384;
    private const int WorksheetTextLimit = 32_767;
    // SpreadsheetML escapes XML-forbidden characters and literal underscores that begin an escape.
    private static readonly Regex ExcelEscape = new(
        @"_(?=x[0-9a-fA-F]{4}_)|[\x00-\x08\x0B\x0C\x0E-\x1F\uFFFE\uFFFF]", RegexOptions.CultureInvariant);

    /// <summary>Writes one logical label per row, with columns in the layout's data-binding order.</summary>
    public static void Write(string path, IReadOnlyList<LabelLayout> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(pages);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx"))
            throw new ArgumentException("Choose a file with a .csv or .xlsx extension.", nameof(path));
        if (extension == ".xlsx" && pages.Count >= WorksheetRowLimit)
            throw new InvalidDataException("The label data exceeds the Excel worksheet row limit.");

        var rows = pages.Select(page => LabelPrintPages.GetBindings(page).Select(ReadValue).ToArray()).ToArray();
        var columnCount = Math.Max(1, rows.Select(row => row.Length).DefaultIfEmpty(0).Max());
        if (extension == ".xlsx" && columnCount > WorksheetColumnLimit)
            throw new InvalidDataException("The label data exceeds the Excel worksheet column limit.");
        var headings = Enumerable.Range(1, columnCount)
            .Select(column => "C" + column.ToString(CultureInfo.InvariantCulture)).ToArray();

        var fullPath = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                if (extension == ".csv")
                    WriteCsv(output, headings, rows);
                else
                    WriteWorkbook(output, headings, rows);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
            created = false;
        }
        finally
        {
            if (created) File.Delete(temporary);
        }
    }

    private static string ReadValue(CellContent content) => content switch
    {
        { Type: CellContentType.Text or CellContentType.Qr, Text: { } text } => text,
        { Type: CellContentType.Barcode, BarcodeData: { } data } => data,
        _ => string.Empty
    };

    private static void WriteCsv(Stream output, string[] headings, string[][] rows)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(true, true), leaveOpen: true) { NewLine = "\r\n" };
        WriteRow(headings);
        foreach (var row in rows) WriteRow(row);

        void WriteRow(string[] row)
        {
            for (var column = 0; column < headings.Length; column++)
            {
                if (column > 0) writer.Write(',');
                writer.Write('"');
                if (column < row.Length) writer.Write(row[column].Replace("\"", "\"\""));
                writer.Write('"');
            }
            writer.WriteLine();
        }
    }

    private static void WriteWorkbook(Stream output, string[] headings, string[][] rows)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        WritePart("[Content_Types].xml", """
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="xml" ContentType="application/xml"/>
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WritePart("_rels/.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WritePart("xl/workbook.xml", $"""
            <workbook xmlns="{SpreadsheetNamespace}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Labels" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        WritePart("xl/_rels/workbook.xml.rels", """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);

        using var sheet = archive.CreateEntry("xl/worksheets/sheet1.xml").Open();
        using var writer = XmlWriter.Create(sheet, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false, true),
            NewLineHandling = NewLineHandling.Entitize
        });
        writer.WriteStartDocument();
        writer.WriteStartElement("worksheet", SpreadsheetNamespace);
        writer.WriteStartElement("sheetData", SpreadsheetNamespace);
        var addresses = Enumerable.Range(1, headings.Length).Select(ColumnAddress).ToArray();
        WriteRow(headings, 1);
        for (var row = 0; row < rows.Length; row++)
            WriteRow(rows[row], row + 2);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();

        void WritePart(string name, string xml)
        {
            using var part = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false, true));
            part.Write(xml);
        }

        void WriteRow(string[] row, int index)
        {
            var rowNumber = index.ToString(CultureInfo.InvariantCulture);
            writer.WriteStartElement("row", SpreadsheetNamespace);
            writer.WriteAttributeString("r", rowNumber);
            for (var column = 0; column < headings.Length; column++)
            {
                var value = column < row.Length ? row[column] : string.Empty;
                if (value.Length > WorksheetTextLimit)
                    throw new InvalidDataException($"Cell {addresses[column]}{rowNumber} exceeds the Excel text limit.");
                writer.WriteStartElement("c", SpreadsheetNamespace);
                writer.WriteAttributeString("r", addresses[column] + rowNumber);
                writer.WriteAttributeString("t", "inlineStr");
                writer.WriteStartElement("is", SpreadsheetNamespace);
                writer.WriteStartElement("t", SpreadsheetNamespace);
                writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                writer.WriteString(ExcelEscape.Replace(value,
                    match => "_x" + ((int)match.Value[0]).ToString("X4", CultureInfo.InvariantCulture) + "_"));
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    private static string ColumnAddress(int column)
    {
        var address = string.Empty;
        while (column > 0)
        {
            column--;
            address = (char)('A' + column % 26) + address;
            column /= 26;
        }
        return address;
    }
}
