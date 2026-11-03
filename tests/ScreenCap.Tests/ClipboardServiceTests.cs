using System.Windows.Media;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

/// <summary>The DIB the clipboard holds; converted without touching the real clipboard.</summary>
public class ClipboardServiceTests
{
    [Fact]
    public void Dib_RoundTrips_PixelForPixel_RightWayUp() => Sta.Run(() =>
    {
        var pixels = new byte[37 * 21 * 4];
        new Random(1).NextBytes(pixels);
        var image = Images.ToBitmap(pixels, 37, 21); // an odd width, so rows can't line up by accident

        var dib = ClipboardService.ToDib(image);
        Assert.Equal(40 + 37 * 21 * 4, dib.Length);
        Assert.Equal(21, BitConverter.ToInt32(dib, 8)); // positive height: bottom-up, which every reader supports

        var back = ClipboardService.FromDib(dib)!;
        Assert.Equal((37, 21), (back.PixelWidth, back.PixelHeight));
        for (int y = 0; y < 21; y += 5)
        {
            for (int x = 0; x < 37; x += 6)
            {
                var expected = Images.PixelAt(image, x, y);
                var actual = Images.PixelAt(back, x, y);
                Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
            }
        }
    });

    [Fact]
    public void FromDib_RejectsTooShortData()
    {
        Assert.Null(ClipboardService.FromDib(new byte[10]));
    }
}
