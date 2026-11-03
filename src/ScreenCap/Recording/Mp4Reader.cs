using System.Runtime.InteropServices;
using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Recording;

/// <summary>
/// Decodes a video to top-down BGRA frames, or its audio to PCM in the <see cref="AudioMixer"/> format,
/// one sample at a time with the Media Foundation source reader. Use from one thread.
/// </summary>
internal sealed unsafe class Mp4Reader : IDisposable
{
    private static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private static readonly Guid MF_MT_MINIMUM_DISPLAY_APERTURE = new("d7388766-18fe-48c6-a177-ee894867c8c4");
    private static readonly Guid MF_PD_DURATION = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private const uint MediaSource = 0xFFFFFFFF; // MF_SOURCE_READER_MEDIASOURCE
    private static readonly Guid IID_IMF2DBuffer = new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
    private const uint FirstVideoStream = 0xFFFFFFFC;
    private const uint FirstAudioStream = 0xFFFFFFFD;
    private const uint EndOfStream = 0x2;
    private const uint CurrentMediaTypeChanged = 0x20;

    public sealed record Frame(TimeSpan Time, byte[] Bgra);

    [DllImport("mfreadwrite.dll")]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IntPtr reader);

    private IntPtr _reader;
    private readonly uint _stream;
    private bool _ended;
    // The decoded frame's layout; the decoder may change it with its first frame.
    private int _left, _top;       // of the picture within the decoded frame
    private int _decodedHeight;    // may be padded to whole macroblocks
    private int _stride;           // bytes from one row to the next, 0 if the media type didn't say

    /// <summary>Size of the picture, without the decoder's padding.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    private Mp4Reader(IntPtr reader, uint stream)
    {
        _reader = reader;
        _stream = stream;
    }

    /// <summary>Opens the video track, decoded to RGB32.</summary>
    public static Mp4Reader OpenVideo(string path)
    {
        Check(MFStartup(MF_VERSION, 0));
        IntPtr attributes = IntPtr.Zero, reader = IntPtr.Zero, type = IntPtr.Zero;
        try
        {
            Check(MFCreateAttributes(out attributes, 1));
            SetUInt32(attributes, MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            Check(MFCreateSourceReaderFromURL(path, attributes, out reader));

            Check(MFCreateMediaType(out type));
            SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGuid(type, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(reader)[7])(reader, FirstVideoStream, IntPtr.Zero, type));
            var opened = new Mp4Reader(reader, FirstVideoStream);
            opened.ReadVideoFormat(keepSize: false);
            reader = IntPtr.Zero; // owned by the reader now
            return opened;
        }
        catch
        {
            Release(reader);
            MFShutdown();
            throw;
        }
        finally
        {
            Release(type);
            Release(attributes);
        }
    }

    /// <summary>Reads the decoded frame's size, picture area and row pitch from the current media type.</summary>
    /// <param name="keepSize">After a format change mid-stream: the picture keeps the size it had.</param>
    private void ReadVideoFormat(bool keepSize)
    {
        IntPtr current;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(_reader)[6])(_reader, _stream, &current)); // GetCurrentMediaType
        try
        {
            ulong size;
            var key = MF_MT_FRAME_SIZE;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Vtbl(current)[8])(current, &key, &size));
            int decodedWidth = (int)(size >> 32), decodedHeight = (int)(size & 0xFFFFFFFF);

            // H.264 codes whole 16 × 16 macroblocks; the picture itself is this area of the decoded frame.
            int left = 0, top = 0, width = decodedWidth, height = decodedHeight;
            var apertureKey = MF_MT_MINIMUM_DISPLAY_APERTURE;
            var area = stackalloc int[4]; // MFVideoArea: OffsetX, OffsetY (16.16 fixed point), cx, cy
            uint blobSize;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int*, uint, uint*, int>)Vtbl(current)[15])(current, &apertureKey, area, 16, &blobSize) >= 0
                && blobSize == 16 && area[2] > 0 && area[3] > 0)
            {
                left = area[0] >> 16;
                top = area[1] >> 16;
                width = Math.Min(area[2], decodedWidth - left);
                height = Math.Min(area[3], decodedHeight - top);
            }
            // Rows are usually padded, e.g. to whole macroblocks when the width isn't a multiple of 16.
            var strideKey = MF_MT_DEFAULT_STRIDE;
            uint stride;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Vtbl(current)[7])(current, &strideKey, &stride) < 0) // GetUINT32
                stride = 0;

            (_left, _top, _decodedHeight, _stride) = (left, top, decodedHeight, (int)stride);
            if (!keepSize)
                (Width, Height) = (width, height);
            else
                (Width, Height) = (Math.Min(Width, decodedWidth - left), Math.Min(Height, decodedHeight - top));
        }
        finally
        {
            Release(current);
        }
    }

    /// <summary>Opens the audio track, decoded to 16-bit PCM in the <see cref="AudioMixer"/> format.</summary>
    /// <returns>Null if the file has no audio track.</returns>
    public static Mp4Reader? OpenAudio(string path)
    {
        Check(MFStartup(MF_VERSION, 0));
        IntPtr reader = IntPtr.Zero, type = IntPtr.Zero;
        try
        {
            Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader));
            Check(MFCreateMediaType(out type));
            SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            SetGuid(type, MF_MT_SUBTYPE, MFAudioFormat_PCM);
            SetUInt32(type, MF_MT_AUDIO_NUM_CHANNELS, AudioMixer.Channels);
            SetUInt32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioMixer.SampleRate);
            SetUInt32(type, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(reader)[7])(reader, FirstAudioStream, IntPtr.Zero, type) < 0)
            {
                Release(reader);
                MFShutdown();
                return null;
            }
            var opened = new Mp4Reader(reader, FirstAudioStream);
            reader = IntPtr.Zero;
            return opened;
        }
        catch
        {
            Release(reader);
            MFShutdown();
            throw;
        }
        finally
        {
            Release(type);
        }
    }

    /// <summary>The length the file says it has; zero if it doesn't say.</summary>
    public TimeSpan Duration
    {
        get
        {
            var value = stackalloc long[3]; // PROPVARIANT: VT_UI8 (21), then the value at offset 8; needs no clearing
            var key = MF_PD_DURATION;
            // GetPresentationAttribute, of the file rather than a stream
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, long*, int>)Vtbl(_reader)[12])(_reader, MediaSource, &key, value) < 0)
                return TimeSpan.Zero;
            return (value[0] & 0xFFFF) == 21 ? TimeSpan.FromTicks(value[1]) : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Continues reading from about <paramref name="time"/>: decoding resumes at the key frame before it,
    /// so the frames read next may start somewhat earlier.
    /// </summary>
    public void Seek(TimeSpan time)
    {
        var position = stackalloc long[3]; // PROPVARIANT: VT_I8 (20), then the value at offset 8
        position[0] = 20;
        position[1] = time.Ticks;
        var timeFormat = Guid.Empty; // 100 ns units
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, long*, int>)Vtbl(_reader)[8])(_reader, &timeFormat, position)); // SetCurrentPosition
        _ended = false;
    }

    /// <summary>The next video frame, or null at the end.</summary>
    public Frame? ReadFrame()
    {
        while (ReadSample(out long time) is { } sample)
        {
            try
            {
                return new Frame(TimeSpan.FromTicks(time), CopyPicture(sample));
            }
            finally
            {
                Release(sample);
            }
        }
        return null;
    }

    /// <summary>The next block of interleaved samples and when it starts, or null at the end.</summary>
    public (TimeSpan Time, short[] Samples)? ReadAudio()
    {
        while (ReadSample(out long time) is { } sample)
        {
            try
            {
                short[] samples = [];
                WithBuffer(sample, (data, length) => samples = new ReadOnlySpan<short>(data, (int)length / 2).ToArray());
                return (TimeSpan.FromTicks(time), samples);
            }
            finally
            {
                Release(sample);
            }
        }
        return null;
    }

    /// <returns>The next sample (to release), or null at the end of the stream.</returns>
    private IntPtr? ReadSample(out long time)
    {
        time = 0;
        while (!_ended)
        {
            uint index, flags;
            long t;
            IntPtr sample;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Vtbl(_reader)[9])(
                _reader, _stream, 0, &index, &flags, &t, &sample));
            if ((flags & EndOfStream) != 0)
            {
                _ended = true;
                Release(sample);
                break;
            }
            // The decoder settles on its output layout (padding, pitch) once it has seen the stream.
            if ((flags & CurrentMediaTypeChanged) != 0 && _stream == FirstVideoStream)
                ReadVideoFormat(keepSize: true);
            if (sample == IntPtr.Zero)
                continue; // e.g. a stream tick
            time = t;
            return sample;
        }
        return null;
    }

    /// <summary>
    /// Copies the picture row by row. The decoder's rows are usually wider than the picture (padded for
    /// alignment, e.g. when the width isn't a multiple of 16), so its own pitch must be used.
    /// </summary>
    private byte[] CopyPicture(IntPtr sample)
    {
        var pixels = new byte[Width * Height * 4];
        int rowBytes = Width * 4;
        IntPtr buffer;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(sample)[40])(sample, 0, &buffer)); // GetBufferByIndex
        try
        {
            var iid = IID_IMF2DBuffer;
            if (Marshal.QueryInterface(buffer, in iid, out IntPtr buffer2d) >= 0)
            {
                try
                {
                    byte* scanline0;
                    int pitch; // negative for a bottom-up buffer; scanline 0 is still the top row
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, int*, int>)Vtbl(buffer2d)[3])(buffer2d, &scanline0, &pitch)); // Lock2D
                    try
                    {
                        CopyRows(scanline0, pitch, pixels, rowBytes);
                    }
                    finally
                    {
                        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(buffer2d)[4])(buffer2d); // Unlock2D
                    }
                    return pixels;
                }
                finally
                {
                    Marshal.Release(buffer2d);
                }
            }
        }
        finally
        {
            Release(buffer);
        }

        WithBuffer(sample, (data, length) => CopyRows(data, Pitch(length), pixels, rowBytes));
        return pixels;
    }

    /// <summary>
    /// The row pitch of a plain buffer: from the media type, or else worked out from its length. The decoder
    /// pads the height to whole macroblocks too (250 rows are stored as 256), so try that first.
    /// </summary>
    private int Pitch(uint length)
    {
        if (_stride != 0)
            return _stride;
        int padded = (_decodedHeight + 15) & ~15;
        if (length % (uint)padded == 0 && length / (uint)padded >= Width * 4)
            return (int)(length / (uint)padded);
        if (length % (uint)_decodedHeight == 0)
            return (int)(length / (uint)_decodedHeight);
        return ((Width + 15) & ~15) * 4;
    }

    private void CopyRows(byte* scanline0, int pitch, byte[] pixels, int rowBytes)
    {
        fixed (byte* dest = pixels)
        {
            for (int y = 0; y < Height; y++)
                Buffer.MemoryCopy(scanline0 + (long)(_top + y) * pitch + _left * 4, dest + (long)y * rowBytes, rowBytes, rowBytes);
        }
    }

    private delegate void ReadBuffer(byte* data, uint length);

    private static void WithBuffer(IntPtr sample, ReadBuffer read)
    {
        IntPtr buffer;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(sample)[41])(sample, &buffer)); // ConvertToContiguousBuffer
        try
        {
            byte* data;
            uint max, length;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer)[3])(buffer, &data, &max, &length));
            try
            {
                read(data, length);
            }
            finally
            {
                Unlock(buffer);
            }
        }
        finally
        {
            Release(buffer);
        }
    }

    public void Dispose()
    {
        if (_reader == IntPtr.Zero)
            return;
        Release(_reader);
        _reader = IntPtr.Zero;
        MFShutdown();
    }

    /// <summary>Decodes every frame at once (for tests and short clips).</summary>
    public static (int Width, int Height, List<Frame> Frames) Read(string path)
    {
        using var reader = OpenVideo(path);
        var frames = new List<Frame>();
        while (reader.ReadFrame() is { } frame)
            frames.Add(frame);
        return (reader.Width, reader.Height, frames);
    }

    /// <summary>Decodes the whole audio track to interleaved samples.</summary>
    /// <returns>Null if the file has no audio track.</returns>
    public static short[]? ReadAllAudio(string path)
    {
        using var reader = OpenAudio(path);
        if (reader == null)
            return null;
        var samples = new List<short>();
        while (reader.ReadAudio() is { } block)
            samples.AddRange(block.Samples);
        return samples.ToArray();
    }
}
