using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Annotations;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class StampAnnotationTests
{
    private static Stamp BuiltIn(string id) => Stamp.BuiltIn.Single(s => s.Id == id);

    private static StampAnnotation Place(Stamp stamp, double size = 40, double scale = 1) =>
        new(new Point(100, 100), stamp, Colors.Red, size, scale);

    [Fact]
    public void BuiltIn_IdsAreUnique() =>
        Assert.Equal(Stamp.BuiltIn.Count, Stamp.BuiltIn.Select(s => s.Id).Distinct().Count());

    [Fact]
    public void Badge_IsACircleOfTheSize()
    {
        var badge = Place(BuiltIn("check"), size: 40, scale: 1.5);
        Assert.Equal(new Rect(70, 70, 60, 60), badge.Bounds); // 40 DIPs at 150%
        Assert.True(badge.HitTest(new Point(128, 128), 0));
        Assert.False(badge.HitTest(new Point(140, 100), 4));
    }

    [Fact]
    public void Seal_IsWiderThanTall_AndTilted()
    {
        var seal = Place(BuiltIn("done"));
        var b = seal.Bounds;
        Assert.True(b.Width > b.Height);
        Assert.True(b.Height > 40); // the tilt makes the bounds taller than the frame
        Assert.Equal(100, b.X + b.Width / 2, 3);
        Assert.Equal(100, b.Y + b.Height / 2, 3);
    }

    [Fact]
    public void Image_FitsLongestSide()
    {
        var picture = Images.ToBitmap(Images.Solid(200, 100, Colors.Blue), 200, 100);
        var stamp = Place(new Stamp("file:x.png", "自訂圖片", StampStyle.Image, Image: picture), size: 80);
        Assert.Equal(new Rect(60, 80, 80, 40), stamp.Bounds);
        Assert.False(stamp.Stamp.UsesColor);
    }

    [Fact]
    public void MovesWhilePlacing_AndWhenDragged()
    {
        var badge = Place(BuiltIn("star"));
        badge.Update(new Point(150, 120));
        Assert.Equal(new Point(150, 120), badge.Center);

        var moved = badge.Clone();
        moved.MoveFrom(badge, new Vector(-50, 10));
        Assert.Equal(new Point(100, 130), ((StampAnnotation)moved).Center);
    }

    [Fact]
    public void Handles_MatchRectangles() // so the resize cursors are the same
    {
        var badge = Place(BuiltIn("star"), size: 40);
        Assert.Equal(RectHandles.Points(badge.Bounds), badge.Handles);
    }

    [Fact]
    public void DraggingAHandle_ResizesFreely()
    {
        var badge = Place(BuiltIn("star"), size: 40); // 80..120
        var resized = (StampAnnotation)badge.Clone();
        resized.DragHandleFrom(badge, 3, new Vector(40, 0)); // right side only
        Assert.Equal(new Rect(80, 80, 80, 40), resized.Bounds);
        Assert.Equal(40, resized.Size, 3);
        Assert.Equal(2, resized.StretchX, 3);
        Assert.Equal(40, badge.Size); // the original is untouched
        Assert.Equal(1, badge.StretchX);

        var taller = (StampAnnotation)resized.Clone();
        taller.DragHandleFrom(resized, 5, new Vector(0, 40)); // bottom side: only taller
        Assert.Equal(new Rect(80, 80, 80, 80), taller.Bounds);
    }

    [Fact]
    public void DraggingAHandle_WithShift_KeepsProportions()
    {
        var picture = Images.ToBitmap(Images.Solid(200, 100, Colors.Blue), 200, 100);
        var stamp = Place(new Stamp("file:x.png", "自訂圖片", StampStyle.Image, Image: picture), size: 80); // 60,80 80×40
        var resized = (StampAnnotation)stamp.Clone();
        resized.DragHandleFrom(stamp, 4, new Vector(80, 10), constrain: true); // bottom-right, mostly sideways
        Assert.Equal(new Rect(60, 80, 160, 80), resized.Bounds); // top-left corner stays
        Assert.Equal(1, resized.StretchX, 3);
    }

    [Fact]
    public void Stretched_Renders() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(300, 200, Colors.White);
        var badge = Place(BuiltIn("star"), size: 40);
        var wide = (StampAnnotation)badge.Clone();
        wide.DragHandleFrom(badge, 3, new Vector(80, 0)); // 80..200 wide, 80..120 tall
        var image = Compositor.Compose(snapshot, new Rect(0, 0, 300, 200), [wide]);
        Assert.True(Images.Distance(Images.PixelAt(image, 190, 100), Colors.Red) < 40); // the stretched circle reaches here
        Assert.True(Images.Distance(Images.PixelAt(image, 210, 100), Colors.White) < 10);
    });

    [Fact]
    public void Renders_EachStyle() => Sta.Run(() =>
    {
        var snapshot = Images.Snapshot(400, 200, Colors.White);
        var badge = new StampAnnotation(new Point(60, 100), BuiltIn("cross"), Colors.Red, 60, 1);
        var seal = new StampAnnotation(new Point(250, 100), BuiltIn("ok"), Colors.Blue, 60, 1);
        var image = Compositor.Compose(snapshot, new Rect(0, 0, 400, 200), [badge, seal]);

        Assert.True(Images.Distance(Images.PixelAt(image, 60, 75), Colors.Red) < 40);   // badge circle, above the symbol
        Assert.True(Images.Distance(Images.PixelAt(image, 60, 100), Colors.White) < 60); // the symbol, in the contrast color
        var frame = seal.Bounds;
        Assert.True(Images.Distance(Images.PixelAt(image, 250, 100), Colors.White) > 10); // seal text or its tint
        Assert.True(Images.Distance(Images.PixelAt(image, (int)frame.Right + 3, 100), Colors.White) < 10); // nothing beyond it
    });

    [Fact]
    public void Preview_IsSquare() => Sta.Run(() =>
    {
        foreach (var stamp in Stamp.BuiltIn)
        {
            var preview = stamp.Preview(Colors.Red, 26);
            Assert.Equal(26, preview.Width, 3);
            Assert.Equal(26, preview.Height, 3);
        }
    });
}

public class ResizeKeepingAspectTests
{
    [Fact]
    public void Corner_FollowsTheLongerPull_OppositeCornerStays()
    {
        var r = RectHandles.ResizeKeepingAspect(new Rect(10, 10, 100, 50), 4, new Vector(100, 5));
        Assert.Equal(new Rect(10, 10, 200, 100), r);
        r = RectHandles.ResizeKeepingAspect(new Rect(10, 10, 100, 50), 0, new Vector(50, 0)); // top-left, inwards
        Assert.Equal(new Rect(60, 35, 50, 25), r);
    }

    [Fact]
    public void Side_ScalesBoth_CenteredOnTheOtherAxis()
    {
        var r = RectHandles.ResizeKeepingAspect(new Rect(0, 0, 100, 50), 5, new Vector(0, 50)); // bottom
        Assert.Equal(new Rect(-50, 0, 200, 100), r);
    }

    [Fact]
    public void Rectangle_WithShift_KeepsProportions()
    {
        var rect = new RectAnnotation(new Point(0, 0), Colors.Red, 2, 1);
        rect.Update(new Point(100, 50));
        var resized = rect.Clone();
        resized.DragHandleFrom(rect, 3, new Vector(100, 0), constrain: true);
        Assert.Equal(new Rect(0, -25, 200, 100), resized.Bounds);
        resized.DragHandleFrom(rect, 3, new Vector(100, 0)); // without Shift: only wider
        Assert.Equal(new Rect(0, 0, 200, 50), resized.Bounds);
    }
}

[Collection(nameof(SettingsCollection))] // the last stamp is a setting
public class StampLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScreenCap.Tests.Stamps." + Guid.NewGuid().ToString("N"));
    private readonly string _original = StampLibrary.Folder;

    public StampLibraryTests()
    {
        StampLibrary.Folder = _folder;
        StampLibrary.Reset();
    }

    public void Dispose()
    {
        StampLibrary.Folder = _original;
        StampLibrary.Reset();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Add_KeepsPicture_AcrossRestarts()
    {
        var stamp = StampLibrary.Add(Images.ToBitmap(Images.Solid(30, 20, Colors.Green), 30, 20));
        Assert.Equal(StampStyle.Image, stamp.Style);
        Assert.StartsWith("file:", stamp.Id);
        Assert.Equal(stamp, Assert.Single(StampLibrary.Custom));

        StampLibrary.Reset();
        var loaded = StampLibrary.Find(stamp.Id);
        Assert.NotNull(loaded);
        Assert.Equal((30, 20), (loaded.Image!.PixelWidth, loaded.Image.PixelHeight));
    }

    [Fact]
    public void Add_ShrinksLargePictures()
    {
        var stamp = StampLibrary.Add(Images.ToBitmap(Images.Solid(2048, 1024, Colors.Green), 2048, 1024));
        Assert.Equal((StampLibrary.MaxSide, StampLibrary.MaxSide / 2), (stamp.Image!.PixelWidth, stamp.Image.PixelHeight));
    }

    [Fact]
    public void Remove_DeletesFile()
    {
        var first = StampLibrary.Add(Images.ToBitmap(Images.Solid(8, 8, Colors.Green), 8, 8));
        var second = StampLibrary.Add(Images.ToBitmap(Images.Solid(8, 8, Colors.Red), 8, 8));
        Assert.Equal([first.Id, second.Id], StampLibrary.Custom.Select(s => s.Id)); // oldest first

        StampLibrary.Remove(first);
        Assert.Equal(second.Id, Assert.Single(StampLibrary.Custom).Id);
        Assert.Single(Directory.GetFiles(_folder, "*.png"));
        Assert.Null(StampLibrary.Find(first.Id));
    }

    [Fact]
    public void Find_BuiltInOrMissing()
    {
        Assert.Equal("check", StampLibrary.Find("check")?.Id);
        Assert.Null(StampLibrary.Find("file:gone.png"));
        Assert.Null(StampLibrary.Find(null));
    }
}
