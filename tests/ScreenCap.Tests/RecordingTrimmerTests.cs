using System.IO;
using System.Windows;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public sealed class RecordingTrimmerTests : IDisposable
{
    private readonly string _source = Path.Combine(Path.GetTempPath(), $"ScreenCap.Tests_{Guid.NewGuid():N}.mp4"); // the sink writer picks the container from the extension
    private readonly string _trimmed = Path.Combine(Path.GetTempPath(), $"ScreenCap.Tests_{Guid.NewGuid():N}_trimmed.mp4");

    public void Dispose()
    {
        foreach (var path in new[] { _source, _trimmed })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private const int W = 60, H = 40;

    /// <summary>One second each: white, then a red square added, then a blue square added, then all green.</summary>
    private void EncodeGif()
    {
        using var stream = File.Create(_source);
        using var encoder = new GifEncoder(stream, W, H);
        var pixels = Images.Solid(W, H, Colors.White);
        encoder.AddFrame((byte[])pixels.Clone(), TimeSpan.Zero);
        Images.FillRect(pixels, W, new Int32Rect(0, 0, 10, 10), Colors.Red);
        encoder.AddFrame((byte[])pixels.Clone(), TimeSpan.FromSeconds(1));
        Images.FillRect(pixels, W, new Int32Rect(30, 20, 10, 10), Colors.Blue);
        encoder.AddFrame((byte[])pixels.Clone(), TimeSpan.FromSeconds(2));
        encoder.AddFrame(Images.Solid(W, H, Colors.Lime), TimeSpan.FromSeconds(3));
        encoder.Finish(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void GifFile_FindsEveryFrameAndItsDelay()
    {
        EncodeGif();
        var gif = GifFile.Parse(File.ReadAllBytes(_source));
        Assert.Equal((W, H), (gif.Width, gif.Height));
        Assert.Equal([100, 100, 100, 100], gif.Frames.Select(f => f.DelayCs));
        Assert.Equal((30, 20), (gif.Frames[2].Left, gif.Frames[2].Top)); // only the changed square
        Assert.Equal(TimeSpan.FromSeconds(4), gif.Duration);
    }

    [Fact]
    public void Gif_TrimmedFromAFrameThatOnlyHoldsChanges_StillShowsTheWholePicture() => Sta.Run(() =>
    {
        EncodeGif();
        RecordingTrimmer.Trim(_source, _trimmed, RecordingFormat.Gif, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));

        var (width, height, frames) = GifReader.Decode(File.ReadAllBytes(_trimmed));
        Assert.Equal((W, H), (width, height));
        Assert.Equal(2, frames.Count);
        Assert.Equal([100, 100], frames.Select(f => f.DelayCs));
        // The first frame is the blue square's, but the red square from before must be there too.
        Assert.Equal(Colors.Red, Images.PixelAt(frames[0].Canvas, W, 5, 5));
        Assert.Equal(Colors.Blue, Images.PixelAt(frames[0].Canvas, W, 35, 25));
        Assert.Equal(Colors.White, Images.PixelAt(frames[0].Canvas, W, 50, 5));
        Assert.Equal(Colors.Lime, Images.PixelAt(frames[1].Canvas, W, 5, 5));
    });

    [Fact]
    public void Gif_TrimmedAtTheEnd_DropsTheLastFrames() => Sta.Run(() =>
    {
        EncodeGif();
        RecordingTrimmer.Trim(_source, _trimmed, RecordingFormat.Gif, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        var frames = GifReader.Decode(File.ReadAllBytes(_trimmed)).Frames;
        Assert.Equal(2, frames.Count);
        Assert.Equal(Colors.White, Images.PixelAt(frames[0].Canvas, W, 5, 5));
        Assert.Equal(Colors.Red, Images.PixelAt(frames[1].Canvas, W, 5, 5));
        Assert.Equal(Colors.White, Images.PixelAt(frames[1].Canvas, W, 35, 25));
    });

    [Fact]
    public void Gif_Timeline_HasEveryFrameAndAThumbnailOfEach() => Sta.Run(() =>
    {
        EncodeGif();
        var timeline = RecordingTrimmer.Load(_source, RecordingFormat.Gif);
        Assert.Equal([0.0, 1, 2, 3], timeline.FrameTimes.Select(t => t.TotalSeconds));
        Assert.Equal(TimeSpan.FromSeconds(4), timeline.Duration);
        Assert.Equal(4, timeline.Thumbnails.Count);
        var third = timeline.ThumbnailAt(TimeSpan.FromSeconds(2.5))!;
        Assert.Equal(Colors.Red, Images.PixelAt(third, 5, 5));
        Assert.Equal(Colors.Blue, Images.PixelAt(third, 35, 25));
    });

    [Fact]
    public void FrameRange_IsTheFramesShowingBetweenTheTimes()
    {
        var times = new[] { 0.0, 1, 2, 3 }.Select(TimeSpan.FromSeconds).ToList();
        Assert.Equal((1, 2), RecordingTrimmer.FrameRange(times, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)));
        Assert.Equal((1, 2), RecordingTrimmer.FrameRange(times, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.5)));
        Assert.Equal((0, 3), RecordingTrimmer.FrameRange(times, TimeSpan.Zero, TimeSpan.FromSeconds(9)));
    }

    /// <summary>Three seconds at 30 fps: red, green, blue, with a tone only during the middle second.</summary>
    private void EncodeMp4()
    {
        const int fps = 30, chunk = AudioMixer.SampleRate / 100;
        Color[] colors = [Colors.Red, Colors.Lime, Colors.Blue];
        using var encoder = new Mp4Encoder(_source, 320, 240, fps, audio: true);
        int frame = 0;
        for (int pos = 0; pos < AudioMixer.SampleRate * 3; pos += chunk)
        {
            var samples = new short[chunk * AudioMixer.Channels];
            for (int i = 0; i < chunk; i++)
            {
                double t = (double)(pos + i) / AudioMixer.SampleRate;
                short value = t is >= 1 and < 2 ? (short)(8000 * Math.Sin(2 * Math.PI * 440 * t)) : (short)0;
                samples[i * 2] = samples[i * 2 + 1] = value;
            }
            encoder.AddAudio(samples, TimeSpan.FromTicks(pos * TimeSpan.TicksPerSecond / AudioMixer.SampleRate));
            for (; frame * AudioMixer.SampleRate / fps <= pos; frame++)
                encoder.AddFrame(Images.Solid(320, 240, colors[frame / fps]), TimeSpan.FromSeconds((double)frame / fps));
        }
        encoder.Finish(TimeSpan.FromSeconds(3));
    }

    private static double Rms(short[] samples, double from, double to)
    {
        int start = (int)(from * AudioMixer.SampleRate), end = (int)(to * AudioMixer.SampleRate);
        double sum = 0;
        for (int i = start; i < end; i++)
            sum += (double)samples[i * AudioMixer.Channels] * samples[i * AudioMixer.Channels];
        return Math.Sqrt(sum / (end - start));
    }

    [Fact]
    public void Mp4_KeepsTheChosenSecond_WithItsSound()
    {
        EncodeMp4();
        var timeline = RecordingTrimmer.Load(_source, RecordingFormat.Mp4);
        Assert.Equal(90, timeline.FrameTimes.Count);
        Assert.InRange(timeline.Duration.TotalSeconds, 2.95, 3.1);

        // From the first green frame to the first blue one, as the trim window would pick.
        var times = timeline.FrameTimes;
        RecordingTrimmer.Trim(_source, _trimmed, RecordingFormat.Mp4, times[30], times[60]);

        var video = Mp4Reader.Read(_trimmed);
        Assert.InRange(video.Frames.Count, 29, 31);
        Assert.All(video.Frames, f => Assert.True(Images.Distance(Colors.Lime, Images.PixelAt(f.Bgra, 320, 160, 120)) < 30));
        var audio = Mp4Reader.ReadAllAudio(_trimmed);
        Assert.NotNull(audio);
        Assert.InRange(audio.Length / AudioMixer.Channels / (double)AudioMixer.SampleRate, 0.95, 1.1);
        Assert.InRange(Rms(audio, 0.1, 0.9), 4500, 7000); // the tone, from the start
    }

    [Fact]
    public void Mp4_FrameAt_IsTheFullSizeFrameShowingThen()
    {
        EncodeMp4();
        Assert.Equal(TimeSpan.Zero, RecordingTrimmer.Load(_source, RecordingFormat.Mp4).FrameTimes[0]); // the timeline starts at zero

        foreach (var (seconds, color) in new[] { (0.0, Colors.Red), (1.5, Colors.Lime), (2.9, Colors.Blue), (1.2, Colors.Lime) })
        {
            var frame = RecordingTrimmer.FrameAt(_source, RecordingFormat.Mp4, TimeSpan.FromSeconds(seconds));
            Assert.Equal((320, 240), (frame.PixelWidth, frame.PixelHeight));
            Assert.True(Images.Distance(color, Images.PixelAt(frame, 160, 120)) < 30, $"{seconds} s: {Images.PixelAt(frame, 160, 120)}");
        }
    }

    [Fact]
    public void Gif_FrameAt_HasEverythingUpToThatFrame() => Sta.Run(() =>
    {
        EncodeGif();
        var frame = RecordingTrimmer.FrameAt(_source, RecordingFormat.Gif, TimeSpan.FromSeconds(2.5));
        Assert.Equal((W, H), (frame.PixelWidth, frame.PixelHeight));
        Assert.Equal(Colors.Red, Images.PixelAt(frame, 5, 5));
        Assert.Equal(Colors.Blue, Images.PixelAt(frame, 35, 25));
    });

    [Fact]
    public void Mp4_Play_ShowsEveryFrameOfTheRange_InTime_AtFullSize()
    {
        EncodeMp4();
        // From the first green frame to the first blue one, as the trim window would pick them.
        var times = RecordingTrimmer.Load(_source, RecordingFormat.Mp4).FrameTimes;
        TimeSpan from = times[30], to = times[60];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var shown = new List<(TimeSpan At, Color Color, int Width)>();
        RecordingTrimmer.Play(_source, RecordingFormat.Mp4, from, to, () => from + clock.Elapsed,
            (pixels, width, _) => shown.Add((clock.Elapsed, Images.PixelAt(pixels, width, 160, 120), width)), CancellationToken.None);

        Assert.InRange(shown.Count, 25, 31); // 30 frames; one or two may be skipped on a slow machine
        Assert.All(shown, f => Assert.Equal(320, f.Width));
        Assert.All(shown, f => Assert.True(Images.Distance(Colors.Lime, f.Color) < 30, f.Color.ToString()));
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.9, 1.5); // real time, not as fast as it decodes
    }

    [Fact]
    public void Play_StopsWhenCancelled()
    {
        EncodeMp4();
        var cancelAfter = TimeSpan.FromMilliseconds(400); // time for even a slow machine to open the file and decode a frame
        using var cancel = new CancellationTokenSource(cancelAfter);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var shownAt = new List<TimeSpan>();
        try
        {
            RecordingTrimmer.Play(_source, RecordingFormat.Mp4, TimeSpan.Zero, TimeSpan.FromSeconds(3), () => clock.Elapsed,
                (_, _, _) => shownAt.Add(clock.Elapsed), cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }
        // How many frames show before the cancel depends on how fast the machine decodes (a slow one shows the late
        // ones in a burst); what matters is that none comes after it, and that it returns instead of playing 3 s.
        Assert.NotEmpty(shownAt);
        Assert.All(shownAt, at => Assert.True(at < cancelAfter + TimeSpan.FromMilliseconds(150), $"a frame shown at {at.TotalMilliseconds:0} ms"));
        Assert.InRange(clock.Elapsed.TotalSeconds, 0, 1.2);
    }

    [Fact]
    public void Gif_Play_StartsWithTheWholePicture() => Sta.Run(() =>
    {
        EncodeGif();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var from = TimeSpan.FromSeconds(2);
        var shown = new List<byte[]>();
        RecordingTrimmer.Play(_source, RecordingFormat.Gif, from, TimeSpan.FromSeconds(3.5), () => from + clock.Elapsed,
            (pixels, _, _) => shown.Add(pixels), CancellationToken.None);

        Assert.Equal(2, shown.Count);
        Assert.Equal(Colors.Red, Images.PixelAt(shown[0], W, 5, 5));   // drawn before the start, still there
        Assert.Equal(Colors.Blue, Images.PixelAt(shown[0], W, 35, 25));
        Assert.Equal(Colors.Lime, Images.PixelAt(shown[1], W, 5, 5));
    });
}
