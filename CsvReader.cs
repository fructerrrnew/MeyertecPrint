using System.Text;

namespace MeyertecPrint;

/// <summary>Strict CSV reader with quoted multiline fields and explicit encodings.</summary>
public static class CsvReader
{
    public sealed record Result(char Delimiter, List<List<string>> Rows);

    public static string ReadFile(string path, string encoding)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (encoding == "1251") return Encoding.GetEncoding(1251).GetString(bytes);
        if (encoding == "utf8") return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (encoding == "utf16")
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.Unicode, true);
            return reader.ReadToEnd();
        }
        if (bytes.Length >= 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe) || (bytes[0] == 0xfe && bytes[1] == 0xff)))
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.Unicode, true);
            return reader.ReadToEnd();
        }
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1251).GetString(bytes); }
    }

    public static Result Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        var delimiter = DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closed = false, fieldStarted = false;
        var line = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { field.Append(ch); if (ch == '\n') line++; }
                continue;
            }
            if (ch == '"')
            {
                if (fieldStarted || closed) throw new FormatException(string.Format(L10n.T("Csv.InvalidQuote"), line));
                quoted = true;
                fieldStarted = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString()); field.Clear(); fieldStarted = false; closed = false;
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString()); rows.Add(row); row = new List<string>(); field.Clear();
                fieldStarted = false; closed = false; line++;
            }
            else
            {
                if (closed) throw new FormatException(string.Format(L10n.T("Csv.InvalidQuote"), line));
                field.Append(ch); fieldStarted = true;
            }
        }
        if (quoted) throw new FormatException(string.Format(L10n.T("Csv.UnclosedQuote"), line));
        if (fieldStarted || closed || field.Length > 0 || row.Count > 0)
        { row.Add(field.ToString()); rows.Add(row); }
        return new Result(delimiter, rows);
    }

    private static char DetectDelimiter(string text)
    {
        var counts = new int[3];
        var choices = new[] { ';', ',', '\t' };
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') i++;
                else quoted = !quoted;
            }
            else if (!quoted && ch == '\n') break;
            else if (!quoted)
                for (var j = 0; j < choices.Length; j++) if (ch == choices[j]) counts[j]++;
        }
        var best = 0;
        for (var i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
        return choices[best];
    }
}
