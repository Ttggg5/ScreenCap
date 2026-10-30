using System.Windows.Media;
using ScreenCap.Pin;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class TextPinRendererTests
{
    [Theory]
    [InlineData("#1E88E5")]
    [InlineData("  1e88e5 ")]
    [InlineData("rgb(30, 136, 229)")]
    [InlineData("RGB(30,136,229)")]
    [InlineData("30, 136, 229")]
    public void ParseColor_RecognizesCommonFormats(string text) =>
        Assert.Equal(Color.FromRgb(30, 136, 229), TextPinRenderer.ParseColor(text));

    [Fact]
    public void ParseColor_ExpandsShortHex() => Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x88), TextPinRenderer.ParseColor("#F08"));

    [Theory]
    [InlineData("123456")]        // plain number, not a color
    [InlineData("hello")]
    [InlineData("300, 0, 0")]     // out of range
    [InlineData("#12345")]
    [InlineData("see #1E88E5")]
    public void ParseColor_LeavesOtherTextAlone(string text) => Assert.Null(TextPinRenderer.ParseColor(text));

    [Fact]
    public void ColorCode_BecomesASwatchOfThatColor() => Sta.Run(() =>
    {
        var image = TextPinRenderer.FromText("#1E88E5", 1)!;
        Assert.Equal(150, image.PixelWidth);
        Assert.Equal(Color.FromRgb(0x1E, 0x88, 0xE5), Images.PixelAt(image, 3, 3));
    });

    [Fact]
    public void Text_IsDrawnOnACard_ScaledToTheMonitor() => Sta.Run(() =>
    {
        var small = TextPinRenderer.FromText("Hello", 1)!;
        var scaled = TextPinRenderer.FromText("Hello", 1.5)!;
        var tall = TextPinRenderer.FromText("Hello\nworld\nthird line", 1)!;

        Assert.InRange(scaled.PixelWidth, small.PixelWidth * 1.4, small.PixelWidth * 1.6);
        Assert.InRange(tall.PixelHeight - small.PixelHeight, 35, 50); // two more 20px lines
        Assert.Equal(Color.FromRgb(0xFB, 0xFB, 0xFB), Images.PixelAt(small, 2, 2)); // card background
        var pixels = Images.Bgra(small);
        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4] < 100); // some dark text
    });

    [Fact]
    public void LongLines_WrapAtTheMaximumWidth() => Sta.Run(() =>
    {
        var image = TextPinRenderer.FromText(string.Join(" ", Enumerable.Repeat("wrap", 400)), 1)!;
        Assert.InRange(image.PixelWidth, 500, 590);
        Assert.True(image.PixelHeight > 100);
    });

    [Fact]
    public void EmptyText_GivesNothing() => Assert.Null(TextPinRenderer.FromText("  \n ", 1));
}
