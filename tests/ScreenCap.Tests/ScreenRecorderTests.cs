using System.Windows;
using ScreenCap.Recording;

namespace ScreenCap.Tests;

/// <summary>Records the real screen, so these need an interactive desktop session.</summary>
[Collection(ScreenCap.Tests.Support.ScreenCollection.Name)]
[Trait("Category", "Desktop")] // left out on CI: no interactive desktop or GPU there
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

    private sealed class FakeAudioEncoder : IFrameEncoder, IAudioEncoder
    {
        public FakeEncoder Video { get; } = new();
        public List<(int Frames, TimeSpan Time)> Audio { get; } = new();

        public void AddAudio(short[] samples, TimeSpan timestamp)
        {
            Video.Threads.Add(Environment.CurrentManagedThreadId);
            Audio.Add((samples.Length / AudioMixer.Channels, timestamp));
        }

        public void AddFrame(byte[] bgra, TimeSpan timestamp) => Video.AddFrame(bgra, timestamp);
        public void Finish(TimeSpan endTime) => Video.Finish(endTime);
        public void Dispose() => Video.Dispose();
    }

    [Fact]
    public async Task Audio_CoversTheWholeRecording_WithoutThePausedTime()
    {
        FakeAudioEncoder? encoder = null;
        // Real devices: without a microphone (or with access denied) that source is reported and stays silent.
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => encoder = new FakeAudioEncoder(),
            audio: AudioSources.System | AudioSources.Microphone);
        await recorder.StartAsync();
        await Task.Delay(400);
        recorder.Pause();
        await Task.Delay(400);
        recorder.Resume();
        await Task.Delay(400);
        await recorder.StopAsync();

        Assert.NotNull(encoder);
        var end = encoder.Video.EndTime!.Value;
        Assert.InRange(end.TotalMilliseconds, 650, 1200); // ~800 ms recorded, not ~1200
        Assert.Equal(TimeSpan.Zero, encoder.Audio[0].Time);
        long frames = 0;
        foreach (var (count, time) in encoder.Audio)
        {
            Assert.Equal(frames * TimeSpan.TicksPerSecond / AudioMixer.SampleRate, time.Ticks); // contiguous, no overlap
            frames += count;
        }
        Assert.Equal(end.Ticks * AudioMixer.SampleRate / TimeSpan.TicksPerSecond, frames); // up to the end of the video
        Assert.Single(encoder.Video.Threads); // audio is written on the encoding thread too
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
    public async Task Pause_StopsCapturing_AndLeavesThePausedTimeOut()
    {
        FakeEncoder? encoder = null;
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => encoder = new FakeEncoder());
        await recorder.StartAsync();
        await Task.Delay(300);
        recorder.Pause();
        Assert.True(recorder.IsPaused);
        var pausedAt = recorder.Elapsed;
        await Task.Delay(700);
        Assert.Equal(pausedAt, recorder.Elapsed); // the clock doesn't run while paused
        recorder.Resume();
        await Task.Delay(300);
        await recorder.StopAsync();

        Assert.NotNull(encoder);
        Assert.InRange(encoder.EndTime!.Value.TotalMilliseconds, 450, 950); // ~600 ms recorded, not ~1300
        var gaps = encoder.Frames.Zip(encoder.Frames.Skip(1), (a, b) => (b.Time - a.Time).TotalMilliseconds);
        Assert.True(gaps.Max() < 250, "the pause shows up as a jump in the timestamps");
        Assert.InRange(encoder.Frames.Count, 6, 20); // ~12 at 20 fps; loose for busy machines
    }

    [Fact]
    public async Task Stop_WhilePaused_Finishes()
    {
        FakeEncoder? encoder = null;
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => encoder = new FakeEncoder());
        await recorder.StartAsync();
        await Task.Delay(150);
        recorder.Pause();
        await Task.Delay(100);
        var stop = recorder.StopAsync();
        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(3000)));
        Assert.True(encoder!.Disposed);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenTheEncoderCannotBeCreated()
    {
        var recorder = new ScreenRecorder(new Int32Rect(0, 0, 64, 48), 20, () => throw new InvalidOperationException("no encoder"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(recorder.StartAsync);
        Assert.Equal("no encoder", error.Message);
    }

    private sealed class LastFrameEncoder : IFrameEncoder
    {
        public byte[]? Last { get; private set; }
        public int Count { get; private set; }
        public TimeSpan? First { get; private set; }
        public void AddFrame(byte[] bgra, TimeSpan timestamp) => (Last, Count, First) = (bgra, Count + 1, First ?? timestamp);
        public void Finish(TimeSpan endTime) { }
        public void Dispose() { }
    }

    /// <summary>Shows a topmost window filled with <paramref name="color"/> on its own UI thread; dispose to close it.</summary>
    private static IDisposable ShowSolidWindow(System.Windows.Media.Color color, Int32Rect bounds)
    {
        System.Windows.Threading.Dispatcher? dispatcher = null;
        var shown = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Background = new System.Windows.Media.SolidColorBrush(color),
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                ScreenCap.Native.NativeMethods.SetWindowPos(hwnd, ScreenCap.Native.NativeMethods.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0);
            };
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            window.Show();
            // A window without content never raises ContentRendered; once idle it has been shown and painted.
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => shown.Set());
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        shown.Wait(TimeSpan.FromSeconds(5));
        // WPF draws on its render thread and DWM composes after that: wait until the screen really shows it.
        var center = new Int32Rect(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 1, 1);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var px = ScreenCap.Services.ScreenCaptureService.CaptureRegion(center);
            if (ScreenCap.Tests.Support.Images.Distance(color, System.Windows.Media.Color.FromRgb(px[2], px[1], px[0])) < 6)
                break;
            Thread.Sleep(50);
        }
        return new Closer(() =>
        {
            dispatcher!.InvokeShutdown();
            thread.Join();
        });
    }

    private sealed class Closer(Action close) : IDisposable
    {
        public void Dispose() => close();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordsWhatIsOnTheScreen(bool graphicsCapture)
    {
        var bounds = new Int32Rect(100, 100, 200, 120);
        var color = System.Windows.Media.Color.FromRgb(0x20, 0xA0, 0x60);
        using (ShowSolidWindow(color, bounds))
        {
            var encoder = new LastFrameEncoder();
            // Inside the window, clear of its edges.
            var region = new Int32Rect(bounds.X + 20, bounds.Y + 20, 64, 48);
            var recorder = new ScreenRecorder(region, 20, () => encoder, useGraphicsCapture: graphicsCapture);
            await recorder.StartAsync();
            await Task.Delay(500);
            await recorder.StopAsync();

            if (graphicsCapture)
            {
                Assert.True(recorder.UsedGraphicsCapture, "Windows.Graphics.Capture should be available on Windows 11");
                Assert.True(recorder.GraphicsCaptureFrames >= encoder.Count - 2, $"{recorder.GraphicsCaptureFrames} of {encoder.Count} frames"); // BitBlt only until the first one arrives
            }
            else
                Assert.False(recorder.UsedGraphicsCapture);
            Assert.True(encoder.Count >= 5, $"{encoder.Count} frames");
            // Setting up the capture happens before the clock starts: no gap at the start of the video.
            Assert.InRange(encoder.First!.Value.TotalMilliseconds, 0, 30);
            Assert.Equal(64 * 48 * 4, encoder.Last!.Length);
            // The middle is the window's color (the cursor may be elsewhere in the region, so not every pixel).
            var pixel = ScreenCap.Tests.Support.Images.PixelAt(encoder.Last, 64, 32, 24);
            Assert.True(ScreenCap.Tests.Support.Images.Distance(color, pixel) < 6, $"got {pixel}");
        }
    }
}
