using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Annotations;

/// <summary>
/// A shape drawn on top of the captured screen. Coordinates are physical pixels relative to the snapshot.
/// Annotations stay editable (move, reshape, restyle) until the overlay closes.
/// </summary>
internal abstract class Annotation
{
    protected Annotation(Color color, double size, double scale)
    {
        Color = color;
        Size = size;
        Scale = scale;
    }

    public Color Color { get; set; }

    /// <summary>Style size level in DIPs as chosen in the toolbar (stroke width, font size, mosaic block).</summary>
    public double Size { get; set; }

    /// <summary>DPI scale of the overlay when the annotation was created.</summary>
    public double Scale { get; }

    /// <summary>0.1–1, applied to the whole annotation.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Draws strokes dashed; only used where <see cref="CanDash"/>.</summary>
    public bool Dashed { get; set; }

    /// <summary>Fills the shape with the color; only used where <see cref="CanFill"/>.</summary>
    public bool Filled { get; set; }

    public virtual bool CanFill => false;
    public virtual bool CanDash => false;
    public virtual bool HasOpacity => true;

    protected virtual double Thickness => Size * Scale;

    /// <summary>Color actually used for strokes; the highlighter makes it translucent.</summary>
    protected virtual Color StrokeColor => Color;

    /// <summary>True when the shape is too small to keep (e.g. a click without dragging).</summary>
    public virtual bool IsEmpty => false;

    public abstract Rect Bounds { get; }

    /// <summary>Points the user can drag to reshape the annotation.</summary>
    public virtual Point[] Handles => [];

    /// <summary>Extends the shape while it is being drawn.</summary>
    /// <param name="constrain">Shift held: squares, circles, 45° arrows and straight strokes.</param>
    public virtual void Update(Point point, bool constrain = false) { }

    /// <summary><paramref name="to"/> moved onto the nearest 45° direction from <paramref name="from"/>, same length.</summary>
    public static Point SnapToAngle(Point from, Point to)
    {
        var v = to - from;
        if (v.Length < 1)
            return to;
        double step = Math.PI / 4;
        double angle = Math.Round(Math.Atan2(v.Y, v.X) / step) * step;
        return from + new Vector(Math.Cos(angle), Math.Sin(angle)) * v.Length;
    }

    /// <summary>Renders the annotation with its opacity.</summary>
    public void Draw(DrawingContext dc)
    {
        if (Opacity >= 1)
        {
            Render(dc);
            return;
        }
        dc.PushOpacity(Opacity);
        Render(dc);
        dc.Pop();
    }

    protected abstract void Render(DrawingContext dc);

    public virtual bool HitTest(Point point, double tolerance) =>
        GetOutline().StrokeContains(new Pen(Brushes.Black, Thickness + 2 * tolerance), point);

    protected virtual Geometry GetOutline() => new RectangleGeometry(Bounds);

    /// <summary>Sets this annotation to <paramref name="origin"/> translated by <paramref name="delta"/>.</summary>
    public abstract void MoveFrom(Annotation origin, Vector delta);

    /// <summary>Sets this annotation to <paramref name="origin"/> with one handle dragged by <paramref name="delta"/>.</summary>
    public virtual void DragHandleFrom(Annotation origin, int handle, Vector delta) { }

    public Annotation Clone()
    {
        var clone = (Annotation)MemberwiseClone();
        clone.OnCloned();
        return clone;
    }

    /// <summary>Deep-copies mutable reference state after <see cref="MemberwiseClone"/>.</summary>
    protected virtual void OnCloned() { }

    protected Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (Dashed && CanDash)
        {
            // In multiples of the thickness; round caps add half a thickness to each end of a dash.
            pen.DashStyle = new DashStyle([1.5, 2.5], 0);
            pen.DashCap = PenLineCap.Round;
        }
        pen.Freeze();
        return pen;
    }
}

/// <summary>Handle positions and resize math shared by the selection rectangle and box-shaped annotations.</summary>
internal static class RectHandles
{
    /// <summary>Order: TL, T, TR, R, BR, B, BL, L.</summary>
    public static Point[] Points(Rect r)
    {
        double cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        return [new(r.Left, r.Top), new(cx, r.Top), new(r.Right, r.Top), new(r.Right, cy),
                new(r.Right, r.Bottom), new(cx, r.Bottom), new(r.Left, r.Bottom), new(r.Left, cy)];
    }

    /// <summary>Moves the edges belonging to <paramref name="handle"/>; flipping past the opposite edge is allowed.</summary>
    public static Rect Resize(Rect r, int handle, Vector delta)
    {
        double l = r.Left, t = r.Top, right = r.Right, b = r.Bottom;
        if (handle is 0 or 6 or 7) l += delta.X;
        if (handle is 0 or 1 or 2) t += delta.Y;
        if (handle is 2 or 3 or 4) right += delta.X;
        if (handle is 4 or 5 or 6) b += delta.Y;
        return new Rect(new Point(Math.Min(l, right), Math.Min(t, b)), new Point(Math.Max(l, right), Math.Max(t, b)));
    }
}

/// <summary>Base for shapes defined by a drag from a start point to an end point.</summary>
internal abstract class DragAnnotation : Annotation
{
    protected DragAnnotation(Point start, Color color, double size, double scale) : base(color, size, scale)
    {
        Start = start;
        End = start;
    }

    public Point Start { get; protected set; }
    public Point End { get; protected set; }
    public override Rect Bounds => new(Start, End);
    public override bool IsEmpty => Bounds.Width < 2 && Bounds.Height < 2;
    public override Point[] Handles => RectHandles.Points(Bounds);

    public override void Update(Point point, bool constrain = false) => End = constrain ? Constrain(point) : point;

    /// <summary>Default constraint: a square box (circle for ellipses).</summary>
    protected virtual Point Constrain(Point point)
    {
        var v = point - Start;
        double side = Math.Max(Math.Abs(v.X), Math.Abs(v.Y));
        return Start + new Vector(v.X < 0 ? -side : side, v.Y < 0 ? -side : side);
    }

    public override void MoveFrom(Annotation origin, Vector delta)
    {
        var o = (DragAnnotation)origin;
        Start = o.Start + delta;
        End = o.End + delta;
    }

    public override void DragHandleFrom(Annotation origin, int handle, Vector delta)
    {
        var r = RectHandles.Resize(origin.Bounds, handle, delta);
        Start = r.TopLeft;
        End = r.BottomRight;
    }
}

/// <summary>Rectangle or ellipse: outlined, optionally filled with the color.</summary>
internal abstract class ShapeAnnotation(Point start, Color color, double size, double scale)
    : DragAnnotation(start, color, size, scale)
{
    public override bool CanFill => true;
    public override bool CanDash => true;

    // A filled shape can be grabbed anywhere inside it.
    public override bool HitTest(Point point, double tolerance) =>
        (Filled && GetOutline().FillContains(point)) || base.HitTest(point, tolerance);

    protected override void Render(DrawingContext dc) =>
        dc.DrawGeometry(Filled ? new SolidColorBrush(Color) : null, CreatePen(), GetOutline());
}

internal sealed class RectAnnotation(Point start, Color color, double size, double scale)
    : ShapeAnnotation(start, color, size, scale);

internal sealed class EllipseAnnotation(Point start, Color color, double size, double scale)
    : ShapeAnnotation(start, color, size, scale)
{
    protected override Geometry GetOutline()
    {
        var b = Bounds;
        return new EllipseGeometry(new Point(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2);
    }
}

/// <summary>A straight line, adjusted by dragging either end.</summary>
internal class LineAnnotation(Point start, Color color, double size, double scale)
    : DragAnnotation(start, color, size, scale)
{
    public override bool CanDash => true;

    public override bool IsEmpty => (End - Start).Length < 3;

    // The two ends rather than a bounding box.
    public override Point[] Handles => [Start, End];

    protected override Point Constrain(Point point) => SnapToAngle(Start, point);

    public override void DragHandleFrom(Annotation origin, int handle, Vector delta)
    {
        var o = (LineAnnotation)origin;
        Start = handle == 0 ? o.Start + delta : o.Start;
        End = handle == 1 ? o.End + delta : o.End;
    }

    protected override Geometry GetOutline() => new LineGeometry(Start, End);

    protected override void Render(DrawingContext dc) => dc.DrawLine(CreatePen(), Start, End);
}

internal sealed class ArrowAnnotation(Point start, Color color, double size, double scale)
    : LineAnnotation(start, color, size, scale)
{
    protected override void Render(DrawingContext dc)
    {
        var v = End - Start;
        double length = v.Length;
        if (length < 1)
            return;
        v.Normalize();

        double headLength = Math.Min(Math.Max(Thickness * 4, 12), length);
        double headWidth = headLength * 0.7;
        var headBase = End - v * headLength;
        var normal = new Vector(-v.Y, v.X) * (headWidth / 2);

        dc.DrawLine(CreatePen(), Start, headBase + v * 1);

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(End, true, true);
            ctx.LineTo(headBase + normal, true, true);
            ctx.LineTo(headBase - normal, true, true);
        }
        head.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color), null, head);
    }
}

internal class PenAnnotation : Annotation
{
    private List<Point> _points = new();

    public PenAnnotation(Point start, Color color, double size, double scale) : base(color, size, scale) => _points.Add(start);

    public override bool CanDash => true;

    public override Rect Bounds
    {
        get
        {
            double minX = _points.Min(p => p.X), minY = _points.Min(p => p.Y);
            double maxX = _points.Max(p => p.X), maxY = _points.Max(p => p.Y);
            var r = new Rect(new Point(minX, minY), new Point(maxX, maxY));
            r.Inflate(Thickness / 2, Thickness / 2);
            return r;
        }
    }

    public override void Update(Point point, bool constrain = false)
    {
        if (constrain)
            _points = [_points[0], SnapToAngle(_points[0], point)]; // straight stroke
        else if (_points[^1] != point)
            _points.Add(point);
    }

    public override void MoveFrom(Annotation origin, Vector delta) =>
        _points = ((PenAnnotation)origin)._points.Select(p => p + delta).ToList();

    protected override void OnCloned() => _points = new List<Point>(_points);

    protected override Geometry GetOutline()
    {
        if (_points.Count == 1)
            return new EllipseGeometry(_points[0], 0.5, 0.5);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_points[0], false, false);
            ctx.PolyLineTo(_points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    protected override void Render(DrawingContext dc)
    {
        if (_points.Count == 1)
            dc.DrawEllipse(new SolidColorBrush(StrokeColor), null, _points[0], Thickness / 2, Thickness / 2);
        else
            dc.DrawGeometry(null, CreatePen(), GetOutline());
    }
}

/// <summary>Translucent wide stroke for marking text, drawn like the pen.</summary>
internal sealed class HighlighterAnnotation(Point start, Color color, double size, double scale)
    : PenAnnotation(start, color, size, scale)
{
    public override bool CanDash => false;

    // Size levels 2/4/6 → 8/16/24 DIP wide.
    protected override double Thickness => Size * 4 * Scale;

    protected override Color StrokeColor => Color.FromArgb(0x70, Color.R, Color.G, Color.B);
}

/// <summary>A numbered circle for step-by-step instructions; dragging while placing moves it.</summary>
internal sealed class NumberAnnotation(Point center, int number, Color color, double size, double scale)
    : Annotation(color, size, scale)
{
    public int Number { get; } = number;
    public Point Center { get; private set; } = center;

    // Size levels 2/4/6 → 12/16/20 DIP radius.
    private double Radius => (8 + Size * 2) * Scale;

    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, 2 * Radius, 2 * Radius);

    public override void Update(Point point, bool constrain = false) => Center = point;

    public override bool HitTest(Point point, double tolerance) => (point - Center).Length <= Radius + tolerance;

    public override void MoveFrom(Annotation origin, Vector delta) => Center = ((NumberAnnotation)origin).Center + delta;

    protected override void Render(DrawingContext dc)
    {
        dc.DrawEllipse(new SolidColorBrush(Color), new Pen(Brushes.White, Math.Max(1, 1.5 * Scale)), Center, Radius, Radius);
        var typeface = new Typeface(TextAnnotation.Font, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var text = new FormattedText(Number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, Radius * (Number < 10 ? 1.15 : 0.95), new SolidColorBrush(TextAnnotation.ContrastColor(Color)), 1.0);
        dc.DrawText(text, new Point(Center.X - text.Width / 2, Center.Y - text.Height / 2));
    }
}

internal sealed class TextAnnotation(string text, Point position, Color color, double size, double scale, bool bold = false, bool background = false)
    : Annotation(color, size, scale)
{
    public static readonly FontFamily Font = new("Microsoft JhengHei UI, Segoe UI");

    /// <summary>Maps a toolbar size level (2/4/6) to a font size in physical pixels.</summary>
    public static double FontSizeFor(double size, double scale) => (12 + size * 2) * scale;

    /// <summary>Black or white, whichever reads better on <paramref name="c"/>.</summary>
    public static Color ContrastColor(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 160 ? Colors.Black : Colors.White;

    /// <summary>Padding around the text when it has a background box, in DIPs.</summary>
    public const double BackgroundPadding = 4;

    public string Text { get; } = text;
    public Point Position { get; private set; } = position;
    public bool Bold { get; set; } = bold;

    /// <summary>Draws the text in a contrasting color on a box filled with <see cref="Annotation.Color"/>.</summary>
    public bool Background { get; set; } = background;

    private Rect TextRect
    {
        get
        {
            var ft = Format(Color);
            return new Rect(Position, new System.Windows.Size(ft.WidthIncludingTrailingWhitespace, ft.Height));
        }
    }

    public override Rect Bounds
    {
        get
        {
            var r = TextRect;
            if (Background)
                r.Inflate(BackgroundPadding * Scale, BackgroundPadding * Scale / 2);
            return r;
        }
    }

    public override bool HitTest(Point point, double tolerance)
    {
        var b = Bounds;
        b.Inflate(tolerance, tolerance);
        return b.Contains(point);
    }

    public override void MoveFrom(Annotation origin, Vector delta) => Position = ((TextAnnotation)origin).Position + delta;

    protected override void Render(DrawingContext dc)
    {
        if (Background)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color), null, Bounds, 3 * Scale, 3 * Scale);
            dc.DrawText(Format(ContrastColor(Color)), Position);
        }
        else
            dc.DrawText(Format(Color), Position);
    }

    private FormattedText Format(Color foreground) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(Font, FontStyles.Normal, Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
        FontSizeFor(Size, Scale), new SolidColorBrush(foreground), 1.0);
}

/// <summary>
/// Hides the dragged area using the original snapshot: pixelated into block-averaged colors,
/// or, with <see cref="Blur"/>, a gaussian-like blur.
/// </summary>
internal sealed class MosaicAnnotation(Point start, ScreenSnapshot snapshot, int strength, double scale, bool blur = false)
    : DragAnnotation(start, Colors.Transparent, 0, scale)
{
    public const int MinStrength = 1;
    public const int MaxStrength = 10;
    public const int DefaultStrength = 4;

    // The cached bitmap is immutable, so clones may share it.
    private BitmapSource? _cache;
    private Rect _cacheRect;
    private int _cacheBlock;
    private bool _cacheBlur;

    /// <summary>Smooth blur instead of mosaic blocks.</summary>
    public bool Blur { get; set; } = blur;

    /// <summary>Blur strength 1–10; higher means larger mosaic blocks.</summary>
    public int Strength { get; set; } = Math.Clamp(strength, MinStrength, MaxStrength);

    // 1 → 5px, 4 → 14px, 10 → 32px blocks (in DIPs).
    private int BlockSize => Math.Max(3, (int)Math.Round((2 + Strength * 3) * Scale));

    // 1 → 3px, 4 → 9px, 10 → 21px box radius (in DIPs); three passes approximate a gaussian.
    internal int BlurRadius => Math.Max(1, (int)Math.Round((1 + Strength * 2) * Scale));

    public override bool HasOpacity => false;

    public override bool HitTest(Point point, double tolerance) => Bounds.Contains(point);

    protected override void Render(DrawingContext dc)
    {
        var rect = PixelRect();
        if (rect.Width < 1 || rect.Height < 1)
            return;
        int amount = Blur ? BlurRadius : BlockSize;
        if (_cache == null || rect != _cacheRect || amount != _cacheBlock || Blur != _cacheBlur)
        {
            _cache = Blur ? BlurRegion(rect, BlurRadius) : Pixelate(rect, BlockSize);
            _cacheRect = rect;
            _cacheBlock = amount;
            _cacheBlur = Blur;
        }
        dc.DrawImage(_cache, rect);
    }

    /// <summary>The area in whole pixels, clipped to the snapshot.</summary>
    private Rect PixelRect()
    {
        var b = Bounds;
        double left = Math.Max(0, Math.Floor(b.Left)), top = Math.Max(0, Math.Floor(b.Top));
        double right = Math.Min(snapshot.Width, Math.Ceiling(b.Right)), bottom = Math.Min(snapshot.Height, Math.Ceiling(b.Bottom));
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty;
    }

    private BitmapSource Pixelate(Rect rect, int blockSize)
    {
        int x0 = (int)rect.X, y0 = (int)rect.Y, w = (int)rect.Width, h = (int)rect.Height;
        int stride = w * 4;
        var output = new byte[stride * h];
        var src = snapshot.Pixels;

        for (int by = 0; by < h; by += blockSize)
        {
            for (int bx = 0; bx < w; bx += blockSize)
            {
                int bw = Math.Min(blockSize, w - bx), bh = Math.Min(blockSize, h - by);
                long b = 0, g = 0, r = 0;
                for (int y = 0; y < bh; y++)
                {
                    int i = (y0 + by + y) * snapshot.Stride + (x0 + bx) * 4;
                    for (int x = 0; x < bw; x++, i += 4)
                    {
                        b += src[i];
                        g += src[i + 1];
                        r += src[i + 2];
                    }
                }
                int n = bw * bh;
                byte ab = (byte)(b / n), ag = (byte)(g / n), ar = (byte)(r / n);
                for (int y = 0; y < bh; y++)
                {
                    int o = (by + y) * stride + bx * 4;
                    for (int x = 0; x < bw; x++, o += 4)
                    {
                        output[o] = ab;
                        output[o + 1] = ag;
                        output[o + 2] = ar;
                        output[o + 3] = 255;
                    }
                }
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, output, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Three horizontal + vertical box-blur passes (≈ gaussian). Samples come from a margin around the
    /// area so the edges blend with their surroundings instead of darkening.
    /// </summary>
    private BitmapSource BlurRegion(Rect rect, int radius)
    {
        int margin = radius * 3;
        int x0 = Math.Max(0, (int)rect.X - margin), y0 = Math.Max(0, (int)rect.Y - margin);
        int x1 = Math.Min(snapshot.Width, (int)(rect.X + rect.Width) + margin);
        int y1 = Math.Min(snapshot.Height, (int)(rect.Y + rect.Height) + margin);
        int w = x1 - x0, h = y1 - y0;

        // Work in planar float channels.
        var channels = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            var plane = channels[c] = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (y0 + y) * snapshot.Stride + x0 * 4 + c;
                for (int x = 0; x < w; x++, src += 4)
                    plane[y * w + x] = snapshot.Pixels[src];
            }
        }

        var scratch = new float[Math.Max(w, h)];
        foreach (var plane in channels)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < h; y++)
                    BoxBlur(plane, y * w, 1, w, radius, scratch);
                for (int x = 0; x < w; x++)
                    BoxBlur(plane, x, w, h, radius, scratch);
            }
        }

        int ox = (int)rect.X - x0, oy = (int)rect.Y - y0, rw = (int)rect.Width, rh = (int)rect.Height;
        int stride = rw * 4;
        var output = new byte[stride * rh];
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < rw; x++)
            {
                int i = (oy + y) * w + ox + x, o = y * stride + x * 4;
                output[o] = (byte)Math.Clamp(channels[0][i] + 0.5f, 0, 255);
                output[o + 1] = (byte)Math.Clamp(channels[1][i] + 0.5f, 0, 255);
                output[o + 2] = (byte)Math.Clamp(channels[2][i] + 0.5f, 0, 255);
                output[o + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(rw, rh, 96, 96, PixelFormats.Bgr32, null, output, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>In-place running-sum box blur of one row or column; samples past the ends repeat the edge.</summary>
    private static void BoxBlur(float[] data, int start, int step, int count, int radius, float[] scratch)
    {
        float sum = 0;
        for (int k = -radius; k <= radius; k++)
            sum += data[start + Math.Clamp(k, 0, count - 1) * step];
        float scale = 1f / (2 * radius + 1);
        for (int i = 0; i < count; i++)
        {
            scratch[i] = sum * scale;
            int add = Math.Min(i + radius + 1, count - 1), remove = Math.Max(i - radius, 0);
            sum += data[start + add * step] - data[start + remove * step];
        }
        for (int i = 0; i < count; i++)
            data[start + i * step] = scratch[i];
    }
}
