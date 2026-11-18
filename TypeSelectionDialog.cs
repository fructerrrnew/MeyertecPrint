namespace MeyertecPrint;

/// <summary>Каталог типов с предварительным просмотром выбранной позиции.</summary>
public class TypeSelectionDialog : BrandForm
{
    private readonly bool _cleatMode;
    private readonly string? _initialType;
    private readonly ImageList _rowHeight = new() { ImageSize = new Size(1, 32) };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
        MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
        BackColor = Theme.Surface, ForeColor = Theme.Text, Margin = new Padding(0, 0, 12, 0)
    };
    private readonly PictureBox _imageBox = new()
    {
        Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = Padding.Empty, TabStop = false
    };
    private readonly LabelPreviewControl _labelPreview = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _name = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, UseMnemonic = false, ForeColor = Theme.Text,
        Font = Theme.UiFont(13, FontStyle.Bold), Margin = new Padding(0, 0, 0, 3)
    };
    private readonly Label _size = new()
    {
        Dock = DockStyle.Fill, AutoSize = true, UseMnemonic = false,
        ForeColor = Theme.SecondaryText, Margin = Padding.Empty
    };
    private readonly Label _notice = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false,
        Padding = new Padding(18), ForeColor = Theme.SecondaryText
    };
    private readonly Button _okButton;
    private Bitmap? _cleatImage;

    public LabelType? SelectedLabelType { get; private set; }
    public CleatType? SelectedCleat { get; private set; }

    private object? Selection => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag;

    public TypeSelectionDialog(bool cleatMode) : this(cleatMode, creatingProject: true) { }

    protected TypeSelectionDialog(bool cleatMode, bool creatingProject, string? initialType = null)
    {
        SuspendLayout();
        _cleatMode = cleatMode;
        _initialType = initialType;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(850, 410);
        MinimumSize = new Size(670, 350);
        BackColor = Theme.Surface;
        Font = Theme.UiFont();
        var catalogTitle = L10n.T(creatingProject ? (cleatMode ? "TypeSel.TitleCleat" : "TypeSel.TitleLabel") : "TypeDlg.Title");
        Text = creatingProject ? string.Format(L10n.T("NewProject.Step"), catalogTitle, 2, cleatMode ? 3 : 2) : catalogTitle;

        _list.AccessibleName = catalogTitle;
        _list.SmallImageList = _rowHeight;
        _list.Columns.Add(L10n.T(cleatMode ? "TypeSel.CleatType" : "TypeSel.Name"));
        _list.Columns.Add(L10n.T(cleatMode ? "TypeSel.TerminalSize" : "TypeSel.Size"));
        if (!cleatMode) _list.Columns.Add(L10n.T("TypeSel.InRowCount"), -2, HorizontalAlignment.Center);
        FillList();

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16)
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.Controls.Add(_list, 0, 0);
        main.Controls.Add(BuildDetails(), 1, 0);

        _okButton = new Button
        {
            Text = L10n.T(creatingProject ? (cleatMode ? "NewProject.Next" : "NewProject.Create") : "Dialog.Apply"),
            DialogResult = DialogResult.OK, Enabled = false
        };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        var back = creatingProject
            ? new Button { Text = L10n.T("Dialog.Back"), DialogResult = DialogResult.Retry }
            : null;
        Controls.Add(main);
        Controls.Add(Theme.DialogFooter(_okButton, cancel, back));
        AcceptButton = _okButton;
        CancelButton = cancel;

        _list.Resize += (_, _) => FitColumns();
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();
        _list.DoubleClick += (_, _) => _okButton.PerformClick();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || !_okButton.Enabled) return;
            e.SuppressKeyPress = true;
            _okButton.PerformClick();
        };
        Shown += (_, _) =>
        {
            if (_list.Items.Count > 0)
            {
                var initial = _list.Items.Cast<ListViewItem>().FirstOrDefault(item =>
                    item.Tag is LabelType type && type.Name == (_initialType ?? LabelTypeCatalog.Stock60x30)) ?? _list.Items[0];
                initial.Selected = initial.Focused = true;
                initial.EnsureVisible();
            }
            UpdateSelection();
            FitColumns();
            _list.Focus();
        };
        ResumeLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.FitDialogToScreen(this);
        _rowHeight.ImageSize = new Size(1, LogicalToDeviceUnits(32));
    }

    private TableLayoutPanel BuildDetails()
    {
        var surface = SystemInformation.HighContrast ? Theme.Surface : Theme.LightBackground;
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = surface, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(15, 13, 15, 11), Margin = Padding.Empty
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var illustration = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        illustration.Controls.Add(_cleatMode ? (Control)_imageBox : _labelPreview);
        illustration.Controls.Add(_notice);
        details.Controls.Add(_name, 0, 0);
        details.Controls.Add(_size, 0, 1);
        details.Controls.Add(illustration, 0, 2);
        return details;
    }

    private void FitColumns()
    {
        var width = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
        if (width <= 0) return;
        _list.Columns[1].Width = width * (_cleatMode ? 42 : 30) / 100;
        if (!_cleatMode) _list.Columns[2].Width = width * 19 / 100;
        _list.Columns[0].Width = width - _list.Columns[1].Width - (_cleatMode ? 0 : _list.Columns[2].Width);
    }

    private void FillList()
    {
        if (_cleatMode)
        {
            foreach (var cleat in CleatCatalog.All)
                _list.Items.Add(new ListViewItem([cleat.DisplayName, cleat.Size.ToString("0.###")]) { Tag = cleat });
            return;
        }

        foreach (var type in LabelTypeCatalog.All)
        {
            var item = new ListViewItem([
                type.DisplayName + (type.ReferenceOnly ? "  *" : ""),
                type.SizeDisplay,
                type.Geometry.LabelsInRow.ToString()
            ]) { Tag = type };
            if (type.ReferenceOnly)
            {
                item.ForeColor = Theme.SecondaryText;
                item.ToolTipText = L10n.T("Catalog.ReferenceHint");
            }
            _list.Items.Add(item);
        }
    }

    private void UpdateSelection()
    {
        var cleat = Selection as CleatType;
        var type = Selection as LabelType;
        _okButton.Enabled = cleat is not null || type is { ReferenceOnly: false };
        _name.Text = cleat?.DisplayName ?? type?.DisplayName ?? string.Empty;
        _size.Text = cleat is not null
            ? string.Format(L10n.T("TypeSel.TerminalSection"), cleat.Size)
            : type?.SizeDisplay ?? string.Empty;
        _imageBox.Image = null;
        _cleatImage?.Dispose();
        _cleatImage = null;
        if (cleat?.ResolveImagePath() is { } path)
        {
            try
            {
                using var original = new Bitmap(path);
                _cleatImage = new Bitmap(original);
                _imageBox.Image = _cleatImage;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }

        _labelPreview.SetType(type is { ReferenceOnly: false } ? type : null);
        if (_list.Items.Count == 0)
            _notice.Text = L10n.T(_cleatMode ? "TypeSel.Empty" : "TypeSel.LabelCatalogEmpty");
        else if (type?.ReferenceOnly == true)
            _notice.Text = L10n.T("Catalog.ReferenceHint");
        else if (cleat is not null && _cleatImage is null)
            _notice.Text = L10n.T("TypeSel.NoImage");
        else
            _notice.Text = string.Empty;

        _notice.Visible = _notice.Text.Length > 0;
        _imageBox.Visible = cleat is not null && !_notice.Visible;
        _labelPreview.Visible = type is { ReferenceOnly: false };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            SelectedLabelType = Selection as LabelType;
            SelectedCleat = Selection as CleatType;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _rowHeight.Dispose();
        _cleatImage?.Dispose();
    }
}

/// <summary>Рисует пропорциональную схему раскладки на носителе.</summary>
public sealed class LabelPreviewControl : Control
{
    private LabelType? _type;

    public LabelPreviewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = SystemInformation.HighContrast ? Theme.Surface : Theme.LightBackground;
    }

    public void SetType(LabelType? type)
    {
        _type = type;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_type is null) return;

        var geometry = _type.Geometry;
        var inRow = Math.Max(1, geometry.LabelsInRow);
        var sheetWidth = geometry.LeftUm + inRow * geometry.WidthUm + (inRow - 1) * geometry.GapBetweenUm + geometry.RightUm;
        var sheetHeight = geometry.TopUm + geometry.HeightUm + geometry.GapBottomUm;
        var available = Rectangle.Inflate(ClientRectangle, -LogicalToDeviceUnits(24), -LogicalToDeviceUnits(24));
        if (sheetWidth <= 0 || sheetHeight <= 0 || available.Width <= 0 || available.Height <= 0) return;

        var scale = Math.Min(available.Width / (double)sheetWidth, available.Height / (double)sheetHeight);
        var width = (float)(sheetWidth * scale);
        var height = (float)(sheetHeight * scale);
        var left = (Width - width) / 2;
        var top = (Height - height) / 2;
        using var edge = new Pen(Theme.SecondaryText);
        e.Graphics.FillRectangle(Brushes.White, left, top, width, height);
        e.Graphics.DrawRectangle(edge, left, top, width, height);

        using var labelPen = new Pen(Theme.Dark) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        var labelWidth = (float)(geometry.WidthUm * scale);
        var labelHeight = (float)(geometry.HeightUm * scale);
        for (var i = 0; i < inRow; i++)
        {
            var x = left + (float)((geometry.LeftUm + i * (geometry.WidthUm + geometry.GapBetweenUm)) * scale);
            var y = top + (float)(geometry.TopUm * scale);
            e.Graphics.DrawRectangle(labelPen, x, y, labelWidth, labelHeight);
        }
    }
}
