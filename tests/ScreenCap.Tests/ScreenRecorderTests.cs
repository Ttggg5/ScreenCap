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
}
