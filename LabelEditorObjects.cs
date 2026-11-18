using System.Drawing.Drawing2D;
using System.Text.Json;

namespace MeyertecPrint;

public partial class LabelEditorControl
{
    private PrintSettings _displayPrintSettings = new();
    private Bitmap? _canvasRaster;
    private string? _canvasRasterError;
    private bool _canvasRasterValid;
    private LabelObject? _selectedObject;
    private LabelObject? _copiedObject;
    private NumericUpDown _objectX = null!, _objectY = null!, _objectWidth = null!, _objectHeight = null!;
    private ComboBox _zoomBox = null!;
    private Button _calibrateButton = null!, _editObjectButton = null!, _deleteObjectButton = null!;
    private TableLayoutPanel _objectFields = null!;
    private Label _objectEmpty = null!, _objectUnitLabel = null!;
    private bool _objectFieldsShown;
    private bool _updatingObjectFields;
    private static readonly double[] ZoomLevels = [0.5, 1, 2, 4];
    private double _pixelsPerMm = 96 / 25.4;
    private Point _dragStart;
    private long _dragX, _dragY;
    private LabelLayout? _dragSnapshot;
    private bool _dragChanged;
    private (int Row, int Col)? _pendingLegacy;

    private void InitializeObjectEditor(FlowLayoutPanel history)
    {
        _objectFields = InspectorFields();
        _objectFields.Padding = new Padding(13, 8, 13, 8);
        AddSection(_objectFields, 0, "Object.Properties");
        _objectX = MakeMmBox(max: 60);
        _objectY = MakeMmBox(max: 30);
        _objectWidth = MakeMmBox(min: 0.1m, max: 60);
        _objectHeight = MakeMmBox(min: 0.1m, max: 30);
        AddGeometryRow(_objectFields, 1, "Object.X", _objectX);
        AddGeometryRow(_objectFields, 2, "Object.Y", _objectY);
        AddGeometryRow(_objectFields, 3, "Object.Width", _objectWidth);
        AddGeometryRow(_objectFields, 4, "Object.Height", _objectHeight);
        _objectUnitLabel = new Label
        {
            Dock = DockStyle.Fill, Height = 23, ForeColor = Theme.SecondaryText,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 0, 6)
        };
        _objectFields.Controls.Add(_objectUnitLabel, 0, 5);
        _objectFields.SetColumnSpan(_objectUnitLabel, 2);
        _editObjectButton = new Button { Dock = DockStyle.Fill, Height = 34, Margin = new Padding(0, 0, 0, 8) };
        Theme.StyleButton(_editObjectButton);
        _editObjectButton.Click += (_, _) => EditSelectedObject();
        _objectFields.Controls.Add(_editObjectButton, 0, 6);
        _objectFields.SetColumnSpan(_editObjectButton, 2);
        _deleteObjectButton = new Button { Dock = DockStyle.Fill, Height = 34, Margin = Padding.Empty };
        Theme.StyleButton(_deleteObjectButton);
        _deleteObjectButton.Click += (_, _) => DeleteSelectedObject();
        _objectFields.Controls.Add(_deleteObjectButton, 0, 7);
        _objectFields.SetColumnSpan(_deleteObjectButton, 2);
        _objectEmpty = new Label
        {
            Dock = DockStyle.Top, Height = 84, ForeColor = Theme.SecondaryText,
            Padding = new Padding(15, 13, 15, 0), UseMnemonic = false
        };
        _objectFields.Visible = false;
        _objectTab.Controls.Add(_objectFields);
        _objectTab.Controls.Add(_objectEmpty);
        foreach (var field in new[] { _objectX, _objectY, _objectWidth, _objectHeight })
            field.ValueChanged += (_, _) => ObjectFieldsChanged();

        _zoomBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 142, Margin = new Padding(0, 6, 8, 0)
        };
        _zoomBox.Items.Add(L10n.T("Editor.Fit"));
        foreach (var scale in ZoomLevels) _zoomBox.Items.Add($"{scale * 100:0} %");
        _zoomBox.SelectedIndex = 0;
        _zoomBox.SelectedIndexChanged += (_, _) =>
        {
            _canvasPanel.AutoScroll = _zoomBox.SelectedIndex > 0;
            _canvasPanel.AutoScrollPosition = Point.Empty;
            ResizePhysicalCanvas();
        };
        _calibrateButton = new Button
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(0, 35),
            Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 10, 0)
        };
        Theme.StyleButton(_calibrateButton);
        _calibrateButton.Click += (_, _) => CalibrateDisplay();
        history.Controls.Add(_zoomBox);
        history.Controls.Add(_calibrateButton);
        history.Controls.SetChildIndex(_zoomBox, 0);
        history.Controls.SetChildIndex(_calibrateButton, 1);
        for (var i = 0; i < history.Controls.Count; i++) history.Controls[i].TabIndex = i;
        _canvasPanel.AutoScroll = false;
        ConfigureObjectUnits();
        UpdateObjectFields();
    }

    private void ApplyObjectStrings()
    {
        if (_calibrateButton is null) return;
        _calibrateButton.Text = L10n.T("Object.Calibrate");
        _deleteObjectButton.Text = L10n.T("Object.Delete");
        _editObjectButton.Text = L10n.T("Object.Edit");
        _objectEmpty.Text = L10n.T("Object.NoSelection");
        _zoomBox.Items[0] = L10n.T("Editor.Fit");
        _zoomBox.AccessibleName = L10n.T("Object.Zoom");
        _tips.SetToolTip(_calibrateButton, L10n.T("Object.CalibrateTip"));
        _tips.SetToolTip(_zoomBox, _zoomBox.AccessibleName);
    }

    private void ResizePhysicalCanvas()
    {
        if (_canvasPanel is null || _canvas is null || _canvasPanel.IsDisposed) return;
        var viewport = _canvasPanel.ClientSize;
        var margin = LogicalToDeviceUnits(108);
        var mediaWidth = Math.Max(1, Units.UmToMm(_layout.MediaWidthUm));
        var mediaHeight = Math.Max(1, Units.UmToMm(_layout.MediaHeightUm));
        var calibrated = DisplayCalibration.Get(_canvas);
        var zoomIndex = _zoomBox?.SelectedIndex ?? 0;
        var fit = Math.Min(Math.Max(1, viewport.Width - margin) / mediaWidth,
            Math.Max(1, viewport.Height - margin) / mediaHeight);
        var scale = zoomIndex <= 0 ? Math.Min(fit, calibrated * ZoomLevels[^1]) : calibrated * ZoomLevels[zoomIndex - 1];
        if (_pixelsPerMm != scale) _canvasRasterValid = false;
        _pixelsPerMm = scale;

        _canvas.Size = zoomIndex <= 0 ? viewport : new Size(
            Math.Max(viewport.Width, (int)Math.Ceiling(mediaWidth * scale) + margin),
            Math.Max(viewport.Height, (int)Math.Ceiling(mediaHeight * scale) + margin));
        _canvas.Invalidate();
    }

    private RectangleF PaperRect()
    {
        var width = (float)(Units.UmToMm(_layout.MediaWidthUm) * _pixelsPerMm);
        var height = (float)(Units.UmToMm(_layout.MediaHeightUm) * _pixelsPerMm);
        return new RectangleF((_canvas.Width - width) / 2, (_canvas.Height - height) / 2, width, height);
    }

    private RectangleF ItemRect(LabelObject obj, RectangleF paper)
    {
        var px = _pixelsPerMm / 1000d;
        return new RectangleF((float)(paper.Left + (obj.XUm + _displayPrintSettings.OffsetXMm * 1000) * px),
            (float)(paper.Top + (obj.YUm + _displayPrintSettings.OffsetYMm * 1000) * px),
            (float)(obj.WidthUm * px), (float)(obj.HeightUm * px));
    }

    private void DrawPhysicalCanvas(Graphics g, Rectangle clip)
    {
        _cellRects.Clear();
        var highContrast = SystemInformation.HighContrast;
        g.Clear(highContrast ? Theme.Surface : Theme.Canvas);
        if (!highContrast) Theme.DrawGrid(g, clip, LogicalToDeviceUnits(17), Theme.Border);
        if (!HasProject) return;
        var paper = PaperRect();
        var paperPixels = Rectangle.Round(paper);
        if (!highContrast)
        {
            using var shadow = new SolidBrush(Color.FromArgb(26, Theme.Dark));
            var offset = LogicalToDeviceUnits(4);
            g.FillRectangle(shadow, paper.X + offset, paper.Y + offset, paper.Width, paper.Height);
        }
        g.FillRectangle(Brushes.White, paper);
        if (!_canvasRasterValid)
        {
            _canvasRaster?.Dispose(); _canvasRaster = null; _canvasRasterError = null;
            try
            {
                var displayDpi = Math.Clamp((float)(_pixelsPerMm * 25.4), 150f, 300f);
                _canvasRaster = LabelPrintRenderer.Render(_layout, _displayPrintSettings, displayDpi, displayDpi);
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentException)
            { _canvasRasterError = ex.Message; }
            _canvasRasterValid = true;
        }
        if (_canvasRaster is not null)
        {
            var old = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_canvasRaster, paper, new RectangleF(0, 0, _canvasRaster.Width, _canvasRaster.Height), GraphicsUnit.Pixel);
            g.InterpolationMode = old;
        }
        else if (_canvasRasterError is not null)
            TextRenderer.DrawText(g, _canvasRasterError, Font,
                new Rectangle(8, 8, Math.Max(10, _canvas.Width - 16), LogicalToDeviceUnits(40)),
                Theme.ErrorText, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        RectangleF? selection = null;
        try
        {
            var plan = LabelPrintLayout.Create(_layout);
            using var cellPen = new Pen(Color.FromArgb(highContrast ? 255 : 140, Theme.Dark)) { DashStyle = DashStyle.Dot };
            foreach (var c in plan.Cells)
            {
                var rect = new RectangleF(
                    (float)(paper.Left + (c.X / 1000d + _displayPrintSettings.OffsetXMm) * _pixelsPerMm),
                    (float)(paper.Top + (c.Y / 1000d + _displayPrintSettings.OffsetYMm) * _pixelsPerMm),
                    (float)(c.Width / 1000d * _pixelsPerMm), (float)(c.Height / 1000d * _pixelsPerMm));
                _cellRects.Add((c.Row, c.Column, rect));
                g.DrawRectangle(cellPen, rect.X, rect.Y, rect.Width, rect.Height);
                if (_selectedRow == c.Row && _selectedCol == c.Column && selection is null) selection = rect;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException) { }
        using (var border = new Pen(highContrast ? SystemColors.WindowFrame : Theme.SecondaryText))
            g.DrawRectangle(border, paperPixels);
        DrawRulers(g, paper);
        if (_selectedObject is not null && (_layout.Objects?.Contains(_selectedObject) ?? false))
            selection = ItemRect(_selectedObject, paper);
        if (selection is { } rectSelected)
        {
            var accent = highContrast ? SystemColors.Highlight : Theme.RailSelectionBorder;
            using var select = new Pen(accent, LogicalToDeviceUnits(2)) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(select, rectSelected.X, rectSelected.Y, rectSelected.Width, rectSelected.Height);
        }
        if (_canvas.Focused && _canvas.KeyboardCuesVisible)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(paperPixels, LogicalToDeviceUnits(4), LogicalToDeviceUnits(4)));
    }

    private void DrawRulers(Graphics g, RectangleF paper)
    {
        using var pen = new Pen(Theme.SecondaryText);
        var y = paper.Top - LogicalToDeviceUnits(8);
        var x = paper.Left - LogicalToDeviceUnits(8);
        var tickLength = LogicalToDeviceUnits(4);
        g.DrawLine(pen, paper.Left, y, paper.Right, y);
        g.DrawLine(pen, x, paper.Top, x, paper.Bottom);
        var pixelsPerUnit = _pixelsPerMm * (_unitInch ? 25.4 : 1);
        double[] intervals = _unitInch ? [0.1, 0.25, 0.5, 1, 2, 5, 10, 25] : [1, 2, 5, 10, 20, 50, 100, 250, 500];
        var interval = intervals.FirstOrDefault(step => step * pixelsPerUnit >= LogicalToDeviceUnits(35), intervals[^1]);
        for (var index = 0; index * interval * pixelsPerUnit <= paper.Width; index++)
        {
            var tick = paper.Left + (float)(index * interval * pixelsPerUnit);
            g.DrawLine(pen, tick, y - tickLength, tick, y);
            var text = (index * interval).ToString("0.##");
            var width = TextRenderer.MeasureText(g, text, Font).Width;
            TextRenderer.DrawText(g, text, Font, new Point((int)tick - width / 2, (int)y - Font.Height - tickLength),
                Theme.SecondaryText);
        }
        for (var index = 0; index * interval * pixelsPerUnit <= paper.Height; index++)
        {
            var tick = paper.Top + (float)(index * interval * pixelsPerUnit);
            g.DrawLine(pen, x - tickLength, tick, x, tick);
            var text = (index * interval).ToString("0.##");
            var width = TextRenderer.MeasureText(g, text, Font).Width;
            TextRenderer.DrawText(g, text, Font, new Point((int)x - width - tickLength, (int)tick - Font.Height / 2),
                Theme.SecondaryText);
        }
        var unit = L10n.T(_unitInch ? "Unit.In" : "Unit.Mm");
        TextRenderer.DrawText(g, unit, Font,
            new Point((int)paper.Right + LogicalToDeviceUnits(10), (int)y - Font.Height - tickLength), Theme.SecondaryText);
    }

    private void HandlePhysicalMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !HasProject) return;
        _canvas.Focus();
        var paper = PaperRect();
        if (!paper.Contains(e.Location)) { SelectObject(null); return; }
        var obj = (_layout.Objects ?? new()).LastOrDefault(o => ItemRect(o, paper).Contains(e.Location));
        if (_tool == EditorTool.Select)
        {
            if (obj is not null)
            {
                SelectObject(obj);
                BeginDrag(e.Location, obj);
                return;
            }
            var hit = _cellRects.FirstOrDefault(c => c.Rect.Contains(e.Location));
            if (hit != default)
            {
                SelectCell(hit.Row, hit.Col);
                if (_layout.Rows[hit.Row].Cells[hit.Col].Content.Type != CellContentType.Empty)
                {
                    _pendingLegacy = (hit.Row, hit.Col);
                    _dragStart = e.Location;
                    _dragSnapshot = _layout.Clone();
                    _canvas.Capture = true;
                }
            }
            else SelectObject(null);
            return;
        }
        if (_tool is EditorTool.SplitColumns or EditorTool.SplitRows)
        {
            var hit = _cellRects.FirstOrDefault(c => c.Rect.Contains(e.Location));
            if (hit != default) SplitCellWithTool(hit.Row, hit.Col);
            return;
        }
        PlaceObject(e.Location, paper);
    }

    private void SplitCellWithTool(int row, int column)
    {
        SelectCell(row, column);
        if (_tool == EditorTool.SplitColumns) SplitColumnsAction(); else SplitRowsAction();
    }

    private void PlaceObject(PointF location, RectangleF paper)
    {
        var content = AskForContent(_tool, null);
        if (content is null) return;
        var width = _tool switch { EditorTool.Text => 30_000L, EditorTool.Qr => 15_000L, EditorTool.Barcode => 40_000L, _ => 25_000L };
        var height = _tool switch { EditorTool.Text => 8_000L, EditorTool.Barcode => 10_000L, _ => 15_000L };
        var x = (long)Math.Round(((location.X - paper.Left) / _pixelsPerMm - _displayPrintSettings.OffsetXMm) * 1000) - width / 2;
        var y = (long)Math.Round(((location.Y - paper.Top) / _pixelsPerMm - _displayPrintSettings.OffsetYMm) * 1000) - height / 2;
        width = Math.Min(width, _layout.MediaWidthUm);
        height = Math.Min(height, _layout.MediaHeightUm);
        var item = new LabelObject { XUm = x, YUm = y, WidthUm = width, HeightUm = height, Content = content };
        ClampObject(item);
        _undo.Push(_layout.Clone());
        (_layout.Objects ??= new()).Add(item);
        SelectObject(item);
        SetTool(EditorTool.Select);
        RevealCanvasRect(ItemRect(item, paper));
        FocusCanvas();
        LayoutChanged?.Invoke();
    }

    private void SelectObject(LabelObject? item)
    {
        var keepCanvasFocus = _canvas.Focused;
        CommitInline();
        _selectedObject = item;
        _selectedRow = _selectedCol = null;
        _pendingLegacy = null;
        if (item?.Content.Type == CellContentType.Text) SyncTextOptionsFromObject(item);
        UpdateSelectionUi();
        if (item is not null && _inspector.SelectedTab != _objectTab)
        {
            _inspector.SelectedTab = _objectTab;
            if (keepCanvasFocus) FocusCanvas();
        }
        InvalidateCanvas();
    }

    private void BeginDrag(Point start, LabelObject item)
    {
        _dragStart = start;
        _dragX = item.XUm;
        _dragY = item.YUm;
        _dragSnapshot = _layout.Clone();
        _dragChanged = false;
        _canvas.Capture = true;
    }

    private void OnObjectMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_canvas.Capture || e.Button != MouseButtons.Left) return;
        if (_pendingLegacy is { } legacy && (Math.Abs(e.X - _dragStart.X) >= 3 || Math.Abs(e.Y - _dragStart.Y) >= 3))
        {
            var item = ConvertLegacy(legacy.Row, legacy.Col);
            SelectObject(item);
            _dragX = item.XUm;
            _dragY = item.YUm;
            _dragChanged = true;
        }
        if (_selectedObject is null) return;
        var beforeX = _selectedObject.XUm;
        var beforeY = _selectedObject.YUm;
        _selectedObject.XUm = _dragX + (long)Math.Round((e.X - _dragStart.X) / _pixelsPerMm * 1000);
        _selectedObject.YUm = _dragY + (long)Math.Round((e.Y - _dragStart.Y) / _pixelsPerMm * 1000);
        ClampObject(_selectedObject);
        _dragChanged |= beforeX != _selectedObject.XUm || beforeY != _selectedObject.YUm;
        UpdateObjectFields();
        InvalidateCanvas();
    }

    private void OnObjectMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_canvas.Capture) return;
        _canvas.Capture = false;
        _pendingLegacy = null;
        if (_dragChanged && _dragSnapshot is not null)
        {
            _undo.Push(_dragSnapshot);
            LayoutChanged?.Invoke();
        }
        _dragSnapshot = null;
        _dragChanged = false;
        UpdateSelectionUi();
        InvalidateCanvas();
    }

    private LabelObject ConvertLegacy(int row, int column)
    {
        var position = LabelPrintLayout.Create(_layout).Cells.First(c => c.Row == row && c.Column == column);
        var content = _layout.Rows[row].Cells[column].Content.Clone();
        long width = position.Width, height = position.Height;
        if (content.Type == CellContentType.Text)
        {
            content.FontSizePt ??= Math.Max(6f, (float)(Units.UmToMm(height) * 0.35 * 72 / 25.4));
            using var bmp = new Bitmap(1, 1);
            bmp.SetResolution(300, 300);
            using var g = Graphics.FromImage(bmp);
            using var font = new Font(string.IsNullOrEmpty(content.FontName) ? Theme.FontName : content.FontName,
                content.FontSizePt.Value, content.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            var measured = g.MeasureString(content.Text, font);
            width = Math.Min(width, Math.Max(1000, Units.MmToUm(measured.Width * 25.4 / 300 + 1)));
            height = Math.Min(height, Math.Max(1000, Units.MmToUm(measured.Height * 25.4 / 300 + 1)));
        }
        else if (content.Type == CellContentType.Qr) width = height = Math.Min(width, height);
        else if (content.Type == CellContentType.Image && !string.IsNullOrEmpty(content.ImageBase64))
        {
            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(content.ImageBase64));
                using var image = Image.FromStream(stream);
                var scale = Math.Min((double)width / image.Width, (double)height / image.Height);
                width = Math.Max(1, (long)(image.Width * scale));
                height = Math.Max(1, (long)(image.Height * scale));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OutOfMemoryException) { }
        }
        var item = new LabelObject
        {
            XUm = position.X + (position.Width - width) / 2,
            YUm = position.Y + (position.Height - height) / 2,
            WidthUm = width, HeightUm = height, Content = content,
            CsvColumn = _layout.Rows.Take(row).Sum(r => r.Cells.Count) + column,
        };
        _layout.Rows[row].Cells[column].Content = new CellContent();
        _layout.Objects.Add(item);
        return item;
    }

    private void EditSelectedObject()
    {
        var obj = _selectedObject;
        var cell = SelectedCell();
        if (obj is null && cell is null) return;
        var original = obj?.Content;
        if (original is null && cell is { } selected)
            original = selected.Layout.Rows[selected.Row].Cells[selected.Col].Content;
        if (original is null) return;
        var tool = original.Type switch
        {
            CellContentType.Text => EditorTool.Text,
            CellContentType.Qr => EditorTool.Qr,
            CellContentType.Barcode => EditorTool.Barcode,
            CellContentType.Image => EditorTool.Image,
            _ => EditorTool.Select,
        };
        var content = AskForContent(tool, original);
        if (content is null) return;
        _undo.Push(_layout.Clone());
        var target = obj ?? ConvertLegacy(cell!.Value.Row, cell.Value.Col);
        target.Content = content;
        SelectObject(target);
        LayoutChanged?.Invoke();
    }

    private CellContent? AskForContent(EditorTool tool, CellContent? original)
    {
        if (tool == EditorTool.Text)
        {
            using var dialog = new BrandForm
            {
                Text = L10n.T("Object.TextTitle"), StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, ShowIcon = false,
                ClientSize = new Size(470, 225), MinimumSize = new Size(360, 190),
                Font = Theme.UiFont(), BackColor = Theme.Surface
            };
            dialog.SuspendLayout();
            dialog.AutoScaleDimensions = new SizeF(96, 96);
            dialog.AutoScaleMode = AutoScaleMode.Dpi;
            var input = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
                Text = original?.Text ?? string.Empty, AccessibleName = dialog.Text
            };
            var fields = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 16, 18, 14) };
            fields.Controls.Add(input);
            var ok = new Button
            {
                Text = L10n.T("Dialog.Apply"), DialogResult = DialogResult.OK, Enabled = input.TextLength > 0
            };
            var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
            input.TextChanged += (_, _) => ok.Enabled = input.TextLength > 0;
            input.KeyDown += (_, e) =>
            {
                if (e.KeyData != (Keys.Control | Keys.Enter) || !ok.Enabled) return;
                e.SuppressKeyPress = true;
                ok.PerformClick();
            };
            dialog.Controls.Add(fields);
            dialog.Controls.Add(Theme.DialogFooter(ok, cancel));
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            dialog.Shown += (_, _) => input.Focus();
            dialog.ResumeLayout();
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || input.Text.Length == 0) return null;
            return new CellContent { Type = CellContentType.Text, Text = input.Text,
                FontName = original is null ? SelectedFontName : original.FontName,
                FontSizePt = original?.FontSizePt ?? (float)_fontSizeBox.Value, Bold = original?.Bold ?? _bold };
        }
        if (tool == EditorTool.Qr)
        {
            using var dialog = new QrCodeDialog(original?.Text);
            return dialog.ShowDialog(FindForm()) == DialogResult.OK && !string.IsNullOrEmpty(dialog.QrText)
                ? new CellContent { Type = CellContentType.Qr, Text = dialog.QrText } : null;
        }
        if (tool == EditorTool.Barcode)
        {
            using var dialog = new BarcodeDialog(original?.BarcodeData, original?.BarcodeSymbol ?? BarcodeSymbol.Code128);
            return dialog.ShowDialog(FindForm()) == DialogResult.OK
                ? new CellContent { Type = CellContentType.Barcode, BarcodeData = dialog.Value, BarcodeSymbol = dialog.Symbol } : null;
        }
        if (tool == EditorTool.Image)
        {
            using var dialog = new BrandedOpenFileDialog { Title = L10n.T("Editor.Image"),
                Filter = $"{L10n.T("File.FilterImage")} (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return null;
            try
            {
                var bytes = File.ReadAllBytes(dialog.FileName);
                using var stream = new MemoryStream(bytes);
                using var image = Image.FromStream(stream); // Проверить до добавления в проект.
                return new CellContent { Type = CellContentType.Image, ImageBase64 = Convert.ToBase64String(bytes),
                    ImageFormat = Path.GetExtension(dialog.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpeg" };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
            {
                BrandMessageBox.Show(FindForm(), string.Format(L10n.T("Error.ImageLoad"), ex.Message), "MEYERTEC PRINT",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        return null;
    }

    private void ConfigureObjectUnits()
    {
        if (_objectX is null) return;
        _updatingObjectFields = true;
        var unit = _unitInch ? 25.4m : 1m;
        foreach (var field in new[] { _objectX, _objectY, _objectWidth, _objectHeight })
        {
            field.Minimum = 0;
            field.Maximum = 762 / unit;
            field.Increment = _unitInch ? 0.01m : 0.1m;
            field.DecimalPlaces = _unitInch ? 4 : 3;
        }
        _updatingObjectFields = false;
        UpdateObjectFields();
    }

    private void UpdateObjectFields()
    {
        if (_objectX is null) return;
        _updatingObjectFields = true;
        var fields = new[] { _objectX, _objectY, _objectWidth, _objectHeight };
        var showFields = _selectedObject is not null;
        if (_objectFieldsShown != showFields)
        {
            _objectFieldsShown = showFields;
            _objectTab.SuspendLayout();
            _objectEmpty.Visible = !showFields;
            _objectFields.Visible = showFields;
            _objectTab.ResumeLayout();
            _objectTab.Invalidate(true);
        }
        if (_selectedObject is { } obj)
        {
            var vals = new[] { obj.XUm + Units.MmToUm(_displayPrintSettings.OffsetXMm),
                obj.YUm + Units.MmToUm(_displayPrintSettings.OffsetYMm), obj.WidthUm, obj.HeightUm };
            for (var i = 0; i < fields.Length; i++)
                fields[i].Value = Math.Clamp(UmToField(vals[i]), fields[i].Minimum, fields[i].Maximum);
        }
        _updatingObjectFields = false;
    }

    private void ObjectFieldsChanged()
    {
        if (_updatingObjectFields || _selectedObject is not { } item) return;
        var snapshot = _layout.Clone();
        item.XUm = MmValueToUm(_objectX.Value) - Units.MmToUm(_displayPrintSettings.OffsetXMm);
        item.YUm = MmValueToUm(_objectY.Value) - Units.MmToUm(_displayPrintSettings.OffsetYMm);
        item.WidthUm = Math.Clamp(MmValueToUm(_objectWidth.Value), 100, _layout.MediaWidthUm);
        item.HeightUm = Math.Clamp(MmValueToUm(_objectHeight.Value), 100, _layout.MediaHeightUm);
        ClampObject(item);
        _undo.Push(snapshot);
        UpdateObjectFields();
        InvalidateCanvas();
        LayoutChanged?.Invoke();
    }

    private void ClampObject(LabelObject item)
    {
        var offsetX = double.IsFinite(_displayPrintSettings.OffsetXMm) ? Units.MmToUm(_displayPrintSettings.OffsetXMm) : 0;
        var offsetY = double.IsFinite(_displayPrintSettings.OffsetYMm) ? Units.MmToUm(_displayPrintSettings.OffsetYMm) : 0;
        var minX = -offsetX;
        var minY = -offsetY;
        var maxX = Math.Max(minX, _layout.MediaWidthUm - item.WidthUm - offsetX);
        var maxY = Math.Max(minY, _layout.MediaHeightUm - item.HeightUm - offsetY);
        item.XUm = Math.Clamp(item.XUm, minX, maxX);
        item.YUm = Math.Clamp(item.YUm, minY, maxY);
    }

    private bool HandleCanvasKey(Keys key)
    {
        if (key == Keys.Escape)
        {
            SetTool(EditorTool.Select);
            SelectObject(null);
            return true;
        }

        if ((key is Keys.PageUp or Keys.PageDown) && (_tool is EditorTool.Select or EditorTool.SplitColumns or EditorTool.SplitRows))
        {
            CycleCanvasSelection(key == Keys.PageDown ? 1 : -1);
            return true;
        }

        if (key == Keys.Enter)
        {
            switch (_tool)
            {
                case EditorTool.Select:
                    EditSelectedObject();
                    break;
                case EditorTool.SplitColumns:
                case EditorTool.SplitRows:
                    if (_selectedRow is null) CycleCanvasSelection(1);
                    if (_selectedRow is { } row && _selectedCol is { } column) SplitCellWithTool(row, column);
                    break;
                default:
                    var paper = PaperRect();
                    PlaceObject(new PointF(paper.Left + paper.Width / 2, paper.Top + paper.Height / 2), paper);
                    break;
            }
            return true;
        }

        return HandleObjectKey(key);
    }

    private void CycleCanvasSelection(int direction)
    {
        var selectObjects = _tool == EditorTool.Select;
        var objects = _layout.Objects;
        var objectCount = selectObjects ? objects.Count : 0;
        var cells = _layout.Rows.SelectMany((row, rowIndex) => row.Cells.Select((cell, columnIndex) =>
            (Row: rowIndex, Col: columnIndex, cell.Content.Type)))
            .Where(cell => !selectObjects || cell.Type != CellContentType.Empty)
            .Select(cell => (cell.Row, cell.Col)).ToList();
        var count = objectCount + cells.Count;
        if (count == 0) return;

        var index = _selectedObject is not null ? objects.IndexOf(_selectedObject) : -1;
        if (_selectedRow is { } row && _selectedCol is { } column)
        {
            var cellIndex = cells.IndexOf((row, column));
            if (cellIndex >= 0) index = objectCount + cellIndex;
        }
        index = index < 0 ? (direction > 0 ? 0 : count - 1) : (index + direction + count) % count;
        if (index < objectCount)
        {
            var item = objects[index];
            SelectObject(item);
            RevealCanvasRect(ItemRect(item, PaperRect()));
        }
        else
        {
            var target = cells[index - objectCount];
            SelectCell(target.Row, target.Col);
            var hit = _cellRects.FirstOrDefault(cell => cell.Row == target.Row && cell.Col == target.Col);
            if (hit != default) RevealCanvasRect(hit.Rect);
        }
    }

    private void RevealCanvasRect(RectangleF target)
    {
        if (!_canvasPanel.AutoScroll) return;
        var position = _canvasPanel.AutoScrollPosition;
        var viewport = new RectangleF(-position.X, -position.Y, _canvasPanel.ClientSize.Width, _canvasPanel.ClientSize.Height);
        if (viewport.Contains(target)) return;
        _canvasPanel.AutoScrollPosition = new Point(
            Math.Max(0, (int)(target.Left + (target.Width - viewport.Width) / 2)),
            Math.Max(0, (int)(target.Top + (target.Height - viewport.Height) / 2)));
    }

    private bool HandleObjectKey(Keys key)
    {
        if (key == (Keys.Control | Keys.C) && _selectedObject is not null)
        { _copiedObject = _selectedObject.Clone(); return true; }
        if (key == (Keys.Control | Keys.V) && _copiedObject is not null)
        {
            var copy = _copiedObject.Clone();
            copy.CsvColumn = null;
            copy.XUm += 2000; copy.YUm += 2000; ClampObject(copy);
            _undo.Push(_layout);
            (_layout.Objects ??= new()).Add(copy);
            SelectObject(copy); LayoutChanged?.Invoke(); return true;
        }
        if (_selectedObject is not { } item) return false;
        if (key == Keys.Delete)
        {
            DeleteSelectedObject();
            return true;
        }
        var step = (key & Keys.Shift) == Keys.Shift ? 1000 : 100;
        switch (key & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: break;
            default: return false;
        }
        var snapshot = _layout.Clone();
        switch (key & Keys.KeyCode)
        {
            case Keys.Left: item.XUm -= step; break;
            case Keys.Right: item.XUm += step; break;
            case Keys.Up: item.YUm -= step; break;
            case Keys.Down: item.YUm += step; break;
        }
        ClampObject(item);
        _undo.Push(snapshot);
        UpdateObjectFields();
        InvalidateCanvas();
        LayoutChanged?.Invoke();
        return true;
    }

    private void DeleteSelectedObject()
    {
        if (_selectedObject is not { } item) return;
        _undo.Push(_layout.Clone());
        _layout.Objects.Remove(item);
        SelectObject(null);
        LayoutChanged?.Invoke();
    }

    private void CalibrateDisplay()
    {
        using var dialog = new DisplayCalibrationDialog(DisplayCalibration.Get(_canvas));
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        DisplayCalibration.Set(_canvas, dialog.PixelsPerMm);
        ResizePhysicalCanvas();
        InvalidateCanvas();
    }
}

/// <summary>Калибровка хранится отдельно для каждого монитора и масштаба Windows.</summary>
internal static class DisplayCalibration
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeyertecPrint", "display-calibration.json");
    private static Dictionary<string, double>? _values;
    private static string Key(Control control) => Screen.FromControl(control.FindForm() ?? control).DeviceName + ":" + control.DeviceDpi;
    private static Dictionary<string, double> Values
    {
        get
        {
            if (_values is not null) return _values;
            try { _values = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(FilePath)) ?? new(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _values = new(); }
            return _values;
        }
    }
    public static double Get(Control control)
        => Values.TryGetValue(Key(control), out var value) && double.IsFinite(value) && value >= 1 && value <= 30
            ? value : control.DeviceDpi / 25.4;
    public static void Set(Control control, double value)
    {
        if (!double.IsFinite(value) || value < 1 || value > 30) return;
        Values[Key(control)] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal sealed class DisplayCalibrationDialog : BrandForm
{
    private readonly NumericUpDown _measured;
    private readonly Panel _sample;
    private int SamplePixels => Math.Max(1, _sample.ClientSize.Width - 2 * LogicalToDeviceUnits(30));
    public double PixelsPerMm => SamplePixels / (double)_measured.Value;

    public DisplayCalibrationDialog(double currentPixelsPerMm)
    {
        SuspendLayout();
        Text = L10n.T("Object.CalibrationTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 212);
        Font = Theme.UiFont();
        BackColor = Theme.Surface;
        var hint = new Label { Text = L10n.T("Object.CalibrationHint"), Left = 15, Top = 12, Width = 430, Height = 42 };
        _sample = new Panel { Left = 15, Top = 58, Width = 430, Height = 40, BackColor = Color.White };
        _sample.Paint += (_, e) =>
        {
            var left = LogicalToDeviceUnits(30);
            var right = _sample.ClientSize.Width - left;
            var center = _sample.ClientSize.Height / 2;
            var tick = LogicalToDeviceUnits(9);
            using var pen = new Pen(Color.Black, LogicalToDeviceUnits(2));
            e.Graphics.DrawLine(pen, left, center, right, center);
            e.Graphics.DrawLine(pen, left, center - tick, left, center + tick);
            e.Graphics.DrawLine(pen, right, center - tick, right, center + tick);
        };
        var label = new Label { Text = L10n.T("Object.MeasuredMm"), Left = 15, Top = 110, Width = 200, Height = 26 };
        _measured = new NumericUpDown { Left = 225, Top = 108, Width = 90, Minimum = 13, Maximum = 370,
            DecimalPlaces = 1, Increment = 0.5m, Value = Math.Clamp((decimal)(370 / currentPixelsPerMm), 13, 370) };
        _measured.AccessibleName = label.Text;
        var ok = new Button { Text = L10n.T("Dialog.Apply"), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel };
        Controls.AddRange([hint, _sample, label, _measured, Theme.DialogFooter(ok, cancel)]);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _measured.Value = Math.Clamp((decimal)(SamplePixels / currentPixelsPerMm), _measured.Minimum, _measured.Maximum);
        ResumeLayout();
    }
}
