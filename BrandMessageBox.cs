namespace MeyertecPrint;

/// <summary>Единые диалоги сообщений с теми же результатами, что и стандартные диалоги.</summary>
public static class BrandMessageBox
{
    public static DialogResult Show(IWin32Window? owner, string text, string caption,
        MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using var dialog = new BrandForm { Text = caption, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowIcon = false, ClientSize = new Size(510, 240), MinimumSize = new Size(480, 240),
            BackColor = Color.White, Font = Theme.UiFont(), AutoScaleMode = AutoScaleMode.Dpi };
        var header = new Label { Dock = DockStyle.Top, Height = 54, BackColor = Color.Black, ForeColor = Color.White,
            Padding = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.UiFont(12, FontStyle.Bold), Text = caption };
        var symbol = new Label { Dock = DockStyle.Left, Width = 62, TextAlign = ContentAlignment.TopCenter,
            Padding = new Padding(0, 17, 0, 0), Font = Theme.UiFont(19, FontStyle.Bold), ForeColor = Theme.Dark,
            Text = icon is MessageBoxIcon.Error or MessageBoxIcon.Warning or MessageBoxIcon.Question ? "!" : "i" };
        var content = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Theme.Dark, ScrollBars = ScrollBars.Vertical, Text = text,
            Margin = Padding.Empty, TabStop = false };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 18, 20, 8), BackColor = Color.White };
        body.Controls.Add(content); body.Controls.Add(symbol);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(14, 12, 14, 12),
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White };
        void Add(string key, DialogResult result, bool primary)
        {
            var button = new Button { Text = L10n.T(key), DialogResult = result, Size = new Size(110, 34), Margin = new Padding(8, 0, 0, 0) };
            Theme.StyleButton(button, primary); footer.Controls.Add(button);
            if (primary) dialog.AcceptButton = button;
            if (result == DialogResult.Cancel || (buttons == MessageBoxButtons.OK && result == DialogResult.OK)) dialog.CancelButton = button;
        }
        switch (buttons)
        {
            case MessageBoxButtons.YesNoCancel:
                Add("Dialog.Cancel", DialogResult.Cancel, false); Add("Dialog.No", DialogResult.No, false); Add("Dialog.Yes", DialogResult.Yes, true); break;
            case MessageBoxButtons.YesNo:
                Add("Dialog.No", DialogResult.No, true); Add("Dialog.Yes", DialogResult.Yes, false); break;
            case MessageBoxButtons.OKCancel:
                Add("Dialog.Cancel", DialogResult.Cancel, false); Add("Dialog.OK", DialogResult.OK, true); break;
            default: Add("Dialog.OK", DialogResult.OK, true); break;
        }
        dialog.Controls.Add(body); dialog.Controls.Add(footer); dialog.Controls.Add(header);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
