using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MeyertecPrint;

/// <summary>Displays the project's labels in print order, with only visible pages rasterized.</summary>
public sealed class LabelSequenceView : Control
{
    private readonly HScrollBar _scroll = new() { Dock = DockStyle.Bottom, TabStop = false };
    private readonly Dictionary<int, (Bitmap? Image, string? Error)> _thumbnails = new();
    private IReadOnlyList<LabelLayout> _pages = [];
    private PrintSettings _settings = new();
    private double _aspectRatio = 2;
    private int _selectedIndex = -1;
    private int _visiblePages = 1;

    public event Action<int>? PageSelected;

    public LabelSequenceView()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.List;
        Controls.Add(_scroll);
        _scroll.Visible = false;
        _scroll.ValueChanged += (_, _) => Invalidate();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, -1, _pages.Count - 1);
            if (_selectedIndex == clamped) return;
            _selectedIndex = clamped;
            if (clamped >= 0 && _pages.Count > _visiblePages)
            {
                var first = _scroll.Value;
                if (clamped < first) _scroll.Value = clamped;
                else if (clamped >= first + _visiblePages)
                    _scroll.Value = clamped - _visiblePages + 1;
            }
            Invalidate();
        }
    }

    /// <summary>Set pages of a project sharing one physical media format.</summary>
    public void SetPages(IReadOnlyList<LabelLayout> pages, PrintSettings settings)
    {
        ReleaseThumbnails();
        _pages = pages;
        _settings = settings;
        if (pages.Count > 0)
        {
            var first = pages[0];
            _aspectRatio = Math.Max(1d, first.MediaWidthUm) / Math.Max(1d, first.MediaHeightUm);
        }
        _selectedIndex = Math.Clamp(_selectedIndex, -1, pages.Count - 1);
        UpdateScroll();
        Invalidate();
    }

    private int MarginPixels => LogicalToDeviceUnits(10);
    private int CaptionHeight => Font.Height + LogicalToDeviceUnits(4);
    private int TileWidth => MarginPixels + Math.Clamp((int)Math.Min(int.MaxValue,
        Math.Max(1, ClientSize.Height - _scroll.Height - CaptionHeight - 2 * MarginPixels) * _aspectRatio),
        LogicalToDeviceUnits(48), LogicalToDeviceUnits(250));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) UpdateScroll();
    }

    private void UpdateScroll()
    {
        _visiblePages = Math.Max(1, (ClientSize.Width - MarginPixels) / Math.Max(1, TileWidth));
        _scroll.Visible = _pages.Count > _visiblePages;
        _scroll.LargeChange = _visiblePages;
        _scroll.Maximum = Math.Max(0, _pages.Count - 1);
        var lastFirst = Math.Max(0, _pages.Count - _visiblePages);
        _scroll.Value = Math.Clamp(_scroll.Value, 0, lastFirst);
        if (_selectedIndex >= 0 && _scroll.Visible)
        {
            if (_selectedIndex < _scroll.Value) _scroll.Value = _selectedIndex;
            else if (_selectedIndex >= _scroll.Value + _visiblePages)
                _scroll.Value = Math.Min(lastFirst, _selectedIndex - _visiblePages + 1);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var highContrast = SystemInformation.HighContrast;
        e.Graphics.Clear(highContrast ? SystemColors.Window : Theme.Canvas);
        if (_pages.Count == 0) return;

        var first = _scroll.Value;
        var width = TileWidth;
        var end = Math.Min(_pages.Count, first + Math.Max(1, ClientSize.Width / width + 1));
        var availableHeight = ClientSize.Height - (_scroll.Visible ? _scroll.Height : 0);
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        for (var index = first; index < end; index++)
        {
            var area = new Rectangle(MarginPixels + (index - first) * width, MarginPixels,
                width - MarginPixels, Math.Max(1, availableHeight - 2 * MarginPixels - CaptionHeight));
            var cached = Thumbnail(index);
            if (cached.Image is { } image)
            {
                var scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
                var size = new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                var picture = new Rectangle(area.Left + (area.Width - size.Width) / 2,
                    area.Top + (area.Height - size.Height) / 2, size.Width, size.Height);
                e.Graphics.DrawImage(image, picture);
                e.Graphics.DrawRectangle(SystemPens.WindowFrame, picture);
            }
            else
                TextRenderer.DrawText(e.Graphics, cached.Error, Font, area, Theme.ErrorText,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            var caption = new Rectangle(area.Left, area.Bottom, area.Width, CaptionHeight);
            if (index == _selectedIndex)
            {
                using var selection = new Pen(highContrast ? SystemColors.Highlight : Theme.RailSelectionBorder, 2);
                e.Graphics.DrawRectangle(selection, Rectangle.Union(area, caption));
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, caption);
            }
            TextRenderer.DrawText(e.Graphics, (index + 1).ToString(), Font, caption, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        foreach (var key in _thumbnails.Keys.Where(key => key < first || key >= end).ToArray())
        {
            _thumbnails[key].Image?.Dispose();
            _thumbnails.Remove(key);
        }
    }

    private (Bitmap? Image, string? Error) Thumbnail(int index)
    {
        if (_thumbnails.TryGetValue(index, out var cached)) return cached;
        try
        {
            using var raster = LabelPrintRenderer.Render(_pages[index], _settings, 300, 300);
            var scale = Math.Min(1d, LogicalToDeviceUnits(280) / (double)Math.Max(raster.Width, raster.Height));
            var thumbnailSize = new Size(Math.Max(1, (int)(raster.Width * scale)), Math.Max(1, (int)(raster.Height * scale)));
            cached = (new Bitmap(raster, thumbnailSize), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or ExternalException)
        { cached = (null, ex.Message); }
        _thumbnails.Add(index, cached);
        return cached;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.X < MarginPixels) return;
        Focus();
        SelectPage(_scroll.Value + (e.X - MarginPixels) / TileWidth);
    }

    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_pages.Count == 0) return;
        var index = e.KeyCode switch
        {
            Keys.Left => Math.Max(0, _selectedIndex - 1),
            Keys.Right => Math.Min(_pages.Count - 1, _selectedIndex + 1),
            Keys.Home => 0,
            Keys.End => _pages.Count - 1,
            _ => -1
        };
        if (index < 0) return;
        SelectPage(index);
        e.Handled = true;
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        SelectedIndex = index;
        PageSelected?.Invoke(index);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void ReleaseThumbnails()
    {
        foreach (var thumbnail in _thumbnails.Values) thumbnail.Image?.Dispose();
        _thumbnails.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseThumbnails();
        base.Dispose(disposing);
    }
}
