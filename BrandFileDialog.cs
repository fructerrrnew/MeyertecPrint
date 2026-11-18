namespace MeyertecPrint;

/// <summary>Файловые диалоги Windows с фильтрами приложения и последней выбранной папкой.</summary>
public abstract class BrandFileDialog : IDisposable
{
    private static string? _lastDirectory;
    private readonly bool _save;

    public string Title { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool AddExtension { get; set; }

    protected BrandFileDialog(bool save) => _save = save;

    public DialogResult ShowDialog(IWin32Window? owner)
    {
        using FileDialog dialog = _save
            ? new SaveFileDialog { OverwritePrompt = true }
            : new OpenFileDialog { CheckFileExists = true };
        dialog.Title = Title;
        dialog.Filter = string.IsNullOrEmpty(Filter) ? $"{L10n.T("File.FilterAll")} (*.*)|*.*" : Filter;
        dialog.FileName = FileName;
        dialog.AddExtension = AddExtension;
        dialog.CheckPathExists = true;
        dialog.RestoreDirectory = true;
        if (Directory.Exists(_lastDirectory)) dialog.InitialDirectory = _lastDirectory;

        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result == DialogResult.OK)
        {
            FileName = dialog.FileName;
            _lastDirectory = Path.GetDirectoryName(FileName);
        }
        return result;
    }

    public void Dispose() { }
}

public sealed class BrandedOpenFileDialog : BrandFileDialog { public BrandedOpenFileDialog() : base(false) { } }
public sealed class BrandedSaveFileDialog : BrandFileDialog { public BrandedSaveFileDialog() : base(true) { } }
