using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Native;
using ScreenCap.Tests.Support;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Tests;

public class PolylineTests
{
    private static PolylineAnnotation Draw(params Point[] points)
    {
        var line = new PolylineAnnotation(points[0], Colors.Blue, 4, 1);
        foreach (var p in points.Skip(1))
        {
            line.Update(p);
            line.AddPoint();
        }
        return line;
    }

    [Fact]
    public void Corners_AreAddedOneByOne_AndFollowTheRubberBand() => Sta.Run(() =>
    {
        var line = Draw(new Point(0, 0), new Point(50, 0));
        line.Update(new Point(50, 40)); // rubber band, not a corner yet
        Assert.Equal(2, line.Points.Count);
        Assert.True(line.Bounds.Bottom >= 40);

        line.AddPoint();
        line.Finish();
        Assert.Equal([new Point(0, 0), new Point(50, 0), new Point(50, 40)], line.Points);
        Assert.Null(line.Preview);
        Assert.Equal(3, line.Handles.Length);
    });

    [Fact]
    public void ClickingTheSameSpotTwice_DoesNotAddADuplicateCorner()
    {
        var line = Draw(new Point(0, 0), new Point(30, 0));
        line.AddPoint(); // e.g. the second click of a double-click
        Assert.Equal(2, line.Points.Count);
    }

    [Fact]
    public void ASingleClick_IsEmpty_AndBackspaceRemovesTheLastCorner()
    {
        var single = new PolylineAnnotation(new Point(5, 5), Colors.Blue, 4, 1);
        single.Finish();
        Assert.True(single.IsEmpty);

        var line = Draw(new Point(0, 0), new Point(10, 0), new Point(10, 10));
        line.RemoveLastPoint();
        Assert.Equal(2, line.Points.Count);
        line.RemoveLastPoint();
        line.RemoveLastPoint(); // never removes the first point
        Assert.Single(line.Points);
    }

    [Fact]
    public void Closing_JoinsBackToTheStart() => Sta.Run(() =>
    {
        var open = Draw(new Point(10, 10), new Point(90, 10), new Point(90, 90));
        var closed = Draw(new Point(10, 10), new Point(90, 10), new Point(90, 90));
        open.Finish();
        closed.Finish(close: true);
        Assert.True(closed.Closed);
        Assert.True(closed.HitTest(new Point(50, 50), 2));  // on the closing diagonal
        Assert.False(open.HitTest(new Point(50, 50), 2));

        var two = Draw(new Point(0, 0), new Point(10, 0));
        two.Finish(close: true);
        Assert.False(two.Closed); // needs three corners to be a shape
    });

    [Fact]
    public void Shift_SnapsTheNextSegment()
    {
        var line = Draw(new Point(0, 0), new Point(50, 0));
        line.Update(new Point(90, 4), constrain: true);
        line.AddPoint();
        Assert.Equal(0, line.Points[^1].Y, 3);
    }

    [Fact]
    public void Corners_CanBeDraggedAndTheShapeMoved_IndependentOfClones()
    {
        var line = Draw(new Point(0, 0), new Point(50, 0), new Point(50, 50));
        line.Finish();
        var origin = (PolylineAnnotation)line.Clone();
        line.DragHandleFrom(origin, 1, new Vector(10, -10));
        Assert.Equal(new Point(60, -10), line.Points[1]);
        Assert.Equal(new Point(50, 0), origin.Points[1]);

        line.MoveFrom(origin, new Vector(5, 5));
        Assert.Equal(new Point(5, 5), line.Points[0]);
        Assert.True(line.CanDash);
    }
}

public class KeystrokeTests
{
    [Theory]
    [InlineData(Key.C, ModifierKeys.Control, "Ctrl + C")]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl + Shift + S")]
    [InlineData(Key.Return, ModifierKeys.None, "Enter")]
    [InlineData(Key.Tab, ModifierKeys.Alt, "Alt + Tab")]
    [InlineData(Key.Left, ModifierKeys.None, "←")]
    [InlineData(Key.D1, ModifierKeys.Windows, "Win + 1")]
    public void Describe_NamesTheCombination(Key key, ModifierKeys modifiers, string expected) =>
        Assert.Equal(expected, KeystrokeMonitor.Describe(key, modifiers));

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LWin)]
    public void Describe_IgnoresModifiersOnTheirOwn(Key key) => Assert.Null(KeystrokeMonitor.Describe(key, ModifierKeys.Control));

    /// <summary>Draws into a 300×120 white GDI bitmap and returns its BGRA pixels.</summary>
    private static byte[] Paint(Action<IntPtr> draw)
    {
        IntPtr screen = GetDC(IntPtr.Zero), dc = CreateCompatibleDC(screen), bitmap = CreateCompatibleBitmap(screen, 300, 120);
        IntPtr old = SelectObject(dc, bitmap);
        try
        {
            BitBlt(dc, 0, 0, 300, 120, IntPtr.Zero, 0, 0, 0x00FF0062); // WHITENESS
            draw(dc);
            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = 300, biHeight = -120, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[300 * 120 * 4];
            SelectObject(dc, old);
            GetDIBits(dc, bitmap, 0, 120, pixels, ref header, 0);
            return pixels;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(dc);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [Fact]
    public void Label_IsDrawnAtTheBottomCenter_ForAShortTime()
    {
        using var painter = new KeyPainter(1);
        var shown = Paint(dc => painter.Draw(dc, 300, 120, "Ctrl + C", pressedAt: 1000, now: 1200));
        var gone = Paint(dc => painter.Draw(dc, 300, 120, "Ctrl + C", pressedAt: 1000, now: 3000));

        bool Dark(byte[] px, int x, int y) => px[(y * 300 + x) * 4] < 0x40;
        Assert.True(Dark(shown, 150, 120 - 24 - 6));  // inside the label, above the bottom margin
        Assert.False(Dark(shown, 150, 10));            // top stays clear
        Assert.False(Dark(shown, 5, 90));              // centered, not at the left edge
        Assert.Contains(Enumerable.Range(100, 100), x => shown[((120 - 24 - 14) * 300 + x) * 4] > 0xC0); // white text inside
        Assert.DoesNotContain(Enumerable.Range(0, 300 * 120), i => gone[i * 4] < 0x40);
    }
}
