using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Recording;

/// <summary>
/// Encodes frames to an H.264 MP4 file with the Media Foundation sink writer built into Windows.
/// Must be created, used and disposed on the same thread.
/// </summary>
internal sealed unsafe class Mp4Encoder : IFrameEncoder
{
    private readonly int _sourceWidth, _width, _height;
    private readonly bool _comInitialized;
    private IntPtr _writer;
    private uint _stream;

    // A sample is written only once the next frame arrives, because its duration depends on that timestamp.
    private IntPtr _pending;
    private long _pendingTime = -1;

    public Mp4Encoder(string path, int width, int height, int fps)
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

            BeginWriting(_writer);
        }
        catch
        {
            Dispose();
            throw;
        }
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

    public void Finish(TimeSpan endTime)
    {
        WritePending(Math.Max(endTime.Ticks, _pendingTime + 1));
        FinalizeWriter(_writer);
    }

    private IntPtr CreateSample(byte[] bgra)
    {
        int rowBytes = _width * 4;
        uint length = (uint)(rowBytes * _height);
        Check(MFCreateMemoryBuffer(length, out var buffer));
        try
        {
            byte* data = Lock(buffer);
            try
            {
                fixed (byte* src = bgra)
                {
                    for (int y = 0; y < _height; y++)
                        Buffer.MemoryCopy(src + (long)y * _sourceWidth * 4, data + (long)y * rowBytes, rowBytes, rowBytes);
                }
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
