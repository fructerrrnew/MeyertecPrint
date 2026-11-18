using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MeyertecPrint;

public static class Theme
{
    public static readonly Color Dark = Color.FromArgb(15, 32, 42);
    public static readonly Color ProjectBar = Color.Black;
    public static readonly Color Yellow = Color.FromArgb(255, 203, 4);
    public static readonly Color LightBackground = Color.FromArgb(245, 247, 247);
    public static readonly Color RailSurface = Color.FromArgb(249, 250, 250);
    public static readonly Color RailSelection = Color.FromArgb(221, 227, 230);
    public static readonly Color RailSelectionBorder = Color.FromArgb(161, 173, 180);
    public static readonly Color Canvas = Color.FromArgb(234, 239, 240);
    public static readonly Color SecondaryText = Color.FromArgb(78, 97, 105);
    public static readonly Color Border = Color.FromArgb(207, 217, 219);
    public static readonly Color Hover = Color.FromArgb(231, 237, 238);
    public static readonly Color SoftYellow = Color.FromArgb(255, 242, 188);

    // Keep the original document font fallback as well as the UI font.
    private static readonly Lazy<string> FontFamilyName = new(() =>
    {
        try
        {
            using var fonts = new InstalledFontCollection();
            return fonts.Families.Any(f => f.Name.Equals("Rubik", StringComparison.OrdinalIgnoreCase))
                ? "Rubik" : "Segoe UI";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return "Segoe UI";
        }
    });

    public static string FontName => FontFamilyName.Value;
    public static Font UiFont(float sizePt = 9f, FontStyle style = FontStyle.Regular)
        => new(FontName, sizePt, style, GraphicsUnit.Point);

    public static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Yellow : Color.White;
        button.ForeColor = Dark;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 217, 67) : Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(239, 184, 0) : Border;
        button.Cursor = Cursors.Hand;
    }

    public static FlowLayoutPanel DialogFooter(Button primary, Button? cancel = null)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16, 12, 16, 12),
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Color.White
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.ClientSize.Width, 0);
        };
        foreach (var button in new[] { primary, cancel })
        {
            if (button is null) continue;
            StyleButton(button, button == primary);
            button.Size = new Size(110, 34);
            button.Margin = new Padding(8, 0, 0, 0);
            bar.Controls.Add(button);
        }
        return bar;
    }

    // Assets are shared by tool buttons and menu items for the life of the process.
    private static readonly Dictionary<(string, int), Bitmap> Icons = new();
    public static Bitmap Icon(string name, int size)
    {
        if (Icons.TryGetValue((name, size), out var image)) return image;
        using var original = new Bitmap(Path.Combine(AppContext.BaseDirectory, "IconsUI", name + ".png"));
        image = new Bitmap(size, size);
        using var g = Graphics.FromImage(image);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var edges = new System.Drawing.Imaging.ImageAttributes();
        edges.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(original, new Rectangle(0, 0, size, size), 0, 0, original.Width, original.Height,
            GraphicsUnit.Pixel, edges);
        Icons[(name, size)] = image;
        return image;
    }

    public static void DrawGrid(Graphics g, Rectangle bounds, int step, Color color)
    {
        using var brush = new SolidBrush(color);
        for (var y = bounds.Top - bounds.Top % step; y < bounds.Bottom; y += step)
            for (var x = bounds.Left - bounds.Left % step; x < bounds.Right; x += step)
                g.FillRectangle(brush, x, y, 1.5f, 1.5f);
    }
}

public sealed class BrandMenuRenderer : ToolStripProfessionalRenderer
{
    public BrandMenuRenderer() : base(new BrandColorTable()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Enabled && e.Item is ToolStripMenuItem)
            e.TextColor = e.Item.Owner is MenuStrip && !e.Item.Selected && !e.Item.Pressed
                ? Color.White : Theme.Dark;
        base.OnRenderItemText(e);
    }
}

public sealed class BrandColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => Theme.ProjectBar;
    public override Color MenuStripGradientEnd => Theme.ProjectBar;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
    public override Color MenuBorder => Theme.Border;
    public override Color MenuItemBorder => Theme.SoftYellow;
    public override Color MenuItemSelected => Theme.SoftYellow;
    public override Color MenuItemSelectedGradientBegin => Theme.SoftYellow;
    public override Color MenuItemSelectedGradientEnd => Theme.SoftYellow;
    public override Color MenuItemPressedGradientBegin => Theme.Yellow;
    public override Color MenuItemPressedGradientMiddle => Theme.Yellow;
    public override Color MenuItemPressedGradientEnd => Theme.Yellow;
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color SeparatorDark => Theme.Border;
    public override Color SeparatorLight => Color.White;
}
