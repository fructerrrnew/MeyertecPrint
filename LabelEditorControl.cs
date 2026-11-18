using System.Drawing.Text;

namespace MeyertecPrint;

/// <summary>Инструменты редактора (левая боковая панель, как в Photoshop).</summary>
public enum EditorTool
{
    Select,
    Text,
    Qr,
    Image,
    Barcode,
    SplitColumns,
    SplitRows,
}

/// <summary>
/// Редактор полосы этикетки в стиле Photoshop:
/// левая боковая панель инструментов (V — выделение, T — текст, Q — QR,
/// I — изображение, B — штрихкод, разбиение), верхняя контекстная панель
/// (для текста: шрифт / размер / жирный; для остальных — подсказка),
/// панель свойств с геометрией (мм на экране / мкм внутри) и холст с ячейками.
/// </summary>
public partial class LabelEditorControl : UserControl
{
    private LabelLayout _layout = new();
    private string _typeName = "Custom label";
    private int? _selectedRow;
    private int? _selectedCol;
    private bool _updating;
    private bool _unitInch;
    private bool _updatingTextOpts;
    private bool _bold;
    private EditorTool _tool = EditorTool.Select;
    private LabelLayout? _editSnapshot;
    private readonly UndoRedo _undo = new();
    private readonly List<(int Row, int Col, RectangleF Rect)> _cellRects = new();
    private readonly Dictionary<EditorTool, Button> _toolButtons = new();

    private readonly Button _undoButton;
    private readonly Button _redoButton;
    private readonly Button _typeButton;
    private Font? _inlineFont;
    private readonly ToolTip _tips = new();
    private readonly Dictionary<Control, string> _captions = new();
    private readonly Dictionary<Control, string> _toolKeys = new();
    private readonly FlowLayoutPanel _contextBar;
    private readonly FlowLayoutPanel _textOptions;
    private readonly Label _contextHint;
    private readonly ComboBox _fontBox;
    private readonly NumericUpDown _fontSizeBox;
    private readonly Button _boldButton;
    private readonly Label _unitLabel;
    private readonly Panel _canvasPanel;
    private readonly TextBox _inlineBox;
    private readonly CanvasPanel _canvas;

    private readonly NumericUpDown _inRowBox;
    private readonly NumericUpDown _topBox;
    private readonly NumericUpDown _leftBox;
    private readonly NumericUpDown _heightBox;
    private readonly NumericUpDown _widthBox;
    private readonly NumericUpDown _rightBox;
    private readonly NumericUpDown _gapBetweenBox;
    private readonly NumericUpDown _gapBottomBox;

    /// <summary>Макет изменён (главное окно помечает проект как не сохранённый).</summary>
    public event Action? LayoutChanged;

    /// <summary>Текущий макет (для сохранения и предпросмотра).</summary>
    public new LabelLayout Layout => _layout;

    /// <summary>Имя типа, загруженного в редактор.</summary>
    public string TypeName => _typeName;

    public LabelEditorControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        Font = Theme.UiFont();
        _canvas = new CanvasPanel { TabStop = true };
        var header = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White,
            Padding = new Padding(12, 7, 12, 7)
        };
        _typeButton = MakeIconButton("Label", "TypeDlg.Title", OpenTypeDialog);
        _typeButton.AutoEllipsis = true;
        _typeButton.Dock = DockStyle.Left;
        _typeButton.Width = 260;
        _typeButton.Font = Theme.UiFont(11, FontStyle.Bold);
        _typeButton.TextAlign = ContentAlignment.MiddleLeft;
        _typeButton.ImageAlign = ContentAlignment.MiddleLeft;
        _typeButton.Padding = new Padding(8, 0, 0, 0);
        _typeButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        var history = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, WrapContents = false,
            Margin = Padding.Empty, BackColor = Color.White
        };
        _undoButton = MakeIconButton("Undo", "Editor.Undo", DoUndo);
        _redoButton = MakeIconButton("Redo", "Editor.Redo", DoRedo);
        _undoButton.Enabled = _redoButton.Enabled = false;
        history.Controls.Add(_undoButton);
        history.Controls.Add(_redoButton);
        history.Controls.Add(MakeIconButton("Info", "Editor.Info", ShowInfo));
        header.Controls.Add(_typeButton);
        header.Controls.Add(history);

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoScroll = true,
            WrapContents = false, BackColor = Theme.RailSurface, Margin = Padding.Empty,
            Padding = new Padding(6, 6, 6, 6)
        };
        foreach (var tool in Enum.GetValues<EditorTool>())
        {
            if (tool == EditorTool.SplitColumns)
            {
                tools.Controls.Add(new Panel
                {
                    Size = new Size(24, 1), Margin = new Padding(6, 6, 6, 6), BackColor = Theme.Border
                });
            }
            var button = MakeIconButton(tool.ToString(), "Tool." + tool, () =>
            {
                SetTool(tool);
                _canvas.Focus();
            }, iconSize: 20);
            button.Tag = tool;
            button.Size = new Size(36, 34);
            button.Margin = new Padding(0, 1, 0, 1);
            tools.Controls.Add(button);
            _toolButtons[tool] = button;
        }

        var inspector = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true, BackColor = Color.White
        };
        var geometry = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(17, 10, 17, 12), BackColor = Color.White
        };
        geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        inspector.Controls.Add(geometry);
        AddSection(geometry, 0, "Inspector.Dimensions");
        _inRowBox = MakeMmBox(isInt: true, min: 1, max: 8, increment: 1m);
        _topBox = MakeMmBox();
        _leftBox = MakeMmBox();
        _heightBox = MakeMmBox(min: 1, max: 300);
        _widthBox = MakeMmBox(min: 1, max: 300);
        _rightBox = MakeMmBox();
        _gapBetweenBox = MakeMmBox();
        _gapBottomBox = MakeMmBox();

        AddGeometryRow(geometry, 1, "Editor.Width", _widthBox);
        AddGeometryRow(geometry, 2, "Editor.Height", _heightBox);
        AddGeometryRow(geometry, 3, "Editor.LabelsInRow", _inRowBox);
        AddSection(geometry, 4, "Inspector.Spacing");
        AddGeometryRow(geometry, 5, "Editor.Top", _topBox);
        AddGeometryRow(geometry, 6, "Editor.Left", _leftBox);
        AddGeometryRow(geometry, 7, "Editor.Right", _rightBox);
        AddGeometryRow(geometry, 8, "Editor.GapBetween", _gapBetweenBox);
        AddGeometryRow(geometry, 9, "Editor.GapBottom", _gapBottomBox);

        _unitLabel = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Height = 25,
            ForeColor = Theme.SecondaryText, Margin = new Padding(0, 8, 0, 0)
        };
        geometry.Controls.Add(_unitLabel, 0, 10);
        geometry.SetColumnSpan(_unitLabel, 2);

        _inRowBox.ValueChanged += (_, _) => OnGeometryChanged();
        _topBox.ValueChanged += (_, _) => OnGeometryChanged();
        _leftBox.ValueChanged += (_, _) => OnGeometryChanged();
        _heightBox.ValueChanged += (_, _) => OnGeometryChanged();
        _widthBox.ValueChanged += (_, _) => OnGeometryChanged();
        _rightBox.ValueChanged += (_, _) => OnGeometryChanged();
        _gapBetweenBox.ValueChanged += (_, _) => OnGeometryChanged();
        _gapBottomBox.ValueChanged += (_, _) => OnGeometryChanged();

        // ---------- Верхняя контекстная панель (опции активного инструмента) ----------
        _contextBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Height = 44, Margin = Padding.Empty,
            Padding = new Padding(8, 3, 8, 3),
            BackColor = Color.White,
        };

        _textOptions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0),
        };
        _fontBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
            Margin = new Padding(2, 6, 6, 0),
        };
        PopulateFonts();
        _fontSizeBox = new NumericUpDown
        {
            Minimum = 6,
            Maximum = 96,
            Value = 12,
            Width = 56,
            Margin = new Padding(2, 5, 6, 0),
        };
        _boldButton = new Button
        {
            Image = Theme.Icon("Bold", LogicalToDeviceUnits(18)),
            Size = new Size(30, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            Margin = new Padding(2, 5, 6, 0),
        };
        _boldButton.FlatAppearance.BorderColor = Theme.Border;
        _boldButton.FlatAppearance.MouseOverBackColor = Theme.Yellow;
        _boldButton.FlatAppearance.MouseDownBackColor = Theme.Yellow;
        _boldButton.Click += (_, _) =>
        {
            _bold = !_bold;
            ApplyTextOptionsStyle();
            OnTextOptionsChanged();
        };
        _fontBox.SelectedIndexChanged += (_, _) => OnTextOptionsChanged();
        _fontSizeBox.ValueChanged += (_, _) => OnTextOptionsChanged();

        _textOptions.Controls.Add(OptionLabel("TextOpts.Font"));
        _textOptions.Controls.Add(_fontBox);
        _textOptions.Controls.Add(OptionLabel("TextOpts.Size"));
        _textOptions.Controls.Add(_fontSizeBox);
        _textOptions.Controls.Add(_boldButton);

        _contextHint = new Label
        {
            AutoSize = true,
            ForeColor = Theme.SecondaryText,
            Margin = new Padding(10, 10, 0, 0),
        };
        _contextBar.Controls.Add(_textOptions);
        _contextBar.Controls.Add(_contextHint);

        // ---------- Холст ----------
        _canvas.Paint += OnCanvasPaint;
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseMove += OnObjectMouseMove;
        _canvas.MouseUp += OnObjectMouseUp;
        _canvas.DoubleClick += OnCanvasDoubleClick;
        _inlineBox = new TextBox
        {
            Visible = false,
            Font = Theme.UiFont(13f),
            TextAlign = HorizontalAlignment.Center,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Theme.Dark,
        };
        _inlineBox.LostFocus += OnInlineLostFocus;
        _canvasPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Canvas, Margin = Padding.Empty,
        };
        _canvasPanel.AutoScroll = true;
        _canvasPanel.Controls.Add(_canvas);
        _canvas.Controls.Add(_inlineBox);
        _canvasPanel.Resize += (_, _) => ResizePhysicalCanvas();

        var work = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(1, 0, 1, 0),
            RowCount = 3, ColumnCount = 1, BackColor = Theme.Border
        };
        work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        work.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        work.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        work.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        work.Controls.Add(header, 0, 0);
        work.Controls.Add(_canvasPanel, 0, 1);
        work.Controls.Add(_contextBar, 0, 2);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Border
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(tools, 0, 0);
        root.Controls.Add(work, 1, 0);
        root.Controls.Add(inspector, 2, 0);
        Controls.Add(root);
        InitializeObjectEditor(geometry, history);
        ApplyStrings();
        SetTool(EditorTool.Select);
    }

    // ---------------- ПУБЛИЧНОЕ API ----------------

    /// <summary>Загружает макет (новый проект / открытие файла / .wstx).</summary>
    public void LoadLayout(LabelLayout layout, string typeName, PrintSettings? printSettings = null)
    {
        CommitInline();
        _layout = layout;
        _layout.Objects ??= new();
        _displayPrintSettings = printSettings ?? new PrintSettings();
        _selectedObject = null;
        _typeName = typeName;
        _selectedRow = null;
        _selectedCol = null;
        _undo.Clear();
        _typeButton.Text = "  " + (typeName == "Custom label" ? L10n.T("Catalog.Custom") : typeName) + "  ▾";
        UpdateGeometryBoxes();
        UpdateSelectionUi();
        ResizePhysicalCanvas();
        InvalidateCanvas();
    }

    /// <summary>Единицы отображения: true — дюймы (внутри всегда мкм).</summary>
    public void SetUnitInch(bool inch)
    {
        if (_unitInch == inch) return;
        CommitInline();
        _unitInch = inch;
        _updating = true;
        var unit = inch ? 25.4m : 1m;
        foreach (var field in new[] { _widthBox, _heightBox, _leftBox, _rightBox, _topBox, _gapBetweenBox, _gapBottomBox })
        {
            var dimension = field == _widthBox || field == _heightBox;
            field.Minimum = (dimension ? 1m : 0m) / unit;
            field.Maximum = (dimension ? 300m : 1000m) / unit;
            field.Increment = inch ? 0.01m : 0.1m;
        }
        _updating = false;
        _unitLabel.Text = L10n.T(inch ? "Editor.AllInch" : "Editor.AllMm");
        InvalidateCanvas();
        UpdateGeometryBoxes();
        ConfigureObjectUnits();
    }

    public void ApplyPrintSettings(PrintSettings settings, long mediaWidthUm, long mediaHeightUm)
    {
        CommitInline();
        if (_layout.MediaWidthUm != mediaWidthUm || _layout.MediaHeightUm != mediaHeightUm)
        {
            _undo.Push(_layout.Clone());
            var g = _layout.Geometry;
            if (_layout.Rows.Count == 1 && _layout.Rows[0].Cells.Count == 1 && g.LabelsInRow == 1 &&
                g.WidthUm == _layout.MediaWidthUm && g.HeightUm == _layout.MediaHeightUm &&
                _layout.Rows[0].HeightUm == g.HeightUm && _layout.Rows[0].Cells[0].WidthUm == g.WidthUm &&
                g.LeftUm == 0 && g.RightUm == 0 && g.TopUm == 0 && g.GapBetweenUm == 0 && g.GapBottomUm == 0)
            {
                g.WidthUm = mediaWidthUm; g.HeightUm = mediaHeightUm;
                _layout.Rows[0].HeightUm = mediaHeightUm;
                _layout.Rows[0].Cells[0].WidthUm = mediaWidthUm;
            }
        }
        _layout.MediaWidthUm = mediaWidthUm;
        _layout.MediaHeightUm = mediaHeightUm;
        UpdateGeometryBoxes();
        _displayPrintSettings = settings;
        SelectCell(null, null);
        ResizePhysicalCanvas();
        InvalidateCanvas();
        LayoutChanged?.Invoke();
    }

    public bool HasProject => _layout.Rows.Count > 0;

    // ---------------- ВЗАИМОДЕЙСТВИЕ ----------------

    private void OnCanvasPaint(object? sender, PaintEventArgs e)
        => DrawPhysicalCanvas(e.Graphics, e.ClipRectangle);

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
        => HandlePhysicalMouseDown(e);

    private void OnCanvasDoubleClick(object? sender, EventArgs e)
        => EditSelectedObject();

    private void SelectCell(int? row, int? col)
    {
        CommitInline();
        _selectedObject = null;
        _selectedRow = row;
        _selectedCol = col;
        UpdateSelectionUi();
        InvalidateCanvas();
    }

    private void UpdateSelectionUi()
    {
        _inlineBox.Visible = false;
        _editSnapshot = null;
        UpdateContextBar();
        UpdateObjectFields();
    }

    private void ApplyInlineFont(LabelCell cell, RectangleF rect)
    {
        var previous = _inlineFont;
        _inlineBox.Font = _inlineFont = FontForCell(cell, rect);
        previous?.Dispose();
    }

    private void PositionInlineBox(RectangleF rect)
    {
        _inlineBox.SetBounds((int)rect.X + 2, (int)(rect.Y + (rect.Height - _inlineBox.Height) / 2),
            Math.Max(1, (int)rect.Width - 4), _inlineBox.Height);
    }

    private void OnInlineLostFocus(object? sender, EventArgs e)
    {
        CommitInline();
    }

    /// <summary>Применяет текст из инлайн-поля к ячейке (с откатом, если изменился).</summary>
    private void CommitInline()
    {
        if (!_inlineBox.Visible)
        {
            _editSnapshot = null;
            return;
        }
        var changed = false;
        if (_selectedRow is not null && _selectedCol is not null)
        {
            var row = _layout.Rows[_selectedRow.Value];
            if (_selectedCol.Value < row.Cells.Count)
            {
                var cell = row.Cells[_selectedCol.Value];
                var newText = _inlineBox.Text;
                if (newText != cell.Content.Text ||
                    cell.Content.Type is not (CellContentType.Text or CellContentType.Empty))
                {
                    if (_editSnapshot is not null) _undo.Push(_editSnapshot);
                    cell.Content = new CellContent
                    {
                        Type = newText.Length == 0 ? CellContentType.Empty : CellContentType.Text,
                        Text = newText,
                        FontName = cell.Content.FontName,
                        FontSizePt = cell.Content.FontSizePt,
                        Bold = cell.Content.Bold,
                    };
                    changed = true;
                }
            }
        }
        _inlineBox.Visible = false;
        _editSnapshot = null;
        InvalidateCanvas();
        if (changed) LayoutChanged?.Invoke();
    }

    /// <summary>Завершает текущий ввод перед предпросмотром или печатью.</summary>
    public void CommitPendingEdit() => CommitInline();

    // ---------------- ГЕОМЕТРИЯ ----------------

    private void OnGeometryChanged()
    {
        if (_updating || _layout.Rows.Count == 0) return;

        var count = (int)_inRowBox.Value;
        var width = MmValueToUm(_widthBox.Value);
        var height = MmValueToUm(_heightBox.Value);
        var countChanged = count != _layout.Geometry.LabelsInRow;
        if (countChanged && _layout.Rows.Any(row => row.Cells.Count > count ||
            row.Cells.Count != _layout.Geometry.LabelsInRow))
        {
            if (BrandMessageBox.Show(FindForm(), L10n.T("Geometry.ConfirmRestructure"),
                L10n.T("Geometry.Title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                UpdateGeometryBoxes();
                return;
            }
        }

        // Calculate on a copy: an invalid resize must not partially change the project.
        var changed = _layout.Clone();
        var g = changed.Geometry;
        g.LabelsInRow = count;
        g.TopUm = MmValueToUm(_topBox.Value);
        g.LeftUm = MmValueToUm(_leftBox.Value);
        g.HeightUm = height;
        g.WidthUm = width;
        g.RightUm = MmValueToUm(_rightBox.Value);
        g.GapBetweenUm = MmValueToUm(_gapBetweenBox.Value);
        g.GapBottomUm = MmValueToUm(_gapBottomBox.Value);
        try { GeometryOperations.Resize(changed, countChanged); }
        catch (InvalidDataException ex)
        {
            UpdateGeometryBoxes();
            BrandMessageBox.Show(FindForm(), ex.Message, L10n.T("Geometry.Title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _undo.Push(_layout);
        _layout = changed;
        SelectCell(null, null);
        InvalidateCanvas();
        LayoutChanged?.Invoke();
    }

    private void UpdateGeometryBoxes()
    {
        _updating = true;
        var g = _layout.Geometry;
        _inRowBox.Value = ClampDecimal(g.LabelsInRow, _inRowBox.Minimum, _inRowBox.Maximum);
        _topBox.Value = UmToField(g.TopUm);
        _leftBox.Value = UmToField(g.LeftUm);
        _heightBox.Value = ClampDecimal(UmToField(g.HeightUm), _heightBox.Minimum, _heightBox.Maximum);
        _widthBox.Value = ClampDecimal(UmToField(g.WidthUm), _widthBox.Minimum, _widthBox.Maximum);
        _rightBox.Value = UmToField(g.RightUm);
        _gapBetweenBox.Value = UmToField(g.GapBetweenUm);
        _gapBottomBox.Value = UmToField(g.GapBottomUm);
        _updating = false;
        _undoButton.Enabled = _undo.CanUndo;
        _redoButton.Enabled = _undo.CanRedo;
    }

    // Конвертация: поле (мм или дюйм) → мкм
    private long MmValueToUm(decimal fieldValue)
        => _unitInch
            ? Units.MmToUm((double)fieldValue * 25.4)
            : Units.MmToUm((double)fieldValue);

    // Конвертация: мкм → поле (мм или дюйм)
    private decimal UmToField(long um)
    {
        var mm = Units.UmToMm(um);
        return _unitInch ? (decimal)mm / 25.4m : (decimal)mm;
    }

    // ---------------- ДЕЙСТВИЯ НАД ЯЧЕЙКОЙ ----------------

    private (LabelLayout Layout, int Row, int Col)? SelectedCell()
        => _selectedRow is not null && _selectedCol is not null
            ? (_layout, _selectedRow.Value, _selectedCol.Value)
            : null;

    private void SplitColumnsAction()
    {
        if (SelectedCell() is not { } sel) return;
        using var dialog = new SplitCellDialog(sel.Layout.Rows[sel.Row].Cells[sel.Col].WidthUm);
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
            return;

        _undo.Push(sel.Layout.Clone());
        sel.Layout.SplitCellIntoColumns(sel.Row, sel.Col, dialog.ColumnCount);
        SelectCell(sel.Row, sel.Col);
        LayoutChanged?.Invoke();
    }

    private void SplitRowsAction()
    {
        if (SelectedCell() is not { } sel) return;
        using var dialog = new SplitRowDialog(sel.Layout.Rows[sel.Row].HeightUm,
            sel.Layout.StripWidthUm, sel.Layout.StripHeightUm);
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
            return;

        _undo.Push(sel.Layout.Clone());
        sel.Layout.SplitRowIntoRows(sel.Row, dialog.RowCount);
        SelectCell(sel.Row, sel.Col);
        LayoutChanged?.Invoke();
    }

    private void QrAction()
    {
        if (SelectedCell() is not { } sel) return;
        using var dialog = new QrCodeDialog();
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
            return;

        _undo.Push(sel.Layout.Clone());
        // QR ЗАМЕНЯЕТ содержимое ячейки (поведение эталона)
        sel.Layout.Rows[sel.Row].Cells[sel.Col].Content = new CellContent
        {
            Type = CellContentType.Qr,
            Text = dialog.QrText,
        };
        SelectCell(sel.Row, sel.Col);
        LayoutChanged?.Invoke();
    }

    private void BarcodeAction()
    {
        if (SelectedCell() is not { } sel) return;
        using var dialog = new BarcodeDialog();
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
            return;

        _undo.Push(sel.Layout.Clone());
        sel.Layout.Rows[sel.Row].Cells[sel.Col].Content = new CellContent
        {
            Type = CellContentType.Barcode,
            BarcodeSymbol = dialog.Symbol,
            BarcodeData = dialog.Value,
        };
        SelectCell(sel.Row, sel.Col);
        LayoutChanged?.Invoke();
    }

    private void ImageAction()
    {
        if (SelectedCell() is not { } sel) return;
        using var dialog = new BrandedOpenFileDialog
        {
            Title = L10n.T("Editor.Image"),
            Filter = $"{L10n.T("File.FilterImage")} (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
        };
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK)
            return;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            BrandMessageBox.Show(this.FindForm(),
                string.Format(L10n.T("Error.ImageLoad"), ex.Message),
                "MEYERTEC PRINT", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _undo.Push(sel.Layout.Clone());
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        sel.Layout.Rows[sel.Row].Cells[sel.Col].Content = new CellContent
        {
            Type = CellContentType.Image,
            ImageBase64 = Convert.ToBase64String(bytes),
            ImageFormat = ext is ".jpg" or ".jpeg" ? "jpeg" : "png",
        };
        SelectCell(sel.Row, sel.Col);
        LayoutChanged?.Invoke();
    }

    // ---------------- UNDO / REDO ----------------

    private void DoUndo()
    {
        CommitInline();
        if (_undo.Undo(ref _layout))
        {
            SelectCell(null, null);
            UpdateGeometryBoxes();
            ResizePhysicalCanvas();
            InvalidateCanvas();
            LayoutChanged?.Invoke();
        }
    }

    private void DoRedo()
    {
        CommitInline();
        if (_undo.Redo(ref _layout))
        {
            SelectCell(null, null);
            UpdateGeometryBoxes();
            ResizePhysicalCanvas();
            InvalidateCanvas();
            LayoutChanged?.Invoke();
        }
    }

    private void ShowInfo()
    {
        using var dialog = new InfoDialog();
        dialog.ShowDialog(this.FindForm());
    }

    /// <summary>Перерисовать холст (после внешних правок макета, напр. импорт CSV).</summary>
    public void Redraw() => InvalidateCanvas();

    // ---------------- ТИП ЭТИКЕТКИ ----------------

    private void OpenTypeDialog()
    {
        using var dialog = new LabelTypeDialog();
        if (dialog.ShowDialog(this.FindForm()) != DialogResult.OK || dialog.SelectedType is null)
            return;

        var type = dialog.SelectedType;
        if (_layout.Rows.Any(row => row.Cells.Count > type.Geometry.LabelsInRow) &&
            BrandMessageBox.Show(FindForm(), L10n.T("Geometry.ConfirmRestructure"), L10n.T("Geometry.Title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _undo.Push(_layout.Clone());
        _layout = BuildLayoutFor(type, _layout);
        _typeName = type.Name;
        _typeButton.Text = "  " + type.DisplayName + "  ▾";
        SelectCell(null, null);
        UpdateGeometryBoxes();
        InvalidateCanvas();
        LayoutChanged?.Invoke();
    }

    /// <summary>
    /// Макет под выбранный тип: геометрия типа; строки пересобираются,
    /// контент ячеек с совпадающими индексами сохраняется.
    /// </summary>
    public static LabelLayout BuildLayoutFor(LabelType type, LabelLayout previous)
    {
        var layout = new LabelLayout
        {
            Geometry = type.Geometry.Clone(),
            MediaWidthUm = previous.MediaWidthUm,
            MediaHeightUm = previous.MediaHeightUm,
            Objects = (previous.Objects ?? new()).Select(o => o.Clone()).ToList(),
        };
        for (var r = 0; r < previous.Rows.Count; r++)
        {
            var row = previous.Rows[r];
            var newRow = new LabelRow
            {
                HeightUm = layout.Geometry.HeightUm / Math.Max(1, previous.Rows.Count),
            };
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                newRow.Cells.Add(new LabelCell
                {
                    WidthUm = layout.Geometry.WidthUm,
                    Content = cell.Content is { Type: not CellContentType.Empty }
                        ? new CellContent
                        {
                            Type = cell.Content.Type,
                            Text = cell.Content.Text,
                            BarcodeData = cell.Content.BarcodeData,
                            BarcodeSymbol = cell.Content.BarcodeSymbol,
                            ImageBase64 = cell.Content.ImageBase64,
                            ImageFormat = cell.Content.ImageFormat,
                            FontName = cell.Content.FontName,
                            FontSizePt = cell.Content.FontSizePt,
                            Bold = cell.Content.Bold,
                        }
                        : new CellContent(),
                });
            }
            layout.Rows.Add(newRow);
        }
        if (layout.Rows.Count == 0)
        {
            var row = new LabelRow { HeightUm = layout.Geometry.HeightUm };
            for (var c = 0; c < layout.Geometry.LabelsInRow; c++)
                row.Cells.Add(new LabelCell { WidthUm = layout.Geometry.WidthUm });
            layout.Rows.Add(row);
        }
        // приводим число ячеек к LabelsInRow типа
        foreach (var row in layout.Rows)
        {
            while (row.Cells.Count < layout.Geometry.LabelsInRow)
                row.Cells.Add(new LabelCell { WidthUm = layout.Geometry.WidthUm });
            if (row.Cells.Count > layout.Geometry.LabelsInRow)
                row.Cells.RemoveRange(layout.Geometry.LabelsInRow, row.Cells.Count - layout.Geometry.LabelsInRow);
            foreach (var cell in row.Cells)
                cell.WidthUm = layout.Geometry.WidthUm;
        }
        return layout;
    }

    private void InvalidateCanvas()
    {
        _canvasRasterValid = false;
        _undoButton.Enabled = _undo.CanUndo;
        _redoButton.Enabled = _undo.CanRedo;
        _canvas.Invalidate();
    }

    // ---------------- СЛУЖЕБНОЕ ----------------

    private NumericUpDown MakeMmBox(bool isInt = false, decimal min = 0, decimal max = 1000, decimal increment = 0.1m)
    {
        var effectiveMin = isInt ? 1m : min;
        return new NumericUpDown
        {
            Minimum = effectiveMin,
            Maximum = max,
            DecimalPlaces = isInt ? 0 : 3,
            Increment = isInt ? 1m : (_unitInch ? 0.01m : increment),
            Width = 86, Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 5, 0, 5),
            // Значение ставим последним и в пределах [Minimum, Maximum],
            // иначе NumericUpDown бросает ArgumentOutOfRangeException
            Value = Math.Clamp(isInt ? 1m : 0m, effectiveMin, max),
        };
    }

    private Label Caption(string key)
    {
        var label = new Label { Text = L10n.T(key), ForeColor = Theme.Dark, UseMnemonic = false };
        _captions.Add(label, key);
        return label;
    }

    private Label OptionLabel(string key)
    {
        var label = Caption(key);
        label.AutoSize = true;
        label.Margin = new Padding(8, 10, 3, 0);
        return label;
    }

    private void AddSection(TableLayoutPanel panel, int row, string key)
    {
        var label = Caption(key);
        label.Dock = DockStyle.Fill;
        label.Font = Theme.UiFont(11, FontStyle.Bold);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, row == 0 ? 0 : 8, 0, 2);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 0 ? 40 : 46));
        panel.Controls.Add(label, 0, row);
        panel.SetColumnSpan(label, 2);
    }

    private void AddGeometryRow(TableLayoutPanel panel, int row, string key, Control field)
    {
        var label = Caption(key);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Margin = new Padding(0, 0, 4, 0);
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(field, 1, row);
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        => Math.Clamp(value, min, max);

    private Button MakeIconButton(string icon, string key, Action action, int iconSize = 23)
    {
        var button = new Button
        {
            Image = Theme.Icon(icon, LogicalToDeviceUnits(iconSize)), Size = new Size(35, 35), Margin = new Padding(2, 0, 0, 0)
        };
        Theme.StyleButton(button);
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        _toolKeys.Add(button, key);
        return button;
    }

    public void ApplyStrings()
    {
        foreach (var (control, key) in _captions) control.Text = L10n.T(key);
        foreach (var (button, key) in _toolKeys)
        {
            button.AccessibleName = L10n.T(key);
            _tips.SetToolTip(button, button.AccessibleName);
        }
        _unitLabel.Text = L10n.T(_unitInch ? "Editor.AllInch" : "Editor.AllMm");
        _typeButton.Text = "  " + (_typeName == "Custom label" ? L10n.T("Catalog.Custom") : _typeName) + "  ▾";
        _boldButton.AccessibleName = L10n.T("TextOpts.Bold");
        _tips.SetToolTip(_boldButton, _boldButton.AccessibleName);
        ApplyObjectStrings();
        UpdateContextBar();
        InvalidateCanvas();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvasRaster?.Dispose();
            _tips.Dispose();
            _inlineFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------- ИНСТРУМЕНТЫ ----------------

    private void SetTool(EditorTool tool)
    {
        _tool = tool;
        foreach (var (t, button) in _toolButtons)
        {
            var active = t == tool;
            button.BackColor = active ? Theme.RailSelection : Theme.RailSurface;
            button.FlatAppearance.BorderSize = active ? 1 : 0;
            button.FlatAppearance.BorderColor = Theme.RailSelectionBorder;
            button.FlatAppearance.MouseOverBackColor = active ? Theme.RailSelection : Theme.Hover;
            button.FlatAppearance.MouseDownBackColor = Theme.RailSelection;
        }
        UpdateContextBar();
    }

    /// <summary>
    /// Верхняя контекстная панель: для инструмента T — группа опций текста,
    /// для остальных — подсказка, что делать кликом по ячейке.
    /// </summary>
    private void UpdateContextBar()
    {
        _textOptions.Visible = HasProject && (_tool == EditorTool.Text || _selectedObject?.Content.Type == CellContentType.Text);
        _contextHint.Visible = !_textOptions.Visible;
        _contextHint.Text = L10n.T(_tool switch
        {
            EditorTool.Select => "Hint.Select",
            EditorTool.Text => "Hint.Text",
            EditorTool.Qr => "Hint.Qr",
            EditorTool.Image => "Hint.Image",
            EditorTool.Barcode => "Hint.Barcode",
            EditorTool.SplitColumns => "Hint.SplitColumns",
            _ => "Hint.SplitRows",
        });
    }

    /// <summary>Текстовый блок в ячейке: шрифт из верхней панели, свой текст сохраняется.</summary>
    private void StartTextInCell(int row, int col)
    {
        var cell = _layout.Rows[row].Cells[col];
        var keepText = cell.Content.Type is CellContentType.Text or CellContentType.Empty
            ? cell.Content.Text
            : string.Empty;
        cell.Content = new CellContent
        {
            Type = CellContentType.Text,
            Text = keepText,
            FontName = _fontBox.SelectedItem as string,
            FontSizePt = (float)_fontSizeBox.Value,
            Bold = _bold,
        };
        SelectCell(row, col);
    }

    /// <summary>Шрифт/размер/жирный из верхней панели применяются к выбранной ячейке.</summary>
    private void OnTextOptionsChanged()
    {
        if (_updatingTextOpts || !HasProject)
            return;
        if (_selectedObject is { Content.Type: CellContentType.Text } selected)
        {
            _undo.Push(_layout.Clone());
            selected.Content.FontName = _fontBox.SelectedItem as string;
            selected.Content.FontSizePt = (float)_fontSizeBox.Value;
            selected.Content.Bold = _bold;
            InvalidateCanvas();
            LayoutChanged?.Invoke();
            return;
        }
        if (_tool != EditorTool.Text) return;
        if (_selectedRow is not { } row || _selectedCol is not { } col)
            return;
        if (row >= _layout.Rows.Count || col >= _layout.Rows[row].Cells.Count)
            return;
        var cell = _layout.Rows[row].Cells[col];
        if (cell.Content.Type is not (CellContentType.Text or CellContentType.Empty))
            return;

        _undo.Push(_layout.Clone());
        cell.Content = new CellContent
        {
            Type = CellContentType.Text,
            Text = cell.Content.Text,
            FontName = _fontBox.SelectedItem as string,
            FontSizePt = (float)_fontSizeBox.Value,
            Bold = _bold,
        };
        if (_inlineBox.Visible)
        {
            var rect = _cellRects.FirstOrDefault(r => r.Row == row && r.Col == col).Rect;
            if (!rect.Size.IsEmpty)
                ApplyInlineFont(cell, rect);
        }
        InvalidateCanvas();
        LayoutChanged?.Invoke();
    }

    private void ApplyTextOptionsStyle()
    {
        _boldButton.BackColor = _bold ? Theme.Yellow : Color.White;
    }

    /// <summary>Настройки шрифта выбранной ячейки загружаются в верхнюю панель.</summary>
    private void SyncTextOptionsFromCell(CellContent content)
    {
        _updatingTextOpts = true;
        try
        {
            if (!string.IsNullOrEmpty(content.FontName) && _fontBox.Items.Contains(content.FontName))
                _fontBox.SelectedItem = content.FontName;
            if (content.FontSizePt is { } size)
                _fontSizeBox.Value = Math.Clamp((decimal)size, _fontSizeBox.Minimum, _fontSizeBox.Maximum);
            _bold = content.Bold;
        }
        finally
        {
            _updatingTextOpts = false;
        }
        ApplyTextOptionsStyle();
    }

    /// <summary>Шрифт для инлайн-поля: настройки ячейки или тот же авто-расчёт, что в рендерере.</summary>
    private static Font FontForCell(LabelCell cell, RectangleF rect)
    {
        var content = cell.Content;
        var pt = content.FontSizePt ?? Math.Max(6f, (float)rect.Height * 0.35f / 96f * 72f);
        var style = content.Bold ? FontStyle.Bold : FontStyle.Regular;
        var name = string.IsNullOrEmpty(content.FontName) ? Theme.FontName : content.FontName;
        try
        {
            return new Font(name, pt * 96f / 72f, style, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(Theme.FontName, pt * 96f / 72f, style, GraphicsUnit.Pixel);
        }
    }

    private void PopulateFonts()
    {
        List<string> fonts;
        try
        {
            using var installed = new InstalledFontCollection();
            fonts = installed.Families.Select(f => f.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            fonts = new List<string> { "Arial", "Segoe UI" };
        }
        foreach (var name in fonts)
            _fontBox.Items.Add(name);
        if (fonts.Count == 0)
            return;
        var def = Theme.FontName;
        _fontBox.SelectedItem = fonts.Contains(def) ? def : fonts[0];
    }

    /// <summary>
    /// Горячие клавиши: Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) — undo/redo,
    /// V/T/Q/I/B — инструменты (когда фокус на холсте, не в текстовом поле).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_inlineBox.Focused) return base.ProcessCmdKey(ref msg, keyData);
        if (keyData == (Keys.Control | Keys.Z))
        {
            DoUndo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        {
            DoRedo();
            return true;
        }
        if (_canvas.Focused)
        {
            if (HandleObjectKey(keyData)) return true;
            EditorTool? tool = keyData switch
            {
                Keys.V => EditorTool.Select,
                Keys.T => EditorTool.Text,
                Keys.Q => EditorTool.Qr,
                Keys.I => EditorTool.Image,
                Keys.B => EditorTool.Barcode,
                _ => null
            };
            if (tool.HasValue)
            {
                SetTool(tool.Value);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

/// <summary>Холст редактора: двойная буферизация, клик — в обработчик.</summary>
public class CanvasPanel : Panel
{
    public CanvasPanel()
    {
        BackColor = Theme.Canvas;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick |
            ControlStyles.StandardDoubleClick | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
    }
}
