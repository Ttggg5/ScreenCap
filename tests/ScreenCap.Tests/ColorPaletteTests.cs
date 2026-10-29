using System.Windows.Media;
using ScreenCap.Capture;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

[Collection(nameof(SettingsCollection))] // the palette reads and writes recent colors
public class ColorPaletteTests : IDisposable
{
    public ColorPaletteTests() => TestSettings.Clear();

    public void Dispose() => TestSettings.Clear();

    [Theory]
    [InlineData("#1E88E5", 0x1E, 0x88, 0xE5)]
    [InlineData("1e88e5", 0x1E, 0x88, 0xE5)]
    [InlineData(" #0F8 ", 0x00, 0xFF, 0x88)]
    public void TryParseHex_AcceptsCommonForms(string text, byte r, byte g, byte b)
    {
        Assert.True(ColorPalette.TryParseHex(text, out var color));
        Assert.Equal(Color.FromRgb(r, g, b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void TryParseHex_RejectsInvalid(string text) => Assert.False(ColorPalette.TryParseHex(text, out _));

    [Fact]
    public void ToHex_FormatsUppercaseWithHash() => Assert.Equal("#0A0B0C", ColorPalette.ToHex(Color.FromRgb(10, 11, 12)));

    [Theory]
    [InlineData("#E53935")]
    [InlineData("#FDD835")]
    [InlineData("#00ACC1")]
    [InlineData("#3949AB")]
    [InlineData("#9E9E9E")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void SetColor_RoundTripsThroughHsv(string hex) => Sta.Run(() =>
    {
        ColorPalette.TryParseHex(hex, out var color);
        var palette = new ColorPalette();
        palette.SetColor(color);
        Assert.True(Images.Distance(color, palette.Color) <= 1, $"{hex} came back as {ColorPalette.ToHex(palette.Color)}");
    });

    [Fact]
    public void SetColor_DoesNotRaisePicked_OrTouchRecentColors() => Sta.Run(() =>
    {
        var palette = new ColorPalette();
        bool raised = false;
        palette.ColorPicked += _ => raised = true;
        palette.SetColor(Colors.Teal);
        Assert.False(raised);
        Assert.Empty(AppSettings.RecentColors);
    });
}
