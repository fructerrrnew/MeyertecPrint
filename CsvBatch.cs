namespace MeyertecPrint;

/// <summary>Build a complete replacement batch before changing the current project.</summary>
public static class CsvBatch
{
    public sealed record Prepared(Dictionary<string, string> FirstFields, List<LabelItem> Remaining);

    public static Prepared Prepare(LabelLayout template, IReadOnlyList<List<string>> rows, IReadOnlyDictionary<int, int> targetColumns)
    {
        if (rows.Count == 0 || targetColumns.Count == 0) throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        var targets = LabelPrintPages.GetBindings(template);
        if (targetColumns.Any(pair => pair.Key < 0 || pair.Key >= targets.Count || pair.Value < 0))
            throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        var lastColumn = targetColumns.Values.Max();
        if (rows.Any(row => row.Count <= lastColumn))
            throw new InvalidDataException(L10n.T("Csv.NoMapping"));
        if (targetColumns.Keys.Any(index => targets[index].Type == CellContentType.Image))
            throw new InvalidDataException(L10n.T("Print.CsvImage"));

        var mapping = targetColumns.Select(pair => (Field: "C" + pair.Key, Column: pair.Value)).ToArray();
        Dictionary<string, string> Fields(IReadOnlyList<string> row)
            => mapping.ToDictionary(pair => pair.Field, pair => row[pair.Column]);
        var remaining = rows.Skip(1).Select((row, index) => new LabelItem
        {
            Index = index + 2, Name = string.Join(" | ", row.Take(3)), Fields = Fields(row),
        }).ToList();
        return new Prepared(Fields(rows[0]), remaining);
    }
}
