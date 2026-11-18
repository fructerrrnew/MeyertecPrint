namespace MeyertecPrint;

/// <summary>Размеры маркера вводятся пользователем: размеры корпуса клеммы — другие данные.</summary>
public sealed class MarkerSizeDialog : BrandForm
{
    private readonly NumericUpDown _width = new() { Minimum = 0, Maximum = 105.7m, DecimalPlaces = 3, Increment = 0.1m, Dock = DockStyle.Fill };
    private readonly NumericUpDown _height = new() { Minimum = 0, Maximum = 300, DecimalPlaces = 3, Increment = 0.1m, Dock = DockStyle.Fill };
    public long WidthUm => Units.MmToUm((double)_width.Value);
    public long HeightUm => Units.MmToUm((double)_height.Value);
    public MarkerSizeDialog(string name)
    {
        SuspendLayout();
        Text = string.Format(L10n.T("NewProject.Step"), L10n.T("Marker.Title"), 3, 3);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowIcon = false;
        ClientSize = new Size(490, 240);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        var info = new Label { Dock = DockStyle.Top, Height = 70, Padding = new Padding(18, 12, 18, 0), Text = string.Format(L10n.T("Marker.Hint"), name) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, Height = 90, ColumnCount = 2, RowCount = 2, Padding = new Padding(18, 4, 18, 4) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _width.AccessibleName = L10n.T("Marker.Width");
        _height.AccessibleName = L10n.T("Marker.Height");
        _width.TextAlign = _height.TextAlign = HorizontalAlignment.Right;
        grid.Controls.Add(new Label { Text = _width.AccessibleName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        grid.Controls.Add(new Label { Text = _height.AccessibleName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        grid.Controls.Add(_width, 1, 0); grid.Controls.Add(_height, 1, 1);
        var ok = new Button { Text = L10n.T("NewProject.Create"), Enabled = false, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        var back = new Button { Text = L10n.T("Dialog.Back"), DialogResult = DialogResult.Retry };
        _width.ValueChanged += (_, _) => ok.Enabled = _width.Value > 0 && _height.Value > 0;
        _height.ValueChanged += (_, _) => ok.Enabled = _width.Value > 0 && _height.Value > 0;
        Controls.Add(grid); Controls.Add(info); Controls.Add(Theme.DialogFooter(ok, cancel, back));
        AcceptButton = ok; CancelButton = cancel;
        ResumeLayout();
    }
}
