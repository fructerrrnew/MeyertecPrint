namespace MeyertecPrint;

/// <summary>Build a complete replacement batch before changing the current project.</summary>
public static class CsvBatch
{
    public sealed record Prepared(Dictionary<string, string> FirstFields, List<LabelItem> Remaining);

    public static Prepared Prepare(LabelLayout template, IReadOnlyList<List<string>> rows, IReadOnlyDictionary<int, int> targetColumns)
    {
        if (rows.Count == 0 || targetColumns.Count == 0) throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        var targetCount = LabelPrintPages.GetBindings(template).Count;
        if (targetColumns.Any(pair => pair.Key < 0 || pair.Key >= targetCount || pair.Value < 0))
            throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        Dictionary<string, string> Fields(IReadOnlyList<string> row)
        {
            if (targetColumns.Values.Any(column => column >= row.Count))
                throw new InvalidDataException(L10n.T("Csv.NoMapping"));
            return targetColumns.ToDictionary(pair => "C" + pair.Key, pair => row[pair.Value]);
        }
        var fields = rows.Select(Fields).ToList();
        foreach (var row in fields)
        {
            var test = template.Clone();
            if (!LabelPrintPages.ApplyFields(test, row)) throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        }
        var remaining = rows.Skip(1).Select((row, index) => new LabelItem
        {
            Index = index + 2, Name = string.Join(" | ", row.Take(3)), Fields = fields[index + 1],
        }).ToList();
        return new Prepared(fields[0], remaining);
    }
}
