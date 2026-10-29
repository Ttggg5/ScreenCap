using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class GifEncoderTests
{
    private const int W = 120, H = 80;

    private static byte[] Frame(Int32Rect box, Color color)
    {
        var pixels = Images.Solid(W, H, Colors.White);
        Images.FillRect(pixels, W, box, color);
        return pixels;
    }

    private static byte[] Encode(params (byte[] Pixels, int Ms)[] frames) => Encode(frames.Last().Ms + 100, frames);

    private static byte[] Encode(int endMs, params (byte[] Pixels, int Ms)[] frames)
    {
        var stream = new MemoryStream();
        var encoder = new GifEncoder(stream, W, H);
        foreach (var (pixels, ms) in frames)
            encoder.AddFrame(pixels, TimeSpan.FromMilliseconds(ms));
        encoder.Finish(TimeSpan.FromMilliseconds(endMs));
        return stream.ToArray();
    }

    private static void AssertSameImage(byte[] expectedBgra, byte[] canvas)
    {
        for (int i = 0; i < expectedBgra.Length; i += 4)
        {
            Assert.True(expectedBgra[i] == canvas[i] && expectedBgra[i + 1] == canvas[i + 1] && expectedBgra[i + 2] == canvas[i + 2],
                $"pixel {i / 4 % W},{i / 4 / W} differs");
        }
    }

    [Fact]
    public void FewColors_AreLossless_AfterCompositing() => Sta.Run(() =>
    {
        var a = Frame(new Int32Rect(10, 10, 20, 20), Colors.Red);
        var b = Frame(new Int32Rect(50, 30, 20, 20), Colors.Blue);
        var gif = GifReader.Decode(Encode((a, 0), (b, 100)));

        Assert.Equal((W, H), (gif.Width, gif.Height));
        Assert.Equal(2, gif.Frames.Count);
        AssertSameImage(a, gif.Frames[0].Canvas);
        AssertSameImage(b, gif.Frames[1].Canvas);
    });

    [Fact]
    public void LaterFrames_OnlyEncodeTheChangedRectangle() => Sta.Run(() =>
    {
        var a = Frame(new Int32Rect(0, 0, 1, 1), Colors.White);
        var b = Frame(new Int32Rect(40, 20, 15, 10), Colors.Green);
        var frames = GifReader.Decode(Encode((a, 0), (b, 100))).Frames;

        Assert.Equal((0, 0, W, H), (frames[0].Left, frames[0].Top, frames[0].Width, frames[0].Height));
        Assert.Equal((40, 20, 15, 10), (frames[1].Left, frames[1].Top, frames[1].Width, frames[1].Height));
    });

    [Fact]
    public void IdenticalFrames_AreMerged_AndDelaysFollowTimestamps() => Sta.Run(() =>
    {
        var a = Frame(new Int32Rect(10, 10, 5, 5), Colors.Red);
        var b = Frame(new Int32Rect(60, 10, 5, 5), Colors.Red);
        var c = Frame(new Int32Rect(60, 50, 5, 5), Colors.Red);
        var frames = GifReader.Decode(Encode(500, (a, 0), ((byte[])a.Clone(), 100), (b, 300), (c, 400))).Frames;

        Assert.Equal([30, 10, 10], frames.Select(f => f.DelayCs)); // a shown 0–300 ms, b 300–400, c 400–500
    });

    [Fact]
    public void ShortDelays_AreClampedToTwoCentiseconds() => Sta.Run(() =>
    {
        var a = Frame(new Int32Rect(0, 0, 5, 5), Colors.Red);
        var b = Frame(new Int32Rect(10, 0, 5, 5), Colors.Red);
        var frames = GifReader.Decode(Encode(105, (a, 0), (b, 100))).Frames;
        Assert.Equal(2, frames[1].DelayCs);
    });

    [Fact]
    public void LoopsForever()
    {
        var gif = Encode((Frame(new Int32Rect(0, 0, 1, 1), Colors.Red), 0));
        string text = Encoding.ASCII.GetString(gif);
        Assert.StartsWith("GIF89a", text);
        int netscape = text.IndexOf("NETSCAPE2.0", StringComparison.Ordinal);
        Assert.True(netscape > 0);
        Assert.Equal([0x03, 0x01, 0x00, 0x00], gif.Skip(netscape + 11).Take(4)); // loop count 0 = forever
        Assert.Equal(0x3B, gif[^1]); // trailer
    }

    [Fact]
    public void ManyColors_AreQuantizedClosely() => Sta.Run(() =>
    {
        // A smooth gradient has far more than 255 colors.
        var pixels = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                pixels[i] = (byte)(x * 255 / W);
                pixels[i + 1] = (byte)(y * 255 / H);
                pixels[i + 2] = (byte)((x + y) * 255 / (W + H));
                pixels[i + 3] = 255;
            }
        }
        var canvas = GifReader.Decode(Encode((pixels, 0))).Frames[0].Canvas;

        double total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            total += Math.Abs(pixels[i] - canvas[i]) + Math.Abs(pixels[i + 1] - canvas[i + 1]) + Math.Abs(pixels[i + 2] - canvas[i + 2]);
        double meanPerChannel = total / (W * H * 3);
        Assert.True(meanPerChannel < 6, $"mean error {meanPerChannel:F2}");
    });

    [Fact]
    public void Dispose_ClosesOwnedStreamOnly()
    {
        var owned = new MemoryStream();
        new GifEncoder(owned, 4, 4, ownsStream: true).Dispose();
        Assert.False(owned.CanWrite);

        var borrowed = new MemoryStream();
        new GifEncoder(borrowed, 4, 4).Dispose();
        Assert.True(borrowed.CanWrite);
    }
}
