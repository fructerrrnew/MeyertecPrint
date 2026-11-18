namespace MeyertecPrint;

/// <summary>Physical media page size, independent of printer connection settings.</summary>
public sealed class PageSettingsDialog : BrandForm
{
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 300, DecimalPlaces = 3, Increment = .1m, Width = 150 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 762, DecimalPlaces = 3, Increment = .1m, Width = 150 };
    private readonly PrinterPlugin? _plugin;
    private readonly Label _error = new() { Dock = DockStyle.Bottom, Height = 42, ForeColor = Color.Firebrick, Padding = new Padding(15, 5, 0, 0) };
    public long MediaWidthUm => Units.MmToUm((double)_width.Value);
    public long MediaHeightUm => Units.MmToUm((double)_height.Value);
    public PageSettingsDialog(LabelLayout layout, PrinterPlugin? plugin)
    {
        _plugin = plugin;
        Text = L10n.T("Toolbar.PageSetup"); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(540, 290); Font = Theme.UiFont(); BackColor = Color.White;
        _width.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaWidthUm), _width.Minimum, _width.Maximum);
        _height.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaHeightUm), _height.Minimum, _height.Maximum);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), ColumnCount = 2, RowCount = 3 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.Controls.Add(new Label { Text = L10n.T("Setup.Width"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        fields.Controls.Add(_width, 1, 0);
        fields.Controls.Add(new Label { Text = L10n.T("Setup.Height"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        fields.Controls.Add(_height, 1, 1);
        var hint = new Label { Text = L10n.T("Setup.MeasureMedia"), Dock = DockStyle.Fill, ForeColor = Theme.SecondaryText };
        fields.Controls.Add(hint, 0, 2); fields.SetColumnSpan(hint, 2);
        var ok = new Button { Text = L10n.T("Dialog.OK"), Width = 110 };
        ok.Click += (_, _) =>
        {
            if (_plugin is not null)
            {
                var minWidth = _plugin.MinMediaWidthMm > 0 ? _plugin.MinMediaWidthMm : 1;
                var maxWidth = _plugin.MaxMediaWidthMm > 0 ? _plugin.MaxMediaWidthMm : _plugin.MaxPrintWidthMm;
                var minLength = _plugin.MinPrintLengthMm > 0 ? _plugin.MinPrintLengthMm : 1;
                if ((double)_width.Value < minWidth || (double)_width.Value > maxWidth ||
                    (double)_height.Value < minLength || (double)_height.Value > _plugin.MaxPrintLengthMm)
                { _error.Text = string.Format(L10n.T("Setup.MediaLimits"), minWidth, maxWidth, minLength, _plugin.MaxPrintLengthMm); return; }
            }
            DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel, Width = 110 };
        Controls.Add(fields); Controls.Add(_error); Controls.Add(Theme.DialogFooter(ok, cancel)); AcceptButton = ok; CancelButton = cancel;
    }
}
