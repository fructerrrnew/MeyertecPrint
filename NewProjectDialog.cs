namespace MeyertecPrint;

public enum NewProjectMode { Label, Cleat }

public class NewProjectDialog : BrandForm
{
    private readonly TextBox _nameBox = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.UiFont(12), Margin = Padding.Empty
    };
    private readonly RadioButton _cleat;
    public string ProjectName => _nameBox.Text.Trim();
    public NewProjectMode Mode => _cleat.Checked ? NewProjectMode.Cleat : NewProjectMode.Label;

    public NewProjectDialog()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 255);
        BackColor = Color.White;
        Font = Theme.UiFont();
        Text = L10n.T("NewProject.Title");

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(24, 14, 24, 12)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var height in new[] { 28, 42, 30, 47 })
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var name = FieldLabel("NewProject.Name");
        fields.Controls.Add(name, 0, 0);
        fields.SetColumnSpan(name, 2);
        _nameBox.AccessibleName = name.Text;
        fields.Controls.Add(_nameBox, 0, 1);
        fields.SetColumnSpan(_nameBox, 2);
        var mode = FieldLabel("NewProject.Mode");
        fields.Controls.Add(mode, 0, 2);
        fields.SetColumnSpan(mode, 2);
        var label = ModeButton("NewProject.ByLabel", "Label");
        _cleat = ModeButton("NewProject.ByCleat", "Terminal");
        _cleat.Margin = new Padding(4, 0, 0, 0);
        fields.Controls.Add(label, 0, 3);
        fields.Controls.Add(_cleat, 1, 3);
        label.Checked = true;

        var next = new Button { Text = L10n.T("NewProject.Next"), DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        _nameBox.TextChanged += (_, _) => next.Enabled = ProjectName.Length > 0;
        Controls.Add(fields);
        Controls.Add(Theme.DialogFooter(next, cancel));
        AcceptButton = next;
        CancelButton = cancel;
        Shown += (_, _) => _nameBox.Focus();
    }

    private static Label FieldLabel(string key) => new()
    {
        Text = L10n.T(key), AutoSize = true, ForeColor = Theme.Dark,
        Font = Theme.UiFont(10, FontStyle.Bold), Margin = Padding.Empty
    };

    private RadioButton ModeButton(string key, string icon)
    {
        var button = new RadioButton
        {
            Appearance = Appearance.Button, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0),
            FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Dark,
            Text = "  " + L10n.T(key), Image = Theme.Icon(icon, LogicalToDeviceUnits(23)),
            TextImageRelation = TextImageRelation.ImageBeforeText, TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.CheckedBackColor = Theme.SoftYellow;
        button.FlatAppearance.MouseOverBackColor = Theme.Hover;
        button.CheckedChanged += (_, _) =>
            button.FlatAppearance.BorderColor = button.Checked ? Color.FromArgb(169, 124, 0) : Theme.Border;
        return button;
    }
}
