using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The notification-area menu stays native: no browser, timers or background work.
public sealed class MusicWidgetTrayMenu : ToolStripDropDown
{
    private readonly Font menuFont = new Font("Segoe UI", 9f, FontStyle.Regular);
    private readonly Font titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
    private readonly Image titleImage;
    internal Image TitleImage { get { return titleImage; } }
    private bool sizing;

    public MusicWidgetTrayMenu(string imagePath)
    {
        Font = menuFont;
        BackColor = MusicWidgetTrayRenderer.Surface;
        ForeColor = MusicWidgetTrayRenderer.Text;
        Renderer = new MusicWidgetTrayRenderer();
        LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
        AutoClose = true;
        DropShadowEnabled = true;
        AutoSize = false;
        Padding = new Padding(5);
        if (System.IO.File.Exists(imagePath))
        {
            using (Image source = Image.FromFile(imagePath))
            {
                titleImage = new Bitmap(32, 32);
                using (Graphics graphics = Graphics.FromImage(titleImage))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(0, 0, 32, 32));
                }
            }
        }
        ToolStripLabel title = new ToolStripLabel("Music Widget", titleImage);
        title.Name = "MusicWidgetTrayTitle";
        title.Font = titleFont;
        title.Tag = "header";
        title.ImageAlign = ContentAlignment.MiddleLeft;
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Margin = Padding.Empty;
        title.AutoSize = false;
        title.Size = new Size(194, 30);
        Items.Add(title);
        Items.Add(new ToolStripSeparator());
    }

    public void RefreshLayout()
    {
        if (sizing) return;
        sizing = true;
        try
        {
            float scale = DeviceDpi / 96f;
            Padding = new Padding((int)Math.Round(5 * scale));
            ImageScalingSize = new Size((int)Math.Round(16 * scale), (int)Math.Round(16 * scale));
            int width = (int)Math.Round(194 * scale);
            foreach (ToolStripItem item in Items)
                width = Math.Max(width, TextRenderer.MeasureText(item.Text, item.Font).Width + (int)(44 * scale));
            int totalHeight = Padding.Vertical;
            foreach (ToolStripItem item in Items)
            {
                item.AutoSize = false;
                item.Margin = Padding.Empty;
                item.Padding = Padding.Empty;
                int height = item is ToolStripSeparator ? 6 : item is ToolStripLabel ? 30 : 28;
                item.Size = new Size(width, (int)Math.Round(height * scale));
                totalHeight += item.Height;
            }
            Size = new Size(width + Padding.Horizontal, totalHeight);
            PerformLayout();
        }
        finally { sizing = false; }
    }

    public void ShowAtCursor()
    {
        if (Visible) { Close(ToolStripDropDownCloseReason.CloseCalled); return; }
        RefreshLayout();
        Show(Cursor.Position);
        if (Visible) { SetForegroundWindow(Handle); Focus(); }
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        RefreshLayout();
        base.OnOpening(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshLayout();
        // Supported on Windows 11; the drawn frame is also the fallback.
        try
        {
            int dark = 1, rounded = 2;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        UpdateShape();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated) UpdateShape();
    }

    private void UpdateShape()
    {
        if (Width < 2 || Height < 2) return;
        using (GraphicsPath path = MusicWidgetTrayRenderer.Rounded(new RectangleF(0, 0, Width, Height), 9f * DeviceDpi / 96f))
        {
            Region previous = Region;
            Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (titleImage != null) titleImage.Dispose();
            menuFont.Dispose(); titleFont.Dispose();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

public sealed class MusicWidgetTrayRenderer : ToolStripProfessionalRenderer
{
    public static readonly Color Surface = Color.FromArgb(17, 17, 19);
    public static readonly Color Text = Color.FromArgb(177, 177, 185);
    private static readonly Color Hover = Color.FromArgb(39, 39, 43);

    public MusicWidgetTrayRenderer() { RoundedEdges = false; }

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (Brush brush = new SolidBrush(Surface))
        using (GraphicsPath path = Rounded(new RectangleF(0, 0, e.ToolStrip.Width, e.ToolStrip.Height), 9f * e.Graphics.DpiX / 96f))
            e.Graphics.FillPath(brush, path);
        MusicWidgetTrayMenu menu = e.ToolStrip as MusicWidgetTrayMenu;
        if (menu != null && menu.Items.Count > 0)
        {
            ToolStripItem header = menu.Items[0];
            float scale = e.Graphics.DpiX / 96f;
            int side = (int)Math.Round(16 * scale);
            if (menu.TitleImage != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(menu.TitleImage, new Rectangle((int)(12 * scale), header.Bounds.Top + (header.Height - side) / 2, side, side));
            }
            int left = (int)Math.Round(36 * scale);
            TextRenderer.DrawText(e.Graphics, header.Text, header.Font,
                new Rectangle(left, header.Bounds.Top, menu.Width - left - 12, header.Height),
                Color.FromArgb(231, 231, 236), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (Pen pen = new Pen(Color.FromArgb(48, 48, 53)))
        using (GraphicsPath path = Rounded(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 9f * e.Graphics.DpiX / 96f))
            e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        float scale = e.Graphics.DpiX / 96f;
        if (e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush brush = new SolidBrush(Hover))
            using (GraphicsPath path = Rounded(new RectangleF(0, 1, e.Item.Width, e.Item.Height - 2), 5f * scale))
                e.Graphics.FillPath(brush, path);
        }
        ToolStripMenuItem item = e.Item as ToolStripMenuItem;
        if (item != null && item.Checked) DrawCheck(e.Graphics, 13 * scale, e.Item.Height / 2f, scale);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripLabel) return;
        e.TextColor = !e.Item.Enabled ? Color.FromArgb(93, 93, 101) : e.Item.Selected ? Color.FromArgb(231, 231, 236) : Text;
        float scale = e.Graphics.DpiX / 96f;
        int left = (int)Math.Round(30 * scale);
        e.TextRectangle = new Rectangle(left, 0, e.Item.Width - left - (int)(12 * scale), e.Item.Height);
        e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (!(e.Item is ToolStripLabel)) base.OnRenderItemImage(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Check glyph is drawn with the row, without a framework image column.
    }

    private static void DrawCheck(Graphics graphics, float x, float y, float scale)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (Pen pen = new Pen(Color.FromArgb(220, 222, 230), 1.5f * scale))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            graphics.DrawLines(pen, new PointF[] { new PointF(x - 4 * scale, y), new PointF(x - 1 * scale, y + 3 * scale), new PointF(x + 5 * scale, y - 4 * scale) });
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using (Pen pen = new Pen(Color.FromArgb(35, 35, 39)))
            e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 6, e.Item.Height / 2);
    }
}
