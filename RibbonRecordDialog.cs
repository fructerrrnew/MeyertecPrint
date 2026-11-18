namespace MeyertecPrint;

/// <summary>Edit the CSV-bound values of a single print record.</summary>
public sealed class RibbonRecordDialog : BrandForm
{
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false, BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None };
    private readonly Dictionary<string, string> _original;
    public Dictionary<string, string> Result { get; private set; } = new();
    public RibbonRecordDialog(LabelLayout layout, Dictionary<string, string> fields)
    {
        SuspendLayout();
        _original = fields;
        Text = L10n.T("Ribbon.EditRecord");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 440);
        MinimumSize = new Size(480, 320);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowIcon = MinimizeBox = false;
        _grid.AccessibleName = Text;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L10n.T("Ribbon.Field"), ReadOnly = true, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L10n.T("Ribbon.Value"), FillWeight = 65 });
        var targets = LabelPrintPages.GetBindings(layout);
        foreach (var (key, value) in fields)
        {
            if (key.Length < 2 || key[0] != 'C' || !int.TryParse(key.AsSpan(1), out var index) || index < 0 || index >= targets.Count) continue;
            var content = targets[index];
            if (content.Type == CellContentType.Image) continue;
            var row = _grid.Rows[_grid.Rows.Add($"{index + 1}. {L10n.T("Csv.Target." + content.Type)}", value)];
            row.Tag = key;
        }
        var ok = new Button { Text = L10n.T("Dialog.Apply") };
        ok.Click += (_, _) =>
        {
            _grid.EndEdit();
            var result = new Dictionary<string, string>(_original);
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Tag is string key) result[key] = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
            try { LabelPrintPages.ApplyFields(layout.Clone(), result); }
            catch (InvalidDataException ex)
            {
                BrandMessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return;
            }
            Result = result; DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel, Width = 110 };
        Controls.Add(_grid); Controls.Add(Theme.DialogFooter(ok, cancel)); AcceptButton = ok; CancelButton = cancel;
        ResumeLayout();
    }
}
