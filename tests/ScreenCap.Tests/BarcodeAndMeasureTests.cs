using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Annotations;
using ScreenCap.Services;
using ScreenCap.Tests.Support;
using ZXing;

namespace ScreenCap.Tests;

public class BarcodeServiceTests
{
    /// <summary>
    /// Draws a code with <paramref name="moduleSize"/> pixels per module at (x, y) on a white image that
    /// also has some text on it, like a screenshot of a web page.
    /// </summary>
    private static BitmapSource Screen(BarcodeFormat format, string content, int x = 60, int y = 50, bool inverted = false, int size = 150)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            // UTF-8 without an ECI marker, as most generators write it, so the reader has to guess the encoding.
            Options = new ZXing.QrCode.QrCodeEncodingOptions
            {
                Width = size,
                Height = format == BarcodeFormat.QR_CODE ? size : 80,
                Margin = 2,
                CharacterSet = "UTF-8",
                DisableECI = true,
            },
        };
        var code = writer.Write(content);
        var codeBitmap = BitmapSource.Create(code.Width, code.Height, 96, 96, PixelFormats.Bgra32, null, code.Pixels, code.Width * 4);

        var visual = new DrawingVisual();
        int w = 600, h = 400;
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(inverted ? Brushes.Black : Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawText(new FormattedText("Scan to open the page", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 18, inverted ? Brushes.White : Brushes.Black, 1.0), new Point(10, 10));
            dc.DrawImage(codeBitmap, new Rect(x, y, code.Width, code.Height));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapSource result = bitmap;
        if (inverted)
            result = Invert(bitmap);
        result.Freeze();
        return result;
    }

    private static BitmapSource Invert(BitmapSource source)
    {
        var pixels = Images.Bgra(source);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(255 - pixels[i]);
            pixels[i + 1] = (byte)(255 - pixels[i + 1]);
            pixels[i + 2] = (byte)(255 - pixels[i + 2]);
        }
        return Images.ToBitmap(pixels, source.PixelWidth, source.PixelHeight);
    }

    private static IReadOnlyList<BarcodeResult> Decode(BitmapSource image) =>
        BarcodeService.DecodeAsync(image).GetAwaiter().GetResult();

    [Fact]
    public void QrCode_IsFoundAmongText() => Sta.Run(() =>
    {
        var codes = Decode(Screen(BarcodeFormat.QR_CODE, "https://example.com/screencap?a=1"));
        var code = Assert.Single(codes);
        Assert.Equal("QR Code", code.Format);
        Assert.Equal("https://example.com/screencap?a=1", code.Text);
        Assert.True(code.IsLink);
    });

    [Fact]
    public void TinyQrCode_WithOnePixelModules_IsFound() => Sta.Run(() =>
        Assert.Equal("tiny", Assert.Single(Decode(Screen(BarcodeFormat.QR_CODE, "tiny", size: 0))).Text));

    [Fact]
    public void LightCodeOnDarkBackground_IsFound() => Sta.Run(() =>
    {
        var code = Assert.Single(Decode(Screen(BarcodeFormat.QR_CODE, "dark mode", inverted: true)));
        Assert.Equal("dark mode", code.Text);
        Assert.False(code.IsLink);
    });

    [Fact]
    public void CjkText_SurvivesTheRoundTrip() => Sta.Run(() =>
    {
        Assert.Equal("截圖工具", Assert.Single(Decode(Screen(BarcodeFormat.QR_CODE, "截圖工具"))).Text);
        Assert.Equal("中文 QR Code 測試，含標點！", Assert.Single(Decode(Screen(BarcodeFormat.QR_CODE, "中文 QR Code 測試，含標點！"))).Text);
    });

    [Fact]
    public void Barcode_IsFound() => Sta.Run(() =>
    {
        var code = Assert.Single(Decode(Screen(BarcodeFormat.CODE_128, "SC-2026-001")));
        Assert.Equal("CODE 128", code.Format);
        Assert.Equal("SC-2026-001", code.Text);
    });

    [Fact]
    public void ImageWithoutCodes_GivesNothing() => Sta.Run(() =>
        Assert.Empty(Decode(Images.Snapshot(300, 200, Colors.White).Bitmap)));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData(" http://example.com/a b ", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("WIFI:S:home;T:WPA;P:secret;;", false)]
    [InlineData("example.com", false)]
    public void IsLink_OnlyForWebAddresses(string text, bool link) =>
        Assert.Equal(link, new BarcodeResult("QR Code", text).IsLink);
}

public class NotifierTests
{
    [Fact]
    public void Preview_JoinsLines_AndShortensLongText()
    {
        Assert.Equal("first second", Notifier.Preview("first\r\n\r\n  second  "));
        string preview = Notifier.Preview(new string('a', 100));
        Assert.Equal(61, preview.Length);
        Assert.EndsWith("…", preview);
    }
}

public class MeasureTests
{
    [Theory]
    [InlineData(120, 0, "120 px")]
    [InlineData(0, -45, "45 px")]
    [InlineData(-120, 80, "144 px (120 × 80, 34°)")]
    [InlineData(30, 30, "42 px (30 × 30, 45°)")]
    [InlineData(0, 0, "0 px")]
    public void Label_ShowsTheDistance_AndForSlantedLinesWidthHeightAndAngle(double dx, double dy, string label) =>
        Assert.Equal(label, MeasureAnnotation.Label(new Vector(dx, dy)));

    private static MeasureAnnotation Measure(Point from, Point to, bool constrain = false)
    {
        var measure = new MeasureAnnotation(from, Colors.Red, 2, 1);
        measure.Update(to, constrain);
        return measure;
    }

    [Fact]
    public void Label_CanBeGrabbed_AndBoundsIncludeIt() => Sta.Run(() =>
    {
        var measure = Measure(new Point(100, 100), new Point(300, 100));
        Assert.True(measure.HitTest(new Point(200, 106), 0)); // on the label, beside the line
        Assert.True(measure.HitTest(new Point(120, 100), 1)); // on the line
        Assert.False(measure.HitTest(new Point(120, 130), 1));
        Assert.True(measure.Bounds.Contains(new Point(200, 106)));
        Assert.Equal([new Point(100, 100), new Point(300, 100)], measure.Handles);
    });

    [Fact]
    public void Shift_SnapsToStraightLines_AndAClickIsEmpty()
    {
        var measure = Measure(new Point(0, 0), new Point(200, 7), constrain: true);
        Assert.Equal(0, measure.End.Y, 6);
        Assert.Equal("200 px", MeasureAnnotation.Label(measure.End - measure.Start));
        Assert.True(Measure(new Point(5, 5), new Point(6, 6)).IsEmpty);
    }

    [Fact]
    public void Renders_LineTicksAndLabel() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(400, 200, Colors.White);
        var measure = Measure(new Point(50, 100), new Point(350, 100));
        var image = Compositor.Compose(snapshot, new Rect(0, 0, 400, 200), [measure]);
        Assert.True(Images.Distance(Images.PixelAt(image, 70, 100), Colors.Red) < 40);  // the line
        Assert.True(Images.Distance(Images.PixelAt(image, 50, 96), Colors.Red) < 40);   // the start tick
        Assert.True(Images.Distance(Images.PixelAt(image, 70, 90), Colors.White) < 10); // nothing above the line
        Assert.True(Images.Distance(Images.PixelAt(image, 176, 95), Colors.Red) < 40);  // label background, in its padding
    });

    [Fact]
    public void IsNotDashable() => Assert.False(Measure(new Point(0, 0), new Point(50, 0)).CanDash);
}
