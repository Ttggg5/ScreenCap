using System.Windows;
using ScreenCap.Capture;

namespace ScreenCap.Tests;

public class SelectionRatioTests
{
    private static readonly SelectionRatio Wide = SelectionRatio.All.Single(r => r.Name == "16:9");
    private static readonly SelectionRatio Square = SelectionRatio.All.Single(r => r.Name == "1:1");
    private static readonly Size Screen = new(1920, 1080);

    [Fact]
    public void Free_SpansThePressAndTheCursor()
    {
        Assert.Equal(new Rect(10, 20, 101, 51), SelectionRatio.Free.Span(new Point(110, 70), new Point(10, 20), Screen));
    }

    [Fact]
    public void Span_GrowsTheShorterSideToTheRatio()
    {
        var r = Wide.Span(new Point(100, 100), new Point(1379, 150), Screen);
        Assert.Equal(new Rect(100, 100, 1280, 720), r);
    }

    [Fact]
    public void Span_TallerThanWide_IsPortrait()
    {
        var r = Wide.Span(new Point(100, 100), new Point(110, 739), Screen);
        Assert.Equal(new Rect(100, 100, 360, 640), r);
    }

    [Fact]
    public void Span_UpAndLeft_EndsAtThePress()
    {
        var r = Square.Span(new Point(500, 500), new Point(401, 450), Screen);
        Assert.Equal(new Rect(401, 401, 100, 100), r);
    }

    [Fact]
    public void Span_StaysOnTheScreen()
    {
        var r = Wide.Span(new Point(1000, 900), new Point(1900, 950), Screen);
        Assert.True(r.Right <= Screen.Width && r.Bottom <= Screen.Height, r.ToString());
        Assert.Equal(16.0 / 9, r.Width / r.Height, 0.01);
        Assert.Equal(new Point(1000, 900), r.TopLeft);
    }

    [Fact]
    public void Fit_CentersTheLargestRatioInside()
    {
        Assert.Equal(new Rect(50, 0, 200, 200), Square.Fit(new Rect(0, 0, 300, 200)));
        var wide = Wide.Fit(new Rect(0, 0, 1280, 1000));
        Assert.Equal(new Rect(0, 140, 1280, 720), wide);
    }

    [Fact]
    public void Resize_KeepsTheRatioAndTheOppositeCorner()
    {
        var r = Wide.Resize(new Rect(100, 100, 1280, 720), handle: 4, new Vector(-640, 0), Screen);
        Assert.Equal(new Rect(100, 100, 640, 360), r);
        var fromTopLeft = Wide.Resize(new Rect(100, 100, 1280, 720), handle: 0, new Vector(640, 0), Screen);
        Assert.Equal(new Rect(740, 460, 640, 360), fromTopLeft);
    }

    [Fact]
    public void Resize_PastTheScreenEdge_IsRefused()
    {
        Assert.Null(Wide.Resize(new Rect(100, 100, 1280, 720), handle: 4, new Vector(1000, 0), Screen));
    }

    [Fact]
    public void Other_FollowsTheOrientation()
    {
        Assert.Equal(720, Wide.Other(1280, typedWidth: true, new Size(16, 9)));
        Assert.Equal(1280, Wide.Other(720, typedWidth: false, new Size(16, 9)));
        Assert.Equal(640, Wide.Other(360, typedWidth: true, new Size(9, 16)));
    }

    [Fact]
    public void FromIndex_WrapsAround()
    {
        Assert.Same(SelectionRatio.Free, SelectionRatio.FromIndex(SelectionRatio.All.Length));
        Assert.Same(SelectionRatio.Free, SelectionRatio.FromIndex(0));
    }
}
