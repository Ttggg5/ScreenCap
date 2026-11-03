using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class WindowCaptureTests
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public void PaintWindow_RoundsTheCorners()
    {
        var dest = Images.Solid(40, 40, Colors.White);
        var red = Images.Solid(20, 20, Colors.Red);
        ScreenSnapshot.PaintWindow(dest, 40 * 4, 40, 40, red, 20, 20, 10, 10, radius: 8);

        Assert.Equal(Colors.White, Images.PixelAt(dest, 40, 10, 10));  // the corner pixel is outside the curve
        Assert.Equal(Colors.Red, Images.PixelAt(dest, 40, 20, 20));    // the middle
        Assert.Equal(Colors.Red, Images.PixelAt(dest, 40, 10, 20));    // the middle of an edge is not cut
        var edge = Images.PixelAt(dest, 40, 12, 12);                   // on the curve: blended
        Assert.True(edge.G is > 0 and < 255, $"expected a blend, got {edge}");
        Assert.Equal(Colors.White, Images.PixelAt(dest, 40, 9, 20));   // outside the window
    }

    [Fact]
    public void PaintWindow_ClipsAtTheScreenEdges()
    {
        var dest = Images.Solid(30, 30, Colors.White);
        var red = Images.Solid(20, 20, Colors.Red);
        ScreenSnapshot.PaintWindow(dest, 30 * 4, 30, 30, red, 20, 20, -10, 25, radius: 0);

        Assert.Equal(Colors.Red, Images.PixelAt(dest, 30, 0, 25));
        Assert.Equal(Colors.Red, Images.PixelAt(dest, 30, 9, 29));
        Assert.Equal(Colors.White, Images.PixelAt(dest, 30, 10, 25));
        Assert.Equal(Colors.White, Images.PixelAt(dest, 30, 0, 24));
    }

    [Fact]
    public void Capture_GetsAWindowCoveredByAnother() => Sta.Run(() =>
    {
        static Window Make(Color color, bool topmost) => new()
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = topmost,
            Left = 200,
            Top = 200,
            Width = 160,
            Height = 120,
            Background = new SolidColorBrush(color),
        };
        var covered = Make(Colors.Red, topmost: false);
        var cover = Make(Colors.Blue, topmost: true);
        covered.Show();
        cover.Show();
        Pump();
        try
        {
            var image = WindowCapture.Capture(new WindowInteropHelper(covered).Handle);
            Assert.NotNull(image);
            int w = image.Bounds.Width, h = image.Bounds.Height;
            Assert.True(w > 0 && h > 0);
            Assert.True(Images.Distance(Images.PixelAt(image.Pixels, w, w / 2, h / 2), Colors.Red) < 10);
        }
        finally
        {
            cover.Close();
            covered.Close();
        }
    });

    [Fact]
    public void IsBlack_TellsAnEmptyDrawing()
    {
        Assert.True(WindowCapture.IsBlack(Images.Solid(4, 4, Colors.Black)));
        Assert.False(WindowCapture.IsBlack(Images.Solid(4, 4, Color.FromRgb(0, 0, 1))));
    }
}
