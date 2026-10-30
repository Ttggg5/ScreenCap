using System.Windows;
using System.Windows.Media;
using ScreenCap.Capture;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class MagnifierTests
{
    private static MagnifierControl At(Color color)
    {
        var pixels = Images.Solid(20, 20, Colors.White);
        Images.FillRect(pixels, 20, new Int32Rect(5, 5, 1, 1), color);
        var snapshot = new Services.ScreenSnapshot(0, 0, 20, 20, pixels, 80, Images.ToBitmap(pixels, 20, 20));
        var magnifier = new MagnifierControl();
        magnifier.Update(snapshot, 5, 5);
        return magnifier;
    }

    [Fact]
    public void CopiesHex_ByDefault() => Sta.Run(() =>
        Assert.Equal("#1A2126", At(Color.FromRgb(0x1A, 0x21, 0x26)).ColorText));

    [Fact]
    public void CopiesRgb_WhenSwitched() => Sta.Run(() =>
    {
        var magnifier = At(Color.FromRgb(26, 33, 38));
        magnifier.UseRgb = true;
        Assert.Equal("rgb(26, 33, 38)", magnifier.ColorText);
        magnifier.UseRgb = false;
        Assert.Equal("#1A2126", magnifier.ColorText);
    });
}
