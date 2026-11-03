using System.Windows.Media;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class BeautifierTests
{
    private static int IndexOf(string name) => Array.FindIndex(BeautifyBackground.All, b => b.Name == name);

    [Fact]
    public void Apply_AddsTheMargin_AndKeepsTheScreenshotSharp() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 60, Colors.Red), 100, 60);
        var options = new BeautifyOptions(true, IndexOf("白色"), Padding: 20, Radius: 0, Shadow: false);
        var result = Beautifier.Apply(shot, options, scale: 1.5);

        Assert.Equal((160, 120), (result.PixelWidth, result.PixelHeight)); // 20 DIP at 150% is 30 px each side
        Assert.Equal(Colors.Red, Images.PixelAt(result, 30, 30));
        Assert.Equal(Colors.Red, Images.PixelAt(result, 129, 89));
        Assert.Equal(Colors.White, Images.PixelAt(result, 29, 30));
        Assert.Equal(Colors.White, Images.PixelAt(result, 130, 89));
    });

    [Fact]
    public void Apply_RoundsTheCorners() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 60, Colors.Red), 100, 60);
        var options = new BeautifyOptions(true, IndexOf("白色"), Padding: 10, Radius: 12, Shadow: false);
        var result = Beautifier.Apply(shot, options, scale: 1);

        Assert.True(Images.Distance(Images.PixelAt(result, 10, 10), Colors.White) < 30); // the corner is cut off
        Assert.Equal(Colors.Red, Images.PixelAt(result, 60, 10));                       // the middle of the top edge is not
        Assert.Equal(Colors.Red, Images.PixelAt(result, 60, 40));
    });

    [Fact]
    public void Apply_TransparentBackground_LeavesTheMarginClear_ButShowsTheShadow() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 60, Colors.Red), 100, 60);
        var options = new BeautifyOptions(true, IndexOf("透明"), Padding: 40, Radius: 0, Shadow: true);
        var result = Beautifier.Apply(shot, options, scale: 1);

        Assert.Equal(0, Images.PixelAt(result, 1, 1).A);              // far from the screenshot: clear
        Assert.True(Images.PixelAt(result, 90, 104).A > 0);           // just below it: the shadow
        Assert.True(Beautifier.HasTransparency(result));
        Assert.Equal(Colors.White, Images.PixelAt(Beautifier.OnWhite(result), 1, 1));
        Assert.Equal(Colors.Red, Images.PixelAt(Beautifier.OnWhite(result), 90, 70));
    });

    [Fact]
    public void Apply_GradientBackground_RunsCornerToCorner() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 100, Colors.Red), 100, 100);
        var background = BeautifyBackground.FromIndex(0);
        var result = Beautifier.Apply(shot, new BeautifyOptions(true, 0, 40, 0, false), scale: 1);

        Assert.True(Images.Distance(Images.PixelAt(result, 0, 0), background.From) < 12);
        Assert.True(Images.Distance(Images.PixelAt(result, 179, 179), background.To) < 12);
    });

    [Fact]
    public void Apply_OuterRadius_RoundsTheBackgroundToo() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 60, Colors.Red), 100, 60);
        var options = new BeautifyOptions(true, IndexOf("白色"), Padding: 20, Radius: 0, Shadow: false, OuterRadius: 16);
        var result = Beautifier.Apply(shot, options, scale: 1);

        Assert.Equal(0, Images.PixelAt(result, 0, 0).A);               // outside the outer corner: clear
        Assert.Equal(Colors.White, Images.PixelAt(result, 70, 0));      // the middle of the top edge: background
        Assert.Equal(Colors.Red, Images.PixelAt(result, 20, 20));       // the screenshot's own corner stays square
        Assert.True(Beautifier.HasTransparency(result));
    });

    [Fact]
    public void Apply_OuterRadius_WithoutMargin_RoundsTheScreenshot() => Sta.Run(() =>
    {
        var shot = Images.ToBitmap(Images.Solid(100, 60, Colors.Red), 100, 60);
        var result = Beautifier.Apply(shot, new BeautifyOptions(true, IndexOf("白色"), 0, 0, false, OuterRadius: 16), scale: 1);

        Assert.Equal(0, Images.PixelAt(result, 0, 0).A);
        Assert.Equal(Colors.Red, Images.PixelAt(result, 50, 30));
    });

    [Fact]
    public void HasTransparency_IsFalseForAnOpaqueScreenshot() => Sta.Run(() =>
    {
        Assert.False(Beautifier.HasTransparency(Images.ToBitmap(Images.Solid(10, 10, Colors.Blue), 10, 10)));
    });

    [Fact]
    public void PaddingPixels_FollowsTheDisplayScale()
    {
        Assert.Equal(96, Beautifier.PaddingPixels(BeautifyOptions.Default with { Padding = 48 }, 2));
        Assert.Equal(BeautifyOptions.MaxPadding, Beautifier.PaddingPixels(BeautifyOptions.Default with { Padding = 999 }, 1));
    }
}
