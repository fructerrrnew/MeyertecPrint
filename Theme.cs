using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MeyertecPrint;

public static class Theme
{
    public static readonly Color Dark = Color.FromArgb(15, 32, 42);
    public static readonly Color ProjectBar = Dark;
    public static readonly Color Yellow = Color.FromArgb(255, 203, 4);
    public static readonly Color LightBackground = Color.FromArgb(245, 247, 247);
    public static readonly Color RailSurface = Color.FromArgb(247, 249, 250);
    public static readonly Color RailSelection = Color.FromArgb(255, 244, 198);
    public static readonly Color RailSelectionBorder = Color.FromArgb(161, 111, 0);
    public static readonly Color Canvas = Color.FromArgb(231, 237, 240);
    public static readonly Color Border = Color.FromArgb(209, 219, 224);
    public static readonly Color Hover = Color.FromArgb(231, 237, 238);
    public static readonly Color SoftYellow = Color.FromArgb(255, 242, 188);
    public static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    public static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Dark;
    public static Color SecondaryText => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(72, 90, 101);
    public static Color ErrorText => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(151, 31, 32);

    // The document font fallback follows the installed faces independently of the interface font.
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
    public static Font UiFont(float sizePt = 10f, FontStyle style = FontStyle.Regular)
        => new(SystemFonts.MessageBoxFont?.Name ?? "Segoe UI", sizePt, style, GraphicsUnit.Point);

    public static void StyleButton(ButtonBase button, bool primary = false)
    {
        if (SystemInformation.HighContrast)
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            return;
        }
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 217, 67) : Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(239, 184, 0) : Border;
        void ApplyColors()
        {
            button.BackColor = button.Enabled ? (primary ? Yellow : Color.White) : LightBackground;
            button.ForeColor = button.Enabled ? Dark : SecondaryText;
            button.FlatAppearance.BorderColor = primary && button.Enabled ? RailSelectionBorder : Border;
        }
        button.EnabledChanged += (_, _) => ApplyColors();
        ApplyColors();
    }

    public static FlowLayoutPanel DialogFooter(Button primary, Button? cancel = null, Button? back = null)
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16, 12, 16, 12),
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Surface
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, 0, bar.ClientSize.Width, 0);
        };
        foreach (var button in new[] { cancel, primary, back })
        {
            if (button is null) continue;
            StyleButton(button, button == primary);
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(92, 34);
            button.Padding = new Padding(10, 0, 10, 0);
            button.Margin = new Padding(8, 0, 0, 0);
            button.TabIndex = button == back ? 0 : button == primary ? 1 : 2;
            bar.Controls.Add(button);
        }
        return bar;
    }

    public static void FitDialogToScreen(Form form)
    {
        var area = Screen.FromControl(form.Owner ?? form).WorkingArea;
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width),
            Math.Min(form.MinimumSize.Height, area.Height));
        var width = Math.Min(form.Width, area.Width);
        var height = Math.Min(form.Height, area.Height);
        form.Bounds = new Rectangle(Math.Clamp(form.Left, area.Left, area.Right - width),
            Math.Clamp(form.Top, area.Top, area.Bottom - height), width, height);
    }

    // Assets are shared by tool buttons and menu items for the life of the process.
    private static readonly Dictionary<(string, int, Color?), Bitmap> Icons = new();
    public static Bitmap Icon(string name, int size, Color? foreground = null)
    {
        var key = (name, size, foreground);
        if (Icons.TryGetValue(key, out var image)) return image;
        using var original = new Bitmap(Path.Combine(AppContext.BaseDirectory, "IconsUI", name + ".png"));
        image = new Bitmap(size, size);
        using var g = Graphics.FromImage(image);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var edges = new System.Drawing.Imaging.ImageAttributes();
        edges.SetWrapMode(WrapMode.TileFlipXY);
        if (foreground is { } tint)
        {
            edges.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix([
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0],
                [tint.R / 255f, tint.G / 255f, tint.B / 255f, 0, 1]
            ]));
        }
        g.DrawImage(original, new Rectangle(0, 0, size, size), 0, 0, original.Width, original.Height,
            GraphicsUnit.Pixel, edges);
        Icons[key] = image;
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

internal sealed class BrandCommandRenderer(ToolStripItem primary) : ToolStripProfessionalRenderer(new BrandColorTable())
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        => e.Graphics.Clear(Theme.Surface);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            base.OnRenderToolStripBorder(e);
            return;
        }
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 0, e.AffectedBounds.Bottom - 1, e.AffectedBounds.Right, e.AffectedBounds.Bottom - 1);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item != primary)
        {
            base.OnRenderButtonBackground(e);
            return;
        }

        var fill = e.Item.Pressed ? Color.FromArgb(239, 184, 0)
            : e.Item.Selected ? Color.FromArgb(255, 221, 86) : Theme.Yellow;
        using var brush = new SolidBrush(fill);
        using var border = new Pen(e.Item.Selected ? Theme.Dark : Theme.RailSelectionBorder);
        e.Graphics.FillRectangle(brush, 0, 0, e.Item.Width - 1, e.Item.Height - 1);
        e.Graphics.DrawRectangle(border, 0, 0, e.Item.Width - 1, e.Item.Height - 1);
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
