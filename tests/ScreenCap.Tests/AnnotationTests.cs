using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class AnnotationTests
{
    private static readonly Color Red = Colors.Red;

    private static T Drawn<T>(T annotation, Point end) where T : Annotation
    {
        annotation.Update(end);
        return annotation;
    }

    [Fact]
    public void Rect_BoundsFollowDrag_AndTinyDragIsEmpty() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(50, 40), Red, 4, 1), new Point(10, 10));
        Assert.Equal(new Rect(10, 10, 40, 30), rect.Bounds);
        Assert.False(rect.IsEmpty);
        Assert.True(Drawn(new RectAnnotation(new Point(5, 5), Red, 4, 1), new Point(6, 6)).IsEmpty);
    });

    [Fact]
    public void MoveFrom_TranslatesFromOrigin_NotCumulatively() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(10, 10), Red, 4, 1), new Point(30, 20));
        var origin = rect.Clone();
        rect.MoveFrom(origin, new Vector(5, 5));
        rect.MoveFrom(origin, new Vector(7, 3));
        Assert.Equal(new Rect(17, 13, 20, 10), rect.Bounds);
        Assert.Equal(new Rect(10, 10, 20, 10), origin.Bounds);
    });

    [Fact]
    public void DragHandle_ResizesBox_AndCanFlip() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(10, 10), Red, 4, 1), new Point(30, 30));
        var origin = rect.Clone();
        rect.DragHandleFrom(origin, 4, new Vector(10, 5)); // bottom-right
        Assert.Equal(new Rect(10, 10, 30, 25), rect.Bounds);
        rect.DragHandleFrom(origin, 0, new Vector(40, 0)); // top-left dragged past the right edge
        Assert.Equal(new Rect(30, 10, 20, 20), rect.Bounds);
    });

    [Fact]
    public void Arrow_HasTwoEndHandles_ThatMoveIndependently() => Sta.Run(() =>
    {
        var arrow = Drawn(new ArrowAnnotation(new Point(0, 0), Red, 4, 1), new Point(100, 0));
        Assert.Equal([new Point(0, 0), new Point(100, 0)], arrow.Handles);
        arrow.DragHandleFrom(arrow.Clone(), 1, new Vector(0, 50));
        Assert.Equal([new Point(0, 0), new Point(100, 50)], arrow.Handles);
    });

    [Fact]
    public void HitTest_IsOnTheStroke_NotInsideTheShape() => Sta.Run(() =>
    {
        var rect = Drawn(new RectAnnotation(new Point(0, 0), Red, 4, 1), new Point(100, 100));
        Assert.True(rect.HitTest(new Point(0, 50), 2));
        Assert.True(rect.HitTest(new Point(102, 50), 2));
        Assert.False(rect.HitTest(new Point(50, 50), 2));
    });

    [Fact]
    public void PenClone_IsIndependentOfTheOriginal() => Sta.Run(() =>
    {
        var pen = new PenAnnotation(new Point(0, 0), Red, 2, 1);
        pen.Update(new Point(10, 0));
        var clone = (PenAnnotation)pen.Clone();
        pen.Update(new Point(50, 50));
        Assert.True(clone.Bounds.Right < 20);
        Assert.True(pen.Bounds.Right > 40);
    });

    [Fact]
    public void Highlighter_IsWiderThanPen_AndKeepsTypeWhenCloned() => Sta.Run(() =>
    {
        var pen = Drawn(new PenAnnotation(new Point(0, 50), Red, 4, 1), new Point(100, 50));
        var highlighter = Drawn(new HighlighterAnnotation(new Point(0, 50), Red, 16, 1), new Point(100, 50));
        Assert.Equal(4, pen.Bounds.Height, 3);
        Assert.Equal(16, highlighter.Bounds.Height, 3);
        Assert.IsType<HighlighterAnnotation>(highlighter.Clone());
    });

    [Fact]
    public void Number_IsCircleAroundCenter_AndUpdateMovesIt() => Sta.Run(() =>
    {
        var number = new NumberAnnotation(new Point(50, 50), 3, Red, 32, 1);
        Assert.Equal(new Rect(34, 34, 32, 32), number.Bounds); // size is the diameter
        Assert.True(number.HitTest(new Point(60, 50), 0));
        Assert.False(number.HitTest(new Point(80, 50), 0));
        number.Update(new Point(100, 100));
        Assert.Equal(new Point(100, 100), number.Center);
        Assert.Equal(3, number.Number);
    });

    [Fact]
    public void TextBackground_AddsPaddingToBounds() => Sta.Run(() =>
    {
        var plain = new TextAnnotation("Hello", new Point(10, 10), Red, 20, 1);
        var boxed = new TextAnnotation("Hello", new Point(10, 10), Red, 20, 1, background: true);
        Assert.Equal(plain.Bounds.Width + 2 * TextAnnotation.BackgroundPadding, boxed.Bounds.Width, 3);
        Assert.True(boxed.HitTest(new Point(boxed.Bounds.Left + 1, boxed.Bounds.Top + 1), 0));
    });

    [Fact]
    public void TextBold_IsWiderThanNormal() => Sta.Run(() =>
    {
        var normal = new TextAnnotation("Wide text", new Point(0, 0), Red, 20, 1);
        var bold = new TextAnnotation("Wide text", new Point(0, 0), Red, 20, 1, bold: true);
        Assert.True(bold.Bounds.Width > normal.Bounds.Width);
    });

    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF, 0x00)] // white → black text
    [InlineData(0xFD, 0xD8, 0x35, 0x00)] // yellow → black
    [InlineData(0x00, 0x00, 0x00, 0xFF)] // black → white
    [InlineData(0x1E, 0x88, 0xE5, 0xFF)] // blue → white
    public void ContrastColor_PicksReadableText(byte r, byte g, byte b, byte expected) =>
        Assert.Equal(expected, TextAnnotation.ContrastColor(Color.FromRgb(r, g, b)).R);

    [Fact]
    public void Mosaic_ClampsStrength() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(100, 100, Colors.White);
        Assert.Equal(MosaicAnnotation.MaxStrength, new MosaicAnnotation(new Point(0, 0), snapshot, 99, 1).Strength);
        Assert.Equal(MosaicAnnotation.MinStrength, new MosaicAnnotation(new Point(0, 0), snapshot, -5, 1).Strength);
    });
}
