using System.IO;
using System.Windows;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public sealed class Mp4EncoderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ScreenCap.Tests_{Guid.NewGuid():N}.mp4");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    /// <summary>White frames with a fixed blue square at the top-left and a red box moving 3 px per frame.</summary>
    private void EncodeMovingBox(int width, int height, int frames, int fps)
    {
        using var encoder = new Mp4Encoder(_path, width, height, fps);
        for (int i = 0; i < frames; i++)
        {
            var pixels = Images.Solid(width, height, Colors.White);
            Images.FillRect(pixels, width, new Int32Rect(0, 0, 20, 20), Colors.Blue);
            Images.FillRect(pixels, width, new Int32Rect(10 + i * 3, 100, 60, 40), Colors.Red);
            encoder.AddFrame(pixels, TimeSpan.FromSeconds((double)i / fps));
        }
        encoder.Finish(TimeSpan.FromSeconds((double)frames / fps));
    }

    [Fact]
    public void Encodes_EvenSize_AllFrames_WithTimestamps()
    {
        EncodeMovingBox(401, 251, 30, 30);
        var video = Mp4Reader.Read(_path);

        Assert.Equal((400, 250), (video.Width, video.Height)); // odd column/row dropped for H.264
        Assert.Equal(30, video.Frames.Count);
        // The decoder may shift all timestamps by a constant (decoding delay), so check the spacing.
        Assert.Equal(29 / 30.0, (video.Frames[^1].Time - video.Frames[0].Time).TotalSeconds, 2);
        Assert.InRange(video.Frames[0].Time.TotalSeconds, 0, 0.1);
        Assert.True(video.Frames.Zip(video.Frames.Skip(1)).All(p => p.Second.Time > p.First.Time));
    }

    [Fact]
    public void Preserves_ColorsOrientationAndMotion()
    {
        EncodeMovingBox(400, 250, 30, 30);
        var video = Mp4Reader.Read(_path);
        var frame = video.Frames[20].Bgra;

        Assert.True(Images.Distance(Colors.Blue, Images.PixelAt(frame, 400, 10, 10)) < 12);  // top-left, so not upside down
        Assert.True(Images.Distance(Colors.White, Images.PixelAt(frame, 400, 10, 240)) < 12);
        Assert.True(Images.Distance(Colors.White, Images.PixelAt(frame, 400, 300, 30)) < 12); // full range, not washed out

        int redLeft = Enumerable.Range(0, 400).First(x => Images.PixelAt(frame, 400, x, 120) is { R: > 180, G: < 80 });
        Assert.InRange(redLeft, 10 + 20 * 3 - 2, 10 + 20 * 3 + 2);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(100, 1)]
    public void TooSmall_Throws(int width, int height) =>
        Assert.Throws<ArgumentException>(() => new Mp4Encoder(_path, width, height, 30));
}
