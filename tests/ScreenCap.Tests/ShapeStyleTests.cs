using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class ShapeStyleTests
{
    private static readonly Color Blue = Colors.Blue;

    private static T Drawn<T>(T annotation, Point end) where T : Annotation
    {
        annotation.Update(end);
        return annotation;
    }

    [Fact]
    public void FilledRectangle_FillsInside_AndCanBeGrabbedAnywhere() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(10, 10), Blue, 4, 1) { Filled = true }, new Point(60, 60));
        var image = Compositor.Compose(Images.Snapshot(80, 80, Colors.White), new Rect(0, 0, 80, 80), [rect]);

        Assert.Equal(Blue, Images.PixelAt(image, 35, 35));
        Assert.True(rect.HitTest(new Point(35, 35), 2));

        rect.Filled = false;
        Assert.False(rect.HitTest(new Point(35, 35), 2)); // outline only: the inside is free for other clicks
    });

    [Fact]
    public void Opacity_BlendsTheAnnotationWithTheScreenshot() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(10, 10), Colors.Black, 4, 1) { Filled = true, Opacity = 0.5 }, new Point(60, 60));
        var pixel = Images.PixelAt(Compositor.Compose(Images.Snapshot(80, 80, Colors.White), new Rect(0, 0, 80, 80), [rect]), 35, 35);
        Assert.InRange(pixel.R, 120, 135); // half black over white
    });

    [Fact]
    public void DashedLine_LeavesGaps() => Sta.Run(() =>
    {
        var line = Drawn(new LineAnnotation(new Point(0, 20), Blue, 4, 1) { Dashed = true }, new Point(200, 20));
        var image = Compositor.Compose(Images.Snapshot(200, 40, Colors.White), new Rect(0, 0, 200, 40), [line]);
        var row = Enumerable.Range(5, 190).Select(x => Images.PixelAt(image, x, 20)).ToList();

        Assert.Contains(row, p => p == Blue);
        Assert.Contains(row, p => p == Colors.White);
    });

    [Fact]
    public void Line_IsAStraightStrokeWithTwoEndHandles() => Sta.Run(() =>
    {
        var line = Drawn(new LineAnnotation(new Point(10, 10), Blue, 4, 1), new Point(90, 50));
        Assert.Equal([new Point(10, 10), new Point(90, 50)], line.Handles);
        Assert.True(line.HitTest(new Point(50, 30), 2));
        Assert.False(line.HitTest(new Point(50, 10), 2));

        line.Update(new Point(90, 14), constrain: true); // Shift snaps to 45° steps
        Assert.Equal(10, line.Handles[1].Y, 3);
    });

    [Fact]
    public void OnlyShapesFill_AndHighlighterAndMosaicKeepTheirLook()
    {
        var snapshot = Images.Snapshot(10, 10, Colors.White);
        Assert.True(new EllipseAnnotation(default, Blue, 4, 1).CanFill);
        Assert.False(new ArrowAnnotation(default, Blue, 4, 1).CanFill);
        Assert.True(new ArrowAnnotation(default, Blue, 4, 1).CanDash);
        Assert.True(new PenAnnotation(default, Blue, 4, 1).CanDash);
        Assert.False(new HighlighterAnnotation(default, Blue, 4, 1).CanDash);
        Assert.False(new MosaicAnnotation(default, snapshot, 4, 1).HasOpacity);
    }

    [Fact]
    public void Styles_AreKeptWhenCloned()
    {
        var rect = new RectAnnotation(default, Blue, 4, 1) { Filled = true, Dashed = true, Opacity = 0.3 };
        var clone = rect.Clone();
        Assert.True(clone.Filled);
        Assert.True(clone.Dashed);
        Assert.Equal(0.3, clone.Opacity);
    }
}
