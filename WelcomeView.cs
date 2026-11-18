using System.Drawing.Drawing2D;

namespace MeyertecPrint;

internal sealed class WelcomeView : TableLayoutPanel
{
    private readonly Label _heading;
    private readonly Label _intro;
    private readonly Button _create;
    private readonly Button _open;

    public WelcomeView(Action create, Action open)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ColumnCount = 2;
        RowCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var content = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 4, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.None, Margin = Padding.Empty
        };
        _heading = new Label
        {
            AutoSize = true, Font = Theme.UiFont(32, FontStyle.Bold), ForeColor = Theme.Dark,
            Margin = new Padding(0, 0, 0, 16), UseMnemonic = false
        };
        _intro = new Label
        {
            AutoSize = true, MaximumSize = new Size(335, 0), Font = Theme.UiFont(11),
            ForeColor = Theme.SecondaryText, Margin = new Padding(2, 0, 0, 30)
        };
        _create = ActionButton("01_new_file", true, create);
        _open = ActionButton("02_open", false, open);
        content.Controls.Add(_heading, 0, 0);
        content.Controls.Add(_intro, 0, 1);
        content.Controls.Add(_create, 0, 2);
        content.Controls.Add(_open, 0, 3);
        Controls.Add(content, 0, 0);
        Controls.Add(new CatalogPlate { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        ApplyStrings();
    }

    private Button ActionButton(string icon, bool primary, Action action)
    {
        var button = new Button
        {
            Size = new Size(320, 46), Font = Theme.UiFont(10.5f, FontStyle.Bold),
            Image = Theme.Icon(icon, LogicalToDeviceUnits(23)), ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText,
            Padding = new Padding(14, 0, 8, 0), Margin = new Padding(2, 0, 0, 10)
        };
        Theme.StyleButton(button, primary);
        button.Click += (_, _) => action();
        return button;
    }

    public void ApplyStrings()
    {
        _heading.Text = L10n.T("Welcome.Heading");
        _intro.Text = L10n.T("Welcome.Intro");
        _create.Text = "  " + L10n.T("File.NewProject");
        _open.Text = "  " + L10n.T("File.OpenProject");
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
            var entry = CleatCatalog.All.FirstOrDefault(c => c.DisplayName == "MTP 2.5");
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
            var inset = LogicalToDeviceUnits(30);
            Theme.DrawGrid(g, e.ClipRectangle, LogicalToDeviceUnits(22), Color.FromArgb(58, 78, 87));
            var box = Rectangle.Inflate(ClientRectangle, -inset, -inset);
            if (box.Width < 20 || box.Height < 20) return;
            using var guide = new Pen(Color.FromArgb(78, 99, 108));
            foreach (var x in new[] { box.Left, box.Right })
            foreach (var y in new[] { box.Top, box.Bottom })
            {
                g.DrawLine(guide, x - 6, y, x + 6, y);
                g.DrawLine(guide, x, y - 6, x, y + 6);
            }
            if (_image is null) return;
            var scale = Math.Min(box.Width * 1.20f, box.Height * 1.02f);
            var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f - inset / 3f);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, center.X - scale / 2, center.Y - scale / 2, scale, scale);
            using var modelFont = Theme.UiFont(15, FontStyle.Bold);
            var textY = box.Bottom - modelFont.GetHeight(g) - inset / 2;
            using var accent = new SolidBrush(Theme.Yellow);
            g.FillRectangle(accent, box.Left + 16, textY + 4, 4, modelFont.GetHeight(g) - 6);
            g.DrawString(_model, modelFont, Brushes.White, box.Left + 30, textY);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
