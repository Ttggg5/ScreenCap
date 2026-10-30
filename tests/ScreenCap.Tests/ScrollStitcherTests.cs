using System.Windows;
using System.Windows.Media;
using ScreenCap.Scrolling;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class ScrollStitcherTests
{
    private const int W = 120, H = 200;

    /// <summary>A tall "page": text-like rows with random widths, separated by blank lines.</summary>
    private static byte[] Page(int height, int seed)
    {
        var random = new Random(seed);
        var page = Images.Solid(W, height, Colors.White);
        for (int y = 0; y < height; y += 6)
        {
            int length = random.Next(10, W);
            var color = Color.FromRgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            Images.FillRect(page, W, new Int32Rect(random.Next(0, W - length + 1), y, length, Math.Min(3, height - y)), color);
        }
        return page;
    }

    /// <summary>The viewport at <paramref name="offset"/>, with optional sticky header and footer bars.</summary>
    private static byte[] View(byte[] page, int offset, int header = 0, int footer = 0)
    {
        var frame = new byte[W * H * 4];
        Array.Copy(page, offset * W * 4, frame, 0, frame.Length);
        if (header > 0)
            Images.FillRect(frame, W, new Int32Rect(0, 0, W, header), Colors.SteelBlue);
        if (footer > 0)
            Images.FillRect(frame, W, new Int32Rect(0, H - footer, W, footer), Colors.DarkOrange);
        return frame;
    }

    private static void AssertRows(byte[] expected, int expectedStart, byte[] actual, int actualStart, int count)
    {
        for (int y = 0; y < count; y++)
        {
            Assert.True(expected.AsSpan((expectedStart + y) * W * 4, W * 4).SequenceEqual(actual.AsSpan((actualStart + y) * W * 4, W * 4)),
                $"row {actualStart + y} differs");
        }
    }

    [Fact]
    public void Stitches_ScrolledFrames_IntoThePage() => Sta.Run(() =>
    {
        var page = Page(900, 1);
        var stitcher = new ScrollStitcher(W, H, View(page, 0));
        int[] offsets = [0, 70, 150, 260, 390, 520, 640, 700];
        for (int i = 1; i < offsets.Length; i++)
        {
            Assert.Equal(StitchOutcome.Appended, stitcher.Add(View(page, offsets[i])));
            Assert.Equal(offsets[i] - offsets[i - 1], stitcher.LastOffset);
        }

        var image = Images.Bgra(stitcher.ToBitmap());
        Assert.Equal(700 + H, stitcher.Height);
        Assert.Equal((700 + H) * W * 4, image.Length);
        AssertRows(page, 0, image, 0, 700 + H);
    });

    [Fact]
    public void StickyHeaderAndFooter_AreKeptOnce() => Sta.Run(() =>
    {
        const int header = 30, footer = 20;
        var page = Page(800, 2);
        var stitcher = new ScrollStitcher(W, H, View(page, 0, header, footer));
        foreach (int offset in new[] { 100, 220, 330, 450 })
            Assert.Equal(StitchOutcome.Appended, stitcher.Add(View(page, offset, header, footer)));

        // header + page rows [header, 450 + H - footer) + footer
        int bodyEnd = 450 + H - footer;
        var image = Images.Bgra(stitcher.ToBitmap());
        Assert.Equal(bodyEnd + footer, stitcher.Height);
        var top = View(page, 0, header, footer);
        AssertRows(top, 0, image, 0, header);
        AssertRows(page, header, image, header, bodyEnd - header);
        var last = View(page, 450, header, footer);
        AssertRows(last, H - footer, image, bodyEnd, footer);
    });

    [Fact]
    public void ToleratesAScrollbarAndCornersThatDontScroll() => Sta.Run(() =>
    {
        var page = Page(900, 8);
        byte[] Frame(int offset)
        {
            var frame = View(page, offset);
            // A scrollbar on the right whose thumb moves, and static "corners" from the desktop behind.
            Images.FillRect(frame, W, new Int32Rect(W - 6, 0, 6, H), Colors.LightGray);
            Images.FillRect(frame, W, new Int32Rect(W - 6, offset * H / 900, 6, 40), Colors.DimGray);
            foreach (var corner in new[] { new Int32Rect(0, 0, 4, 4), new Int32Rect(0, H - 4, 4, 4) })
                Images.FillRect(frame, W, corner, Colors.Black);
            return frame;
        }

        var stitcher = new ScrollStitcher(W, H, Frame(0));
        foreach (int offset in new[] { 110, 230, 350, 480, 600, 700 })
            Assert.Equal(StitchOutcome.Appended, stitcher.Add(Frame(offset)));
        Assert.Equal(700 + H, stitcher.Height);

        // Content columns (left of the scrollbar, below the corner) match the page exactly.
        var image = Images.Bgra(stitcher.ToBitmap());
        for (int y = 4; y < 700 + H - 4; y++)
        {
            for (int x = 4; x < W - 6; x++)
            {
                int i = (y * W + x) * 4;
                Assert.True(page[i] == image[i] && page[i + 1] == image[i + 1] && page[i + 2] == image[i + 2], $"pixel {x},{y}");
            }
        }
    });

    [Fact]
    public void SameFrame_IsNoChange()
    {
        var page = Page(400, 3);
        var stitcher = new ScrollStitcher(W, H, View(page, 50));
        Assert.Equal(StitchOutcome.NoChange, stitcher.Add(View(page, 50)));
        Assert.Equal(H, stitcher.Height);
    }

    [Fact]
    public void UnrelatedFrame_IsLost()
    {
        var stitcher = new ScrollStitcher(W, H, View(Page(400, 4), 0));
        Assert.Equal(StitchOutcome.Lost, stitcher.Add(View(Page(400, 5), 0)));
    }

    [Fact]
    public void AfterTheEnd_FurtherFramesAreNoChange()
    {
        var page = Page(500, 6);
        var stitcher = new ScrollStitcher(W, H, View(page, 0));
        Assert.Equal(StitchOutcome.Appended, stitcher.Add(View(page, 150)));
        Assert.Equal(StitchOutcome.Appended, stitcher.Add(View(page, 300)));
        Assert.Equal(StitchOutcome.NoChange, stitcher.Add(View(page, 300))); // the page didn't move any more
        Assert.Equal(300 + H, stitcher.Height);
    }

    [Fact]
    public void NeverScrolled_ReturnsTheFirstFrame() => Sta.Run(() =>
    {
        var first = View(Page(400, 7), 0);
        var image = new ScrollStitcher(W, H, first).ToBitmap();
        Assert.Equal((W, H), (image.PixelWidth, image.PixelHeight));
        AssertRows(first, 0, Images.Bgra(image), 0, H);
    });
}
