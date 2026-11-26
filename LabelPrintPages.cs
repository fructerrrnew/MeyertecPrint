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

    public static int CountImported(RibbonData ribbon) => PrintableRecords(ribbon).Count();

    public static IEnumerable<LabelItem> PrintableRecords(RibbonData ribbon) =>
        PrintableRecords(RecordTemplate(ribbon), ribbon.Labels);

    public static IEnumerable<LabelItem> PrintableRecords(LabelLayout template, IEnumerable<LabelItem>? labels)
    {
        if (labels is null) yield break;
        var count = GetBindings(template).Count;
        foreach (var label in labels)
            if (label is not null && (label.Layout is not null || label.UseLayout || label.Fields is not null && label.Fields.Keys.Any(key =>
                key.Length > 1 && key[0] == 'C' && int.TryParse(key.AsSpan(1), out var index)
                && index >= 0 && (index < count || label.Index > 0) && key == "C" + index)))
                yield return label;
    }

    public static Dictionary<string, string> CaptureFields(LabelLayout layout) =>
        GetBindings(layout).Select((content, index) => (content, index))
            .Where(field => field.content.Type != CellContentType.Image)
            .ToDictionary(field => "C" + field.index, field => field.content.Type == CellContentType.Barcode
                ? field.content.BarcodeData : field.content.Text);

    public static bool ApplyFields(LabelLayout page, IReadOnlyDictionary<string, string> fields) =>
        ApplyFields(page, fields, ignoreImageTargets: false);

    private static bool ApplyFields(LabelLayout page, IReadOnlyDictionary<string, string> fields, bool ignoreImageTargets)
    {
        var contents = GetBindings(page);
        if (!ignoreImageTargets)
            for (var i = 0; i < contents.Count; i++)
                if (fields.ContainsKey("C" + i) && contents[i].Type == CellContentType.Image)
                    throw new InvalidDataException(L10n.T("Print.CsvImage"));
        var matched = false;
        for (var i = 0; i < contents.Count; i++)
        {
            if (contents[i].Type == CellContentType.Image || !fields.TryGetValue("C" + i, out var value)) continue;
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

    public static IReadOnlyList<LabelLayout> Create(RibbonData ribbon, bool includeImported = true) =>
        new PageCollection(ribbon.GetLayout(), includeImported ? ribbon.Labels : null,
            ribbon.ApplyLayoutToAll, ribbon.IndividualLayoutBase);

    public static void SetApplyLayoutToAll(RibbonData ribbon, bool applyLayoutToAll)
    {
        if (ribbon.ApplyLayoutToAll == applyLayoutToAll) return;
        ribbon.IndividualLayoutBase = applyLayoutToAll ? null : ribbon.GetLayout().Clone();
        ribbon.ApplyLayoutToAll = applyLayoutToAll;
    }

    public static void RefreshIndividualLayoutBase(RibbonData ribbon) =>
        ribbon.IndividualLayoutBase = ribbon.ApplyLayoutToAll ? null : ribbon.GetLayout().Clone();

    private static LabelLayout RecordTemplate(RibbonData ribbon) =>
        ribbon.ApplyLayoutToAll ? ribbon.GetLayout() : ribbon.IndividualLayoutBase ?? ribbon.GetLayout();

    public static LabelLayout ResolveRecordLayout(RibbonData ribbon, LabelItem record) =>
        ResolveRecordLayout(ribbon.GetLayout(), record, ribbon.ApplyLayoutToAll, ribbon.IndividualLayoutBase);

    private static LabelLayout ResolveRecordLayout(LabelLayout template, LabelItem record, bool applyLayoutToAll,
        LabelLayout? individualBase)
    {
        var layout = (applyLayoutToAll ? template : record.Layout ?? individualBase ?? template).Clone();
        ApplyStockDimensions(layout, template);
        layout.IsNotchedSection = false;
        if (!record.UseLayout && record.Fields is not null)
            ApplyFields(layout, record.Fields, ignoreImageTargets: true);
        return layout;
    }

    private static void ApplyStockDimensions(LabelLayout layout, LabelLayout template)
    {
        if (layout.AlternateNotch is not null && template.AlternateNotch is not null && layout.MediaHeightUm != template.MediaHeightUm)
            GeometryOperations.ResizeNotchedStockPitch(layout, template.MediaHeightUm);
        else if (layout.AlternateNotch is null && layout.Rows is [var row] && row.Cells is [var cell] &&
            layout.Geometry is { LabelsInRow: 1, LeftUm: 0, RightUm: 0, TopUm: 0, GapBetweenUm: 0, GapBottomUm: 0 } geometry &&
            geometry.WidthUm == layout.MediaWidthUm && geometry.HeightUm == layout.MediaHeightUm &&
            cell.WidthUm == geometry.WidthUm && row.HeightUm == geometry.HeightUm)
        {
            geometry.WidthUm = cell.WidthUm = template.MediaWidthUm;
            geometry.HeightUm = row.HeightUm = template.MediaHeightUm;
        }
        layout.MediaWidthUm = template.MediaWidthUm;
        layout.MediaHeightUm = template.MediaHeightUm;
        layout.AlternateNotch = template.AlternateNotch;
    }

    public static void StoreRecordLayout(LabelItem record, LabelLayout layout)
    {
        var snapshot = layout.Clone();
        snapshot.IsNotchedSection = false;
        var fields = CaptureFields(snapshot);
        record.Layout = snapshot;
        record.Fields = fields;
        record.UseLayout = fields.Count == 0;
        record.Name = string.Join(" | ", fields.Values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(3));
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

    public static LabelLayout AtFeedPosition(LabelLayout page, long physicalIndex, bool startOnNotchedSection = false)
    {
        var notched = page.AlternateNotch is not null && ((physicalIndex % 2 != 0) != startOnNotchedSection);
        if (page.IsNotchedSection == notched) return page;
        var positioned = page.Clone();
        positioned.IsNotchedSection = notched;
        return positioned;
    }

    public static IReadOnlyList<LabelLayout> CreateStockPreview(LabelLayout layout)
    {
        var full = layout.Clone();
        full.IsNotchedSection = false;
        if (full.AlternateNotch is null) return [full];

        return [full, AtFeedPosition(full, 1), full.Clone()];
    }

    // Clone only the requested page, so large batches do not allocate one layout per record.
    private sealed class PageCollection : IReadOnlyList<LabelLayout>
    {
        private readonly LabelLayout _template;
        private readonly List<LabelItem> _records = new();
        private readonly bool _applyLayoutToAll;
        private readonly LabelLayout? _individualBase;
        public PageCollection(LabelLayout template, IEnumerable<LabelItem>? labels, bool applyLayoutToAll = true,
            LabelLayout? individualBase = null)
        {
            _template = template.Clone();
            _applyLayoutToAll = applyLayoutToAll;
            _individualBase = applyLayoutToAll ? null : individualBase?.Clone();
            foreach (var label in PrintableRecords(_individualBase ?? _template, labels))
                _records.Add(new LabelItem
                {
                    Layout = applyLayoutToAll ? label.Layout : label.Layout?.Clone(),
                    UseLayout = label.UseLayout,
                    Fields = label.Fields is null ? new() : new(label.Fields)
                });
        }
        public int Count => 1 + _records.Count;
        public LabelLayout this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                var page = index == 0 ? _template.Clone() :
                    ResolveRecordLayout(_template, _records[index - 1], _applyLayoutToAll, _individualBase);
                page.IsNotchedSection = page.AlternateNotch is not null && index % 2 != 0;
                return page;
            }
        }
        public IEnumerator<LabelLayout> GetEnumerator()
        { for (var i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
