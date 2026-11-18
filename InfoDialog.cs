namespace MeyertecPrint;

/// <summary>
/// Информационная панель (аналог Info block эталона): название, версия,
/// дата, описание + кнопка закрытия.
/// </summary>
public class InfoDialog : BrandForm
{
    public InfoDialog()
    {
        SuspendLayout();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 230);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();
        Text = L10n.T("Info.Title");

        var title = new Label
        {
            Text = "MEYERTEC PRINT",
            Font = new Font(Theme.UiFont(13f), FontStyle.Bold),
            ForeColor = Theme.Dark,
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(16, 10, 16, 0),
        };

        var version = new Label
        {
            Text = string.Format(L10n.T("Info.Version"),
                typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"),
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(16, 2, 16, 0),
            ForeColor = Theme.SecondaryText,
        };

        var date = new Label
        {
            Text = string.Format(L10n.T("Info.Date"), DateTime.Now.ToString("yyyy-MM-dd")),
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(16, 0, 16, 0),
            ForeColor = Theme.SecondaryText,
        };

        var body = new Label
        {
            Text = L10n.T("Info.Text"),
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 0),
            ForeColor = Theme.Dark,
        };

        var closeButton = new Button
        {
            Text = L10n.T("Info.Close"),
            DialogResult = DialogResult.OK,
            Width = 110,
        };
        var buttons = Theme.DialogFooter(closeButton);

        Controls.Add(body);
        Controls.Add(date);
        Controls.Add(version);
        Controls.Add(title);
        Controls.Add(buttons);
        AcceptButton = closeButton;
        CancelButton = closeButton;
        ResumeLayout();
    }
}
