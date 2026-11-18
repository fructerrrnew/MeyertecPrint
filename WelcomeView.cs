using System.Drawing.Drawing2D;

namespace MeyertecPrint;

internal sealed class WelcomeView : TableLayoutPanel
{
    private readonly Label _heading;
    private readonly Label _intro;
    private readonly Button _create;
    private readonly Button _open;
    private readonly CatalogPlate _plate;

    public WelcomeView(Action create, Action open)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface;
        ColumnCount = 2;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var content = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 5, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.None, Margin = Padding.Empty
        };
        var mark = new PictureBox
        {
            Size = new Size(46, 46), Image = Theme.Icon("Brand", LogicalToDeviceUnits(46)),
            SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(3, 0, 0, 20), TabStop = false
        };
        _heading = new Label
        {
            AutoSize = true, Font = Theme.UiFont(36, FontStyle.Bold), ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, 14), UseMnemonic = false
        };
        _intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(333, 0), Font = Theme.UiFont(11),
            ForeColor = Theme.SecondaryText, Margin = new Padding(3, 0, 0, 28)
        };
        _create = ActionButton("01_new_file", true, create);
        _open = ActionButton("02_open", false, open);
        content.Controls.Add(mark, 0, 0);
        content.Controls.Add(_heading, 0, 1);
        content.Controls.Add(_intro, 0, 2);
        content.Controls.Add(_create, 0, 3);
        content.Controls.Add(_open, 0, 4);
        Controls.Add(content, 0, 0);
        _plate = new CatalogPlate { Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = false };
        Controls.Add(_plate, 1, 0);
        Resize += (_, _) => LayoutPlate();
        LayoutPlate();
        ApplyStrings();
    }

    private void LayoutPlate()
    {
        var showPlate = !SystemInformation.HighContrast && Width >= LogicalToDeviceUnits(760);
        ColumnStyles[0].Width = showPlate ? 46 : 100;
        ColumnStyles[1].Width = showPlate ? 54 : 0;
        _plate.Visible = showPlate;
    }

    private Button ActionButton(string icon, bool primary, Action action)
    {
        var button = new Button
        {
            Size = new Size(300, 46), Font = Theme.UiFont(10.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            Image = Theme.Icon(icon, LogicalToDeviceUnits(20)), ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(12, 0, 8, 0), Margin = new Padding(3, 0, 0, 10)
        };
        Theme.StyleButton(button, primary);
        button.Click += (_, _) => action();
        return button;
    }

    public void ApplyStrings()
    {
        _heading.Text = L10n.T("Welcome.Heading");
        _intro.Text = L10n.T("Welcome.Intro");
        _create.Text = L10n.T("Welcome.Create");
        _open.Text = L10n.T("Welcome.Open");
    }

    // The plate uses the same terminal photo and model name as the project catalog.
    private sealed class CatalogPlate : Control
    {
        private readonly Bitmap? _image;
        private readonly string _model = string.Empty;

        public CatalogPlate()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Dark;
            var entry = CleatCatalog.All.FirstOrDefault(c => c.DisplayName == "MTP-2.5");
            if (entry?.ResolveImagePath() is not { } path) return;
            try
            {
                using var original = new Bitmap(path);
                _image = new Bitmap(original);
                _model = entry.DisplayName;
            }
            catch (ArgumentException) { /* The start actions also work without a catalog photo. */ }
            catch (IOException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using var backdrop = new LinearGradientBrush(ClientRectangle, Color.FromArgb(30, 54, 68), Theme.Dark, 115);
            g.FillRectangle(backdrop, ClientRectangle);
            var inset = LogicalToDeviceUnits(42);
            Theme.DrawGrid(g, e.ClipRectangle, LogicalToDeviceUnits(24), Color.FromArgb(70, 94, 107));
            var box = Rectangle.Inflate(ClientRectangle, -inset, -inset);
            if (box.Width < 20 || box.Height < 20) return;
            using var guide = new Pen(Color.FromArgb(117, 139, 150));
            var mark = LogicalToDeviceUnits(9);
            foreach (var x in new[] { box.Left, box.Right })
            foreach (var y in new[] { box.Top, box.Bottom })
            {
                g.DrawLine(guide, x - mark, y, x + mark, y);
                g.DrawLine(guide, x, y - mark, x, y + mark);
            }
            if (_image is null) return;
            var scale = Math.Min(box.Width * 1.27f, box.Height * 1.12f);
            var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f - inset / 4f);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, center.X - scale / 2, center.Y - scale / 2, scale, scale);
            using var modelFont = Theme.UiFont(11, FontStyle.Bold);
            var textY = box.Bottom - modelFont.GetHeight(g) - inset / 2;
            using var accent = new SolidBrush(Theme.Yellow);
            g.FillRectangle(accent, box.Left + mark, textY + 2, LogicalToDeviceUnits(3), modelFont.GetHeight(g) - 4);
            g.DrawString(_model, modelFont, Brushes.White, box.Left + 3 * mark, textY);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
