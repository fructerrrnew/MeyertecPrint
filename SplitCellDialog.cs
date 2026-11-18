namespace MeyertecPrint;

/// <summary>
/// Разбивка ячейки на колонки (аналог SplitCellDialog эталона):
/// число колонок (минимум 2), равное деление ширины, превью,
/// ширина колонки. Контент ячейки копируется во все под-ячейки.
/// </summary>
public class SplitCellDialog : BrandForm
{
    private readonly NumericUpDown _countBox = new()
    {
        Minimum = 2,
        Maximum = 16,
        Value = 2,
        Width = 70,
    };

    private readonly TextBox _widthBox = new()
    {
        ReadOnly = true,
        Width = 110,
        BackColor = Color.White,
    };

    private readonly PreviewPanel _preview;

    private readonly long _cellWidthUm;

    /// <summary>Число колонок (>= 2).</summary>
    public int ColumnCount => (int)_countBox.Value;

    public SplitCellDialog(long cellWidthUm)
    {
        SuspendLayout();
        _cellWidthUm = cellWidthUm;

        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 300);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();
        Text = L10n.T("Split.TitleColumns");

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Height = 64,
            Padding = new Padding(16, 12, 16, 4),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        fields.Controls.Add(MakeLabel(L10n.T("Split.Columns")), 0, 0);
        fields.Controls.Add(_countBox, 1, 0);
        fields.Controls.Add(MakeLabel(L10n.T("Split.ColumnWidth")), 0, 1);
        fields.Controls.Add(_widthBox, 1, 1);

        _preview = new PreviewPanel((g, rect) =>
        {
            var count = (int)_countBox.Value;
            using var pen = new Pen(Theme.Dark, 1f);
            g.DrawRectangle(pen, rect);
            var step = rect.Width / count;
            for (var i = 1; i < count; i++)
                g.DrawLine(pen, rect.X + step * i, rect.Y, rect.X + step * i, rect.Bottom);
        });
        _preview.Dock = DockStyle.Fill;

        _countBox.ValueChanged += (_, _) => UpdateWidth();

        var okButton = new Button
        {
            Text = L10n.T("Dialog.OK"),
            DialogResult = DialogResult.OK,
            Width = 110,
        };
        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        var buttons = Theme.DialogFooter(okButton, cancelButton);

        Controls.Add(_preview);
        Controls.Add(fields);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;
        UpdateWidth();
        ResumeLayout();
    }

    private void UpdateWidth()
    {
        var per = _cellWidthUm / (int)_countBox.Value;
        _widthBox.Text = $"{Units.UmToMm(per):0.###} {L10n.T("Unit.Mm")}";
        _preview.Invalidate();
    }

    private static Label MakeLabel(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Dark,
        };
}

/// <summary>Панель превью: светлый фон + рисуемая область в центре.
/// Логика отрисовки передаётся конструктором.</summary>
public class PreviewPanel : Panel
{
    private readonly Action<Graphics, RectangleF>? _painter;

    public PreviewPanel(Action<Graphics, RectangleF>? painter)
    {
        _painter = painter;
        BackColor = Color.White;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Paint += (_, e) =>
        {
            var rect = RectangleF.FromLTRB(
                e.ClipRectangle.Left + 24, e.ClipRectangle.Top + 24,
                e.ClipRectangle.Right - 24, e.ClipRectangle.Bottom - 24);
            _painter?.Invoke(e.Graphics, rect);
        };
    }
}
