namespace MeyertecPrint;

internal sealed class NumberingDialog : BrandForm
{
    private readonly LabelLayout _layout;
    private readonly ComboBox _field = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _count = Numeric(1, LabelNumbering.MaximumCount, 10);
    private readonly NumericUpDown _start = Numeric(long.MinValue, long.MaxValue, 1);
    private readonly NumericUpDown _step = Numeric(long.MinValue, long.MaxValue, 1);
    private readonly NumericUpDown _digits = Numeric(1, LabelNumbering.MaximumDigits, 3);
    private readonly TextBox _prefix = new() { Dock = DockStyle.Fill };
    private readonly TextBox _suffix = new() { Dock = DockStyle.Fill };
    private readonly Label _example = new()
    {
        AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), UseMnemonic = false
    };

    private sealed record Target(int Index, string Caption) { public override string ToString() => Caption; }

    public CsvBatch.Prepared? Prepared { get; private set; }

    public NumberingDialog(LabelLayout layout)
    {
        _layout = layout;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L10n.T("Numbering.Title");
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 370);
        MinimumSize = new Size(430, 370);
        MinimizeBox = ShowIcon = false;
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(18, 14, 18, 14)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        Add(fields, 0, "Numbering.Field", _field);
        Add(fields, 1, "Numbering.Count", _count);
        Add(fields, 2, "Numbering.Start", _start);
        Add(fields, 3, "Numbering.Step", _step);
        Add(fields, 4, "Numbering.Digits", _digits);
        Add(fields, 5, "Numbering.Prefix", _prefix);
        Add(fields, 6, "Numbering.Suffix", _suffix);
        fields.Controls.Add(_example, 0, 7);
        fields.SetColumnSpan(_example, 2);
        var targets = LabelPrintPages.GetBindings(layout);
        for (var i = 0; i < targets.Count; i++)
        {
            var content = targets[i];
            if (content.Type == CellContentType.Image) continue;
            var value = (content.Type == CellContentType.Barcode ? content.BarcodeData : content.Text) ?? string.Empty;
            if (value.Length > 30) value = value[..30] + "…";
            _field.Items.Add(new Target(i, $"{i + 1}. {L10n.T("Csv.Target." + content.Type)} {value}".TrimEnd()));
        }
        if (_field.Items.Count > 0) _field.SelectedIndex = 0;

        var apply = new Button { Text = L10n.T("Numbering.Generate") };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        void UpdateExample()
        {
            try
            {
                if (_field.SelectedItem is null) throw new InvalidDataException(L10n.T("Numbering.SelectField"));
                var last = LabelNumbering.LastValue((long)_start.Value, (long)_step.Value, (int)_count.Value);
                string Format(long value) => LabelNumbering.Format(value, (int)_digits.Value, _prefix.Text, _suffix.Text);
                _example.Text = string.Format(L10n.T("Numbering.Preview"), Format((long)_start.Value), Format(last));
                _example.ForeColor = Theme.SecondaryText;
                apply.Enabled = true;
            }
            catch (InvalidDataException ex)
            {
                _example.Text = ex.Message;
                _example.ForeColor = Theme.ErrorText;
                apply.Enabled = false;
            }
        }
        foreach (var number in new[] { _count, _start, _step, _digits })
            number.ValueChanged += (_, _) => UpdateExample();
        _field.SelectedIndexChanged += (_, _) => UpdateExample();
        _prefix.TextChanged += (_, _) => UpdateExample();
        _suffix.TextChanged += (_, _) => UpdateExample();
        apply.Click += (_, _) =>
        {
            if (_field.SelectedItem is not Target target) return;
            try
            {
                Prepared = LabelNumbering.Prepare(_layout, target.Index, (int)_count.Value, (long)_start.Value,
                    (long)_step.Value, (int)_digits.Value, _prefix.Text, _suffix.Text);
                DialogResult = DialogResult.OK;
            }
            catch (InvalidDataException) { UpdateExample(); }
        };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(fields);
        Controls.Add(scroll);
        Controls.Add(Theme.DialogFooter(apply, cancel));
        AcceptButton = apply;
        CancelButton = cancel;
        UpdateExample();
        ResumeLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.FitDialogToScreen(this);
    }

    private static NumericUpDown Numeric(decimal min, decimal max, decimal value)
        => new() { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill, ThousandsSeparator = false };

    private static void Add(TableLayoutPanel table, int row, string key, Control field)
    {
        field.AccessibleName = L10n.T(key);
        table.Controls.Add(new Label
        {
            Text = field.AccessibleName, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Text
        }, 0, row);
        table.Controls.Add(field, 1, row);
    }
}
