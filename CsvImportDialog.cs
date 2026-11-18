namespace MeyertecPrint;

/// <summary>CSV preview and explicit source-column to label-field mapping.</summary>
public sealed class CsvImportDialog : BrandForm
{
    private readonly TextBox _pathBox = new() { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White };
    private readonly ListView _preview = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly FlowLayoutPanel _mapping = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, BackColor = Color.White };
    private readonly CheckBox _headers = new() { AutoSize = true };
    private readonly ComboBox _encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175 };
    private readonly Button _ok = new() { Width = 110, Enabled = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, ForeColor = Theme.SecondaryText };
    private readonly IReadOnlyList<CellContent> _targets;
    private readonly List<(int Target, ComboBox Selector)> _selectors = new();
    private CsvReader.Result? _parsed;
    public string FilePath { get; private set; } = string.Empty;
    public List<List<string>> DataRows { get; private set; } = new();
    public Dictionary<int, int> TargetColumns { get; private set; } = new();
    private sealed record ColumnOption(int Index, string Name) { public override string ToString() => Name; }
    private sealed record EncodingOption(string Key, string Name) { public override string ToString() => Name; }

    public CsvImportDialog(LabelLayout layout)
    {
        _targets = LabelPrintPages.GetBindings(layout);
        Text = L10n.T("Csv.Title"); StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 650); MinimumSize = new Size(720, 520);
        Font = Theme.UiFont(); BackColor = Theme.LightBackground;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        var file = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(12, 6, 12, 3), ColumnCount = 2 };
        file.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); file.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        var browse = new Button { Text = L10n.T("Csv.Browse"), Dock = DockStyle.Fill };
        Theme.StyleButton(browse); browse.Click += OnBrowse;
        file.Controls.Add(_pathBox, 0, 0); file.Controls.Add(browse, 1, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(12, 4, 0, 0), WrapContents = false };
        options.Controls.Add(new Label { Text = L10n.T("Csv.Encoding"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _encoding.Items.AddRange(new object[] { new EncodingOption("auto", L10n.T("Csv.AutoEncoding")),
            new EncodingOption("utf8", "UTF-8"), new EncodingOption("1251", "Windows-1251"), new EncodingOption("utf16", "UTF-16") });
        _encoding.SelectedIndex = 0; options.Controls.Add(_encoding);
        _headers.Text = L10n.T("Csv.HasHeaders"); _headers.Margin = new Padding(18, 5, 0, 0); options.Controls.Add(_headers);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(860, 450), SplitterDistance = 245, Panel1MinSize = 90, Panel2MinSize = 100 };
        split.Panel1.Controls.Add(_preview);
        split.Panel2.Controls.Add(_mapping);
        var mappingTitle = new Label { Dock = DockStyle.Top, Height = 32, Padding = new Padding(12, 7, 0, 0), Text = L10n.T("Csv.Mapping") };
        split.Panel2.Controls.Add(mappingTitle);
        _status.Padding = new Padding(12, 3, 0, 0);
        _ok.Text = L10n.T("Dialog.OK"); _ok.Click += AcceptImport;
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel, Width = 110 };
        Controls.Add(split); Controls.Add(_status); Controls.Add(options); Controls.Add(file); Controls.Add(Theme.DialogFooter(_ok, cancel));
        AcceptButton = _ok; CancelButton = cancel;
        _encoding.SelectedIndexChanged += (_, _) => LoadPreview();
        _headers.CheckedChanged += (_, _) => UpdatePreview();
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new BrandedOpenFileDialog { Title = L10n.T("Csv.Title"),
            Filter = $"{L10n.T("File.FilterCsv")} (*.csv)|*.csv|{L10n.T("File.FilterAll")} (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        FilePath = dialog.FileName; _pathBox.Text = FilePath; LoadPreview();
    }

    private void LoadPreview()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            _parsed = CsvReader.Parse(CsvReader.ReadFile(FilePath, (_encoding.SelectedItem as EncodingOption)?.Key ?? "auto"));
            UpdatePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.DecoderFallbackException or ArgumentException)
        {
            _parsed = null; _preview.Items.Clear(); _preview.Columns.Clear(); _mapping.Controls.Clear();
            _selectors.Clear(); _ok.Enabled = false; _status.Text = string.Format(L10n.T("Csv.ErrorParse"), ex.Message);
        }
    }

    private void UpdatePreview()
    {
        if (_parsed is null) return;
        var all = _parsed.Rows;
        var rows = all.Skip(_headers.Checked ? 1 : 0).ToList();
        var columns = all.Count == 0 ? 0 : all.Max(r => r.Count);
        _preview.BeginUpdate(); _preview.Items.Clear(); _preview.Columns.Clear();
        for (var i = 0; i < columns; i++)
        {
            var name = _headers.Checked && all.Count > 0 && i < all[0].Count ? all[0][i] : "";
            _preview.Columns.Add(new ColumnHeader { Width = 140, Text = $"{i + 1}: {name}" });
        }
        foreach (var row in rows.Take(100))
        {
            var values = Enumerable.Range(0, columns).Select(i => i < row.Count ? row[i] : string.Empty).ToArray();
            if (values.Length > 0) _preview.Items.Add(new ListViewItem(values));
        }
        _preview.EndUpdate();
        _mapping.Controls.Clear(); _selectors.Clear();
        var defaultColumn = 0;
        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            if (target.Type == CellContentType.Image) continue; // Images remain constant.
            var line = new FlowLayoutPanel { Width = 700, Height = 34, WrapContents = false };
            var description = target.Type == CellContentType.Barcode ? target.BarcodeData : target.Text;
            if (description.Length > 24) description = description[..24] + "…";
            var label = new Label { Width = 280, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                Text = $"{i + 1}. {L10n.T("Csv.Target." + target.Type)}  {description}" };
            var selector = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
            selector.Items.Add(new ColumnOption(-1, L10n.T("Csv.Constant")));
            for (var c = 0; c < columns; c++)
            {
                var name = _headers.Checked && all.Count > 0 && c < all[0].Count ? all[0][c] : "";
                selector.Items.Add(new ColumnOption(c, $"{c + 1}: {name}"));
            }
            selector.SelectedIndex = defaultColumn < columns ? ++defaultColumn : 0;
            line.Controls.Add(label); line.Controls.Add(selector); _mapping.Controls.Add(line);
            _selectors.Add((i, selector));
        }
        _ok.Enabled = rows.Count > 0 && _selectors.Count > 0;
        _status.Text = string.Format(L10n.T("Csv.RowCount"), rows.Count);
    }

    private void AcceptImport(object? sender, EventArgs e)
    {
        if (_parsed is null) return;
        var rows = _parsed.Rows.Skip(_headers.Checked ? 1 : 0).ToList();
        var mapping = _selectors.Where(x => (x.Selector.SelectedItem as ColumnOption)?.Index >= 0)
            .ToDictionary(x => x.Target, x => ((ColumnOption)x.Selector.SelectedItem!).Index);
        if (mapping.Count == 0) { _status.Text = L10n.T("Csv.NoMapping"); return; }
        var required = mapping.Values.Max();
        var bad = rows.FindIndex(row => row.Count <= required);
        if (bad >= 0) { _status.Text = string.Format(L10n.T("Csv.ShortRow"), bad + 1 + (_headers.Checked ? 1 : 0)); return; }
        var used = mapping.Values.ToHashSet();
        var hasUnused = rows.Any(row => row.Where((value, index) => !used.Contains(index) && !string.IsNullOrWhiteSpace(value)).Any());
        if (hasUnused && BrandMessageBox.Show(this, L10n.T("Csv.Unmapped"), L10n.T("Csv.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        DataRows = rows; TargetColumns = mapping; DialogResult = DialogResult.OK; Close();
    }
}
