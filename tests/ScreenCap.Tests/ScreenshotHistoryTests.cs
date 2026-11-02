using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

[Collection(nameof(SettingsCollection))] // the history limit is a setting
public class ScreenshotHistoryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScreenCap.Tests.History." + Guid.NewGuid().ToString("N"));
    private readonly string _original = ScreenshotHistory.Folder;

    public ScreenshotHistoryTests()
    {
        TestSettings.Clear();
        ScreenshotHistory.Folder = _folder;
        ScreenshotHistory.Reset();
    }

    public void Dispose()
    {
        ScreenshotHistory.IndexTextAsync().Wait(); // let background recognition finish with this folder
        ScreenshotHistory.Folder = _original;
        ScreenshotHistory.Reset();
        TestSettings.Clear();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Adds and waits for its text to be recognized, which otherwise races with the test.</summary>
    private static async Task Add(BitmapSource image, string? title = null)
    {
        await ScreenshotHistory.AddAsync(image, title);
        await ScreenshotHistory.IndexTextAsync();
    }

    private static BitmapSource Image(int width, int height, Color color) => Images.ToBitmap(Images.Solid(width, height, color), width, height);

    [Fact]
    public async Task Add_KeepsPngNewestFirst()
    {
        await Add(Image(40, 30, Colors.Red), "Notepad");
        await Add(Image(20, 10, Colors.Blue), "  ");

        var entries = ScreenshotHistory.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].Title); // blank titles are not kept
        Assert.Equal((20, 10), (entries[0].Width, entries[0].Height));
        Assert.Equal("Notepad", entries[1].Title);
        Assert.True(entries[0].Time >= entries[1].Time);

        var image = ScreenshotHistory.LoadImage(entries[1]);
        Assert.Equal((40, 30), (image.PixelWidth, image.PixelHeight));
        var pixels = Images.Bgra(image);
        Assert.Equal((0, 0, 255), (pixels[0], pixels[1], pixels[2]));
    }

    [Fact]
    public async Task Add_UnfrozenImage()
    {
        var image = new WriteableBitmap(Image(8, 8, Colors.Green));
        await Add(image);
        Assert.Single(ScreenshotHistory.Entries);
    }

    [Fact]
    public async Task Index_SurvivesRestart()
    {
        await Add(Image(12, 9, Colors.Red), "Chrome");
        ScreenshotHistory.Reset();

        var entry = Assert.Single(ScreenshotHistory.Entries);
        Assert.Equal("Chrome", entry.Title);
        Assert.Equal((12, 9), (entry.Width, entry.Height));
    }

    [Fact]
    public async Task MissingIndex_RebuiltFromFiles()
    {
        await Add(Image(12, 9, Colors.Red), "Chrome");
        File.Delete(Path.Combine(_folder, "index.json"));
        ScreenshotHistory.Reset();

        var entry = Assert.Single(ScreenshotHistory.Entries);
        Assert.Null(entry.Title); // only the index knows it
        Assert.Equal((12, 9), (entry.Width, entry.Height));
    }

    [Fact]
    public async Task DeletedFile_DroppedFromIndex()
    {
        await Add(Image(5, 5, Colors.Red));
        File.Delete(ScreenshotHistory.PathOf(ScreenshotHistory.Entries[0]));
        ScreenshotHistory.Reset();
        Assert.Empty(ScreenshotHistory.Entries);
    }

    [Fact]
    public async Task Limit_DeletesOldest()
    {
        AppSettings.HistoryLimit = 2;
        for (int i = 0; i < 3; i++)
            await Add(Image(5, 5, Colors.Red), $"#{i}");

        Assert.Equal(["#2", "#1"], ScreenshotHistory.Entries.Select(e => e.Title));
        Assert.Equal(2, Directory.GetFiles(_folder, "*.png").Length);

        AppSettings.HistoryLimit = 1;
        ScreenshotHistory.Trim();
        Assert.Equal("#2", Assert.Single(ScreenshotHistory.Entries).Title);
        Assert.Single(Directory.GetFiles(_folder, "*.png"));
    }

    [Fact]
    public async Task Disabled_KeepsNothing()
    {
        AppSettings.HistoryLimit = 0;
        await Add(Image(5, 5, Colors.Red));
        Assert.Empty(ScreenshotHistory.Entries);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task DeleteAndClear_RemoveFiles()
    {
        for (int i = 0; i < 3; i++)
            await Add(Image(5, 5, Colors.Red), $"#{i}");
        await ScreenshotHistory.IndexTextAsync(); // its own change notifications would be counted too
        int changes = 0;
        void Count() => changes++;
        ScreenshotHistory.Changed += Count;
        try
        {
            ScreenshotHistory.Delete(ScreenshotHistory.Entries[1]);
            Assert.Equal(["#2", "#0"], ScreenshotHistory.Entries.Select(e => e.Title));
            Assert.Equal(2, Directory.GetFiles(_folder, "*.png").Length);

            ScreenshotHistory.Clear();
            Assert.Empty(ScreenshotHistory.Entries);
            Assert.Empty(Directory.GetFiles(_folder, "*.png"));
            Assert.Equal(2, changes);
        }
        finally
        {
            ScreenshotHistory.Changed -= Count;
        }
    }

    [Theory]
    [InlineData(1000, 300, 220, 66)] // wide: fits the width
    [InlineData(300, 3000, 13, 132)] // a tall scroll capture: fits the height
    [InlineData(100, 50, 100, 50)] // small: not enlarged
    public async Task Thumbnail_FitsBox(int width, int height, int thumbWidth, int thumbHeight)
    {
        await Add(Image(width, height, Colors.Red));
        var thumbnail = ScreenshotHistory.LoadThumbnail(ScreenshotHistory.Entries[0], 220, 132);
        Assert.Equal((thumbWidth, thumbHeight), (thumbnail.PixelWidth, thumbnail.PixelHeight));
    }

    private static HistoryEntry Entry(string? title, string? text) => new("a.png", DateTime.Now, title, 1, 1, text);

    [Fact]
    public void Matches_TitleOrText_EveryWord()
    {
        var entry = Entry("Visual Studio", "Build failed\r\n找不到 檔案");
        Assert.True(ScreenshotHistory.Matches(entry, ""));
        Assert.True(ScreenshotHistory.Matches(entry, "studio"));
        Assert.True(ScreenshotHistory.Matches(entry, "BUILD failed"));
        Assert.True(ScreenshotHistory.Matches(entry, "visual failed")); // one word in each
        Assert.True(ScreenshotHistory.Matches(entry, "找不到檔案")); // OCR spaces between CJK are ignored
        Assert.False(ScreenshotHistory.Matches(entry, "build passed"));
        Assert.False(ScreenshotHistory.Matches(Entry(null, null), "x"));
    }

    [Fact]
    public void Snippet_AroundTheMatch()
    {
        string text = string.Concat(Enumerable.Repeat("lorem ", 20)) + "\nERROR 42 happened\n" +string.Concat(Enumerable.Repeat("ipsum ", 20));
        string snippet = ScreenshotHistory.Snippet(Entry(null, text), "error", 30)!;
        Assert.Contains("ERROR 42 happened", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.DoesNotContain("\n", snippet);
        Assert.Equal("short", ScreenshotHistory.Snippet(Entry(null, "short"), "sho"));
        Assert.Null(ScreenshotHistory.Snippet(Entry("error", "nothing"), "error")); // only in the title
    }

    [Fact]
    public async Task Index_RecognizesTextForSearch()
    {
        if (OcrService.DefaultLanguage == null)
            return; // no OCR language on this machine
        await Add(RenderText("Hello ScreenCap 2026"));
        await Add(Image(40, 40, Colors.White));
        await ScreenshotHistory.IndexTextAsync();

        var entries = ScreenshotHistory.Entries;
        Assert.All(entries, e => Assert.NotNull(e.Text));
        Assert.Equal("", entries[0].Text); // blank image: recognized, nothing found
        Assert.True(ScreenshotHistory.Matches(entries[1], "screencap 2026"));

        ScreenshotHistory.Reset();
        Assert.True(ScreenshotHistory.Matches(ScreenshotHistory.Entries[1], "screencap")); // kept in the index
    }

    private static BitmapSource RenderText(string text)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 32, Brushes.Black, 1.0);
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

    [Fact]
    public void Limit_DefaultAndRoundTrip()
    {
        Assert.Equal(AppSettings.DefaultHistoryLimit, AppSettings.HistoryLimit);
        AppSettings.HistoryLimit = 0;
        Assert.Equal(0, AppSettings.HistoryLimit);
        Assert.Equal(AppSettings.DefaultHistoryHotkey, AppSettings.HistoryHotkey);
    }
}
