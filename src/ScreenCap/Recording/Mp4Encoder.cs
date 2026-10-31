using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Recording;

/// <summary>
/// Encodes frames to an H.264 MP4 file with the Media Foundation sink writer built into Windows,
/// optionally with an AAC audio track. Must be created, used and disposed on the same thread.
/// </summary>
internal sealed unsafe class Mp4Encoder : IFrameEncoder, IAudioEncoder
{
    private const uint AacBytesPerSecond = 24000; // 192 kbps; the AAC encoder accepts 12000, 16000, 20000 or 24000

    private readonly int _sourceWidth, _width, _height;
    private readonly bool _comInitialized;
    private IntPtr _writer;
    private uint _stream;
    private uint _audioStream = uint.MaxValue; // none

    // A sample is written only once the next frame arrives, because its duration depends on that timestamp.
    private IntPtr _pending;
    private long _pendingTime = -1;

    /// <param name="audio">Add an audio track, fed through <see cref="AddAudio"/>.</param>
    public Mp4Encoder(string path, int width, int height, int fps, bool audio = false)
    {
        // H.264 needs even dimensions; drop the odd last column/row.
        _sourceWidth = width;
        _width = width & ~1;
        _height = height & ~1;
        if (_width < 2 || _height < 2)
            throw new ArgumentException("錄製範圍太小。");

        _comInitialized = CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED) >= 0;
        try
        {
            Check(MFStartup(MF_VERSION, 0));
        }
        catch
        {
            if (_comInitialized)
                CoUninitialize();
            throw;
        }

        try
        {
            Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out _writer));

            IntPtr output = CreateVideoType(MFVideoFormat_H264, fps);
            try
            {
                uint bitrate = (uint)Math.Clamp((long)_width * _height * fps / 5, 1_000_000, 40_000_000);
                SetUInt32(output, MF_MT_AVG_BITRATE, bitrate);
                SetUInt32(output, MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
                _stream = AddStream(_writer, output);
            }
            finally
            {
                Release(output);
            }

            IntPtr input = CreateVideoType(MFVideoFormat_RGB32, fps);
            try
            {
                SetUInt32(input, MF_MT_DEFAULT_STRIDE, (uint)(_width * 4)); // top-down rows
                SetInputType(input);
            }
            finally
            {
                Release(input);
            }

            if (audio)
                AddAudioStream();

            BeginWriting(_writer);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>AAC out, 16-bit PCM in, in the <see cref="AudioMixer"/> format.</summary>
    private void AddAudioStream()
    {
        IntPtr output = CreateAudioType(MFAudioFormat_AAC);
        try
        {
            SetUInt32(output, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AacBytesPerSecond);
            _audioStream = AddStream(_writer, output);
        }
        finally
        {
            Release(output);
        }

        IntPtr input = CreateAudioType(MFAudioFormat_PCM);
        try
        {
            SetUInt32(input, MF_MT_AUDIO_BLOCK_ALIGNMENT, AudioMixer.Channels * 2);
            SetUInt32(input, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AudioMixer.SampleRate * AudioMixer.Channels * 2);
            Check(SetInputMediaType(_writer, _audioStream, input, IntPtr.Zero));
        }
        finally
        {
            Release(input);
        }
    }

    private static IntPtr CreateAudioType(Guid subtype)
    {
        Check(MFCreateMediaType(out var type));
        SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        SetGuid(type, MF_MT_SUBTYPE, subtype);
        SetUInt32(type, MF_MT_AUDIO_NUM_CHANNELS, AudioMixer.Channels);
        SetUInt32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioMixer.SampleRate);
        SetUInt32(type, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        return type;
    }

    private IntPtr CreateVideoType(Guid subtype, int fps)
    {
        Check(MFCreateMediaType(out var type));
        SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Video);
        SetGuid(type, MF_MT_SUBTYPE, subtype);
        SetUInt32(type, MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
        SetPair(type, MF_MT_FRAME_SIZE, (uint)_width, (uint)_height);
        SetPair(type, MF_MT_FRAME_RATE, (uint)fps, 1);
        SetPair(type, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
        return type;
    }

    /// <summary>Prefers quality-based VBR, which keeps text sharp and static screens small.</summary>
    private void SetInputType(IntPtr input)
    {
        Check(MFCreateAttributes(out var parameters, 2));
        try
        {
            SetUInt32(parameters, CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_Quality);
            SetUInt32(parameters, CODECAPI_AVEncCommonQuality, 75);
            if (SetInputMediaType(_writer, _stream, input, parameters) >= 0)
                return;
        }
        finally
        {
            Release(parameters);
        }
        // Encoder doesn't accept those settings: fall back to its default (bitrate-based) mode.
        Check(SetInputMediaType(_writer, _stream, input, IntPtr.Zero));
    }

    public void AddFrame(byte[] bgra, TimeSpan timestamp)
    {
        long time = Math.Max(timestamp.Ticks, _pendingTime + 1); // 100 ns units, strictly increasing
        IntPtr sample = CreateSample(bgra);
        WritePending(time);
        _pending = sample;
        _pendingTime = time;
    }

    public void AddAudio(short[] samples, TimeSpan timestamp)
    {
        if (_audioStream == uint.MaxValue || samples.Length == 0)
            return;
        uint length = (uint)(samples.Length * 2);
        IntPtr sample = CreateSample(length, data =>
        {
            fixed (short* src = samples)
                Buffer.MemoryCopy(src, data, length, length);
        });
        try
        {
            long frames = samples.Length / AudioMixer.Channels;
            SetSampleTime(sample, timestamp.Ticks);
            SetSampleDuration(sample, frames * TimeSpan.TicksPerSecond / AudioMixer.SampleRate);
            WriteSample(_writer, _audioStream, sample);
        }
        finally
        {
            Release(sample);
        }
    }

    public void Finish(TimeSpan endTime)
    {
        WritePending(Math.Max(endTime.Ticks, _pendingTime + 1));
        FinalizeWriter(_writer);
    }

    private IntPtr CreateSample(byte[] bgra)
    {
        int rowBytes = _width * 4;
        return CreateSample((uint)(rowBytes * _height), data =>
        {
            fixed (byte* src = bgra)
            {
                for (int y = 0; y < _height; y++)
                    Buffer.MemoryCopy(src + (long)y * _sourceWidth * 4, data + (long)y * rowBytes, rowBytes, rowBytes);
            }
        });
    }

    private delegate void FillBuffer(byte* data);

    /// <summary>Creates a sample with one buffer of <paramref name="length"/> bytes, filled by <paramref name="fill"/>.</summary>
    private static IntPtr CreateSample(uint length, FillBuffer fill)
    {
        Check(MFCreateMemoryBuffer(length, out var buffer));
        try
        {
            byte* data = Lock(buffer);
            try
            {
                fill(data);
            }
            finally
            {
                Unlock(buffer);
            }
            SetCurrentLength(buffer, length);

            Check(MFCreateSample(out var sample));
            try
            {
                AddBuffer(sample, buffer);
            }
            catch
            {
                Release(sample);
                throw;
            }
            return sample;
        }
        finally
        {
            Release(buffer); // the sample holds its own reference
        }
    }

    private void WritePending(long now)
    {
        if (_pending == IntPtr.Zero)
            return;
        try
        {
            SetSampleTime(_pending, _pendingTime);
            SetSampleDuration(_pending, now - _pendingTime);
            WriteSample(_writer, _stream, _pending);
        }
        finally
        {
            Release(_pending);
            _pending = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Release(_pending);
        _pending = IntPtr.Zero;
        Release(_writer);
        _writer = IntPtr.Zero;
        MFShutdown();
        if (_comInitialized)
            CoUninitialize();
    }
}
