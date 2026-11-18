namespace MeyertecPrint;

public class LabelTypeDialog : TypeSelectionDialog
{
    public LabelTypeDialog() : this(null) { }

    internal LabelTypeDialog(string? initialType) : base(cleatMode: false, creatingProject: false, initialType) { }

    public LabelType? SelectedType => SelectedLabelType;
}
