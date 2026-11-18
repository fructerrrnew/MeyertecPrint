using System.Drawing.Text;

namespace MeyertecPrint;

/// <summary>
/// Диалог QR-кода: ссылка (или произвольный текст) до 256 символов,
/// живой предпросмотр — QR генерируется из введённой ссылки по мере ввода.
/// Внесённый QR ЗАМЕНЯЕТ содержимое ячейки (поведение эталона).
/// </summary>
public class QrCodeDialog : BrandForm
{
    public const int MaxLength = 256;

    private readonly TextBox _textBox = new()
    {
        Dock = DockStyle.Fill,
        Font = Theme.UiFont(11f),
    };

    private readonly Label _counterLabel = new()
    {
        AutoSize = true,
        ForeColor = Theme.SecondaryText,
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
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
        Text = L10n.T("Dialog.OK"),
        DialogResult = DialogResult.OK,
        Width = 110,
    };

    private Bitmap? _preview;

    /// <summary>Введённый текст/ссылка (в пределах лимита).</summary>
    public string QrText { get; private set; } = string.Empty;

    public QrCodeDialog(string? initialText = null)
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 260);
        BackColor = Theme.LightBackground;
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
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

        var header = new Label
        {
            Text = L10n.T("Qr.Link"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Dark,
        };

        _counterLabel.Text = CounterText(MaxLength);
        _textBox.TextChanged += (_, _) =>
        {
            var leftCount = MaxLength - _textBox.Text.Length;
            _counterLabel.Text = CounterText(leftCount);
            _counterLabel.ForeColor = leftCount < 0 ? Color.Firebrick : Theme.SecondaryText;
            _okButton.Enabled = leftCount >= 0;
            UpdatePreview();
        };

        _textBox.Text = initialText ?? string.Empty;
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
    }

    /// <summary>Перерисовывает предпросмотр: QR генерируется из введённой ссылки.</summary>
    private void UpdatePreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewBox.Image = null;

        var data = _textBox.Text;
        if (string.IsNullOrEmpty(data))
            return;

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
        }
        catch
        {
            // Данные не кодируются в QR (превышен объём) — предпросмотр пустой
        }
    }

    private static string CounterText(int left)
        => string.Format(L10n.T("Qr.Counter"), left);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _textBox.Text.Length <= MaxLength)
            QrText = _textBox.Text;
        _preview?.Dispose();
        base.OnFormClosing(e);
    }
}
