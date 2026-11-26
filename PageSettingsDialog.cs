namespace MeyertecPrint;

/// <summary>Physical media page size, independent of printer connection settings.</summary>
public sealed class PageSettingsDialog : BrandForm
{
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 300, DecimalPlaces = 3, Increment = .1m, Width = 150 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 762, DecimalPlaces = 3, Increment = .1m, Width = 150 };
    private readonly NumericUpDown? _notchLength;
    private readonly PrinterPlugin? _plugin;
    private readonly Label _error = new()
    {
        Dock = DockStyle.Bottom, Height = 50, ForeColor = Theme.ErrorText, Padding = new Padding(18, 5, 18, 0),
        AccessibleRole = AccessibleRole.Alert, Visible = false
    };
    public long MediaWidthUm => Units.MmToUm((double)_width.Value);
    public long MediaHeightUm => Units.MmToUm((double)_height.Value);
    public long? NotchHeightUm => _notchLength is null ? null : Units.MmToUm((double)_notchLength.Value);
    public PageSettingsDialog(LabelLayout layout, PrinterPlugin? plugin)
    {
        SuspendLayout();
        _plugin = plugin;
        Text = L10n.T("Toolbar.PageSetup");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        var fixedStock = layout.AlternateNotch is not null;
        ClientSize = new Size(530, fixedStock ? 306 : 268);
        if (fixedStock) _height.Minimum = 4;
        _width.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaWidthUm), _width.Minimum, _width.Maximum);
        _height.Value = Math.Clamp((decimal)Units.UmToMm(layout.MediaHeightUm), _height.Minimum, _height.Maximum);
        _width.Enabled = !fixedStock;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), ColumnCount = 2, RowCount = fixedStock ? 4 : 3 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _width.AccessibleName = L10n.T("Setup.Width");
        _height.AccessibleName = L10n.T(fixedStock ? "Setup.FeedPitch" : "Setup.Height");
        _width.TextAlign = _height.TextAlign = HorizontalAlignment.Right;
        fields.Controls.Add(new Label { Text = _width.AccessibleName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        fields.Controls.Add(_width, 1, 0);
        fields.Controls.Add(new Label { Text = _height.AccessibleName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        fields.Controls.Add(_height, 1, 1);
        var hintRow = fixedStock ? 3 : 2;
        if (layout.AlternateNotch is { } notch)
        {
            _notchLength = new NumericUpDown
            {
                Minimum = .001m, Maximum = 762, DecimalPlaces = 3, Increment = .1m, Width = 150,
                Value = Math.Clamp((decimal)Units.UmToMm(notch.HeightUm), .001m, 762m),
                AccessibleName = L10n.T("Setup.NotchLength"), TextAlign = HorizontalAlignment.Right
            };
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            fields.Controls.Add(new Label { Text = _notchLength.AccessibleName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            fields.Controls.Add(_notchLength, 1, 2);
        }
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var hint = new Label { Text = L10n.T(fixedStock ? "Setup.FixedNotchedStock" : "Setup.MeasureMedia"), Dock = DockStyle.Fill, ForeColor = Theme.SecondaryText };
        fields.Controls.Add(hint, 0, hintRow); fields.SetColumnSpan(hint, 2);
        var ok = new Button { Text = L10n.T("Dialog.Apply") };
        ok.Click += (_, _) =>
        {
            if (_plugin is not null)
            {
                var minWidth = _plugin.MinMediaWidthMm > 0 ? _plugin.MinMediaWidthMm : 1;
                var maxWidth = _plugin.MaxMediaWidthMm > 0 ? _plugin.MaxMediaWidthMm : _plugin.MaxPrintWidthMm;
                var minLength = _plugin.MinPrintLengthMm > 0 ? _plugin.MinPrintLengthMm : 1;
                var invalidWidth = (double)_width.Value < minWidth || (double)_width.Value > maxWidth;
                if (invalidWidth ||
                    (double)_height.Value < minLength || (double)_height.Value > _plugin.MaxPrintLengthMm)
                {
                    _error.Text = string.Format(L10n.T("Setup.MediaLimits"), minWidth, maxWidth, minLength, _plugin.MaxPrintLengthMm);
                    _error.Visible = true;
                    (invalidWidth ? _width : _height).Focus();
                    return;
                }
            }
            if (_notchLength is not null)
            {
                try
                {
                    GeometryOperations.ResizeNotchedStockPitch(layout.Clone(), MediaHeightUm, NotchHeightUm);
                }
                catch (InvalidDataException ex)
                {
                    _error.Text = ex.Message;
                    _error.Visible = true;
                    _notchLength.Focus();
                    return;
                }
            }
            DialogResult = DialogResult.OK; Close();
        };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        Controls.Add(fields);
        Controls.Add(_error);
        Controls.Add(Theme.DialogFooter(ok, cancel));
        AcceptButton = ok;
        CancelButton = cancel;
        ResumeLayout();
    }
}
