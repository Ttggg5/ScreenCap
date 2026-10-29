using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class CompositorTests
{
    [Fact]
    public void WithoutAnnotations_ReturnsTheCroppedRegion() => Sta.Run(() =>
    {
        var pixels = Images.Solid(100, 80, Colors.White);
        Images.FillRect(pixels, 100, new Int32Rect(20, 10, 5, 5), Colors.Red);
        var snapshot = new Services.ScreenSnapshot(0, 0, 100, 80, pixels, 400, Images.ToBitmap(pixels, 100, 80));

        var image = Compositor.Compose(snapshot, new Rect(20, 10, 30, 20), []);
        Assert.Equal(30, image.PixelWidth);
        Assert.Equal(20, image.PixelHeight);
        Assert.Equal(Colors.Red, Images.PixelAt(image, 0, 0));
        Assert.Equal(Colors.White, Images.PixelAt(image, 10, 10));
    });

    [Fact]
    public void Annotations_AreBurnedIn_RelativeToTheSelection() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(200, 100, Colors.White);
        var rect = new RectAnnotation(new Point(60, 30), Colors.Blue, 4, 1);
        rect.Update(new Point(90, 60));

        var image = Compositor.Compose(snapshot, new Rect(50, 20, 100, 60), [rect]);
        Assert.Equal(Colors.Blue, Images.PixelAt(image, 10, 20));  // left edge at x=60 → 10
        Assert.Equal(Colors.White, Images.PixelAt(image, 25, 25)); // inside stays clear
    });

    [Fact]
    public void Highlighter_IsTranslucent() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(100, 50, Colors.White);
        var highlighter = new HighlighterAnnotation(new Point(10, 25), Color.FromRgb(0xFD, 0xD8, 0x35), 4, 1);
        highlighter.Update(new Point(90, 25));

        var pixel = Images.PixelAt(Compositor.Compose(snapshot, new Rect(0, 0, 100, 50), [highlighter]), 50, 25);
        Assert.True(pixel.R > 245);                 // yellow tint over white...
        Assert.InRange(pixel.B, 120, 220);         // ...but not fully opaque yellow (B=0x35)
    });

    [Fact]
    public void Number_DrawsFilledCircle_AndTextBackground_FillsTheBox() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(200, 100, Colors.White);
        var number = new NumberAnnotation(new Point(30, 30), 1, Colors.Blue, 4, 1);
        var text = new TextAnnotation("Hi", new Point(120, 40), Colors.Blue, 4, 1, background: true);

        var image = Compositor.Compose(snapshot, new Rect(0, 0, 200, 100), [number, text]);
        Assert.Equal(Colors.Blue, Images.PixelAt(image, 22, 22)); // inside the circle, off the digit
        var box = text.Bounds;
        Assert.Equal(Colors.Blue, Images.PixelAt(image, (int)box.Left + 2, (int)box.Top + 2));
    });

    [Fact]
    public void Mosaic_AveragesBlocks() => Sta.Run(() =>
    {
        // Alternating black/white columns average to gray.
        var pixels = Images.Solid(60, 60, Colors.White);
        for (int x = 0; x < 60; x += 2)
            Images.FillRect(pixels, 60, new Int32Rect(x, 0, 1, 60), Colors.Black);
        var snapshot = new Services.ScreenSnapshot(0, 0, 60, 60, pixels, 240, Images.ToBitmap(pixels, 60, 60));
        var mosaic = new MosaicAnnotation(new Point(0, 0), snapshot, 4, 1); // 14px blocks
        mosaic.Update(new Point(56, 56));

        var image = Compositor.Compose(snapshot, new Rect(0, 0, 60, 60), [mosaic]);
        var center = Images.PixelAt(image, 7, 7);
        Assert.InRange(center.R, 110, 145);
        Assert.Equal(center, Images.PixelAt(image, 8, 7)); // whole block is one color
    });
}
