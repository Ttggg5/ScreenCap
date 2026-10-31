using ScreenCap.Recording;

namespace ScreenCap.Tests;

public class AudioMixerTests
{
    private const int Ms = AudioMixer.SampleRate / 1000; // frames per millisecond

    /// <summary>Interleaved stereo samples with the same value in every channel.</summary>
    private static short[] Tone(int frames, short value) => Enumerable.Repeat(value, frames * AudioMixer.Channels).ToArray();

    private static short At(short[] mixed, int frame) => mixed[frame * AudioMixer.Channels];

    [Fact]
    public void ContinuousSource_PlaysBackWithoutGaps()
    {
        var mixer = new AudioMixer(1);
        // 10 ms packets that arrive a little late and irregularly, as a real device delivers them.
        long[] arrivals = [12, 21, 33, 41, 52, 60, 73, 80, 91, 100];
        for (int i = 0; i < arrivals.Length; i++)
            mixer.Add(0, Tone(10 * Ms, (short)(i + 1)), arrivals[i] * Ms);

        var mixed = mixer.Mix(100 * Ms);

        Assert.Equal(100 * Ms * AudioMixer.Channels, mixed.Length);
        for (int i = 0; i < 10; i++)
            Assert.Equal(i + 1, At(mixed, i * 10 * Ms + 5 * Ms));
    }

    [Fact]
    public void SilentSource_LeavesSilence_AndLaterSoundLinesUpWithTheClock()
    {
        var mixer = new AudioMixer(1);
        mixer.Add(0, Tone(100 * Ms, 1000), 100 * Ms);
        Assert.Equal(1000, At(mixer.Mix(150 * Ms), 50 * Ms));

        // Loopback delivers nothing while no sound plays; sound resumes at 500 ms.
        Assert.All(mixer.Mix(400 * Ms), s => Assert.Equal(0, s));
        mixer.Add(0, Tone(50 * Ms, 2000), 550 * Ms);
        var mixed = mixer.Mix(600 * Ms); // frames 400..600 ms

        Assert.Equal(0, At(mixed, 90 * Ms));   // 490 ms
        Assert.Equal(2000, At(mixed, 110 * Ms)); // 510 ms
        Assert.Equal(2000, At(mixed, 140 * Ms)); // 540 ms
        Assert.Equal(0, At(mixed, 160 * Ms));   // 560 ms
    }

    [Fact]
    public void Sources_AreAddedTogether_AndClipped()
    {
        var mixer = new AudioMixer(2);
        mixer.Add(0, Tone(10 * Ms, 1000), 10 * Ms);
        mixer.Add(1, Tone(10 * Ms, 500), 10 * Ms);
        Assert.Equal(1500, At(mixer.Mix(10 * Ms), 5 * Ms));

        mixer.Add(0, Tone(10 * Ms, 30000), 20 * Ms);
        mixer.Add(1, Tone(10 * Ms, 30000), 20 * Ms);
        Assert.Equal(short.MaxValue, At(mixer.Mix(20 * Ms), 5 * Ms));
    }

    [Fact]
    public void AudioArrivingAfterItsTimeWasWritten_FollowsWithoutBeingLost()
    {
        var mixer = new AudioMixer(1);
        Assert.All(mixer.Mix(100 * Ms), s => Assert.Equal(0, s));

        // Captured 50..110 ms, but up to 100 ms has been written already: it continues from there.
        mixer.Add(0, Tone(60 * Ms, 700), 110 * Ms);
        var mixed = mixer.Mix(170 * Ms);

        Assert.Equal(700, At(mixed, 5 * Ms));
        Assert.Equal(700, At(mixed, 55 * Ms));
        Assert.Equal(0, At(mixed, 65 * Ms));
        Assert.Equal(170 * Ms, mixer.Position);
    }

    [Fact]
    public void SourceRunningAheadOfTheClock_IsPulledBack()
    {
        var mixer = new AudioMixer(1);
        // A device whose clock runs fast delivers 500 ms of audio within 100 ms of recording time.
        for (int i = 1; i <= 10; i++)
            mixer.Add(0, Tone(50 * Ms, (short)i), i * 10 * Ms);

        var mixed = mixer.Mix(100 * Ms);

        Assert.True(mixer.Mix(100 * Ms + AudioMixer.AheadFrames + 10 * Ms).Skip(AudioMixer.AheadFrames * AudioMixer.Channels).All(s => s == 0),
            "no more than the allowed lead stays buffered");
        Assert.NotEqual(0, At(mixed, 0));
    }

    [Fact]
    public void Mix_BeforeCurrentPosition_ReturnsNothing()
    {
        var mixer = new AudioMixer(1);
        mixer.Mix(50 * Ms);
        Assert.Empty(mixer.Mix(40 * Ms));
        Assert.Equal(50 * Ms, mixer.Position);
    }
}
