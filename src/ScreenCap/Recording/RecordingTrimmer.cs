using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Recording;

/// <summary>When each frame of a recording starts, and small pictures of it to scrub through.</summary>
/// <param name="FrameTimes">Start time of every frame, ascending; the first is zero.</param>
/// <param name="Thumbnails">Pictures at some of the frame times, ascending.</param>
internal sealed record RecordingTimeline(IReadOnlyList<TimeSpan> FrameTimes, TimeSpan Duration, IReadOnlyList<(TimeSpan Time, BitmapSource Image)> Thumbnails)
{
    /// <summary>The thumbnail showing at <paramref name="time"/>: the last one at or before it.</summary>
    public BitmapSource? ThumbnailAt(TimeSpan time)
    {
        BitmapSource? found = Thumbnails.Count > 0 ? Thumbnails[0].Image : null;
        foreach (var (t, image) in Thumbnails)
        {
            if (t > time)
                break;
            found = image;
        }
        return found;
    }
}

/// <summary>Cuts the start and end off a finished GIF or MP4 recording.</summary>
internal static class RecordingTrimmer
{
    /// <summary>Thumbnails are kept at most this many, spread over the recording, so long recordings stay small in memory.</summary>
    private const int MaxThumbnails = 300;
    private const int ThumbnailSize = 480; // longest side, in pixels

    /// <param name="progress">Told how much is done, from 0 to 1.</param>
    public static RecordingTimeline Load(string path, RecordingFormat format, CancellationToken cancel = default, IProgress<double>? progress = null)
    {
        var reporter = new ProgressReporter(progress);
        var timeline = format == RecordingFormat.Gif ? LoadGif(path, cancel, reporter) : LoadMp4(path, cancel, reporter);
        reporter.Report(1);
        return timeline;
    }

    /// <summary>
    /// Plays <paramref name="from"/> to <paramref name="to"/> at full size: decodes the frames in order and hands each
    /// to <paramref name="show"/> (top-down BGRA, width, height) when <paramref name="position"/> reaches its time.
    /// Frames decoded too late to show in time are skipped. Returns at the end or when cancelled.
    /// </summary>
    /// <param name="position">The playback position now, from the caller's clock.</param>
    public static void Play(string path, RecordingFormat format, TimeSpan from, TimeSpan to, Func<TimeSpan> position,
        Action<byte[], int, int> show, CancellationToken cancel)
    {
        var late = TimeSpan.FromMilliseconds(100);
        foreach (var (time, pixels, width, height) in FramesFrom(path, format, from, cancel))
        {
            if (time >= to)
                break;
            var wait = time - position();
            if (wait > TimeSpan.Zero && cancel.WaitHandle.WaitOne(wait))
                break;
            if (cancel.IsCancellationRequested)
                break;
            if (position() - time <= late || time <= from)
                show(pixels, width, height);
        }
    }

    /// <summary>The frame showing at <paramref name="from"/> (stamped <paramref name="from"/>), then every frame after it.</summary>
    private static IEnumerable<(TimeSpan Time, byte[] Pixels, int Width, int Height)> FramesFrom(string path, RecordingFormat format, TimeSpan from,
        CancellationToken cancel)
    {
        if (format == RecordingFormat.Gif)
        {
            var bytes = File.ReadAllBytes(path);
            var gif = GifFile.Parse(bytes);
            var times = gif.FrameTimes();
            int first = FrameRange(times, from, from).First;
            var canvas = new byte[gif.Width * gif.Height * 4];
            var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            for (int i = 0; i < gif.Frames.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                Composite(decoder.Frames[i], gif.Frames[i], canvas, gif.Width);
                if (i >= first)
                    yield return (i == first ? from : times[i], (byte[])canvas.Clone(), gif.Width, gif.Height); // the canvas changes on
            }
            yield break;
        }

        using var reader = Mp4Reader.OpenVideo(path);
        reader.Seek(from); // decoding resumes at the key frame before it
        Mp4Reader.Frame? showing = null;
        while (reader.ReadFrame() is { } frame)
        {
            cancel.ThrowIfCancellationRequested();
            if (frame.Time <= from)
            {
                showing = frame;
                continue;
            }
            if (showing != null)
            {
                yield return (from, showing.Bgra, reader.Width, reader.Height);
                showing = null;
            }
            yield return (frame.Time, frame.Bgra, reader.Width, reader.Height);
        }
        if (showing != null)
            yield return (from, showing.Bgra, reader.Width, reader.Height); // the last frame
    }

    /// <summary>The frame showing at <paramref name="time"/>, at full size; slower than the thumbnails, for when scrubbing stops.</summary>
    public static BitmapSource FrameAt(string path, RecordingFormat format, TimeSpan time, CancellationToken cancel = default)
    {
        int width, height;
        byte[] pixels;
        if (format == RecordingFormat.Gif)
        {
            var bytes = File.ReadAllBytes(path);
            var gif = GifFile.Parse(bytes);
            (width, height) = (gif.Width, gif.Height);
            int index = FrameRange(gif.FrameTimes(), time, time).First;
            pixels = new byte[width * height * 4];
            var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            for (int i = 0; i <= index; i++)
            {
                cancel.ThrowIfCancellationRequested();
                Composite(decoder.Frames[i], gif.Frames[i], pixels, width);
            }
        }
        else
        {
            using var reader = Mp4Reader.OpenVideo(path);
            (width, height) = (reader.Width, reader.Height);
            reader.Seek(time);
            // The last frame at or before the time; decoding starts at the key frame before it.
            Mp4Reader.Frame? showing = null;
            while (reader.ReadFrame() is { } frame)
            {
                cancel.ThrowIfCancellationRequested();
                if (frame.Time > time && showing != null)
                    break;
                showing = frame;
                if (frame.Time > time)
                    break; // the time is before the first frame
            }
            pixels = showing?.Bgra ?? new byte[width * height * 4];
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes the part of <paramref name="source"/> from <paramref name="start"/> to <paramref name="end"/> to <paramref name="destination"/>.</summary>
    /// <param name="progress">Told how much is done, from 0 to 1.</param>
    public static void Trim(string source, string destination, RecordingFormat format, TimeSpan start, TimeSpan end, int mp4FramesPerSecond = 30,
        IProgress<double>? progress = null)
    {
        var reporter = new ProgressReporter(progress);
        if (format == RecordingFormat.Gif)
            TrimGif(source, destination, start, end, reporter);
        else
            TrimMp4(source, destination, start, end, mp4FramesPerSecond, reporter);
        reporter.Report(1);
    }

    /// <summary>Passes progress on only when it moves by a percent, so decoding hundreds of frames a second doesn't flood the UI.</summary>
    private sealed class ProgressReporter(IProgress<double>? progress)
    {
        private int _percent = -1;

        public void Report(double fraction)
        {
            if (progress == null || double.IsNaN(fraction))
                return;
            int percent = (int)(Math.Clamp(fraction, 0, 1) * 100);
            if (percent == _percent)
                return;
            _percent = percent;
            progress.Report(percent / 100.0);
        }

        /// <summary>Reports how far <paramref name="done"/> is through <paramref name="total"/>.</summary>
        public void Report(TimeSpan done, TimeSpan total)
        {
            if (total > TimeSpan.Zero)
                Report(done / total);
        }
    }

    // ---------------------------------------------------------------- GIF

    private static RecordingTimeline LoadGif(string path, CancellationToken cancel, ProgressReporter progress)
    {
        var bytes = File.ReadAllBytes(path);
        var gif = GifFile.Parse(bytes);
        var times = gif.FrameTimes();
        var thumbs = new List<(TimeSpan, BitmapSource)>();
        var due = ThumbnailTimes(times, gif.Duration);
        var canvas = new byte[gif.Width * gif.Height * 4];
        var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        for (int i = 0; i < gif.Frames.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            Composite(decoder.Frames[i], gif.Frames[i], canvas, gif.Width);
            if (due.Contains(i))
                thumbs.Add((times[i], Thumbnail(canvas, gif.Width, gif.Height)));
            progress.Report((i + 1.0) / gif.Frames.Count);
        }
        return new RecordingTimeline(times, gif.Duration, thumbs);
    }

    /// <summary>
    /// Frames are copied as they are, except the first kept one: it only holds what changed since the frame
    /// before, so it is replaced by the whole picture at that point. The frames after it change that picture as before.
    /// </summary>
    private static void TrimGif(string source, string destination, TimeSpan start, TimeSpan end, ProgressReporter progress)
    {
        var bytes = File.ReadAllBytes(source);
        var gif = GifFile.Parse(bytes);
        var times = gif.FrameTimes();
        var (first, last) = FrameRange(times, start, end);

        var canvas = new byte[gif.Width * gif.Height * 4];
        var decoder = new GifBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        // Building the first kept frame is the slow part; the rest is a copy.
        for (int i = 0; i <= first; i++)
        {
            Composite(decoder.Frames[i], gif.Frames[i], canvas, gif.Width);
            progress.Report(0.9 * (i + 1) / (first + 1));
        }

        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        using var encoder = new GifEncoder(output, gif.Width, gif.Height);
        encoder.AddFrame(canvas, TimeSpan.Zero);
        encoder.Flush(TimeSpan.FromMilliseconds(gif.Frames[first].DelayCs * 10));
        if (last > first)
        {
            int from = gif.Frames[first + 1].Offset;
            encoder.WriteRaw(bytes.AsSpan(from, gif.Frames[last].End - from));
        }
        encoder.Finish(TimeSpan.Zero);
    }

    /// <summary>Paints a decoded frame over the picture so far; its transparent pixels leave the picture as it was.</summary>
    private static void Composite(BitmapFrame frame, GifFile.Frame info, byte[] canvas, int width)
    {
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        int height = canvas.Length / 4 / width;
        for (int y = 0; y < h && info.Top + y < height; y++)
        {
            for (int x = 0; x < w && info.Left + x < width; x++)
            {
                int s = (y * w + x) * 4;
                if (pixels[s + 3] == 0)
                    continue;
                int d = ((info.Top + y) * width + info.Left + x) * 4;
                canvas[d] = pixels[s];
                canvas[d + 1] = pixels[s + 1];
                canvas[d + 2] = pixels[s + 2];
                canvas[d + 3] = 255;
            }
        }
    }

    // ---------------------------------------------------------------- MP4

    private static RecordingTimeline LoadMp4(string path, CancellationToken cancel, ProgressReporter progress)
    {
        using var reader = Mp4Reader.OpenVideo(path);
        var expected = reader.Duration;
        if (expected <= TimeSpan.Zero)
            return LoadMp4TwoPasses(reader, path, cancel, progress);

        // One pass: the file's duration says how to spread the thumbnails. A frame gets one if it is showing at one
        // of the evenly spread times; that is only known once the next frame's time is, so each waits for the next.
        var times = new List<TimeSpan>();
        var thumbs = new List<(TimeSpan, BitmapSource)>();
        long spacing = Math.Max(1, expected.Ticks / MaxThumbnails);
        long nextDue = 0;
        Mp4Reader.Frame? previous = null;
        // The frame shows until the time given: it gets a thumbnail if a due time falls before then.
        void Consider(Mp4Reader.Frame frame, TimeSpan until)
        {
            if (nextDue >= until.Ticks)
                return;
            thumbs.Add((frame.Time, Thumbnail(frame.Bgra, reader.Width, reader.Height)));
            nextDue = (until.Ticks + spacing - 1) / spacing * spacing; // the first due time at or after it
        }
        while (reader.ReadFrame() is { } frame)
        {
            cancel.ThrowIfCancellationRequested();
            if (previous != null)
                Consider(previous, frame.Time);
            times.Add(frame.Time);
            previous = frame;
            progress.Report(frame.Time, expected);
        }
        if (previous == null)
            return new RecordingTimeline([TimeSpan.Zero], TimeSpan.Zero, []);
        times[0] = TimeSpan.Zero; // the first frame shows from the start, even if stamped a little after it
        var duration = Mp4Duration(times);
        Consider(previous, duration);
        if (thumbs.Count > 0)
            thumbs[0] = (TimeSpan.Zero, thumbs[0].Item2); // shown from the start, like the first frame
        return new RecordingTimeline(times, duration, thumbs);
    }

    /// <summary>For a file that doesn't say how long it is: the frame times first, to know how to spread the thumbnails.</summary>
    private static RecordingTimeline LoadMp4TwoPasses(Mp4Reader first, string path, CancellationToken cancel, ProgressReporter progress)
    {
        var times = new List<TimeSpan>();
        while (first.ReadFrame() is { } frame)
        {
            cancel.ThrowIfCancellationRequested();
            times.Add(frame.Time);
        }
        progress.Report(0.5);
        if (times.Count == 0)
            return new RecordingTimeline([TimeSpan.Zero], TimeSpan.Zero, []);
        times[0] = TimeSpan.Zero;
        var duration = Mp4Duration(times);
        var due = ThumbnailTimes(times, duration);
        var thumbs = new List<(TimeSpan, BitmapSource)>();
        using (var reader = Mp4Reader.OpenVideo(path))
        {
            for (int i = 0; reader.ReadFrame() is { } frame; i++)
            {
                cancel.ThrowIfCancellationRequested();
                if (due.Contains(i))
                    thumbs.Add((i == 0 ? TimeSpan.Zero : frame.Time, Thumbnail(frame.Bgra, reader.Width, reader.Height)));
                progress.Report(0.5 + 0.5 * (i + 1) / times.Count);
            }
        }
        return new RecordingTimeline(times, duration, thumbs);
    }

    /// <summary>The last frame shows for as long as the average frame.</summary>
    private static TimeSpan Mp4Duration(List<TimeSpan> times) =>
        times[^1] + (times.Count > 1 ? (times[^1] - times[0]) / (times.Count - 1) : TimeSpan.FromSeconds(1.0 / 30));

    /// <summary>Decodes the kept part and encodes it again: H.264 frames depend on the ones before them, so they can't simply be cut out.</summary>
    private static void TrimMp4(string source, string destination, TimeSpan start, TimeSpan end, int fps, ProgressReporter progress)
    {
        using var audio = Mp4Reader.OpenAudio(source);
        using var video = Mp4Reader.OpenVideo(source);
        using var encoder = new Mp4Encoder(destination, video.Width, video.Height, fps, audio != null);

        // Audio is written alongside the video, up to each frame's time, as the recorder does.
        (TimeSpan Time, short[] Samples)? block = audio?.ReadAudio();
        void WriteAudioUntil(TimeSpan limit)
        {
            while (block is { } current && current.Time < limit)
            {
                var (time, samples) = current;
                // Cut the block to the kept range, at whole sample frames (one sample per channel).
                int count = samples.Length / AudioMixer.Channels;
                int from = Math.Clamp((int)Math.Ceiling((start - time).TotalSeconds * AudioMixer.SampleRate), 0, count);
                int to = Math.Clamp((int)((end - time).TotalSeconds * AudioMixer.SampleRate), 0, count);
                if (to > from)
                {
                    var kept = samples.AsSpan(from * AudioMixer.Channels, (to - from) * AudioMixer.Channels).ToArray();
                    encoder.AddAudio(kept, time + TimeSpan.FromSeconds((double)from / AudioMixer.SampleRate) - start);
                }
                block = audio!.ReadAudio();
            }
        }

        // The frame showing at the start is the last one at or before it.
        Mp4Reader.Frame? showing = null;
        bool started = false;
        while (video.ReadFrame() is { } frame && frame.Time < end)
        {
            if (frame.Time <= start)
            {
                showing = frame;
                continue;
            }
            if (!started && showing != null)
                encoder.AddFrame(showing.Bgra, TimeSpan.Zero);
            started = true;
            WriteAudioUntil(frame.Time);
            encoder.AddFrame(frame.Bgra, frame.Time - start);
            progress.Report(frame.Time - start, end - start);
        }
        if (!started && showing != null)
            encoder.AddFrame(showing.Bgra, TimeSpan.Zero); // a single frame was kept
        WriteAudioUntil(end);
        encoder.Finish(end - start);
    }

    // ---------------------------------------------------------------- shared

    /// <summary>The first and last frame shown between <paramref name="start"/> and <paramref name="end"/>.</summary>
    internal static (int First, int Last) FrameRange(IReadOnlyList<TimeSpan> times, TimeSpan start, TimeSpan end)
    {
        int first = 0;
        while (first + 1 < times.Count && times[first + 1] <= start)
            first++;
        int last = first;
        while (last + 1 < times.Count && times[last + 1] < end)
            last++;
        return (first, last);
    }

    /// <summary>Indices of the frames to keep a thumbnail of: all of them, or evenly spread when there are many.</summary>
    private static HashSet<int> ThumbnailTimes(IReadOnlyList<TimeSpan> times, TimeSpan duration)
    {
        var due = new HashSet<int>();
        if (times.Count <= MaxThumbnails)
        {
            for (int i = 0; i < times.Count; i++)
                due.Add(i);
            return due;
        }
        for (int k = 0; k < MaxThumbnails; k++)
        {
            var t = duration * k / MaxThumbnails;
            due.Add(FrameRange(times, t, t).First);
        }
        return due;
    }

    private static BitmapSource Thumbnail(byte[] bgra, int width, int height)
    {
        var full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, width * 4);
        double scale = Math.Min(1, (double)ThumbnailSize / Math.Max(width, height));
        BitmapSource small = full;
        if (scale < 1)
            small = new TransformedBitmap(full, new ScaleTransform(scale, scale));
        // Copy out the small pixels, so the full-size frame isn't kept alive by the thumbnail.
        int w = small.PixelWidth, h = small.PixelHeight;
        var pixels = new byte[w * h * 4];
        small.CopyPixels(pixels, w * 4, 0);
        var thumb = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
        thumb.Freeze();
        return thumb;
    }
}

/// <summary>The frame layout of a GIF: where each frame's bytes are, its position and how long it shows.</summary>
internal sealed class GifFile
{
    /// <param name="Offset">Start of the frame's graphic control extension (or of its image, if it has none).</param>
    /// <param name="End">Just past the end of its image data.</param>
    /// <param name="DelayCs">How long it shows, in 1/100 s.</param>
    public sealed record Frame(int Offset, int End, int Left, int Top, int DelayCs);

    public int Width { get; private init; }
    public int Height { get; private init; }
    public List<Frame> Frames { get; } = new();

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Frames.Sum(f => f.DelayCs) * 10);

    public List<TimeSpan> FrameTimes()
    {
        var times = new List<TimeSpan>(Frames.Count);
        int cs = 0;
        foreach (var frame in Frames)
        {
            times.Add(TimeSpan.FromMilliseconds(cs * 10));
            cs += frame.DelayCs;
        }
        return times;
    }

    public static GifFile Parse(byte[] b)
    {
        if (b.Length < 13 || b[0] != 'G' || b[1] != 'I' || b[2] != 'F')
            throw new InvalidDataException("不是 GIF 檔。");
        var gif = new GifFile { Width = b[6] | b[7] << 8, Height = b[8] | b[9] << 8 };
        int pos = 13;
        if ((b[10] & 0x80) != 0)
            pos += 3 << ((b[10] & 7) + 1); // global color table

        int pendingOffset = -1, pendingDelay = 0;
        while (pos < b.Length)
        {
            switch (b[pos])
            {
                case 0x21: // extension
                    int start = pos;
                    byte label = b[pos + 1];
                    if (label == 0xF9 && b[pos + 2] >= 3)
                    {
                        pendingOffset = start;
                        pendingDelay = b[pos + 4] | b[pos + 5] << 8;
                    }
                    pos = SkipSubBlocks(b, pos + 2);
                    break;
                case 0x2C: // image
                    int imageStart = pos;
                    int left = b[pos + 1] | b[pos + 2] << 8, top = b[pos + 3] | b[pos + 4] << 8;
                    byte flags = b[pos + 9];
                    pos += 10;
                    if ((flags & 0x80) != 0)
                        pos += 3 << ((flags & 7) + 1); // local color table
                    pos = SkipSubBlocks(b, pos + 1); // after the LZW minimum code size
                    gif.Frames.Add(new Frame(pendingOffset >= 0 ? pendingOffset : imageStart, pos, left, top, pendingDelay));
                    pendingOffset = -1;
                    pendingDelay = 0;
                    break;
                case 0x3B: // trailer
                    return gif;
                default:
                    throw new InvalidDataException("GIF 檔的格式不正確。");
            }
        }
        return gif;
    }

    /// <returns>The position just past the block terminator.</returns>
    private static int SkipSubBlocks(byte[] b, int pos)
    {
        while (pos < b.Length && b[pos] != 0)
            pos += b[pos] + 1;
        return pos + 1;
    }
}
