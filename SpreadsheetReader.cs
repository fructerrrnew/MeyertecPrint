using System.Globalization;
using System.Text;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using ExcelNumberFormat;

namespace MeyertecPrint;

/// <summary>Reads columns from CSV or a single worksheet, retaining Excel's formatted cell values.</summary>
public static class SpreadsheetReader
{
    public static bool IsExcelFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xls" or ".xlsb";

    public static IReadOnlyList<string> GetWorksheetNames(string path)
    {
        using var reader = OpenWorkbook(path);
        var names = new List<string>();
        if (reader.ResultsCount > 0)
            do { names.Add(reader.Name); } while (reader.NextResult());
        return names;
    }

    public static List<List<string>> ReadFile(string path, string encoding = "auto", string? worksheet = null)
    {
        if (!IsExcelFile(path))
            return CsvReader.Parse(CsvReader.ReadFile(path, encoding)).Rows;

        using var reader = OpenWorkbook(path);
        if (reader.ResultsCount == 0)
            throw new InvalidDataException(L10n.T("Data.NoWorksheets"));
        while (worksheet is not null && !string.Equals(reader.Name, worksheet, StringComparison.Ordinal))
            if (!reader.NextResult())
                throw new InvalidDataException(string.Format(L10n.T("Data.WorksheetMissing"), worksheet));

        var culture = CultureInfo.CurrentCulture;
        var formats = new Dictionary<string, NumberFormat>(StringComparer.Ordinal);
        var rows = new List<List<string>>();
        var width = 0;
        while (reader.Read())
        {
            var row = new List<string>(reader.FieldCount);
            for (var column = 0; column < reader.FieldCount; column++)
                row.Add(ReadCell(reader, column, culture, formats));
            var lastValue = row.FindLastIndex(value => value.Length != 0);
            if (lastValue < 0) continue;
            row.RemoveRange(lastValue + 1, row.Count - lastValue - 1);
            width = Math.Max(width, row.Count);
            rows.Add(row);
        }
        // An empty cell in a short worksheet row belongs to the same column as values in other rows.
        foreach (var row in rows)
            while (row.Count < width) row.Add(string.Empty);
        return rows;
    }

    private static IExcelDataReader OpenWorkbook(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { SinglePassMode = true });
        }
        catch (Exception ex) when (ex is HeaderException or InvalidPasswordException)
        {
            stream.Dispose();
            throw new InvalidDataException(L10n.T("Data.WorkbookUnreadable"), ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static string ReadCell(IExcelDataReader reader, int column, CultureInfo culture,
        Dictionary<string, NumberFormat> formats)
    {
        var value = reader.GetValue(column);
        if (value is string text) return text;
        if (value is null) return reader.GetCellError(column) switch
        {
            CellError.NULL => "#NULL!",
            CellError.DIV0 => "#DIV/0!",
            CellError.VALUE => "#VALUE!",
            CellError.REF => "#REF!",
            CellError.NAME => "#NAME?",
            CellError.NUM => "#NUM!",
            CellError.NA => "#N/A",
            CellError.GETTING_DATA => "#GETTING_DATA",
            _ => string.Empty
        };
        if (value is bool boolean) return boolean ? "TRUE" : "FALSE";
        var format = reader.GetNumberFormatString(column);
        if (!string.IsNullOrEmpty(format) && !format.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            if (!formats.TryGetValue(format, out var formatter))
                formats.Add(format, formatter = new NumberFormat(format));
            if (formatter.IsValid)
                return formatter.Format(value, culture);
        }
        return value is DateTime date
            ? date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
