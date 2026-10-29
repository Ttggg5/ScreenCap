using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace ScreenCap.Tray;

/// <summary>Flat, rounded, light/dark rendering for the tray's WinForms context menu.</summary>
internal sealed class TrayMenuRenderer(bool dark) : Forms.ToolStripRenderer
{
    private readonly Color _background = dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);
    private readonly Color _text = dark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
    private readonly Color _disabled = dark ? Color.FromArgb(0x6E, 0x6E, 0x6E) : Color.FromArgb(0xA0, 0xA0, 0xA0);
    private readonly Color _hover = dark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xEA, 0xEA, 0xEA);
    private readonly Color _divider = dark ? Color.FromArgb(0x40, 0x40, 0x40) : Color.FromArgb(0xE0, 0xE0, 0xE0);
    private readonly Color _accent = dark ? Color.FromArgb(0x60, 0xCD, 0xFF) : Color.FromArgb(0x00, 0x5F, 0xB8);

    public Color Background => _background;

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) => e.Graphics.Clear(_background);

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        // Windows 11 draws the rounded border; on Windows 10 a hairline keeps the menu from blending in.
        using var pen = new Pen(_divider);
        e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
            return;
        var bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = RoundedRectangle(bounds, 4);
        using var brush = new SolidBrush(_hover);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _text : _disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        // A check glyph in the accent color instead of the boxed default.
        var r = e.ImageRectangle;
        using var pen = new Pen(_accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, [
            new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
            new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.3f),
        ]);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_divider);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }

    private static GraphicsPath RoundedRectangle(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
