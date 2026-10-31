using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using ScreenCap.Native;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Recording;

/// <summary>Writes captured frames to a file (GIF or MP4).</summary>
internal interface IFrameEncoder : IDisposable
{
    /// <param name="bgra">Top-down BGRA pixels of the full frame. The encoder may keep a reference; don't reuse the buffer.</param>
    /// <param name="timestamp">Time since the recording started.</param>
    void AddFrame(byte[] bgra, TimeSpan timestamp);

    void Finish(TimeSpan endTime);
}

/// <summary>An encoder that can also write an audio track.</summary>
internal interface IAudioEncoder
{
    /// <param name="samples">Interleaved 16-bit samples in the <see cref="AudioMixer"/> format.</param>
    void AddAudio(short[] samples, TimeSpan timestamp);
}

/// <summary>
/// Records a screen region through an <see cref="IFrameEncoder"/>. Frames are captured on one thread and
/// encoded on another, streaming to disk so memory use stays flat regardless of recording length.
/// </summary>
internal sealed class ScreenRecorder
{
    private const long MaxQueuedBytes = 256L << 20;

    private readonly Int32Rect _region; // physical screen pixels
    private readonly int _fps;
    private readonly Func<IFrameEncoder> _createEncoder;
    private readonly bool _highlightClicks;
    private readonly KeystrokeMonitor? _keys;
    private readonly AudioSources _audioSources;
    private readonly BlockingCollection<(byte[] Pixels, TimeSpan Time)> _frames;
    private readonly ConcurrentQueue<(short[] Samples, TimeSpan Time)> _audio = new();
    private AudioRecorder? _audioRecorder;
    private readonly ManualResetEventSlim _stopSignal = new();
    private readonly ManualResetEventSlim _running = new(true); // reset while paused

    // Paused time is left out: the clock stops, so timestamps continue where they left off.
    private readonly Stopwatch _clock = new();
    private readonly Lock _clockLock = new();
    private Task? _captureTask, _encodeTask;
    private TimeSpan _endTime;

    /// <param name="createEncoder">Called on the encoding thread, which also uses and disposes the encoder.</param>
    /// <param name="highlightClicks">Draw a ring around the cursor when a mouse button is pressed.</param>
    /// <param name="keys">If given, the keys pressed are shown at the bottom of the frames.</param>
    /// <param name="audio">Sound to record; the encoder must be an <see cref="IAudioEncoder"/>.</param>
    public ScreenRecorder(Int32Rect region, int fps, Func<IFrameEncoder> createEncoder, bool highlightClicks = false, KeystrokeMonitor? keys = null,
        AudioSources audio = AudioSources.None)
    {
        _keys = keys;
        _audioSources = audio;
        _region = region;
        _fps = fps;
        _createEncoder = createEncoder;
        _highlightClicks = highlightClicks;
        long frameBytes = (long)region.Width * region.Height * 4;
        _frames = new(boundedCapacity: (int)Math.Clamp(MaxQueuedBytes / frameBytes, 4, 60));
    }

    public TimeSpan Elapsed
    {
        get
        {
            lock (_clockLock)
                return _clock.Elapsed;
        }
    }

    public bool IsPaused => !_running.IsSet;

    /// <summary>Audio sources that were requested but couldn't be opened (e.g. no microphone).</summary>
    public AudioSources FailedAudio { get; private set; }

    /// <summary>Stops capturing frames until <see cref="Resume"/>; the paused time is not part of the recording.</summary>
    public void Pause()
    {
        lock (_clockLock)
        {
            _running.Reset();
            _clock.Stop();
        }
    }

    public void Resume()
    {
        lock (_clockLock)
        {
            _clock.Start();
            _running.Set();
        }
    }

    /// <summary>Creates the encoder, then starts capturing. Throws if the encoder can't be created.</summary>
    public async Task StartAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _encodeTask = Task.Factory.StartNew(() => EncodeLoop(ready), TaskCreationOptions.LongRunning);
        await ready.Task;
        if (_audioSources != AudioSources.None)
        {
            _audioRecorder = new AudioRecorder(_audioSources, () => Elapsed, () => IsPaused, (samples, time) => _audio.Enqueue((samples, time)));
            try
            {
                FailedAudio = await _audioRecorder.StartAsync();
            }
            catch
            {
                _audioRecorder = null;
                FailedAudio = _audioSources;
            }
        }
        lock (_clockLock)
            _clock.Start();
        _captureTask = Task.Factory.StartNew(CaptureLoop, TaskCreationOptions.LongRunning);
    }

    /// <summary>Stops capturing and waits for the encoder to finish writing the file.</summary>
    public async Task StopAsync()
    {
        _stopSignal.Set();
        _running.Set(); // wake a paused capture loop so it can exit
        // No UI context needed here, which also makes a blocking Wait() on exit safe.
        await Task.WhenAll(_captureTask!, _encodeTask!).ConfigureAwait(false);
    }

    private void CaptureLoop()
    {
        try
        {
            using var grabber = new RegionGrabber(_region, _highlightClicks, _keys);
            double interval = 1000.0 / _fps;
            for (long frame = 0; ;)
            {
                _running.Wait();
                if (_stopSignal.IsSet)
                    break;
                var time = Elapsed;
                if (IsPaused)
                    continue; // paused between the wait and reading the clock
                // If the encoder falls behind, drop the frame; the previous one is simply shown longer.
                _frames.TryAdd((grabber.Grab(), time));

                double wait = ++frame * interval - Elapsed.TotalMilliseconds;
                if (wait > 0)
                    _stopSignal.Wait(TimeSpan.FromMilliseconds(wait));
            }
        }
        finally
        {
            _endTime = Elapsed;
            try
            {
                _audioRecorder?.Stop(_endTime); // flushes the remaining audio into the queue
            }
            finally
            {
                _frames.CompleteAdding();
            }
        }
    }

    private void EncodeLoop(TaskCompletionSource ready)
    {
        IFrameEncoder encoder;
        try
        {
            encoder = _createEncoder();
        }
        catch (Exception ex)
        {
            ready.SetException(ex);
            return;
        }
        ready.SetResult();

        try
        {
            using (encoder)
            {
                foreach (var (pixels, time) in _frames.GetConsumingEnumerable())
                {
                    WriteAudio(encoder);
                    encoder.AddFrame(pixels, time);
                }
                WriteAudio(encoder);
                encoder.Finish(_endTime);
            }
        }
        catch
        {
            // Unblock the capture thread so StopAsync can surface the error.
            _stopSignal.Set();
            _running.Set();
            throw;
        }
    }
    private void WriteAudio(IFrameEncoder encoder)
    {
        while (_audio.TryDequeue(out var chunk))
            (encoder as IAudioEncoder)?.AddAudio(chunk.Samples, chunk.Time);
    }

    /// <summary>Reusable GDI resources for grabbing one region, including the mouse cursor.</summary>
    private sealed class RegionGrabber : IDisposable
    {
        private readonly Int32Rect _r;
        private readonly IntPtr _screenDc, _memDc, _bitmap, _oldBitmap;
        private BITMAPINFOHEADER _header;
        private readonly ClickPainter? _clicks;
        private readonly KeystrokeMonitor? _keys;
        private readonly KeyPainter? _keyPainter;

        public RegionGrabber(Int32Rect region, bool highlightClicks, KeystrokeMonitor? keys)
        {
            _r = region;
            var center = new POINT { X = region.X + region.Width / 2, Y = region.Y + region.Height / 2 };
            double scale = GetDpiForMonitor(MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
            if (highlightClicks)
                _clicks = new ClickPainter(scale);
            if (keys != null)
            {
                _keys = keys;
                _keyPainter = new KeyPainter(scale);
            }
            _screenDc = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(_screenDc);
            _bitmap = CreateCompatibleBitmap(_screenDc, region.Width, region.Height);
            _oldBitmap = SelectObject(_memDc, _bitmap);
            _header = new BITMAPINFOHEADER
            {
                biSize = 40,
                biWidth = region.Width,
                biHeight = -region.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
        }

        public byte[] Grab()
        {
            // No CAPTUREBLT here: it makes the cursor flicker when capturing repeatedly.
            BitBlt(_memDc, 0, 0, _r.Width, _r.Height, _screenDc, _r.X, _r.Y, SRCCOPY);
            _clicks?.Draw(_memDc, _r.X, _r.Y); // under the cursor
            CursorPainter.Draw(_memDc, _r.X, _r.Y);
            if (_keyPainter != null)
            {
                var (text, time) = _keys!.Latest;
                _keyPainter.Draw(_memDc, _r.Width, _r.Height, text, time, Environment.TickCount64);
            }

            var pixels = new byte[_r.Width * _r.Height * 4];
            SelectObject(_memDc, _oldBitmap);
            GetDIBits(_memDc, _bitmap, 0, (uint)_r.Height, pixels, ref _header, 0);
            SelectObject(_memDc, _bitmap);
            return pixels;
        }

        public void Dispose()
        {
            _clicks?.Dispose();
            _keyPainter?.Dispose();
            SelectObject(_memDc, _oldBitmap);
            DeleteObject(_bitmap);
            DeleteDC(_memDc);
            ReleaseDC(IntPtr.Zero, _screenDc);
        }
    }
}
