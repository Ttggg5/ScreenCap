using System.Windows;
using System.Windows.Media;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class StoredSnapshotTests
{
    [Fact]
    public void Restore_GivesBackTheSameScreen_AfterCompressingIt() => Sta.Run(() =>
    {
        // Like a screen: mostly flat areas, some detail.
        var pixels = Images.Solid(640, 360, Colors.WhiteSmoke);
        Images.FillRect(pixels, 640, new Int32Rect(0, 0, 640, 30), Colors.SteelBlue);
        var noise = new byte[200 * 4 * 100];
        new Random(2).NextBytes(noise);
        for (int y = 0; y < 100; y++)
            Buffer.BlockCopy(noise, y * 800, pixels, ((100 + y) * 640 + 50) * 4, 800);
        var snapshot = new ScreenSnapshot(-1920, 0, 640, 360, pixels, 640 * 4, Images.ToBitmap(pixels, 640, 360));

        var stored = new StoredSnapshot(snapshot);
        stored.Compressing.Wait();
        Assert.InRange(stored.CompressedLength, 1, pixels.Length / 4); // far smaller than the pixels

        var restored = stored.Restore();
        Assert.NotSame(snapshot, restored);
        Assert.Equal((-1920, 0, 640, 360), (restored.Left, restored.Top, restored.Width, restored.Height));
        Assert.Equal(pixels, restored.Pixels);
        Assert.Equal(Images.PixelAt(snapshot.Bitmap, 120, 150), Images.PixelAt(restored.Bitmap, 120, 150));
    });

    [Fact]
    public void Detached_IsATinyStandIn()
    {
        Assert.Equal((1, 1), (ScreenSnapshot.Detached.Width, ScreenSnapshot.Detached.Height));
    }
}
