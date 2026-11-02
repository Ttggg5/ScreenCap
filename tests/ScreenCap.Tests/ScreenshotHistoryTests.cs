using System.IO;
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
        ScreenshotHistory.Folder = _original;
        ScreenshotHistory.Reset();
        TestSettings.Clear();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static BitmapSource Image(int width, int height, Color color) => Images.ToBitmap(Images.Solid(width, height, color), width, height);

    [Fact]
    public async Task Add_KeepsPngNewestFirst()
    {
        await ScreenshotHistory.AddAsync(Image(40, 30, Colors.Red), "Notepad");
        await ScreenshotHistory.AddAsync(Image(20, 10, Colors.Blue), "  ");

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
        await ScreenshotHistory.AddAsync(image);
        Assert.Single(ScreenshotHistory.Entries);
    }

    [Fact]
    public async Task Index_SurvivesRestart()
    {
        await ScreenshotHistory.AddAsync(Image(12, 9, Colors.Red), "Chrome");
        ScreenshotHistory.Reset();

        var entry = Assert.Single(ScreenshotHistory.Entries);
        Assert.Equal("Chrome", entry.Title);
        Assert.Equal((12, 9), (entry.Width, entry.Height));
    }

    [Fact]
    public async Task MissingIndex_RebuiltFromFiles()
    {
        await ScreenshotHistory.AddAsync(Image(12, 9, Colors.Red), "Chrome");
        File.Delete(Path.Combine(_folder, "index.json"));
        ScreenshotHistory.Reset();

        var entry = Assert.Single(ScreenshotHistory.Entries);
        Assert.Null(entry.Title); // only the index knows it
        Assert.Equal((12, 9), (entry.Width, entry.Height));
    }

    [Fact]
    public async Task DeletedFile_DroppedFromIndex()
    {
        await ScreenshotHistory.AddAsync(Image(5, 5, Colors.Red));
        File.Delete(ScreenshotHistory.PathOf(ScreenshotHistory.Entries[0]));
        ScreenshotHistory.Reset();
        Assert.Empty(ScreenshotHistory.Entries);
    }

    [Fact]
    public async Task Limit_DeletesOldest()
    {
        AppSettings.HistoryLimit = 2;
        for (int i = 0; i < 3; i++)
            await ScreenshotHistory.AddAsync(Image(5, 5, Colors.Red), $"#{i}");

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
        await ScreenshotHistory.AddAsync(Image(5, 5, Colors.Red));
        Assert.Empty(ScreenshotHistory.Entries);
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public async Task DeleteAndClear_RemoveFiles()
    {
        for (int i = 0; i < 3; i++)
            await ScreenshotHistory.AddAsync(Image(5, 5, Colors.Red), $"#{i}");
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
        await ScreenshotHistory.AddAsync(Image(width, height, Colors.Red));
        var thumbnail = ScreenshotHistory.LoadThumbnail(ScreenshotHistory.Entries[0], 220, 132);
        Assert.Equal((thumbWidth, thumbHeight), (thumbnail.PixelWidth, thumbnail.PixelHeight));
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
