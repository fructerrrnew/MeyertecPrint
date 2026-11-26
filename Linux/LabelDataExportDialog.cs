using Avalonia.Platform.Storage;

namespace MeyertecPrint;

internal sealed partial class LinuxMainWindow
{
    private async Task ExportLabelData()
    {
        if (_project is not { } project) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L10n.T("File.ExportLabelData"),
            SuggestedFileName = project.Name,
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV (*.csv)") { Patterns = ["*.csv"] },
                new FilePickerFileType("Excel (*.xlsx)") { Patterns = ["*.xlsx"] }
            ],
            ShowOverwritePrompt = true
        });
        if (file is null) return;
        var path = file.TryGetLocalPath() ?? throw new IOException(Tr("Выберите локальный файл.", "Choose a local file."));
        LabelDataExport.Write(path, LabelPrintPages.Create(project.Ribbon));
    }
}
