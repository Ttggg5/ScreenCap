using System.Windows;

namespace ScreenCap.Capture;

/// <summary>
/// An aspect ratio the selection is locked to while dragging and resizing. The ratio is given landscape;
/// a drag taller than wide makes it portrait (16:9 becomes 9:16).
/// </summary>
internal sealed record SelectionRatio(string Name, double Value)
{
    public static readonly SelectionRatio Free = new("自由", 0);

    /// <summary>The ratios R cycles through.</summary>
    public static readonly SelectionRatio[] All =
    [
        Free,
        new("1:1", 1),
        new("4:3", 4.0 / 3),
        new("3:2", 3.0 / 2),
        new("16:9", 16.0 / 9),
    ];

    public bool IsFree => Value <= 0;

    public static SelectionRatio FromIndex(int index) => All[(index % All.Length + All.Length) % All.Length];

    public int Index => Array.IndexOf(All, this);

    /// <summary>The width / height to use for a selection shaped like <paramref name="w"/> × <paramref name="h"/>.</summary>
    private double Oriented(double w, double h) => w >= h ? Value : 1 / Value;

    /// <summary>
    /// The selection dragged from <paramref name="press"/> to <paramref name="p"/> (both inclusive pixels), grown to the
    /// ratio so it reaches the cursor, and shrunk if needed to stay inside <paramref name="screen"/>.
    /// </summary>
    public Rect Span(Point press, Point p, Size screen)
    {
        double w = Math.Abs(p.X - press.X) + 1, h = Math.Abs(p.Y - press.Y) + 1;
        bool right = p.X >= press.X, down = p.Y >= press.Y;
        if (IsFree)
            return new Rect(right ? press.X : p.X, down ? press.Y : p.Y, w, h);

        double ratio = Oriented(w, h);
        if (w < h * ratio)
            w = h * ratio;
        else
            h = w / ratio;

        // Room from the press point to the screen edge in the drag direction.
        double roomW = right ? screen.Width - press.X : press.X + 1;
        double roomH = down ? screen.Height - press.Y : press.Y + 1;
        if (w > roomW)
            (w, h) = (roomW, roomW / ratio);
        if (h > roomH)
            (w, h) = (roomH * ratio, roomH);

        (w, h) = Round(w, h, ratio);
        return new Rect(right ? press.X : press.X + 1 - w, down ? press.Y : press.Y + 1 - h, w, h);
    }

    /// <summary>The largest selection with this ratio inside <paramref name="r"/>, centered on it, in the same orientation.</summary>
    public Rect Fit(Rect r)
    {
        if (IsFree || r.Width < 1 || r.Height < 1)
            return r;
        double ratio = Oriented(r.Width, r.Height);
        double w = r.Width, h = r.Height;
        if (w > h * ratio)
            w = h * ratio;
        else
            h = w / ratio;
        (w, h) = Round(w, h, ratio);
        return new Rect(Math.Round(r.X + (r.Width - w) / 2), Math.Round(r.Y + (r.Height - h) / 2), w, h);
    }

    /// <summary>
    /// Drags handle <paramref name="handle"/> of <paramref name="r"/> (which has this ratio) by <paramref name="delta"/>,
    /// keeping the ratio and the opposite side. Null if the result would leave <paramref name="screen"/>.
    /// </summary>
    public Rect? Resize(Rect r, int handle, Vector delta, Size screen)
    {
        var resized = Annotations.RectHandles.ResizeKeepingAspect(r, handle, delta);
        var (w, h) = Round(resized.Width, resized.Height, Oriented(r.Width, r.Height));
        double left = handle is 0 or 6 or 7 ? r.Right - w : handle is 1 or 5 ? Math.Round((r.Left + r.Right - w) / 2) : r.Left;
        double top = handle is 0 or 1 or 2 ? r.Bottom - h : handle is 3 or 7 ? Math.Round((r.Top + r.Bottom - h) / 2) : r.Top;
        if (left < 0 || top < 0 || left + w > screen.Width || top + h > screen.Height)
            return null;
        return new Rect(left, top, w, h);
    }

    /// <summary>The height for a <paramref name="typed"/> width, or the width for a typed height, in the orientation of <paramref name="current"/>.</summary>
    public double Other(double typed, bool typedWidth, Size current)
    {
        double ratio = Oriented(current.Width, current.Height);
        return Math.Max(1, Math.Round(typedWidth ? typed / ratio : typed * ratio));
    }

    /// <summary>Whole pixels; the side derived from the other is rounded from it, so 1280 at 16:9 gives exactly 720.</summary>
    private static (double W, double H) Round(double w, double h, double ratio)
    {
        if (ratio >= 1)
        {
            w = Math.Max(1, Math.Floor(w));
            return (w, Math.Max(1, Math.Round(w / ratio)));
        }
        h = Math.Max(1, Math.Floor(h));
        return (Math.Max(1, Math.Round(h * ratio)), h);
    }
}
