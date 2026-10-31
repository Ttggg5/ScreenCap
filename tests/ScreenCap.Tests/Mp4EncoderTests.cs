using System.IO;
using System.Windows;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public sealed class Mp4EncoderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ScreenCap.Tests_{Guid.NewGuid():N}.mp4");

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    /// <summary>White frames with a fixed blue square at the top-left and a red box moving 3 px per frame.</summary>
    private void EncodeMovingBox(int width, int height, int frames, int fps)
    {
        using var encoder = new Mp4Encoder(_path, width, height, fps);
        for (int i = 0; i < frames; i++)
        {
            var pixels = Images.Solid(width, height, Colors.White);
            Images.FillRect(pixels, width, new Int32Rect(0, 0, 20, 20), Colors.Blue);
            Images.FillRect(pixels, width, new Int32Rect(10 + i * 3, 100, 60, 40), Colors.Red);
            encoder.AddFrame(pixels, TimeSpan.FromSeconds((double)i / fps));
        }
        encoder.Finish(TimeSpan.FromSeconds((double)frames / fps));
    }

    [Fact]
    public void Encodes_EvenSize_AllFrames_WithTimestamps()
    {
        EncodeMovingBox(401, 251, 30, 30);
        var video = Mp4Reader.Read(_path);

        Assert.Equal((400, 250), (video.Width, video.Height)); // odd column/row dropped for H.264
        Assert.Equal(30, video.Frames.Count);
        // The decoder may shift all timestamps by a constant (decoding delay), so check the spacing.
        Assert.Equal(29 / 30.0, (video.Frames[^1].Time - video.Frames[0].Time).TotalSeconds, 2);
        Assert.InRange(video.Frames[0].Time.TotalSeconds, 0, 0.1);
        Assert.True(video.Frames.Zip(video.Frames.Skip(1)).All(p => p.Second.Time > p.First.Time));
    }

    [Fact]
    public void Preserves_ColorsOrientationAndMotion()
    {
        EncodeMovingBox(400, 250, 30, 30);
        var video = Mp4Reader.Read(_path);
        var frame = video.Frames[20].Bgra;

        Assert.True(Images.Distance(Colors.Blue, Images.PixelAt(frame, 400, 10, 10)) < 12);  // top-left, so not upside down
        Assert.True(Images.Distance(Colors.White, Images.PixelAt(frame, 400, 10, 240)) < 12);
        Assert.True(Images.Distance(Colors.White, Images.PixelAt(frame, 400, 300, 30)) < 12); // full range, not washed out

        int redLeft = Enumerable.Range(0, 400).First(x => Images.PixelAt(frame, 400, x, 120) is { R: > 180, G: < 80 });
        Assert.InRange(redLeft, 10 + 20 * 3 - 2, 10 + 20 * 3 + 2);
    }

    /// <summary>Root mean square of the left channel between two times, in seconds.</summary>
    private static double Rms(short[] samples, double from, double to)
    {
        int start = (int)(from * AudioMixer.SampleRate), end = (int)(to * AudioMixer.SampleRate);
        double sum = 0;
        for (int i = start; i < end; i++)
            sum += (double)samples[i * AudioMixer.Channels] * samples[i * AudioMixer.Channels];
        return Math.Sqrt(sum / (end - start));
    }

    [Fact]
    public void Audio_IsEncodedInTime_WithTheVideo()
    {
        const int fps = 30;
        const double toneFrom = 0.5, seconds = 1.0;
        const int chunk = AudioMixer.SampleRate / 100; // 10 ms
        using (var encoder = new Mp4Encoder(_path, 320, 240, fps, audio: true))
        {
            int frame = 0;
            for (int pos = 0; pos < AudioMixer.SampleRate * seconds; pos += chunk)
            {
                var samples = new short[chunk * AudioMixer.Channels];
                for (int i = 0; i < chunk; i++)
                {
                    double t = (double)(pos + i) / AudioMixer.SampleRate;
                    short value = t >= toneFrom ? (short)(8000 * Math.Sin(2 * Math.PI * 440 * t)) : (short)0;
                    samples[i * 2] = samples[i * 2 + 1] = value;
                }
                encoder.AddAudio(samples, TimeSpan.FromTicks(pos * TimeSpan.TicksPerSecond / AudioMixer.SampleRate));
                // Interleave the video frames by time, as the recorder does.
                for (; frame * AudioMixer.SampleRate / fps <= pos; frame++)
                    encoder.AddFrame(Images.Solid(320, 240, Colors.White), TimeSpan.FromSeconds((double)frame / fps));
            }
            encoder.Finish(TimeSpan.FromSeconds(seconds));
        }

        var audio = Mp4Reader.ReadAudio(_path);
        Assert.NotNull(audio);
        Assert.InRange(audio.Length / AudioMixer.Channels / (double)AudioMixer.SampleRate, seconds - 0.05, seconds + 0.1);
        Assert.InRange(Rms(audio, 0.05, 0.4), 0, 100);      // silence
        Assert.InRange(Rms(audio, 0.6, 0.9), 4500, 7000);   // the tone: 8000 / sqrt(2) ≈ 5657
        Assert.Equal(30, Mp4Reader.Read(_path).Frames.Count);
    }

    [Fact]
    public void WithoutAudio_HasNoAudioTrack()
    {
        EncodeMovingBox(320, 240, 5, 30);
        Assert.Null(Mp4Reader.ReadAudio(_path));
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(100, 1)]
    public void TooSmall_Throws(int width, int height) =>
        Assert.Throws<ArgumentException>(() => new Mp4Encoder(_path, width, height, 30));
}
