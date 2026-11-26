namespace MeyertecPrint;

public partial class MainForm
{
    private void ExportLabelData()
    {
        if (_project is not { } project) return;
        _editor.CommitPendingEdit();
        var title = L10n.T("File.ExportLabelData");
        try
        {
            using var dialog = new BrandedSaveFileDialog
            {
                Title = title,
                Filter = "CSV (*.csv)|*.csv|Excel (*.xlsx)|*.xlsx",
                FileName = project.Name,
                AddExtension = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                LabelDataExport.Write(dialog.FileName, LabelPrintPages.Create(project.Ribbon));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            BrandMessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
