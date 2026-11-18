namespace MeyertecPrint;

/// <summary>
/// Разбивка строки на строки (аналог SplitRowDialog эталона):
/// число строк (минимум 2), равное деление высоты, превью с размером полосы,
/// высота новой строки. Контент ячеек копируется в каждую новую строку.
/// </summary>
public class SplitRowDialog : BrandForm
{
    private readonly NumericUpDown _countBox = new()
    {
        Minimum = 2,
        Maximum = 10,
        Value = 2,
        Width = 70,
    };

    private readonly TextBox _heightBox = new()
    {
        ReadOnly = true,
        Width = 110,
        BackColor = Color.White,
    };

    private readonly PreviewPanel _preview;

    private readonly long _rowHeightUm;

    /// <summary>Число строк (>= 2).</summary>
    public int RowCount => (int)_countBox.Value;

    public SplitRowDialog(long rowHeightUm, long stripWidthUm, long stripHeightUm)
    {
        SuspendLayout();
        _rowHeightUm = rowHeightUm;

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
        Text = L10n.T("Split.TitleRows");

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

        fields.Controls.Add(MakeLabel(L10n.T("Split.Rows")), 0, 0);
        fields.Controls.Add(_countBox, 1, 0);
        fields.Controls.Add(MakeLabel(L10n.T("Split.RowHeight")), 0, 1);
        fields.Controls.Add(_heightBox, 1, 1);

        var aspect = stripWidthUm > 0 && stripHeightUm > 0 ? stripWidthUm / (float)stripHeightUm : 4f;
        _preview = new PreviewPanel((g, rect) =>
        {
            // Полоса с учётом аспекта + горизонтальные линии разбивки
            var h = rect.Height;
            var w = Math.Min(rect.Width, h * aspect);
            var full = RectangleF.FromLTRB(rect.X + (rect.Width - w) / 2, rect.Y, rect.X + (rect.Width - w) / 2 + w, rect.Bottom);
            using var pen = new Pen(Theme.Dark, 1f);
            g.DrawRectangle(pen, full);
            var count = (int)_countBox.Value;
            var step = full.Height / count;
            for (var i = 1; i < count; i++)
                g.DrawLine(pen, full.X, full.Y + step * i, full.Right, full.Y + step * i);
            var sizeText = $"{Units.UmToMm(stripWidthUm):0.###} × {Units.UmToMm(stripHeightUm):0.###} {L10n.T("Unit.Mm")}";
            g.DrawString(sizeText, Theme.UiFont(8.5f), Brushes.Gray, full.X, full.Bottom + 4);
        });
        _preview.Dock = DockStyle.Fill;

        _countBox.ValueChanged += (_, _) => UpdateHeight();

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
        UpdateHeight();
        ResumeLayout();
    }

    private void UpdateHeight()
    {
        var per = _rowHeightUm / (int)_countBox.Value;
        _heightBox.Text = $"{Units.UmToMm(per):0.###} {L10n.T("Unit.Mm")}";
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
