namespace MeyertecPrint;

/// <summary>Spreadsheet preview and explicit source-column to label-field mapping.</summary>
public sealed class CsvImportDialog : BrandForm
{
    private const int PreviewLimit = 100;
    private readonly TextBox _pathBox = new() { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Theme.Surface };
    private readonly ListView _preview = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly TableLayoutPanel _mapping = new()
    {
        Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(12, 0, 12, 4), BackColor = Theme.Surface
    };
    private readonly Label _previewEmpty = new()
    {
        Dock = DockStyle.Fill, Padding = new Padding(18), TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Theme.SecondaryText, BackColor = Theme.Surface
    };
    private readonly CheckBox _headers = new() { AutoSize = true };
    private readonly SplitContainer _split;
    private readonly ComboBox _encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 218 };
    private readonly ComboBox _worksheet = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 218, Visible = false };
    private readonly Label _encodingLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
    private readonly Label _worksheetLabel = new() { AutoSize = true, Margin = new Padding(0, 6, 8, 0), Visible = false };
    private readonly Button _ok = new() { Width = 110, Enabled = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 38, ForeColor = Theme.SecondaryText, UseMnemonic = false };
    private readonly IReadOnlyList<CellContent> _targets;
    private readonly List<(int Target, ComboBox Selector)> _selectors = new();
    private List<List<string>>? _parsed;
    public string FilePath { get; private set; } = string.Empty;
    public List<List<string>> DataRows { get; private set; } = new();
    public Dictionary<int, int> TargetColumns { get; private set; } = new();
    private sealed record ColumnOption(int Index, string Name) { public override string ToString() => Name; }
    private sealed record EncodingOption(string Key, string Name) { public override string ToString() => Name; }

    public CsvImportDialog(LabelLayout layout)
    {
        SuspendLayout();
        _targets = LabelPrintPages.GetBindings(layout);
        Text = L10n.T("Csv.Title");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 635);
        MinimumSize = new Size(620, 430);
        ShowIcon = MinimizeBox = false;
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        var file = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 55, Padding = new Padding(14, 9, 14, 7), ColumnCount = 2, RowCount = 1
        };
        file.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        file.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pathBox.AccessibleName = L10n.T("Csv.File");
        _pathBox.Dock = DockStyle.None;
        _pathBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var browse = new Button
        {
            Text = L10n.T("Csv.Browse"), Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(9, 0, 9, 0)
        };
        Theme.StyleButton(browse);
        browse.Click += OnBrowse;
        file.Controls.Add(_pathBox, 0, 0);
        file.Controls.Add(browse, 1, 0);
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(16, 4, 0, 0), WrapContents = false
        };
        _encoding.AccessibleName = L10n.T("Csv.Encoding");
        _encodingLabel.Text = _encoding.AccessibleName;
        options.Controls.Add(_encodingLabel);
        _encoding.Items.AddRange(new object[] { new EncodingOption("auto", L10n.T("Csv.AutoEncoding")),
            new EncodingOption("utf8", "UTF-8"), new EncodingOption("1251", "Windows-1251"), new EncodingOption("utf16", "UTF-16") });
        _encoding.SelectedIndex = 0;
        options.Controls.Add(_encoding);
        _worksheet.AccessibleName = _worksheetLabel.Text = L10n.T("Data.Worksheet");
        options.Controls.Add(_worksheetLabel);
        options.Controls.Add(_worksheet);
        _headers.Text = L10n.T("Csv.HasHeaders");
        _headers.Margin = new Padding(18, 5, 0, 0);
        options.Controls.Add(_headers);
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(860, 450), Padding = Padding.Empty,
            SplitterDistance = 225, Panel1MinSize = 75, Panel2MinSize = 105,
            BackColor = SystemInformation.HighContrast ? SystemColors.WindowFrame : Theme.Border
        };
        _split.Panel1.BackColor = _split.Panel2.BackColor = Theme.Surface;
        _split.Panel1.Padding = new Padding(14, 0, 14, 10);
        _preview.AccessibleName = L10n.T("Csv.Preview");
        _previewEmpty.Text = L10n.T("Csv.ChooseFile");
        _preview.Visible = false;
        _split.Panel1.Controls.Add(_preview);
        _split.Panel1.Controls.Add(_previewEmpty);
        _mapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        _mapping.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        var mappingScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        mappingScroll.Controls.Add(_mapping);
        _split.Panel2.Controls.Add(mappingScroll);
        var mappingTitle = new Label
        {
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(14, 8, 0, 0),
            Font = Theme.UiFont(10, FontStyle.Bold), ForeColor = Theme.Text, Text = L10n.T("Csv.Mapping")
        };
        _split.Panel2.Controls.Add(mappingTitle);
        _split.Panel2Collapsed = true;
        _status.Padding = new Padding(14, 5, 14, 0);
        _status.Visible = false;
        _ok.Text = L10n.T("Csv.Import");
        _ok.Click += AcceptImport;
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        Controls.Add(_split);
        Controls.Add(_status);
        Controls.Add(options);
        Controls.Add(file);
        Controls.Add(Theme.DialogFooter(_ok, cancel));
        AcceptButton = _ok;
        CancelButton = cancel;
        Shown += (_, _) => browse.Focus();
        _encoding.SelectedIndexChanged += (_, _) => LoadPreview();
        _worksheet.SelectionChangeCommitted += (_, _) => { ClearMapping(); LoadPreview(); };
        _headers.CheckedChanged += (_, _) => UpdatePreview();
        ResumeLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.FitDialogToScreen(this);
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new BrandedOpenFileDialog { Title = L10n.T("Csv.Title"),
            Filter = $"{L10n.T("Data.Filter")} (*.csv, *.xlsx, *.xls, *.xlsb)|*.csv;*.xlsx;*.xls;*.xlsb|{L10n.T("File.FilterAll")} (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var samePath = string.Equals(FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase);
        var previousWorksheet = samePath ? _worksheet.SelectedItem as string : null;
        if (!samePath) ClearMapping();
        _worksheet.Items.Clear();
        FilePath = dialog.FileName;
        _pathBox.Text = FilePath;
        LoadPreview(previousWorksheet);
    }

    private void LoadPreview(string? previousWorksheet = null)
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        var excel = SpreadsheetReader.IsExcelFile(FilePath);
        _encoding.Visible = _encodingLabel.Visible = !excel;
        _worksheet.Visible = _worksheetLabel.Visible = excel;
        try
        {
            if (excel && _worksheet.Items.Count == 0)
            {
                _worksheet.Items.AddRange(SpreadsheetReader.GetWorksheetNames(FilePath).Cast<object>().ToArray());
                if (_worksheet.Items.Count > 0)
                {
                    var index = previousWorksheet is null ? 0 : _worksheet.Items.IndexOf(previousWorksheet);
                    if (index < 0) ClearMapping();
                    _worksheet.SelectedIndex = Math.Max(0, index);
                }
            }
            _parsed = SpreadsheetReader.ReadFile(FilePath, (_encoding.SelectedItem as EncodingOption)?.Key ?? "auto",
                _worksheet.SelectedItem as string);
            UpdatePreview();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException
            or ArgumentException or System.Xml.XmlException)
        {
            _parsed = null;
            _preview.Items.Clear();
            _preview.Columns.Clear();
            ClearMapping();
            _ok.Enabled = false;
            _preview.Visible = false;
            _previewEmpty.Visible = true;
            _split.Panel2Collapsed = true;
            ShowStatus(string.Format(L10n.T("Csv.ErrorParse"), ex.Message), error: true);
        }
    }

    private void UpdatePreview()
    {
        if (_parsed is null) return;
        var all = _parsed;
        var rows = all.Skip(_headers.Checked ? 1 : 0).ToList();
        var columns = all.Count == 0 ? 0 : all.Max(r => r.Count);
        _preview.BeginUpdate();
        _preview.Items.Clear();
        _preview.Columns.Clear();
        for (var i = 0; i < columns; i++)
        {
            var name = _headers.Checked && all.Count > 0 && i < all[0].Count ? all[0][i] : "";
            _preview.Columns.Add(new ColumnHeader { Width = 140, Text = $"{i + 1}: {name}" });
        }
        foreach (var row in rows.Take(PreviewLimit))
        {
            var values = Enumerable.Range(0, columns).Select(i => i < row.Count ? row[i] : string.Empty).ToArray();
            if (values.Length > 0) _preview.Items.Add(new ListViewItem(values));
        }
        _preview.EndUpdate();
        _preview.Visible = true;
        _previewEmpty.Visible = false;
        _split.Panel2Collapsed = false;
        var previous = _selectors.ToDictionary(entry => entry.Target, entry => entry.Selector.SelectedIndex);
        ClearMapping();
        _mapping.SuspendLayout();
        var defaultColumn = 0;
        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            if (target.Type == CellContentType.Image) continue; // Images remain constant.
            var description = (target.Type == CellContentType.Barcode ? target.BarcodeData : target.Text) ?? string.Empty;
            if (description.Length > 24) description = description[..24] + "…";
            var label = new Label
            {
                Dock = DockStyle.Fill, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, UseMnemonic = false,
                Text = $"{i + 1}. {L10n.T("Csv.Target." + target.Type)}  {description}"
            };
            var selector = new ComboBox
            {
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = label.Text, Margin = new Padding(4, 4, 0, 0)
            };
            selector.Items.Add(new ColumnOption(-1, L10n.T("Csv.Constant")));
            for (var c = 0; c < columns; c++)
            {
                var name = _headers.Checked && all.Count > 0 && c < all[0].Count ? all[0][c] : "";
                selector.Items.Add(new ColumnOption(c, $"{c + 1}: {name}"));
            }
            var suggestedColumn = defaultColumn < columns ? ++defaultColumn : 0;
            selector.SelectedIndex = previous.TryGetValue(i, out var selected) && selected >= 0 && selected < selector.Items.Count
                ? selected : suggestedColumn;
            _mapping.Controls.Add(label, 0, _selectors.Count);
            _mapping.Controls.Add(selector, 1, _selectors.Count);
            _selectors.Add((i, selector));
        }
        _mapping.ResumeLayout();
        _ok.Enabled = rows.Count > 0 && _selectors.Count > 0;
        ShowStatus(string.Format(L10n.T(rows.Count > PreviewLimit ? "Csv.RowsLimited" : "Csv.RowCount"), rows.Count, PreviewLimit));
    }

    private void ClearMapping()
    {
        _mapping.SuspendLayout();
        while (_mapping.Controls.Count > 0) _mapping.Controls[0].Dispose();
        _selectors.Clear();
        _mapping.ResumeLayout();
    }

    private void ShowStatus(string message, bool error = false)
    {
        _status.ForeColor = error ? Theme.ErrorText : Theme.SecondaryText;
        _status.Text = message;
        _status.Visible = true;
    }

    private void AcceptImport(object? sender, EventArgs e)
    {
        if (_parsed is null) return;
        var rows = _parsed.Skip(_headers.Checked ? 1 : 0).ToList();
        var mapping = _selectors.Where(x => (x.Selector.SelectedItem as ColumnOption)?.Index >= 0)
            .ToDictionary(x => x.Target, x => ((ColumnOption)x.Selector.SelectedItem!).Index);
        if (mapping.Count == 0) { ShowStatus(L10n.T("Csv.NoMapping"), error: true); return; }
        var required = mapping.Values.Max();
        var bad = rows.FindIndex(row => row.Count <= required);
        if (bad >= 0) { ShowStatus(string.Format(L10n.T("Csv.ShortRow"), bad + 1 + (_headers.Checked ? 1 : 0)), error: true); return; }
        var used = mapping.Values.ToHashSet();
        var hasUnused = rows.Any(row => row.Where((value, index) => !used.Contains(index) && !string.IsNullOrWhiteSpace(value)).Any());
        if (hasUnused && BrandMessageBox.Show(this, L10n.T("Csv.Unmapped"), L10n.T("Csv.Title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        DataRows = rows; TargetColumns = mapping; DialogResult = DialogResult.OK; Close();
    }
}
