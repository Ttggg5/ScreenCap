using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Native;
using ScreenCap.Tests.Support;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Tests;

public class SpotlightTests
{
    [Fact]
    public void Spotlight_DimsEverythingElse_AndKeepsItsAreas() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(200, 100, Colors.White);
        var a = new SpotlightAnnotation(new Point(10, 10), 1);
        a.Update(new Point(50, 50));
        var b = new SpotlightAnnotation(new Point(120, 20), 1);
        b.Update(new Point(180, 80));

        var image = Compositor.Compose(snapshot, new Rect(0, 0, 200, 100), [a, b]);
        Assert.Equal(Colors.White, Images.PixelAt(image, 30, 30));  // inside the first
        Assert.Equal(Colors.White, Images.PixelAt(image, 150, 50)); // inside the second
        var dimmed = Images.PixelAt(image, 80, 50);
        Assert.InRange(dimmed.R, 90, 110);                          // ~40% of white
    });

    [Fact]
    public void Spotlight_IsGrabbedInside_AndHasNoOpacity()
    {
        var spot = new SpotlightAnnotation(new Point(10, 10), 1);
        spot.Update(new Point(50, 50));
        Assert.True(spot.HitTest(new Point(30, 30), 2));
        Assert.False(spot.HasOpacity);
        Assert.Equal(8, spot.Handles.Length);
    }
}

public class TextStyleTests
{
    [Fact]
    public void Style_RoundTrips_AndIsKeptWhenCloned()
    {
        var style = new TextStyle(Bold: true, Background: false, Italic: true, Underline: true, FontName: "Consolas");
        var text = new TextAnnotation("Hi", new Point(0, 0), Colors.Red, 20, 1) { Style = style };
        Assert.Equal(style, text.Style);
        Assert.Equal(style, ((TextAnnotation)text.Clone()).Style);
    }

    [Fact]
    public void Font_ChangesTheLayout() => Sta.Run(() =>
    {
        var proportional = new TextAnnotation("iiiiiiii", new Point(0, 0), Colors.Red, 20, 1);
        var mono = new TextAnnotation("iiiiiiii", new Point(0, 0), Colors.Red, 20, 1) { Style = TextStyle.Default with { FontName = "Consolas" } };
        Assert.True(mono.Bounds.Width > proportional.Bounds.Width * 1.5); // narrow i vs fixed-width i
    });

    [Fact]
    public void Underline_DrawsALineUnderTheText() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(200, 60, Colors.White);
        var plain = new TextAnnotation("____ ____", new Point(10, 10), Colors.Black, 20, 1);
        var underlined = new TextAnnotation("HHHH HHHH", new Point(10, 10), Colors.Black, 20, 1) { Underline = true };
        var withoutLine = new TextAnnotation("HHHH HHHH", new Point(10, 10), Colors.Black, 20, 1);

        int DarkPixels(TextAnnotation text)
        {
            var px = Images.Bgra(Compositor.Compose(snapshot, new Rect(0, 0, 200, 60), [text]));
            return Enumerable.Range(0, px.Length / 4).Count(i => px[i * 4] < 128);
        }
        Assert.True(DarkPixels(underlined) > DarkPixels(withoutLine) + 40);
        Assert.True(DarkPixels(plain) > 0);
    });
}

public class ClickPainterTests
{
    /// <summary>Draws into a 100×100 white GDI bitmap and returns its BGRA pixels.</summary>
    private static byte[] Paint(Action<IntPtr> draw)
    {
        IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, 100, 100);
        IntPtr old = SelectObject(dc, bitmap);
        try
        {
            BitBlt(dc, 0, 0, 100, 100, IntPtr.Zero, 0, 0, 0x00FF0062); // WHITENESS
            draw(dc);
            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = 100, biHeight = -100, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[100 * 100 * 4];
            SelectObject(dc, old);
            GetDIBits(dc, bitmap, 0, 100, pixels, ref header, 0);
            return pixels;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static Color At(byte[] px, int x, int y) => Images.PixelAt(px, 100, x, y) with { A = 255 };

    [Fact]
    public void HeldButton_DrawsARingAroundTheCursor()
    {
        using var painter = new ClickPainter(1);
        var left = Paint(dc => painter.Draw(dc, 50, 50, primary: true, secondary: false, now: 0));
        Assert.Equal(Color.FromRgb(0xFF, 0xB3, 0x00), At(left, 50 + 16, 50)); // on the 16px ring
        Assert.Equal(Colors.White, At(left, 50, 50));                          // hollow: the cursor stays visible

        using var other = new ClickPainter(1);
        var right = Paint(dc => other.Draw(dc, 50, 50, primary: false, secondary: true, now: 0));
        Assert.Equal(Color.FromRgb(0x1E, 0x88, 0xE5), At(right, 50 + 16, 50));
    }

    [Fact]
    public void Ring_LingersBrieflyAfterRelease_ThenDisappears()
    {
        using var painter = new ClickPainter(1);
        Paint(dc => painter.Draw(dc, 50, 50, true, false, now: 1000));
        var soon = Paint(dc => painter.Draw(dc, 50, 50, false, false, now: 1100)); // released at 1100
        var later = Paint(dc => painter.Draw(dc, 50, 50, false, false, now: 1500));
        Assert.Contains(Enumerable.Range(0, 100), x => At(soon, x, 50) != Colors.White);
        Assert.DoesNotContain(Enumerable.Range(0, 100), x => At(later, x, 50) != Colors.White);
    }

    [Fact]
    public void NoRing_BeforeAnyClick()
    {
        using var painter = new ClickPainter(1);
        var px = Paint(dc => painter.Draw(dc, 50, 50, false, false, now: 5));
        Assert.DoesNotContain(Enumerable.Range(0, 100), x => At(px, x, 50) != Colors.White);
    }
}

public class SizeRangeTests
{
    [Fact]
    public void EachToolHasItsOwnRange_WithMoreSteps()
    {
        var stroke = Capture.SizeRange.For(Capture.ToolKind.Pen);
        Assert.Equal((1.0, 20.0, 4.0), (stroke.Min, stroke.Max, stroke.Default));
        Assert.Equal("大小", Capture.SizeRange.For(Capture.ToolKind.Text).Label);
        Assert.True(Capture.SizeRange.For(Capture.ToolKind.Text).Max >= 72);
        Assert.Equal(16, Capture.SizeRange.For(Capture.ToolKind.Highlighter).Default);
    }

    [Fact]
    public void Size_IsTheFontSize_ForText() =>
        Assert.Equal(36, TextAnnotation.FontSizeFor(24, 1.5));

    [Fact]
    public void SpotlightDim_SetsHowDarkTheRestIs() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(100, 50, Colors.White);
        var light = new SpotlightAnnotation(new Point(0, 0), 1, dim: 20);
        light.Update(new Point(20, 20));
        var dark = new SpotlightAnnotation(new Point(0, 0), 1, dim: 90);
        dark.Update(new Point(20, 20));

        byte Outside(SpotlightAnnotation s) => Images.PixelAt(Compositor.Compose(snapshot, new Rect(0, 0, 100, 50), [s]), 60, 30).R;
        Assert.InRange(Outside(light), 195, 210); // 80% of white left
        Assert.InRange(Outside(dark), 20, 32);    // 10% left
        Assert.Equal(90, new SpotlightAnnotation(default, 1, dim: 200).Dim); // clamped
    });
}
