using System.Drawing.Drawing2D;
using System.Globalization;
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
    private Button _calibrateButton = null!, _deleteObjectButton = null!;
    private bool _updatingObjectFields;
    private double _zoom = 2;
    private Point _dragStart;
    private long _dragX, _dragY;
    private LabelLayout? _dragSnapshot;
    private bool _dragChanged;
    private (int Row, int Col)? _pendingLegacy;

    private void InitializeObjectEditor(TableLayoutPanel geometry, FlowLayoutPanel history)
    {
        AddSection(geometry, 11, "Object.Properties");
        _objectX = MakeMmBox(max: 60);
        _objectY = MakeMmBox(max: 30);
        _objectWidth = MakeMmBox(min: 0.1m, max: 60);
        _objectHeight = MakeMmBox(min: 0.1m, max: 30);
        AddGeometryRow(geometry, 12, "Object.X", _objectX);
        AddGeometryRow(geometry, 13, "Object.Y", _objectY);
        AddGeometryRow(geometry, 14, "Object.Width", _objectWidth);
        AddGeometryRow(geometry, 15, "Object.Height", _objectHeight);
        _deleteObjectButton = new Button { Dock = DockStyle.Fill, Height = 30, Margin = new Padding(0, 6, 0, 0) };
        Theme.StyleButton(_deleteObjectButton);
        _deleteObjectButton.Click += (_, _) => DeleteSelectedObject();
        geometry.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        geometry.Controls.Add(_deleteObjectButton, 0, 16);
        geometry.SetColumnSpan(_deleteObjectButton, 2);
        foreach (var field in new[] { _objectX, _objectY, _objectWidth, _objectHeight })
            field.ValueChanged += (_, _) => ObjectFieldsChanged();
        _zoomBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 78, Margin = new Padding(4, 5, 4, 0) };
        _zoomBox.Items.AddRange(new object[] { "50 %", "100 %", "200 %", "400 %" });
        _zoomBox.SelectedIndex = 2;
        _zoomBox.SelectedIndexChanged += (_, _) =>
        {
            _zoom = new[] { 0.5, 1.0, 2.0, 4.0 }[_zoomBox.SelectedIndex];
            ResizePhysicalCanvas();
            InvalidateCanvas();
        };
        _calibrateButton = new Button { AutoSize = true, Height = 30, Margin = new Padding(3, 3, 8, 0) };
        Theme.StyleButton(_calibrateButton);
        _calibrateButton.Click += (_, _) => CalibrateDisplay();
        history.Controls.Add(_zoomBox);
        history.Controls.Add(_calibrateButton);
        history.Controls.SetChildIndex(_zoomBox, 0);
        history.Controls.SetChildIndex(_calibrateButton, 1);
        ConfigureObjectUnits();
        UpdateObjectFields();
    }

    private void ApplyObjectStrings()
    {
        if (_calibrateButton is null) return;
        _calibrateButton.Text = L10n.T("Object.Calibrate");
        _deleteObjectButton.Text = L10n.T("Object.Delete");
        _tips.SetToolTip(_calibrateButton, L10n.T("Object.CalibrateTip"));
        _tips.SetToolTip(_zoomBox, L10n.T("Object.Zoom"));
    }

    private double PixelsPerMm => DisplayCalibration.Get(_canvas) * _zoom;

    private void ResizePhysicalCanvas()
    {
        if (_canvasPanel is null || _canvas is null || _canvasPanel.IsDisposed) return;
        var margin = LogicalToDeviceUnits(120);
        var width = (int)Math.Ceiling(Units.UmToMm(_layout.MediaWidthUm) * PixelsPerMm) + margin;
        var height = (int)Math.Ceiling(Units.UmToMm(_layout.MediaHeightUm) * PixelsPerMm) + margin;
        _canvas.Size = new Size(Math.Max(_canvasPanel.ClientSize.Width - 2, width),
            Math.Max(_canvasPanel.ClientSize.Height - 2, height));
    }

    private RectangleF PaperRect()
    {
        var width = (float)(Units.UmToMm(_layout.MediaWidthUm) * PixelsPerMm);
        var height = (float)(Units.UmToMm(_layout.MediaHeightUm) * PixelsPerMm);
        var margin = LogicalToDeviceUnits(45);
        return new RectangleF(Math.Max(margin, (_canvas.Width - width) / 2),
            Math.Max(margin, (_canvas.Height - height) / 2), width, height);
    }

    private RectangleF ItemRect(LabelObject obj, RectangleF paper)
    {
        var px = PixelsPerMm / 1000d;
        return new RectangleF((float)(paper.Left + (obj.XUm + _displayPrintSettings.OffsetXMm * 1000) * px),
            (float)(paper.Top + (obj.YUm + _displayPrintSettings.OffsetYMm * 1000) * px),
            (float)(obj.WidthUm * px), (float)(obj.HeightUm * px));
    }

    private void DrawPhysicalCanvas(Graphics g, Rectangle clip)
    {
        _cellRects.Clear();
        g.Clear(Theme.Canvas);
        Theme.DrawGrid(g, clip, LogicalToDeviceUnits(17), Theme.Border);
        if (!HasProject) return;
        var paper = PaperRect();
        var paperPixels = Rectangle.Round(paper);
        using (var shadow = new SolidBrush(Color.FromArgb(32, Color.Black)))
            g.FillRectangle(shadow, paper.X + 4, paper.Y + 4, paper.Width, paper.Height);
        g.FillRectangle(Brushes.White, paper);
        if (!_canvasRasterValid)
        {
            _canvasRaster?.Dispose(); _canvasRaster = null; _canvasRasterError = null;
            try
            {
                var displayDpi = Math.Clamp((float)(PixelsPerMm * 25.4), 150f, 300f);
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
                Color.Firebrick, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        try
        {
            var plan = LabelPrintLayout.Create(_layout);
            using var cellPen = new Pen(Color.FromArgb(115, Theme.SecondaryText)) { DashStyle = DashStyle.Dot };
            foreach (var c in plan.Cells)
            {
                var rect = new RectangleF(
                    (float)(paper.Left + (c.X / 1000d + _displayPrintSettings.OffsetXMm) * PixelsPerMm),
                    (float)(paper.Top + (c.Y / 1000d + _displayPrintSettings.OffsetYMm) * PixelsPerMm),
                    (float)(c.Width / 1000d * PixelsPerMm), (float)(c.Height / 1000d * PixelsPerMm));
                _cellRects.Add((c.Row, c.Column, rect));
                g.DrawRectangle(cellPen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException) { }
        using (var border = new Pen(Theme.Dark)) g.DrawRectangle(border, paperPixels);
        DrawRulers(g, paper);
        if (_selectedObject is not null && (_layout.Objects?.Contains(_selectedObject) ?? false))
        {
            var rect = ItemRect(_selectedObject, paper);
            using var select = new Pen(Color.FromArgb(255, 188, 0), 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(select, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private void DrawRulers(Graphics g, RectangleF paper)
    {
        using var pen = new Pen(Theme.SecondaryText);
        var y = paper.Top - 7;
        var x = paper.Left - 7;
        g.DrawLine(pen, paper.Left, y, paper.Right, y);
        g.DrawLine(pen, x, paper.Top, x, paper.Bottom);
        var interval = PixelsPerMm < 2.5 ? 20 : PixelsPerMm < 4 ? 10 : 5;
        for (var mm = 0; mm <= Units.UmToMm(_layout.MediaWidthUm); mm += interval)
        {
            var tick = paper.Left + (float)(mm * PixelsPerMm);
            g.DrawLine(pen, tick, y - 4, tick, y + 2);
            TextRenderer.DrawText(g, mm.ToString(CultureInfo.InvariantCulture), Font,
                new Point((int)tick - 7, (int)y - Font.Height - 3), Theme.SecondaryText);
        }
        for (var mm = 0; mm <= Units.UmToMm(_layout.MediaHeightUm); mm += interval)
        {
            var tick = paper.Top + (float)(mm * PixelsPerMm);
            g.DrawLine(pen, x - 4, tick, x + 2, tick);
            TextRenderer.DrawText(g, mm.ToString(CultureInfo.InvariantCulture), Font,
                new Point((int)x - LogicalToDeviceUnits(27), (int)tick - Font.Height / 2), Theme.SecondaryText);
        }
        TextRenderer.DrawText(g, L10n.T("Unit.Mm"), Font,
            new Point((int)paper.Right + 4, (int)y - Font.Height), Theme.SecondaryText);
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
            SelectObject(null);
            if (hit != default)
            {
                _selectedRow = hit.Row;
                _selectedCol = hit.Col;
                if (_layout.Rows[hit.Row].Cells[hit.Col].Content.Type != CellContentType.Empty)
                {
                    _pendingLegacy = (hit.Row, hit.Col);
                    _dragStart = e.Location;
                    _dragSnapshot = _layout.Clone();
                    _canvas.Capture = true;
                }
            }
            return;
        }
        if (_tool is EditorTool.SplitColumns or EditorTool.SplitRows)
        {
            var hit = _cellRects.FirstOrDefault(c => c.Rect.Contains(e.Location));
            if (hit == default) return;
            SelectCell(hit.Row, hit.Col);
            if (_tool == EditorTool.SplitColumns) SplitColumnsAction(); else SplitRowsAction();
            return;
        }
        var content = AskForContent(_tool, null);
        if (content is null) return;
        var width = _tool switch { EditorTool.Text => 30_000L, EditorTool.Qr => 15_000L, EditorTool.Barcode => 40_000L, _ => 25_000L };
        var height = _tool switch { EditorTool.Text => 8_000L, EditorTool.Barcode => 10_000L, _ => 15_000L };
        var x = (long)Math.Round(((e.X - paper.Left) / PixelsPerMm - _displayPrintSettings.OffsetXMm) * 1000) - width / 2;
        var y = (long)Math.Round(((e.Y - paper.Top) / PixelsPerMm - _displayPrintSettings.OffsetYMm) * 1000) - height / 2;
        width = Math.Min(width, _layout.MediaWidthUm);
        height = Math.Min(height, _layout.MediaHeightUm);
        var item = new LabelObject { XUm = x, YUm = y, WidthUm = width, HeightUm = height, Content = content };
        ClampObject(item);
        _undo.Push(_layout.Clone());
        (_layout.Objects ??= new()).Add(item);
        SelectObject(item);
        SetTool(EditorTool.Select);
        LayoutChanged?.Invoke();
    }

    private void SelectObject(LabelObject? item)
    {
        CommitInline();
        _selectedObject = item;
        _selectedRow = _selectedCol = null;
        _pendingLegacy = null;
        if (item?.Content.Type == CellContentType.Text) SyncTextOptionsFromCell(item.Content);
        UpdateSelectionUi();
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
            _pendingLegacy = null;
            _selectedObject = item;
            _selectedRow = _selectedCol = null;
            _dragX = item.XUm;
            _dragY = item.YUm;
            if (item.Content.Type == CellContentType.Text) SyncTextOptionsFromCell(item.Content);
            _dragChanged = true;
        }
        if (_selectedObject is null) return;
        var beforeX = _selectedObject.XUm;
        var beforeY = _selectedObject.YUm;
        _selectedObject.XUm = _dragX + (long)Math.Round((e.X - _dragStart.X) / PixelsPerMm * 1000);
        _selectedObject.YUm = _dragY + (long)Math.Round((e.Y - _dragStart.Y) / PixelsPerMm * 1000);
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
        if (_selectedObject is null && _selectedRow is { } row && _selectedCol is { } col &&
            _layout.Rows[row].Cells[col].Content.Type != CellContentType.Empty)
        {
            var snapshot = _layout.Clone();
            var converted = ConvertLegacy(row, col);
            _undo.Push(snapshot);
            SelectObject(converted);
            LayoutChanged?.Invoke();
        }
        var obj = _selectedObject;
        if (obj is null) return;
        var tool = obj.Content.Type switch
        {
            CellContentType.Text => EditorTool.Text,
            CellContentType.Qr => EditorTool.Qr,
            CellContentType.Barcode => EditorTool.Barcode,
            CellContentType.Image => EditorTool.Image,
            _ => EditorTool.Select,
        };
        var content = AskForContent(tool, obj.Content);
        if (content is null) return;
        _undo.Push(_layout.Clone());
        obj.Content = content;
        SelectObject(obj);
        LayoutChanged?.Invoke();
    }

    private CellContent? AskForContent(EditorTool tool, CellContent? original)
    {
        if (tool == EditorTool.Text)
        {
            using var dialog = new BrandForm { Text = L10n.T("Object.TextTitle"), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ClientSize = new Size(430, 180), Font = Theme.UiFont(), BackColor = Color.White };
            var input = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Left = 12, Top = 12,
                Width = 406, Height = 110, Text = original?.Text ?? string.Empty };
            var ok = new Button { Text = L10n.T("Dialog.OK"), DialogResult = DialogResult.OK, Left = 230, Top = 135, Width = 90 };
            var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel, Left = 328, Top = 135, Width = 90 };
            dialog.Controls.AddRange(new Control[] { input, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            dialog.Shown += (_, _) => input.Focus();
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || input.Text.Length == 0) return null;
            return new CellContent { Type = CellContentType.Text, Text = input.Text,
                FontName = original?.FontName ?? _fontBox.SelectedItem as string,
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
        foreach (var field in fields) field.Enabled = _selectedObject is not null;
        if (_deleteObjectButton is not null) _deleteObjectButton.Enabled = _selectedObject is not null;
        if (_selectedObject is { } obj)
        {
            var vals = new[] { obj.XUm + Units.MmToUm(_displayPrintSettings.OffsetXMm),
                obj.YUm + Units.MmToUm(_displayPrintSettings.OffsetYMm), obj.WidthUm, obj.HeightUm };
            for (var i = 0; i < fields.Length; i++)
                fields[i].Value = Math.Clamp(UmToField(vals[i]), fields[i].Minimum, fields[i].Maximum);
        }
        else foreach (var field in fields) field.Value = 0;
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
    private int SamplePixels => Math.Max(1, _sample.ClientSize.Width - 60);
    public double PixelsPerMm => SamplePixels / (double)_measured.Value;

    public DisplayCalibrationDialog(double currentPixelsPerMm)
    {
        Text = L10n.T("Object.CalibrationTitle");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(460, 200);
        Font = Theme.UiFont();
        BackColor = Color.White;
        var hint = new Label { Text = L10n.T("Object.CalibrationHint"), Left = 15, Top = 12, Width = 430, Height = 42 };
        _sample = new Panel { Left = 15, Top = 58, Width = 430, Height = 40, BackColor = Color.White };
        _sample.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.Black, 2);
            e.Graphics.DrawLine(pen, 30, 18, _sample.ClientSize.Width - 30, 18);
            e.Graphics.DrawLine(pen, 30, 9, 30, 27);
            e.Graphics.DrawLine(pen, _sample.ClientSize.Width - 30, 9, _sample.ClientSize.Width - 30, 27);
        };
        var label = new Label { Text = L10n.T("Object.MeasuredMm"), Left = 15, Top = 110, Width = 200, Height = 26 };
        _measured = new NumericUpDown { Left = 225, Top = 108, Width = 90, Minimum = 13, Maximum = 370,
            DecimalPlaces = 1, Increment = 0.5m, Value = Math.Clamp((decimal)(370 / currentPixelsPerMm), 13, 370) };
        var ok = new Button { Text = L10n.T("Dialog.OK"), DialogResult = DialogResult.OK, Left = 245, Top = 155, Width = 90 };
        var cancel = new Button { Text = L10n.T("Dialog.Cancel"), DialogResult = DialogResult.Cancel, Left = 345, Top = 155, Width = 90 };
        Controls.AddRange(new Control[] { hint, _sample, label, _measured, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _measured.Value = Math.Clamp((decimal)(SamplePixels / currentPixelsPerMm), _measured.Minimum, _measured.Maximum);
    }
}
