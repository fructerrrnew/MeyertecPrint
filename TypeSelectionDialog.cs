using System.Drawing;
using System.Windows.Forms;

namespace MeyertecPrint;

/// <summary>
/// Второй шаг создания проекта: слева — раскрытый список всех типов
/// (типы этикеток каталога или клеммы из каталога cleat),
/// справа — изображение выбранного типа.
/// </summary>
public class TypeSelectionDialog : BrandForm
{
    private readonly bool _cleatMode;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        BackColor = Color.White,
        ForeColor = Theme.Dark,
    };
    private readonly PictureBox _imageBox = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White,
    };
    private readonly LabelPreviewControl _labelPreview = new() { Dock = DockStyle.Fill };
    private readonly Panel _rightPanel = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
    };

    private readonly Label _referenceNotice = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(16), ForeColor = Theme.SecondaryText, BackColor = Color.White, Visible = false };
    private Button _okButton = null!;
    private Bitmap? _cleatImage;
    private Bitmap? _placeholder;

    /// <summary>Выбранный тип этикетки (режим этикеток; null, если отменено).</summary>
    public LabelType? SelectedLabelType { get; private set; }

    /// <summary>Выбранная клемма (режим клемм; null, если отменено).</summary>
    public CleatType? SelectedCleat { get; private set; }

    public TypeSelectionDialog(bool cleatMode)
    {
        _cleatMode = cleatMode;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(810, 480);
        BackColor = Theme.LightBackground;
        Font = Theme.UiFont();
        Text = L10n.T(cleatMode ? "TypeSel.TitleCleat" : "TypeSel.TitleLabel");

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 16, 16, 14),
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));

        BuildColumns();
        FillList();

        if (_cleatMode && _list.Items.Count == 0)
        {
            // Каталога клемм нет (папки cleat рядом с exe) — подсказка справа
            _rightPanel.Controls.Add(new Label
            {
                Text = L10n.T("TypeSel.Empty"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.SecondaryText,
            });
        }
        else
        {
            _rightPanel.Controls.Add(_cleatMode ? (Control)_imageBox : _labelPreview);
            if (!_cleatMode) { _referenceNotice.Text = L10n.T("Catalog.ReferenceHint"); _rightPanel.Controls.Add(_referenceNotice); _referenceNotice.BringToFront(); }
        }

        main.Controls.Add(_list, 0, 0);
        main.Controls.Add(_rightPanel, 1, 0);

        _list.SelectedIndexChanged += OnSelectionChanged;

        _okButton = new Button
        {
            Text = L10n.T("NewProject.Create"),
            DialogResult = DialogResult.OK,
            Width = 110,
            Enabled = _list.Items.Count > 0,
        };
        var cancelButton = new Button
        {
            Text = L10n.T("Dialog.Cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110,
        };
        var buttons = Theme.DialogFooter(_okButton, cancelButton);

        Controls.Add(main);
        Controls.Add(buttons);
        AcceptButton = _okButton;
        CancelButton = cancelButton;

        _list.MultiSelect = false;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Resize += (_, _) => FitColumns();
        ApplyColumnHeaders();
        Shown += (_, _) =>
        {
            FitColumns();
            if (_list.Items.Count > 0)
            {
                var defaultItem = _cleatMode ? _list.Items[0] :
                    _list.Items.Cast<ListViewItem>().FirstOrDefault(item =>
                        item.Tag is LabelType type && type.Name == LabelTypeCatalog.Stock60x30) ?? _list.Items[0];
                defaultItem.Selected = true;
                defaultItem.Focused = true;
                defaultItem.EnsureVisible();
                OnSelectionChanged(this, EventArgs.Empty);
            }
            _list.Focus();
        };
    }

    private void BuildColumns()
    {
        if (_cleatMode)
        {
            _list.Columns.Add(new ColumnHeader { Width = 240 });
            _list.Columns.Add(new ColumnHeader { Width = 120 });
        }
        else
        {
            _list.Columns.Add(new ColumnHeader { Width = 220 });
            _list.Columns.Add(new ColumnHeader { Width = 110 });
            _list.Columns.Add(new ColumnHeader { Width = 80 });
        }
    }

    private void FitColumns()
    {
        var width = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
        if (width <= 0) return;
        if (_cleatMode)
        {
            _list.Columns[0].Width = width * 2 / 3;
            _list.Columns[1].Width = width - _list.Columns[0].Width;
        }
        else
        {
            _list.Columns[1].Width = width / 4;
            _list.Columns[2].Width = width / 6;
            _list.Columns[0].Width = width - _list.Columns[1].Width - _list.Columns[2].Width;
        }
    }

    private void ApplyColumnHeaders()
    {
        if (_cleatMode)
        {
            _list.Columns[0].Text = L10n.T("TypeSel.CleatType");
            _list.Columns[1].Text = L10n.T("TypeSel.Size");
        }
        else
        {
            _list.Columns[0].Text = L10n.T("TypeSel.Name");
            _list.Columns[1].Text = L10n.T("TypeSel.Size");
            _list.Columns[2].Text = L10n.T("TypeSel.InRowCount");
        }
    }

    private void FillList()
    {
        var items = new List<ListViewItem>();
        if (_cleatMode)
        {
            foreach (var cleat in CleatCatalog.All)
            {
                var item = new ListViewItem(new[] { cleat.Type, $"{cleat.Size:0.###}" });
                item.Tag = cleat;
                items.Add(item);
            }
        }
        else
        {
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
        }
        _list.Items.AddRange(items.ToArray());
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _okButton.Enabled = _list.SelectedItems.Count > 0 &&
            (_cleatMode || _list.SelectedItems[0].Tag is LabelType { ReferenceOnly: false });
        if (_list.SelectedItems.Count == 0) return;

        if (_cleatMode)
        {
            ShowCleatImage(_list.SelectedItems[0].Tag as CleatType);
        }
        else
        {
            var selected = _list.SelectedItems[0].Tag as LabelType;
            _referenceNotice.Visible = selected?.ReferenceOnly == true;
            _labelPreview.Visible = !_referenceNotice.Visible;
            _labelPreview.SetType(selected?.ReferenceOnly == true ? null : selected);
        }
    }

    private void ShowCleatImage(CleatType? cleat)
    {
        _cleatImage?.Dispose();
        _cleatImage = null;
        _imageBox.Image = null;

        if (cleat is null)
            return;

        var path = cleat.ResolveImagePath();
        if (path is null)
        {
            _imageBox.Image = MakePlaceholder();
            return;
        }

        try
        {
            _cleatImage = new Bitmap(path);
            _imageBox.Image = _cleatImage;
        }
        catch
        {
            // Файл изображения не читается — заглушка
            _imageBox.Image = MakePlaceholder();
        }
    }

    /// <summary>Заглушка «Нет изображения» (файл отсутствует или не читается).</summary>
    private Image MakePlaceholder()
    {
        if (_placeholder is null)
        {
            _placeholder = new Bitmap(240, 120);
            using var g = Graphics.FromImage(_placeholder);
            g.Clear(Color.White);
            using var brush = new SolidBrush(Theme.SecondaryText);
            using var font = Theme.UiFont(10f);
            var format = new StringFormat(StringFormatFlags.NoClip)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(L10n.T("TypeSel.NoImage"), font, brush,
                new RectangleF(0, 0, _placeholder.Width, _placeholder.Height), format);
        }
        return _placeholder;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _list.SelectedItems.Count > 0)
        {
            var item = _list.SelectedItems[0];
            SelectedLabelType = item.Tag as LabelType;
            SelectedCleat = item.Tag as CleatType;
        }
        _cleatImage?.Dispose();
        _placeholder?.Dispose();
        base.OnFormClosing(e);
    }
}

/// <summary>
/// Предпросмотр полосы для выбранного типа этикетки: рисует геометрию
/// (у типов этикеток файлов изображений в каталоге нет).
/// </summary>
public sealed class LabelPreviewControl : Control
{
    private LabelType? _type;

    public LabelPreviewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.White;
    }

    public void SetType(LabelType? type)
    {
        _type = type;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 20 || Height < 20)
            return;

        using (var bg = new SolidBrush(Color.White))
            e.Graphics.FillRectangle(bg, ClientRectangle);

        if (_type is null)
            return;

        var g = _type.Geometry;
        var inRow = Math.Max(1, g.LabelsInRow);
        var stripWUm = g.LeftUm + inRow * g.WidthUm + (inRow - 1) * g.GapBetweenUm + g.RightUm;
        var stripHUUm = g.TopUm + g.HeightUm + g.GapBottomUm;
        if (stripWUm <= 0 || stripHUUm <= 0)
            return;

        const int caption = 26;
        const int margin = 20;
        var availW = Width - 2 * margin;
        var availH = Height - caption - 2 * margin;
        if (availW < 20 || availH < 20)
            return;

        var scale = Math.Min(availW / (double)stripWUm, availH / (double)stripHUUm);
        var w = (int)(stripWUm * scale);
        var h = (int)(stripHUUm * scale);
        var x0 = (Width - w) / 2;
        var y0 = caption + margin + (availH - h) / 2;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        using (var pen = new Pen(Theme.Border))
        using (var strip = new SolidBrush(Color.White))
        {
            e.Graphics.FillRectangle(strip, x0, y0, w, h);
            e.Graphics.DrawRectangle(pen, x0, y0, w, h);
        }

        using var labelPen = new Pen(Theme.Dark);
        var labelW = (int)(g.WidthUm * scale);
        var labelH = (int)(g.HeightUm * scale);
        var ly = y0 + (int)(g.TopUm * scale);
        var lx = x0 + (int)(g.LeftUm * scale);
        for (var i = 0; i < inRow; i++)
        {
            e.Graphics.DrawRectangle(labelPen, lx, ly, labelW, labelH);
            lx += (int)((g.WidthUm + g.GapBetweenUm) * scale);
        }

        using var captionBrush = new SolidBrush(Theme.Dark);
        using var captionFont = Theme.UiFont(9.5f);
        var text = $"{_type.DisplayName} — {_type.SizeDisplay}";
        if (inRow > 1)
            text += $", {inRow} {L10n.T("TypeSel.InRow")}";
        e.Graphics.DrawString(text, captionFont, captionBrush, margin, 6);
    }
}
