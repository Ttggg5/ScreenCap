using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Annotations;

/// <summary>A shape drawn on top of the captured screen. Coordinates are physical pixels relative to the snapshot.</summary>
internal abstract class Annotation
{
    protected Annotation(Color color, double thickness)
    {
        Color = color;
        Thickness = thickness;
    }

    public Color Color { get; }
    public double Thickness { get; }

    /// <summary>True when the shape is too small to keep (e.g. a click without dragging).</summary>
    public virtual bool IsEmpty => false;

    public virtual void Update(Point point) { }

    public abstract void Render(DrawingContext dc);

    protected Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(Color), Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }
}

/// <summary>Base for shapes defined by a drag from a start point to an end point.</summary>
internal abstract class DragAnnotation : Annotation
{
    protected DragAnnotation(Point start, Color color, double thickness) : base(color, thickness)
    {
        Start = start;
        End = start;
    }

    public Point Start { get; }
    public Point End { get; private set; }
    public Rect Bounds => new(Start, End);
    public override bool IsEmpty => Bounds.Width < 2 && Bounds.Height < 2;

    public override void Update(Point point) => End = point;
}

internal sealed class RectAnnotation(Point start, Color color, double thickness) : DragAnnotation(start, color, thickness)
{
    public override void Render(DrawingContext dc) => dc.DrawRectangle(null, CreatePen(), Bounds);
}

internal sealed class EllipseAnnotation(Point start, Color color, double thickness) : DragAnnotation(start, color, thickness)
{
    public override void Render(DrawingContext dc)
    {
        var b = Bounds;
        dc.DrawEllipse(null, CreatePen(), new Point(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2);
    }
}

internal sealed class ArrowAnnotation(Point start, Color color, double thickness) : DragAnnotation(start, color, thickness)
{
    public override void Render(DrawingContext dc)
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

internal sealed class PenAnnotation : Annotation
{
    private readonly List<Point> _points = new();

    public PenAnnotation(Point start, Color color, double thickness) : base(color, thickness) => _points.Add(start);

    public override void Update(Point point)
    {
        if (_points[^1] != point)
            _points.Add(point);
    }

    public override void Render(DrawingContext dc)
    {
        if (_points.Count == 1)
        {
            dc.DrawEllipse(new SolidColorBrush(Color), null, _points[0], Thickness / 2, Thickness / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_points[0], false, false);
            ctx.PolyLineTo(_points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, CreatePen(), geometry);
    }
}

internal sealed class TextAnnotation(string text, Point position, double fontSize, Color color) : Annotation(color, 1)
{
    public static readonly FontFamily Font = new("Microsoft JhengHei UI, Segoe UI");
    private static readonly Typeface Typeface = new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public override void Render(DrawingContext dc)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Typeface, fontSize, new SolidColorBrush(Color), 1.0);
        dc.DrawText(formatted, position);
    }
}

/// <summary>Pixelates the dragged area using block-averaged colors from the original snapshot.</summary>
internal sealed class MosaicAnnotation(Point start, ScreenSnapshot snapshot, int blockSize)
    : DragAnnotation(start, Colors.Transparent, 0)
{
    private BitmapSource? _cache;
    private Rect _cacheRect;

    public override void Render(DrawingContext dc)
    {
        var rect = PixelRect();
        if (rect.Width < 1 || rect.Height < 1)
            return;
        if (_cache == null || rect != _cacheRect)
        {
            _cache = Pixelate(rect);
            _cacheRect = rect;
        }
        dc.DrawImage(_cache, rect);
    }

    private Rect PixelRect()
    {
        var b = Bounds;
        b.Intersect(new Rect(0, 0, snapshot.Width, snapshot.Height));
        if (b.IsEmpty)
            return Rect.Empty;
        return new Rect(Math.Floor(b.X), Math.Floor(b.Y), Math.Ceiling(b.Width), Math.Ceiling(b.Height));
    }

    private BitmapSource Pixelate(Rect rect)
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
}
