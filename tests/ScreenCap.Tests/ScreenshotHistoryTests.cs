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

    private static Task Add(BitmapSource image) => ScreenshotHistory.AddAsync(image);

    /// <summary>The widths, which tell the screenshots of a test apart.</summary>
    private static int[] Widths() => ScreenshotHistory.Entries.Select(e => e.Width).ToArray();

    private static BitmapSource Image(int width, int height, Color color) => Images.ToBitmap(Images.Solid(width, height, color), width, height);

    [Fact]
    public async Task Add_KeepsPngNewestFirst()
    {
        await Add(Image(40, 30, Colors.Red));
        await Add(Image(20, 10, Colors.Blue));

        var entries = ScreenshotHistory.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal((20, 10), (entries[0].Width, entries[0].Height));
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
    public async Task Restart_ReadsTheFolder()
    {
        for (int i = 1; i <= 3; i++)
            await Add(Image(i, 9, Colors.Red));
        var before = ScreenshotHistory.Entries;
        ScreenshotHistory.Reset();

        var after = ScreenshotHistory.Entries;
        Assert.Equal([3, 2, 1], after.Select(e => e.Width));
        Assert.All(after, e => Assert.Equal(9, e.Height));
        // The time comes from the file name, to the millisecond.
        Assert.Equal(before.Select(e => e.Time.Ticks / TimeSpan.TicksPerMillisecond), after.Select(e => e.Time.Ticks / TimeSpan.TicksPerMillisecond));
    }

    [Fact]
    public async Task SameMillisecond_NewestFirst()
    {
        await Add(Image(1, 1, Colors.Red));
        string first = ScreenshotHistory.PathOf(ScreenshotHistory.Entries[0]);
        string name = Path.GetFileNameWithoutExtension(first);
        for (int n = 2; n <= 10; n++)
            File.Copy(first, Path.Combine(_folder, $"{name}-{n}.png"));
        ScreenshotHistory.Reset();
        Assert.Equal([.. Enumerable.Range(2, 9).Reverse().Select(n => $"{name}-{n}.png"), $"{name}.png"],
            ScreenshotHistory.Entries.Select(e => e.File));
    }

    [Fact]
    public async Task OldIndex_Deleted()
    {
        await Add(Image(12, 9, Colors.Red));
        File.WriteAllText(Path.Combine(_folder, "index.json"), "[]");
        ScreenshotHistory.Reset();

        Assert.Single(ScreenshotHistory.Entries);
        Assert.False(File.Exists(Path.Combine(_folder, "index.json")));
    }

    [Fact]
    public async Task DeletedFile_Dropped()
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
        for (int i = 1; i <= 3; i++)
            await Add(Image(i, 5, Colors.Red));

        Assert.Equal([3, 2], Widths());
        Assert.Equal(2, Directory.GetFiles(_folder, "*.png").Length);

        AppSettings.HistoryLimit = 1;
        ScreenshotHistory.Trim();
        Assert.Equal([3], Widths());
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
    public async Task Delete_RemovesFile()
    {
        for (int i = 1; i <= 3; i++)
            await Add(Image(i, 5, Colors.Red));
        int changes = 0;
        void Count() => changes++;
        ScreenshotHistory.Changed += Count;
        try
        {
            ScreenshotHistory.Delete(ScreenshotHistory.Entries[1]);
            Assert.Equal([3, 1], Widths());
            Assert.Equal(2, Directory.GetFiles(_folder, "*.png").Length);
            Assert.Equal(1, changes);
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

    [Fact]
    public void Limit_DefaultAndRoundTrip()
    {
        Assert.Equal(AppSettings.DefaultHistoryLimit, AppSettings.HistoryLimit);
        AppSettings.HistoryLimit = 0;
        Assert.Equal(0, AppSettings.HistoryLimit);
        Assert.Equal(AppSettings.DefaultHistoryHotkey, AppSettings.HistoryHotkey);
    }
}
