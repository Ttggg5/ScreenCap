using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Annotations;

internal enum StampStyle
{
    /// <summary>A symbol in a colored circle, like the number marker.</summary>
    Badge,
    /// <summary>A word in a tilted rubber-stamp frame.</summary>
    Seal,
    /// <summary>A picture the user added; ignores the color.</summary>
    Image,
}

/// <summary>Something that can be stamped onto a capture.</summary>
/// <param name="Id">Remembered as the last stamp used; custom images use "file:" and their file name.</param>
/// <param name="Text">The symbol or word drawn by badges and seals.</param>
internal sealed record Stamp(string Id, string Name, StampStyle Style, string Text = "", BitmapSource? Image = null)
{
    public static readonly IReadOnlyList<Stamp> BuiltIn =
    [
        new("check", "勾", StampStyle.Badge, "✔"),
        new("cross", "叉", StampStyle.Badge, "✖"),
        new("question", "問號", StampStyle.Badge, "?"),
        new("exclamation", "驚嘆號", StampStyle.Badge, "!"),
        new("star", "星號", StampStyle.Badge, "★"),
        new("heart", "愛心", StampStyle.Badge, "♥"),
        new("ok", "OK", StampStyle.Seal, "OK"),
        new("ng", "NG", StampStyle.Seal, "NG"),
        new("important", "重要", StampStyle.Seal, "重要"),
        new("done", "已完成", StampStyle.Seal, "已完成"),
        new("todo", "待確認", StampStyle.Seal, "待確認"),
        new("secret", "機密", StampStyle.Seal, "機密"),
    ];

    public bool UsesColor => Style != StampStyle.Image;

    /// <summary>A small picture of the stamp for the toolbar, <paramref name="size"/> DIPs across.</summary>
    public ImageSource Preview(Color color, double size)
    {
        var sample = new StampAnnotation(new Point(size / 2, size / 2), this, color, size, 1);
        // Seals are wider than tall; shrink them to fit the square, leaving room for the outline.
        var bounds = sample.Bounds;
        double fit = Math.Min(1, size / Math.Max(bounds.Width, bounds.Height)) * 0.92;
        sample.Size *= fit;
        var square = new Rect(0, 0, size, size);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(square)); // keeps the image exactly square
            dc.DrawRectangle(Brushes.Transparent, null, square);
            sample.Draw(dc);
            dc.Pop();
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}

/// <summary>
/// A stamp placed with one click; dragging while placing moves it. The handles resize it like a box,
/// stretching it freely, or keeping its proportions with Shift.
/// </summary>
internal sealed class StampAnnotation(Point center, Stamp stamp, Color color, double size, double scale)
    : Annotation(color, size, scale)
{
    /// <summary>Seals are drawn tilted, like a hand-pressed rubber stamp.</summary>
    private const double SealAngle = -8;

    public Stamp Stamp { get; set; } = stamp;
    public Point Center { get; private set; } = center;

    /// <summary>Horizontal stretch relative to the stamp's natural proportions; 1 = undistorted.</summary>
    public double StretchX { get; private set; } = 1;

    /// <summary>Size is the height of badges and seals, and the longest side of images, in DIPs.</summary>
    private double Extent => Size * Scale;

    public override Rect Bounds => Stamp.Style == StampStyle.Seal
        ? SealTransform.TransformBounds(Stretched(SealRect))
        : Stretched(Stamp.Style == StampStyle.Image ? ImageRect : new Rect(Center.X - Extent / 2, Center.Y - Extent / 2, Extent, Extent));

    // The same eight handles as rectangles, so the resize cursors match.
    public override Point[] Handles => RectHandles.Points(Bounds);

    /// <summary><paramref name="natural"/>, centered on <see cref="Center"/>, widened by <see cref="StretchX"/>.</summary>
    private Rect Stretched(Rect natural)
    {
        double w = natural.Width * StretchX;
        return new Rect(Center.X - w / 2, natural.Y, w, natural.Height);
    }

    private ScaleTransform StretchTransform => new(StretchX, 1, Center.X, Center.Y);

    public override void Update(Point point, bool constrain = false) => Center = point;

    public override bool HitTest(Point point, double tolerance)
    {
        var b = Bounds;
        b.Inflate(tolerance, tolerance);
        return b.Contains(point);
    }

    public override void MoveFrom(Annotation origin, Vector delta) => Center = ((StampAnnotation)origin).Center + delta;

    /// <summary>Resizes the stamp's box like a rectangle: the height sets the size, the width the stretch.</summary>
    public override void DragHandleFrom(Annotation origin, int handle, Vector delta, bool constrain = false)
    {
        var from = (StampAnnotation)origin;
        var before = from.Bounds;
        if (before.Width < 1 || before.Height < 1)
            return;
        var after = constrain ? RectHandles.ResizeKeepingAspect(before, handle, delta) : RectHandles.Resize(before, handle, delta);
        Size = Math.Clamp(from.Size * after.Height / before.Height, 8, 1000);
        double heightRatio = Size / from.Size; // after clamping
        StretchX = Math.Clamp(from.StretchX * after.Width / before.Width / heightRatio, 0.05, 20);
        // The side opposite the handle stays put, so the center moves with the box.
        Center = new Point(after.X + after.Width / 2, after.Y + after.Height / 2);
    }

    protected override void Render(DrawingContext dc)
    {
        if (Stamp.Style == StampStyle.Seal)
            dc.PushTransform(SealTransform); // stretched in its own tilted frame, then tilted
        dc.PushTransform(StretchTransform);
        switch (Stamp.Style)
        {
            case StampStyle.Image when Stamp.Image != null:
                dc.DrawImage(Stamp.Image, ImageRect);
                break;
            case StampStyle.Seal:
                RenderSeal(dc);
                break;
            case StampStyle.Badge:
                RenderBadge(dc);
                break;
        }
        dc.Pop();
        if (Stamp.Style == StampStyle.Seal)
            dc.Pop();
    }

    private Rect ImageRect
    {
        get
        {
            if (Stamp.Image is not { PixelWidth: > 0, PixelHeight: > 0 } image)
                return new Rect(Center, new Size(0, 0));
            double fit = Extent / Math.Max(image.PixelWidth, image.PixelHeight);
            double w = image.PixelWidth * fit, h = image.PixelHeight * fit;
            return new Rect(Center.X - w / 2, Center.Y - h / 2, w, h);
        }
    }

    private void RenderBadge(DrawingContext dc)
    {
        double radius = Extent / 2;
        dc.DrawEllipse(new SolidColorBrush(Color), new Pen(Brushes.White, Math.Max(1, radius / 12)), Center, radius, radius);
        // Fit the symbol's actual outline in the circle; font metrics would leave "?" and "★" off center.
        var glyph = Glyph(Stamp.Text);
        var shape = glyph.Bounds;
        double fit = Math.Min(1.05 * radius / shape.Width, 1.05 * radius / shape.Height);
        dc.PushTransform(new MatrixTransform(fit, 0, 0, fit, Center.X - (shape.X + shape.Width / 2) * fit, Center.Y - (shape.Y + shape.Height / 2) * fit));
        dc.DrawGeometry(new SolidColorBrush(TextAnnotation.ContrastColor(Color)), null, glyph);
        dc.Pop();
    }

    private FormattedText SealText => new(Stamp.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(TextAnnotation.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), Extent * 0.55, Brushes.Black, 1.0);

    private Rect SealRect
    {
        get
        {
            double w = SealText.WidthIncludingTrailingWhitespace + Extent * 0.6;
            return new Rect(Center.X - w / 2, Center.Y - Extent / 2, w, Extent);
        }
    }

    private RotateTransform SealTransform => new(SealAngle, Center.X, Center.Y);

    private void RenderSeal(DrawingContext dc)
    {
        var rect = SealRect;
        double border = Math.Max(1, Extent * 0.08);
        var inner = rect;
        inner.Inflate(-border / 2, -border / 2);
        var brush = new SolidColorBrush(Color);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, Color.R, Color.G, Color.B)), new Pen(brush, border),
            inner, Extent * 0.16, Extent * 0.16);
        var text = SealText;
        text.SetForegroundBrush(brush);
        dc.DrawText(text, new Point(Center.X - text.Width / 2, Center.Y - text.Height / 2));
    }

    private static readonly Dictionary<string, Geometry> Glyphs = new();

    /// <summary>Outline of a badge symbol at a fixed size; drawn scaled, so it is built once.</summary>
    private static Geometry Glyph(string text)
    {
        lock (Glyphs)
        {
            if (!Glyphs.TryGetValue(text, out var geometry))
            {
                var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI Symbol, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    100, Brushes.Black, 1.0);
                geometry = formatted.BuildGeometry(new Point(0, 0));
                geometry.Freeze();
                Glyphs[text] = geometry;
            }
            return geometry;
        }
    }
}
