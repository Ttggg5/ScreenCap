using System.Windows;
using System.Windows.Media;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class ScreenSnapshotTests
{
    [Fact]
    public void WithImage_PaintsAtPosition_AndLeavesOriginalUntouched() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(100, 60, Colors.White, left: -1920, top: 0);
        var red = Images.ToBitmap(Images.Solid(10, 10, Colors.Red), 10, 10);

        var painted = snapshot.WithImage(red, 40, 20);
        Assert.Equal(Colors.Red, painted.GetPixel(45, 25));
        Assert.Equal(Colors.White, painted.GetPixel(39, 25));
        Assert.Equal(Colors.White, painted.GetPixel(50, 25));
        Assert.Equal(Colors.White, snapshot.GetPixel(45, 25));
        Assert.Equal(Colors.Red, Images.PixelAt(painted.Bitmap, 45, 25));
        Assert.Equal((-1920, 0, 100, 60), (painted.Left, painted.Top, painted.Width, painted.Height));
    });

    [Fact]
    public void WithImage_ClipsAtTheEdge() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(50, 50, Colors.White);
        var red = Images.ToBitmap(Images.Solid(10, 10, Colors.Red), 10, 10);
        var painted = snapshot.WithImage(red, 45, 45);
        Assert.Equal(Colors.Red, painted.GetPixel(49, 49));
    });

    [Fact]
    public void GetPixel_ClampsOutOfRangeCoordinates() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(10, 10, Colors.Green);
        Assert.Equal(Colors.Green, snapshot.GetPixel(-5, 50));
    });

    [Fact]
    public void Crop_ReturnsRegion() => Sta.Run(() =>
    {
        var crop = Images.Snapshot(100, 100, Colors.White).Crop(new Rect(10, 20, 30, 40));
        Assert.Equal((30, 40), (crop.PixelWidth, crop.PixelHeight));
    });
}
