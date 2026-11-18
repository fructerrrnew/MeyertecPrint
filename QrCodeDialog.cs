using System.Drawing.Text;

namespace MeyertecPrint;

/// <summary>
/// Ввод данных и предварительный просмотр QR-кода.
/// </summary>
public class QrCodeDialog : BrandForm
{
    public const int MaxLength = 256;

    private readonly TextBox _textBox = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Font = Theme.UiFont(11)
    };

    private readonly Label _counterLabel = new()
    {
        AutoSize = true,
        ForeColor = Theme.SecondaryText,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false
    };

    private readonly PictureBox _previewBox = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private readonly Button _okButton = new()
    {
        Text = L10n.T("Dialog.Apply"),
        DialogResult = DialogResult.OK,
        Width = 110,
    };

    private Bitmap? _preview;

    /// <summary>Введённый текст/ссылка (в пределах лимита).</summary>
    public string QrText { get; private set; } = string.Empty;

    public QrCodeDialog(string? initialText = null)
    {
        SuspendLayout();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 278);
        BackColor = Theme.Surface;
        Font = Theme.UiFont();
        Text = L10n.T("Qr.Title");

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 12, 8, 8),
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        var header = new Label
        {
            Text = L10n.T("Qr.Link"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text
        };
        _textBox.AccessibleName = header.Text;
        _previewBox.AccessibleName = L10n.T("Qr.Preview");
        _textBox.TextChanged += (_, _) => UpdatePreview();

        _textBox.Text = initialText ?? string.Empty;
        UpdatePreview();
        left.Controls.Add(header, 0, 0);
        left.Controls.Add(_textBox, 0, 1);
        left.Controls.Add(_counterLabel, 0, 2);

        var right = new Panel
        {
            Dock = DockStyle.Right,
            Width = 200,
            Padding = new Padding(8, 12, 14, 8),
        };
        right.Controls.Add(_previewBox);

        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        var buttons = Theme.DialogFooter(_okButton, cancelButton);

        Controls.Add(left);
        Controls.Add(right);
        Controls.Add(buttons);
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _textBox.Focus();
        ResumeLayout();
    }

    /// <summary>Перерисовывает предпросмотр: QR генерируется из введённой ссылки.</summary>
    private void UpdatePreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewBox.Image = null;

        var data = _textBox.Text;
        var left = MaxLength - data.Length;
        _counterLabel.Text = left < 0 ? L10n.T("Qr.TooLong") : string.Format(L10n.T("Qr.Counter"), data.Length, MaxLength);
        _counterLabel.ForeColor = left < 0 ? Theme.ErrorText : Theme.SecondaryText;
        _okButton.Enabled = false;
        if (data.Length == 0 || left < 0) return;

        try
        {
            var matrix = QrEncoder.Encode(data);
            const int module = 4;
            const int quiet = 4;
            var size = matrix.GetLength(0) + quiet * 2;
            _preview = new Bitmap(size * module, size * module);
            using var g = Graphics.FromImage(_preview);
            g.Clear(Color.White);
            using var black = new SolidBrush(Color.Black);
            for (var r = 0; r < matrix.GetLength(0); r++)
            for (var c = 0; c < matrix.GetLength(1); c++)
            {
                if (matrix[r, c])
                    g.FillRectangle(black, (c + quiet) * module, (r + quiet) * module, module, module);
            }
            _previewBox.Image = _preview;
            _okButton.Enabled = true;
        }
        catch (Exception ex) when (ex is ZXing.WriterException or ArgumentException)
        {
            _counterLabel.Text = L10n.T("Qr.InvalidData");
            _counterLabel.ForeColor = Theme.ErrorText;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _okButton.Enabled)
            QrText = _textBox.Text;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview?.Dispose();
        base.Dispose(disposing);
    }
}
