namespace MeyertecPrint;

/// <summary>
/// Ввод данных для Code 128 или EAN-13 с проверкой значения.
/// </summary>
public class BarcodeDialog : BrandForm
{
    private readonly ComboBox _symbolBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
    };

    private readonly TextBox _valueBox = new()
    {
        Dock = DockStyle.Fill,
    };

    private readonly Label _validationLabel = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Theme.ErrorText,
        UseMnemonic = false,
        AccessibleRole = AccessibleRole.Alert
    };

    private readonly Button _okButton = new()
    {
        Text = L10n.T("Dialog.Apply"),
        DialogResult = DialogResult.OK,
        Width = 110,
        Enabled = false,
    };

    public BarcodeSymbol Symbol { get; private set; } = BarcodeSymbol.Code128;

    public string Value { get; private set; } = string.Empty;

    public BarcodeDialog(string? initialValue = null, BarcodeSymbol initialSymbol = BarcodeSymbol.Code128)
    {
        SuspendLayout();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(456, 211);
        BackColor = Theme.Surface;
        Font = Theme.UiFont();
        Text = L10n.T("Barcode.Title");

        _symbolBox.Items.AddRange(new object[] { "Code 128", "EAN-13" });
        _symbolBox.SelectedIndex = 0;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Height = 88,
            Padding = new Padding(16, 12, 16, 4),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));

        _symbolBox.AccessibleName = L10n.T("Barcode.Symbol");
        _valueBox.AccessibleName = L10n.T("Barcode.Value");
        fields.Controls.Add(MakeLabel(_symbolBox.AccessibleName), 0, 0);
        fields.Controls.Add(_symbolBox, 1, 0);
        fields.Controls.Add(MakeLabel(_valueBox.AccessibleName), 0, 1);
        fields.Controls.Add(_valueBox, 1, 1);

        _validationLabel.Visible = false;
        var validationPanel = new Panel { Dock = DockStyle.Top, Height = 53, Padding = new Padding(18, 4, 16, 0) };
        validationPanel.Controls.Add(_validationLabel);

        _symbolBox.SelectedIndexChanged += (_, _) => Validate();
        _valueBox.TextChanged += (_, _) => Validate();
        _symbolBox.SelectedIndex = initialSymbol == BarcodeSymbol.Ean13 ? 1 : 0;
        _valueBox.Text = initialValue ?? string.Empty;
        Validate();

        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        var buttons = Theme.DialogFooter(_okButton, cancelButton);

        Controls.Add(validationPanel);
        Controls.Add(fields);
        Controls.Add(buttons);
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        Shown += (_, _) => _valueBox.Focus();
        ResumeLayout();
    }

    private new void Validate()
    {
        Symbol = _symbolBox.SelectedIndex == 1 ? BarcodeSymbol.Ean13 : BarcodeSymbol.Code128;
        var value = _valueBox.Text;
        var error = string.Empty;

        if (string.IsNullOrEmpty(value))
        {
            error = L10n.T("Barcode.ErrorEmpty");
        }
        else if (Symbol == BarcodeSymbol.Ean13)
        {
            error = BarcodeEncoder.IsValidEan13(value) ? string.Empty : L10n.T("Barcode.ErrorEan");
        }
        else
        {
            error = value.All(ch => ch >= 32 && ch <= 126)
                ? string.Empty
                : L10n.T("Barcode.ErrorCode128");
        }

        _validationLabel.Visible = error.Length > 0;
        _validationLabel.Text = error;
        _okButton.Enabled = error.Length == 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _okButton.Enabled)
            Value = _valueBox.Text;
        base.OnFormClosing(e);
    }

    private static Label MakeLabel(string text)
        => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text
        };
}
