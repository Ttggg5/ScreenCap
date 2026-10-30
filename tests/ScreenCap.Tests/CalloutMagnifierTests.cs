using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class CalloutTests
{
    private static CalloutAnnotation Callout(bool filled = false) =>
        new("Hello", new Point(100, 20), new Point(30, 90), Colors.Blue, 20, 1) { Background = filled };

    [Fact]
    public void Bubble_CoversTheTextAndTheTailReachesTheTip() => Sta.Run(() =>
    {
        var callout = Callout();
        Assert.True(callout.HitTest(new Point(105, 30), 0));   // in the box
        Assert.True(callout.HitTest(new Point(32, 88), 1));    // at the tip
        Assert.False(callout.HitTest(new Point(30, 20), 0));   // beside the tail
        Assert.True(callout.Bounds.Contains(new Point(30, 90)));
        Assert.Equal([new Point(30, 90)], callout.Handles);
    });

    [Fact]
    public void Tip_CanBeDraggedOnItsOwn_AndMovesWithTheBubble() => Sta.Run(() =>
    {
        var callout = Callout();
        var origin = (CalloutAnnotation)callout.Clone();
        callout.DragHandleFrom(origin, 0, new Vector(-10, 5));
        Assert.Equal(new Point(20, 95), callout.Tip);
        Assert.Equal(new Point(100, 20), callout.Position);

        callout.MoveFrom(origin, new Vector(10, 10));
        Assert.Equal(new Point(40, 100), callout.Tip);
        Assert.Equal(new Point(110, 30), callout.Position);
    });

    [Fact]
    public void Filled_UsesTheColor_OtherwiseWhiteWithOutline() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(250, 120, Colors.Gray);
        Color Inside(CalloutAnnotation c)
        {
            var box = c.Bounds;
            return Images.PixelAt(Compositor.Compose(snapshot, new Rect(0, 0, 250, 120), [c]), (int)box.Right - 6, (int)box.Top + 6);
        }
        Assert.Equal(Colors.Blue, Inside(Callout(filled: true)));
        Assert.Equal(Colors.White, Inside(Callout(filled: false)));
    });

    [Fact]
    public void Clone_KeepsTheTipAndTextStyle()
    {
        var callout = Callout();
        callout.Style = callout.Style with { Bold = true, FontName = "Consolas" };
        var clone = (CalloutAnnotation)callout.Clone();
        Assert.Equal(callout.Tip, clone.Tip);
        Assert.Equal(callout.Style, clone.Style);
    }
}

public class MagnifierAnnotationTests
{
    /// <summary>White screen with a red 10×10 square at (20, 20).</summary>
    private static ScreenSnapshot Screen(int width = 300, int height = 150)
    {
        var pixels = Images.Solid(width, height, Colors.White);
        Images.FillRect(pixels, width, new Int32Rect(20, 20, 10, 10), Colors.Red);
        return new ScreenSnapshot(0, 0, width, height, pixels, width * 4, Images.ToBitmap(pixels, width, height));
    }

    private static MagnifierAnnotation Magnify(ScreenSnapshot screen, Point from, Point to, double zoom = 2)
    {
        var m = new MagnifierAnnotation(from, screen, Colors.Blue, zoom, 1);
        m.Update(to);
        return m;
    }

    [Fact]
    public void View_IsTheSourceEnlarged_OnTheRight()
    {
        var m = Magnify(Screen(), new Point(10, 10), new Point(40, 40));
        var view = m.View;
        Assert.Equal(60, view.Width);
        Assert.Equal(60, view.Height);
        Assert.True(view.Left > m.Source.Right);
        Assert.Equal(30, view.Y + view.Height / 2, 3); // 60px view centered on the source would start at -5; moved down to 0
    }

    [Fact]
    public void View_StaysOnScreen_NearTheTopEdge()
    {
        var m = Magnify(Screen(), new Point(10, 10), new Point(40, 40), zoom: 3); // 90px view around y=25
        Assert.Equal(0, m.View.Top, 3);
    }

    [Fact]
    public void View_FlipsLeft_NearTheRightEdge()
    {
        var m = Magnify(Screen(), new Point(240, 10), new Point(270, 40));
        Assert.True(m.View.Right < m.Source.Left);
    }

    [Fact]
    public void ViewHandle_MovesOnlyTheView_AndZoomFollowsSize()
    {
        var m = Magnify(Screen(), new Point(10, 10), new Point(40, 40));
        var origin = (MagnifierAnnotation)m.Clone();
        var before = m.View;
        m.DragHandleFrom(origin, MagnifierAnnotation.ViewHandle, new Vector(0, 50));
        Assert.Equal(before.Y + 50, m.View.Y, 3);
        Assert.Equal(origin.Source, m.Source);

        m.Size = 4;
        Assert.Equal(120, m.View.Width);
        Assert.Equal(9, m.Handles.Length);
    }

    [Fact]
    public void Render_ShowsTheEnlargedPixels() => Sta.Run(() =>
    {
        var screen = Screen();
        var m = Magnify(screen, new Point(10, 10), new Point(40, 40), zoom: 3);
        var image = Compositor.Compose(screen, new Rect(0, 0, 300, 150), [m]);
        var view = m.View;
        // The red square sits at (10..20) inside the 30px source → (30..60) in the 3× view.
        Assert.True(view.Top >= 0 && view.Bottom <= 150);
        Assert.Equal(Colors.Red, Images.PixelAt(image, (int)(view.X + 45), (int)(view.Y + 45)));
        Assert.Equal(Colors.White, Images.PixelAt(image, (int)(view.X + 15), (int)(view.Y + 15)));
    });

    [Fact]
    public void Snapshot_CanBeSwapped_ForPastingIntoAnotherCapture() => Sta.Run(() =>
    {
        var m = Magnify(Screen(), new Point(10, 10), new Point(40, 40), zoom: 3);
        var blank = Images.Snapshot(300, 150, Colors.Black);
        m.Snapshot = blank;
        var view = m.View;
        Assert.Equal(Colors.Black, Images.PixelAt(Compositor.Compose(blank, new Rect(0, 0, 300, 150), [m]), (int)(view.X + 45), (int)(view.Y + 45)));
    });
}

public class LayerOrderTests
{
    private static RectAnnotation Shape() => new(new Point(0, 0), Colors.Red, 4, 1);

    [Fact]
    public void Move_ReordersAsOneUndoStep()
    {
        var stack = new UndoStack();
        var a = Shape();
        var b = Shape();
        var c = Shape();
        stack.Add(a);
        stack.Add(b);
        stack.Add(c);

        Assert.True(stack.Move(a, 2)); // to the top
        Assert.Equal([b, c, a], stack.Items);
        Assert.False(stack.Move(a, 5)); // already on top (clamped)

        stack.Undo();
        Assert.Equal(3, stack.Items.Count);
        Assert.Equal(3, stack.Items.Count(i => i is RectAnnotation));
        Assert.True(stack.Undo()); // the adds are still undoable
    }

    [Fact]
    public void Load_StartsFreshWithoutHistory()
    {
        var stack = new UndoStack();
        stack.Add(Shape());
        stack.Load([Shape(), Shape()]);
        Assert.Equal(2, stack.Items.Count);
        Assert.False(stack.Undo()); // reopened annotations are not undone away
    }

    [Fact]
    public void ReopenHotkey_DefaultsToCtrlShiftF1() =>
        Assert.Equal(new Hotkey(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.F1),
            AppSettings.DefaultReopenHotkey);
}
