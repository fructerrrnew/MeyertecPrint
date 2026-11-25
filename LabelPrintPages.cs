namespace MeyertecPrint;

/// <summary>Подстановка CSV без изменения положения объектов и шаблона печати.</summary>
public static class LabelPrintPages
{
    // Старые ячейки и преобразованные в объекты данные сохраняют свои
    // номера столбцов. В новом пустом макете столбцы идут по порядку объектов.
    public static List<CellContent> GetBindings(LabelLayout layout)
    {
        var cells = layout.Rows.SelectMany(row => row.Cells).Select(cell => cell.Content).ToList();
        var objects = layout.Objects ?? new List<LabelObject>();
        if (objects.Count == 0) return cells;
        var hasLegacy = cells.Any(c => c.Type != CellContentType.Empty) || objects.Any(o => o.CsvColumn.HasValue);
        if (!hasLegacy) return objects.Select(o => o.Content).ToList();
        foreach (var obj in objects)
        {
            if (obj.CsvColumn is int column && column >= 0 && column < cells.Count && cells[column].Type == CellContentType.Empty)
                cells[column] = obj.Content;
            else
                cells.Add(obj.Content);
        }
        return cells;
    }

    public static int CountImported(LabelLayout template, IEnumerable<LabelItem>? labels)
        => PrintableRecords(template, labels).Count();

    public static IEnumerable<LabelItem> PrintableRecords(LabelLayout template, IEnumerable<LabelItem>? labels)
    {
        if (labels is null) yield break;
        var count = GetBindings(template).Count;
        foreach (var label in labels)
            if (label is not null && (label.UseLayout || label.Fields is not null && label.Fields.Keys.Any(key =>
                key.Length > 1 && key[0] == 'C' && int.TryParse(key.AsSpan(1), out var index)
                && index >= 0 && index < count && key == "C" + index)))
                yield return label;
    }

    public static Dictionary<string, string> CaptureFields(LabelLayout layout) =>
        GetBindings(layout).Select((content, index) => (content, index))
            .Where(field => field.content.Type != CellContentType.Image)
            .ToDictionary(field => "C" + field.index, field => field.content.Type == CellContentType.Barcode
                ? field.content.BarcodeData : field.content.Text);

    public static bool ApplyFields(LabelLayout page, IReadOnlyDictionary<string, string> fields)
    {
        var contents = GetBindings(page);
        for (var i = 0; i < contents.Count; i++)
            if (fields.ContainsKey("C" + i) && contents[i].Type == CellContentType.Image)
                throw new InvalidDataException(L10n.T("Print.CsvImage"));
        var matched = false;
        for (var i = 0; i < contents.Count; i++)
        {
            if (!fields.TryGetValue("C" + i, out var value)) continue;
            value ??= string.Empty;
            var content = contents[i];
            matched = true;
            switch (content.Type)
            {
                case CellContentType.Empty:
                case CellContentType.Text:
                    if (value.Length > 0) content.Type = CellContentType.Text;
                    content.Text = value;
                    break;
                case CellContentType.Qr: content.Text = value; break;
                case CellContentType.Barcode: content.BarcodeData = value; break;
            }
        }
        return matched;
    }

    public static IReadOnlyList<LabelLayout> Create(LabelLayout template, IEnumerable<LabelItem>? labels, bool includeImported)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new PageCollection(template, includeImported ? labels : null);
    }

    internal static List<LabelItem> CreateStockSampleRecords(LabelLayout layout)
    {
        if (layout.AlternateNotch is null) return [];
        var fields = CaptureFields(layout);
        var name = string.Join(" | ", fields.Values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(3));
        return [Sample(2), Sample(3)];

        LabelItem Sample(int index) => new()
        {
            Index = index, Name = name, Fields = new(fields), UseLayout = fields.Count == 0
        };
    }

    internal static int PhysicalSectionsPerLabel(LabelLayout layout) => layout.AlternateNotch is null ? 1 : 2;

    internal static int LeadingBlankSections(LabelLayout layout, bool startOnNotchedSection) =>
        startOnNotchedSection && layout.AlternateNotch is not null ? 1 : 0;

    public static IReadOnlyList<LabelLayout> CreateStockPreview(LabelLayout layout)
    {
        var full = layout.Clone();
        full.IsNotchedSection = false;
        if (full.AlternateNotch is null) return [full];

        var skipped = full.Clone();
        skipped.IsNotchedSection = true;
        return [full, skipped, full.Clone()];
    }

    // Clone only the requested page, so large batches do not allocate one layout per record.
    private sealed class PageCollection : IReadOnlyList<LabelLayout>
    {
        private readonly LabelLayout _template;
        private readonly List<Dictionary<string, string>> _records = new();
        public PageCollection(LabelLayout template, IEnumerable<LabelItem>? labels)
        {
            _template = template.Clone();
            foreach (var label in PrintableRecords(_template, labels))
                _records.Add(label.UseLayout ? new Dictionary<string, string>() : new Dictionary<string, string>(label.Fields));
        }
        public int Count => 1 + _records.Count;
        public LabelLayout this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                var page = _template.Clone();
                page.IsNotchedSection = false;
                if (index > 0) ApplyFields(page, _records[index - 1]);
                return page;
            }
        }
        public IEnumerator<LabelLayout> GetEnumerator()
        { for (var i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
