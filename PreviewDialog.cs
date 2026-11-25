using System.Drawing.Drawing2D;

namespace MeyertecPrint;

/// <summary>Предпросмотр страниц в том же порядке, что и в задании на печать.</summary>
public sealed class PreviewDialog : BrandForm
{
    private readonly NumericUpDown _pageNumber;

    public PreviewDialog(IReadOnlyList<LabelLayout> pages, PrintSettings settings, float dpiX, float dpiY)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) throw new ArgumentException("No pages", nameof(pages));
        SuspendLayout();
        ShowIcon = MinimizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(940, 640);
        MinimumSize = new Size(500, 365);
        BackColor = Theme.Surface;
        Font = Theme.UiFont();
        Text = L10n.T("Preview.Title");

        var preview = new StripPreviewPanel { Dock = DockStyle.Fill, AccessibleName = Text, TabStop = false };
        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 52, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(12, 8, 12, 7), BackColor = Theme.Surface
        };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var pageControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty
        };
        var previous = PageButton("‹", "Preview.Previous", -1);
        var next = PageButton("›", "Preview.Next", 1);
        _pageNumber = new NumericUpDown
        {
            Minimum = 1, Maximum = pages.Count, Value = 1, Width = 62, TextAlign = HorizontalAlignment.Right,
            AccessibleName = L10n.T("Preview.Page"), Margin = new Padding(8, 5, 0, 0)
        };
        _pageNumber.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            _ = _pageNumber.Value;
            e.SuppressKeyPress = true;
        };
        pageControls.Controls.Add(previous);
        pageControls.Controls.Add(new Label
        {
            Text = _pageNumber.AccessibleName, AutoSize = true, Margin = new Padding(9, 8, 0, 0)
        });
        pageControls.Controls.Add(_pageNumber);
        pageControls.Controls.Add(new Label
        {
            Text = $"/ {pages.Count}", AutoSize = true, Margin = new Padding(6, 8, 9, 0), ForeColor = Theme.SecondaryText
        });
        pageControls.Controls.Add(next);
        var details = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, UseMnemonic = false,
            ForeColor = Theme.SecondaryText, Margin = new Padding(12, 0, 0, 0)
        };
        navigation.Controls.Add(pageControls, 0, 0);
        navigation.Controls.Add(details, 1, 0);

        var print = new Button { Text = L10n.T("Toolbar.Print") + "…", DialogResult = DialogResult.Yes };
        var close = new Button { Text = L10n.T("Dialog.Close"), DialogResult = DialogResult.Cancel };
        Controls.Add(preview);
        Controls.Add(navigation);
        Controls.Add(Theme.DialogFooter(print, close));
        CancelButton = close;

        void ShowPage()
        {
            var index = (int)_pageNumber.Value;
            var layout = pages[index - 1];
            previous.Enabled = index > 1;
            next.Enabled = index < pages.Count;
            details.Text = $"{Units.UmToMm(layout.MediaWidthUm):0.###} × {Units.UmToMm(layout.MediaHeightUm):0.###} {L10n.T("Unit.Mm")}  ·  {dpiX:0} × {dpiY:0} DPI";
            preview.AccessibleDescription = details.Text;
            preview.SetPage(layout, settings, dpiX, dpiY);
        }
        _pageNumber.ValueChanged += (_, _) => ShowPage();
        ShowPage();
        ResumeLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.FitDialogToScreen(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.PageUp or Keys.PageDown)
        {
            Navigate(keyData == Keys.PageUp ? -1 : 1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Button PageButton(string text, string key, int direction)
    {
        var button = new Button
        {
            Text = text, Font = Theme.UiFont(14, FontStyle.Bold), Size = new Size(36, 34),
            Margin = Padding.Empty, AccessibleName = L10n.T(key)
        };
        Theme.StyleButton(button);
        button.Click += (_, _) => Navigate(direction);
        return button;
    }

    private void Navigate(int direction)
        => _pageNumber.Value = Math.Clamp(_pageNumber.Value + direction, _pageNumber.Minimum, _pageNumber.Maximum);

    private sealed class StripPreviewPanel : Panel
    {
        private Bitmap? _preview;
        private bool _hasNotchedStock;
        private readonly Label _error;

        public StripPreviewPanel()
        {
            BackColor = SystemInformation.HighContrast ? Theme.Surface : Theme.Canvas;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            _error = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(30),
                ForeColor = Theme.ErrorText, UseMnemonic = false
            };
            Controls.Add(_error);
        }

        public void SetPage(LabelLayout layout, PrintSettings settings, float dpiX, float dpiY)
        {
            _preview?.Dispose();
            _preview = null;
            _hasNotchedStock = layout.AlternateNotch is not null;
            try { _preview = LabelPrintRenderer.Render(layout, settings, dpiX, dpiY, validateForPrint: true, showMedia: true); }
            catch (Exception ex) { _error.Text = ex.Message; }
            _error.Visible = _preview is null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_preview is null) return;
            var available = Rectangle.Inflate(ClientRectangle, -LogicalToDeviceUnits(34), -LogicalToDeviceUnits(34));
            if (available.Width <= 0 || available.Height <= 0) return;

            var factor = Math.Min(available.Width / (double)_preview.Width, available.Height / (double)_preview.Height);
            var width = Math.Max(1, (int)(_preview.Width * factor));
            var height = Math.Max(1, (int)(_preview.Height * factor));
            var bounds = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
            if (!_hasNotchedStock && !SystemInformation.HighContrast)
            {
                var shadowBounds = bounds;
                shadowBounds.Offset(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4));
                using var shadow = new SolidBrush(Color.FromArgb(26, Theme.Dark));
                e.Graphics.FillRectangle(shadow, shadowBounds);
            }
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(_preview, bounds);
            if (!_hasNotchedStock)
            {
                using var edge = new Pen(Theme.SecondaryText);
                e.Graphics.DrawRectangle(edge, bounds);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _preview?.Dispose();
            base.Dispose(disposing);
        }
    }
}
