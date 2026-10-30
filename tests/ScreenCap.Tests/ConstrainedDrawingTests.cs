using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class ConstrainedDrawingTests
{
    private static readonly Color Red = Colors.Red;

    [Fact]
    public void Rectangle_BecomesSquare_TowardsTheLongerSide() => Sta.Run(() =>
    {
        var rect = new RectAnnotation(new Point(100, 100), Red, 4, 1);
        rect.Update(new Point(160, 120), constrain: true);
        Assert.Equal(new Rect(100, 100, 60, 60), rect.Bounds);
        rect.Update(new Point(70, 150), constrain: true); // up-left/down quadrants keep their direction
        Assert.Equal(new Rect(50, 100, 50, 50), rect.Bounds);
    });

    [Fact]
    public void Ellipse_BecomesCircle() => Sta.Run(() =>
    {
        var ellipse = new EllipseAnnotation(new Point(0, 0), Red, 4, 1);
        ellipse.Update(new Point(-30, 80), constrain: true);
        Assert.Equal(new Rect(-80, 0, 80, 80), ellipse.Bounds);
    });

    [Fact]
    public void WithoutShift_ShapesFollowTheCursor() => Sta.Run(() =>
    {
        var rect = new RectAnnotation(new Point(0, 0), Red, 4, 1);
        rect.Update(new Point(60, 20));
        Assert.Equal(new Rect(0, 0, 60, 20), rect.Bounds);
    });

    [Theory]
    [InlineData(100, 8, 1, 0)]     // almost horizontal → horizontal
    [InlineData(5, -100, 0, -1)]   // almost vertical → vertical
    [InlineData(70, 75, 1, 1)]     // near diagonal → 45°
    public void Arrow_SnapsTo45Degrees_KeepingLength(double x, double y, double dx, double dy) => Sta.Run(() =>
    {
        var arrow = new ArrowAnnotation(new Point(0, 0), Red, 4, 1);
        arrow.Update(new Point(x, y), constrain: true);

        var direction = new Vector(dx, dy);
        direction.Normalize();
        var expected = new Point(0, 0) + direction * Math.Sqrt(x * x + y * y);
        var end = arrow.Handles[1];
        Assert.Equal(expected.X, end.X, 3);
        Assert.Equal(expected.Y, end.Y, 3);
    });
    [Fact]
    public void Pen_WithShift_DrawsAStraightLine() => Sta.Run(() =>
    {
        var pen = new PenAnnotation(new Point(10, 50), Red, 4, 1);
        pen.Update(new Point(30, 70));
        pen.Update(new Point(60, 40));
        pen.Update(new Point(110, 53), constrain: true); // snaps to horizontal
        var b = pen.Bounds;
        Assert.Equal(4, b.Height, 3); // a horizontal line only as tall as the stroke
        Assert.Equal(new Point(10, 50), new Point(b.Left + 2, b.Top + 2));
    });

    [Fact]
    public void Highlighter_WithShift_IsStraightToo() => Sta.Run(() =>
    {
        var highlighter = new HighlighterAnnotation(new Point(0, 20), Red, 4, 1);
        highlighter.Update(new Point(50, 30));
        highlighter.Update(new Point(200, 24), constrain: true);
        Assert.Equal(16, highlighter.Bounds.Height, 3);
    });
}
