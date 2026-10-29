using System.Windows;
using ScreenCap.Recording;

namespace ScreenCap.Tests;

/// <summary>Records the real screen, so these need an interactive desktop session.</summary>
public class ScreenRecorderTests
{
    private sealed class FakeEncoder : IFrameEncoder
    {
        public List<(int Length, TimeSpan Time)> Frames { get; } = new();
        public TimeSpan? EndTime { get; private set; }
        public bool Disposed { get; private set; }
        public HashSet<int> Threads { get; } = new();

        public FakeEncoder() => Threads.Add(Environment.CurrentManagedThreadId);

        public void AddFrame(byte[] bgra, TimeSpan timestamp)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            Frames.Add((bgra.Length, timestamp));
        }

        public void Finish(TimeSpan endTime)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            EndTime = endTime;
        }

        public void Dispose()
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            Disposed = true;
        }
    }

    [Fact]
    public async Task Records_FramesAtTheRequestedRate_ThenFinishesAndDisposes()
    {
        FakeEncoder? encoder = null;
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => encoder = new FakeEncoder());
        await recorder.StartAsync();
        await Task.Delay(600);
        await recorder.StopAsync();

        Assert.NotNull(encoder);
        Assert.InRange(encoder.Frames.Count, 6, 16); // ~12 at 20 fps
        Assert.All(encoder.Frames, f => Assert.Equal(64 * 48 * 4, f.Length));
        Assert.True(encoder.Frames.Zip(encoder.Frames.Skip(1)).All(p => p.Second.Time > p.First.Time));
        Assert.True(encoder.EndTime >= encoder.Frames[^1].Time);
        Assert.True(encoder.Disposed);
        Assert.Single(encoder.Threads); // created, used and disposed on one thread (required by Media Foundation)
    }

    [Fact]
    public async Task StartAsync_Throws_WhenTheEncoderCannotBeCreated()
    {
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => throw new InvalidOperationException("no encoder"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(recorder.StartAsync);
        Assert.Equal("no encoder", error.Message);
    }
}
