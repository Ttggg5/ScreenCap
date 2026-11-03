using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenCap.Annotations;
using ScreenCap.Capture;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class OverlayRenderingTests
{
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>What the overlay shows, rendered from its content.</summary>
    private static BitmapSource Render(OverlayWindow overlay, int width, int height)
    {
        var root = (FrameworkElement)overlay.Content;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }

    [Fact]
    public void DimsOutside_ShowsInside_ClipsAnnotations() => Sta.Run(() =>
    {
        var snap = Images.Snapshot(400, 300, Colors.White);
        // A stamp straddling the right edge of the selection: only its left half may show.
        var stamp = new StampAnnotation(new Point(300, 150), Stamp.BuiltIn[0], Colors.Red, 48, 1);
        var overlay = new OverlayWindow(snap, WindowDetector.Snapshot(snap), OverlayMode.Capture, new Rect(100, 100, 200, 100), [stamp]);
        overlay.Show();
        Pump();
        var image = Render(overlay, 400, 300);
        overlay.Close();

        Assert.True(Images.Distance(Images.PixelAt(image, 50, 50), Color.FromRgb(0x7F, 0x7F, 0x7F)) < 10);  // dimmed
        Assert.True(Images.Distance(Images.PixelAt(image, 150, 130), Colors.White) < 10);                   // selection, undimmed
        Assert.True(Images.Distance(Images.PixelAt(image, 282, 145), Colors.Red) < 60);                     // the stamp inside
        Assert.True(Images.Distance(Images.PixelAt(image, 315, 145), Color.FromRgb(0x7F, 0x7F, 0x7F)) < 10); // clipped outside
    });

    [Fact]
    public void FollowsTheSelection() => Sta.Run(() =>
    {
        var snap = Images.Snapshot(400, 300, Colors.White);
        var overlay = new OverlayWindow(snap, WindowDetector.Snapshot(snap), OverlayMode.Capture, new Rect(20, 20, 100, 100));
        overlay.Show();
        Pump();
        const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(OverlayWindow).GetField("_sel", any)!.SetValue(overlay, new Rect(250, 150, 100, 100));
        typeof(OverlayWindow).GetMethod("Refresh", any, Type.EmptyTypes)!.Invoke(overlay, null);
        Pump();
        var image = Render(overlay, 400, 300);
        overlay.Close();

        Assert.True(Images.Distance(Images.PixelAt(image, 60, 60), Color.FromRgb(0x7F, 0x7F, 0x7F)) < 10); // the old area is dimmed again
        Assert.True(Images.Distance(Images.PixelAt(image, 300, 200), Colors.White) < 10);                 // the new area is not
    });

    [Fact]
    public void ChoosingAnotherSelection_PutsBackTheScreenWithoutTheCapturedWindow() => Sta.Run(() =>
    {
        var original = Images.Snapshot(400, 300, Colors.White);
        var red = Images.ToBitmap(Images.Solid(100, 100, Colors.Red), 100, 100);
        var withWindow = original.WithImage(red, 50, 50); // as if the window there was captured with W
        var overlay = new OverlayWindow(withWindow, WindowDetector.Snapshot(original), OverlayMode.Capture, new Rect(50, 50, 100, 100),
            original: original);
        overlay.Show();
        Pump();
        Assert.Same(withWindow, overlay.Snapshot);

        typeof(OverlayWindow).GetMethod("ResetSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(overlay, null);
        Pump();
        var image = Render(overlay, 400, 300);
        overlay.Close();

        Assert.Same(original, overlay.Snapshot);
        Assert.Same(original, overlay.OriginalSnapshot);
        // White, dimmed or not (the selection detected under the real cursor may cover it), but no longer the red window.
        var pixel = Images.PixelAt(image, 100, 100);
        Assert.True(pixel.R == pixel.G && pixel.G == pixel.B, $"expected gray or white, got {pixel}");
    });
}
