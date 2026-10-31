using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Recording;

[Flags]
internal enum AudioSources
{
    None = 0,
    /// <summary>What the default speakers play (WASAPI loopback).</summary>
    System = 1,
    Microphone = 2,
}

/// <summary>
/// Captures and mixes the chosen audio sources on its own thread, following the recording clock:
/// audio arriving while paused is dropped, and the mix trails the clock a little so late packets still fit.
/// </summary>
internal sealed class AudioRecorder
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(150);

    private readonly AudioSources _sources;
    private readonly Func<TimeSpan> _clock;
    private readonly Func<bool> _isPaused;
    private readonly Action<short[], TimeSpan> _output;
    private readonly ManualResetEventSlim _stop = new();
    private Task? _task;
    private TimeSpan _end;

    /// <param name="clock">Recording time; stands still while paused.</param>
    /// <param name="output">Receives the mixed interleaved samples and their start time, on the audio thread.</param>
    public AudioRecorder(AudioSources sources, Func<TimeSpan> clock, Func<bool> isPaused, Action<short[], TimeSpan> output)
    {
        _sources = sources;
        _clock = clock;
        _isPaused = isPaused;
        _output = output;
    }

    /// <summary>Opens the devices and starts capturing.</summary>
    /// <returns>The sources that couldn't be opened, e.g. no microphone; they are left out (silent).</returns>
    public Task<AudioSources> StartAsync()
    {
        var ready = new TaskCompletionSource<AudioSources>(TaskCreationOptions.RunContinuationsAsynchronously);
        _task = Task.Factory.StartNew(() => Run(ready), TaskCreationOptions.LongRunning);
        return ready.Task;
    }

    /// <summary>Writes the rest of the audio up to <paramref name="end"/> and stops. Blocks until done.</summary>
    public void Stop(TimeSpan end)
    {
        _end = end;
        _stop.Set();
        _task?.Wait();
    }

    private void Run(TaskCompletionSource<AudioSources> ready)
    {
        bool com = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED) >= 0;
        var endpoints = new List<AudioEndpoint?>();
        try
        {
            var failed = AudioSources.None;
            foreach (var source in new[] { AudioSources.System, AudioSources.Microphone })
            {
                if (!_sources.HasFlag(source))
                    continue;
                try
                {
                    endpoints.Add(new AudioEndpoint(loopback: source == AudioSources.System));
                }
                catch (Exception)
                {
                    failed |= source;
                }
            }
            ready.SetResult(failed);

            var mixer = new AudioMixer(endpoints.Count);
            while (!_stop.Wait(Interval))
            {
                Read(endpoints, mixer);
                MixUntil(mixer, _clock() - Latency);
            }
            Read(endpoints, mixer);
            MixUntil(mixer, _end);
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
        }
        finally
        {
            foreach (var endpoint in endpoints)
                endpoint?.Dispose();
            if (com)
                CoUninitialize();
        }
    }

    private void Read(List<AudioEndpoint?> endpoints, AudioMixer mixer)
    {
        long now = Frames(_clock());
        bool paused = _isPaused();
        for (int i = 0; i < endpoints.Count; i++)
        {
            if (endpoints[i] is not { } endpoint)
                continue;
            short[] samples;
            try
            {
                samples = endpoint.Read();
            }
            catch (Exception)
            {
                // The device went away (e.g. headphones unplugged): keep recording without it.
                endpoint.Dispose();
                endpoints[i] = null;
                continue;
            }
            if (!paused && samples.Length > 0)
                mixer.Add(i, samples, now);
        }
    }

    private void MixUntil(AudioMixer mixer, TimeSpan time)
    {
        long start = mixer.Position;
        long until = Frames(time);
        if (until <= start)
            return;
        _output(mixer.Mix(until), TimeSpan.FromTicks(start * TimeSpan.TicksPerSecond / AudioMixer.SampleRate));
    }

    private static long Frames(TimeSpan time) => time.Ticks * AudioMixer.SampleRate / TimeSpan.TicksPerSecond;
}
