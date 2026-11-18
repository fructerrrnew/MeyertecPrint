namespace MeyertecPrint;

/// <summary>
/// Стартовое окно: показывается при запуске приложения.
/// Позволяет создать новый проект или открыть существующий.
/// OK = новый проект, Yes = открыть, Cancel = закрыть без действия.
/// </summary>
public class StartupDialog : BrandForm
{
    public StartupDialog()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 250);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();
        Text = L10n.T("Startup.Title");

        var newButton = MakeActionButton(L10n.T("Startup.NewProject"), DialogResult.OK);
        var openButton = MakeActionButton(L10n.T("Startup.OpenProject"), DialogResult.Yes);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(24, 20, 24, 8),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        newButton.Margin = new Padding(0, 0, 0, 12);
        panel.Controls.Add(newButton, 0, 0);
        panel.Controls.Add(openButton, 0, 1);

        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(24, 4, 24, 12),
        };
        buttons.Controls.Add(cancelButton);

        Controls.Add(panel);
        Controls.Add(buttons);
        AcceptButton = newButton;
        CancelButton = cancelButton;
    }

    /// <summary>Большая фирменная кнопка: тёмная, при наведении — жёлтая.</summary>
    private static Button MakeActionButton(string text, DialogResult result)
    {
        var button = new Button
        {
            Text = text,
            DialogResult = result,
            Dock = DockStyle.Fill,
            Height = 44,
            Font = Theme.UiFont(10f),
            BackColor = Theme.Dark,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        button.FlatAppearance.BorderSize = 0;
        button.MouseEnter += (_, _) =>
        {
            button.BackColor = Theme.Yellow;
            button.ForeColor = Theme.Dark;
        };
        button.MouseLeave += (_, _) =>
        {
            button.BackColor = Theme.Dark;
            button.ForeColor = Color.White;
        };
        return button;
    }
}
