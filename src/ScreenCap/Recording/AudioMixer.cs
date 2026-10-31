namespace ScreenCap.Recording;

/// <summary>
/// Mixes several audio sources into one 16-bit stereo stream laid out on the recording clock.
/// Positions are in frames (one sample per channel) since the recording started.
/// </summary>
/// <remarks>
/// Each source's samples are placed where the clock says they were captured, so a source that delivers
/// nothing for a while (loopback while no sound plays) leaves silence, and the sound that follows still
/// lines up with the video. Small timing jitter is ignored so continuous sources play back without gaps.
/// </remarks>
internal sealed class AudioMixer
{
    public const int SampleRate = 48000, Channels = 2;

    /// <summary>A source this far behind the clock has skipped audio: the gap is filled with silence.</summary>
    public static readonly int GapFrames = SampleRate * 40 / 1000;

    /// <summary>A source this far ahead of the clock (its device clock runs fast) is pulled back to it.</summary>
    public static readonly int AheadFrames = SampleRate * 200 / 1000;

    private sealed class Track
    {
        public readonly List<short> Samples = [];
        public long Start; // frame position of Samples[0]
        public long End => Start + Samples.Count / Channels;
    }

    private readonly Track[] _tracks;

    public AudioMixer(int sources) => _tracks = Enumerable.Range(0, sources).Select(_ => new Track()).ToArray();

    /// <summary>Frames mixed so far; the next <see cref="Mix"/> starts here.</summary>
    public long Position { get; private set; }

    /// <summary>Adds samples from <paramref name="source"/> whose last frame was captured at <paramref name="capturedEnd"/>.</summary>
    public void Add(int source, ReadOnlySpan<short> samples, long capturedEnd)
    {
        var track = _tracks[source];
        int frames = samples.Length / Channels;
        long start = capturedEnd - frames;
        long behind = start - track.End;
        if (behind > GapFrames)
            track.Samples.AddRange(new short[behind * Channels]);
        else if (-behind > AheadFrames)
        {
            int skip = (int)Math.Min(frames, -behind);
            samples = samples[(skip * Channels)..];
        }
        track.Samples.AddRange(samples);
        DropMixed(track);
    }

    /// <summary>Mixes everything up to frame <paramref name="until"/>; sources without audio there are silent.</summary>
    /// <returns>Interleaved samples from <see cref="Position"/> to <paramref name="until"/>.</returns>
    public short[] Mix(long until)
    {
        int frames = (int)Math.Max(0, until - Position);
        var sum = new int[frames * Channels];
        foreach (var track in _tracks)
        {
            DropMixed(track);
            long offset = track.Start - Position;
            int take = (int)Math.Clamp(Math.Min(frames - offset, track.Samples.Count / Channels), 0, frames);
            for (int i = 0; i < take * Channels; i++)
                sum[offset * Channels + i] += track.Samples[i];
            track.Samples.RemoveRange(0, take * Channels);
            track.Start += take;
            if (track.Samples.Count == 0 && track.Start < until)
                track.Start = until; // nothing buffered: the source is silent up to here
        }

        var mixed = new short[sum.Length];
        for (int i = 0; i < sum.Length; i++)
            mixed[i] = (short)Math.Clamp(sum[i], short.MinValue, short.MaxValue);
        Position = Math.Max(Position, until);
        return mixed;
    }

    /// <summary>Discards samples that fall before <see cref="Position"/>: that part of the output is already written.</summary>
    private void DropMixed(Track track)
    {
        if (track.Start >= Position)
            return;
        int drop = (int)Math.Min(Position - track.Start, track.Samples.Count / Channels);
        track.Samples.RemoveRange(0, drop * Channels);
        track.Start = track.Samples.Count == 0 ? Position : track.Start + drop;
    }
}
