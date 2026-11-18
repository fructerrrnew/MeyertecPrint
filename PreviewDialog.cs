namespace MeyertecPrint;

/// <summary>Preview the same ordered set of pages that is available to print.</summary>
public sealed class PreviewDialog : BrandForm
{
    public PreviewDialog(IReadOnlyList<LabelLayout> pages, PrintSettings settings, float dpiX, float dpiY)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) throw new ArgumentException("No pages", nameof(pages));
        FormBorderStyle = FormBorderStyle.Sizable; ShowIcon = false;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent; Size = new Size(900, 600); MinimumSize = new Size(480, 360);
        BackColor = Theme.LightBackground; Font = Theme.UiFont(); Text = L10n.T("Preview.Title");
        var panel = new StripPreviewPanel { Dock = DockStyle.Fill };
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(12, 5, 0, 0), WrapContents = false };
        navigation.Controls.Add(new Label { Text = L10n.T("Preview.Page"), AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        var number = new NumericUpDown { Minimum = 1, Maximum = pages.Count, Width = 80, Value = 1 };
        navigation.Controls.Add(number);
        navigation.Controls.Add(new Label { Text = $"/ {pages.Count}  ({dpiX:0} × {dpiY:0} DPI)", AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
        number.ValueChanged += (_, _) => panel.SetPage(pages[(int)number.Value - 1], settings, dpiX, dpiY);
        var ok = new Button { Text = L10n.T("Dialog.OK"), DialogResult = DialogResult.OK, Width = 110 };
        Controls.Add(panel); Controls.Add(navigation); Controls.Add(Theme.DialogFooter(ok));
        AcceptButton = ok;
        panel.SetPage(pages[0], settings, dpiX, dpiY);
    }

    private sealed class StripPreviewPanel : Panel
    {
        private Bitmap? _preview;
        private string? _error;
        public StripPreviewPanel()
        {
            BackColor = Theme.Canvas;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        public void SetPage(LabelLayout layout, PrintSettings settings, float dpiX, float dpiY)
        {
            _preview?.Dispose(); _preview = null; _error = null;
            try { _preview = LabelPrintRenderer.Render(layout, settings, dpiX, dpiY, validateForPrint: true); }
            catch (Exception ex) { _error = ex.Message; }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_preview is null)
            {
                TextRenderer.DrawText(e.Graphics, _error ?? L10n.T("Preview.Empty"), Font,
                    Rectangle.Inflate(ClientRectangle, -24, -24), Theme.SecondaryText, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            var available = Rectangle.Inflate(ClientRectangle, -32, -40);
            if (available.Width <= 0 || available.Height <= 0) return;
            var factor = Math.Min(available.Width / (double)_preview.Width, available.Height / (double)_preview.Height);
            var width = (int)(_preview.Width * factor); var height = (int)(_preview.Height * factor);
            var bounds = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImage(_preview, bounds);
            using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, bounds);
        }
        protected override void Dispose(bool disposing) { if (disposing) _preview?.Dispose(); base.Dispose(disposing); }
    }
}
