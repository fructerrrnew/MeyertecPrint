namespace MeyertecPrint;

/// <summary>
/// Выбор типа этикетки: список 37 позиций (Custom label — первая, как в эталоне).
/// Выбор подставляет геометрию типа в редактор.
/// </summary>
public class LabelTypeDialog : BrandForm
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        BackColor = Color.White,
        ForeColor = Theme.Dark,
    };

    /// <summary>Выбранный тип (null, если пользователь отменил).</summary>
    public LabelType? SelectedType { get; private set; }

    public LabelTypeDialog()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 420);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();
        Text = L10n.T("TypeDlg.Title");

        var nameColumn = new ColumnHeader { Width = 220 };
        var sizeColumn = new ColumnHeader { Width = 110 };
        var inRowColumn = new ColumnHeader { Width = 80 };
        _list.Columns.Add(nameColumn);
        _list.Columns.Add(sizeColumn);
        _list.Columns.Add(inRowColumn);

        var items = new List<ListViewItem>();
        foreach (var type in LabelTypeCatalog.All)
        {
            var item = new ListViewItem(new[]
            {
                type.DisplayName + (type.ReferenceOnly ? "  *" : ""),
                type.SizeDisplay,
                type.Geometry.LabelsInRow.ToString(),
            });
            item.Tag = type;
            items.Add(item);
        }
        _list.Items.AddRange(items.ToArray());
        if (_list.Items.Count > 0)
            _list.Items[0].Selected = true;

        var okButton = new Button
        {
            Text = L10n.T("Dialog.OK"),
            DialogResult = DialogResult.OK,
            Width = 110,
        };
        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        _list.SelectedIndexChanged += (_, _) =>
            okButton.Enabled = _list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is LabelType { ReferenceOnly: false };
        var buttons = Theme.DialogFooter(okButton, cancelButton);

        var reference = new Label { Text = L10n.T("Catalog.ReferenceHint"), Dock = DockStyle.Top,
            Height = 52, Padding = new Padding(10, 8, 10, 4), ForeColor = Theme.SecondaryText };
        Controls.Add(_list);
        Controls.Add(reference);
        Controls.Add(buttons);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        ApplyColumnHeaders();
        L10n.Changed += ApplyColumnHeaders;
        Shown += (_, _) =>
        {
            L10n.Changed -= ApplyColumnHeaders;
            _list.Focus();
        };
    }

    private void ApplyColumnHeaders()
    {
        _list.Columns[0].Text = L10n.T("TypeDlg.Name");
        _list.Columns[1].Text = L10n.T("TypeDlg.Size");
        _list.Columns[2].Text = L10n.T("TypeDlg.LabelsInRow");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SelectedType = _list.SelectedItems.Count > 0
            ? _list.SelectedItems[0].Tag as LabelType
            : null;
        base.OnFormClosing(e);
    }
}
