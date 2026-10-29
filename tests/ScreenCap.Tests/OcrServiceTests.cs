using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;
using ScreenCap.Tests.Support;
using Language = Windows.Globalization.Language;

namespace ScreenCap.Tests;

public class OcrServiceTests
{
    /// <summary>Lays out words left to right; a gap of 2 is letter spacing, 10 is a real space (height 20).</summary>
    private static string Join(params (string Text, int GapBefore)[] words)
    {
        var boxes = new List<OcrWordBox>();
        double x = 0;
        foreach (var (text, gap) in words)
        {
            x += gap;
            double width = text.Length * 16;
            boxes.Add(new OcrWordBox(text, x, x + width, 20));
            x += width;
        }
        return OcrService.JoinWords(boxes);
    }

    [Fact]
    public void JoinWords_LatinWords_AlwaysGetSpaces() =>
        Assert.Equal("Hello world", Join(("Hello", 0), ("world", 2)));

    [Fact]
    public void JoinWords_CjkCharacters_AreJoined() =>
        Assert.Equal("截圖工具", Join(("截", 0), ("圖", 2), ("工", 2), ("具", 2)));

    [Fact]
    public void JoinWords_CjkNextToLatin_KeepsOnlyRealSpaces()
    {
        Assert.Equal("ScreenCap 文字", Join(("ScreenCap", 0), ("文", 10), ("字", 2)));
        Assert.Equal("使用Windows內建", Join(("使", 0), ("用", 2), ("Windows", 2), ("內", 2), ("建", 2)));
        Assert.Equal("版本2.0，OK", Join(("版", 0), ("本", 2), ("2.0", 2), ("，", 2), ("OK", 2)));
    }

    [Fact]
    public void JoinWords_SkipsEmptyWords() =>
        Assert.Equal("a b", Join(("", 0), ("a", 0), ("", 2), ("b", 2)));
    /// <summary>Renders black text on white, large enough for reliable recognition.</summary>
    private static BitmapSource Render(string text)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft JhengHei UI"), 32, Brushes.Black, 1.0);
        int w = (int)formatted.Width + 40, h = (int)formatted.Height + 40;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawText(formatted, new Point(20, 20));
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Language? Installed(string tagPrefix) =>
        OcrService.Languages.FirstOrDefault(l => l.LanguageTag.StartsWith(tagPrefix, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Recognizes_EnglishText() => Sta.Run(() =>
    {
        if (Installed("en") is not { } english)
            return; // no English OCR on this machine
        string text = OcrService.RecognizeAsync(Render("Hello ScreenCap 2026"), english).GetAwaiter().GetResult();
        Assert.Equal("Hello ScreenCap 2026", text);
    });

    [Fact]
    public void Recognizes_TraditionalChinese_WithoutSpacesBetweenCharacters() => Sta.Run(() =>
    {
        if (Installed("zh-Hant") is not { } chinese)
            return; // no Traditional Chinese OCR on this machine
        string text = OcrService.RecognizeAsync(Render("截圖工具文字辨識"), chinese).GetAwaiter().GetResult();
        Assert.Equal("截圖工具文字辨識", text);
    });

    [Fact]
    public void Recognizes_MixedChineseAndEnglish_KeepingTheSpace() => Sta.Run(() =>
    {
        if (Installed("zh-Hant") is not { } chinese)
            return;
        string text = OcrService.RecognizeAsync(Render("ScreenCap 截圖工具"), chinese).GetAwaiter().GetResult();
        Assert.Equal("ScreenCap 截圖工具", text);
    });

    [Fact]
    public void Recognizes_MultipleLines() => Sta.Run(() =>
    {
        if (Installed("en") is not { } english)
            return;
        string text = OcrService.RecognizeAsync(Render("First line\nSecond line"), english).GetAwaiter().GetResult();
        Assert.Equal(["First line", "Second line"], text.Split(Environment.NewLine));
    });

    [Fact]
    public void BlankImage_GivesEmptyText() => Sta.Run(() =>
    {
        string text = OcrService.RecognizeAsync(Images.Snapshot(300, 100, Colors.White).Bitmap).GetAwaiter().GetResult();
        Assert.Equal("", text);
    });
}
