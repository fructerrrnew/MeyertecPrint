namespace MeyertecPrint;

/// <summary>Системные сообщения с локализованным текстом и безопасным выбором по умолчанию.</summary>
public static class BrandMessageBox
{
    public static DialogResult Show(IWin32Window? owner, string text, string caption,
        MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var defaultButton = buttons == MessageBoxButtons.YesNo
            ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1;
        return owner is null
            ? MessageBox.Show(text, caption, buttons, icon, defaultButton)
            : MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
    }
}
