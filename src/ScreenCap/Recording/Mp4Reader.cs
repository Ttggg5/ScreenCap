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
    private const uint FirstVideoStream = 0xFFFFFFFC;
    private const uint FirstAudioStream = 0xFFFFFFFD;
    private const uint EndOfStream = 0x2;

    public sealed record Frame(TimeSpan Time, byte[] Bgra);

    [DllImport("mfreadwrite.dll")]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IntPtr reader);

    private IntPtr _reader;
    private readonly uint _stream;
    private bool _ended;

    public int Width { get; }
    public int Height { get; }

    private Mp4Reader(IntPtr reader, uint stream, int width, int height)
    {
        _reader = reader;
        _stream = stream;
        Width = width;
        Height = height;
    }

    /// <summary>Opens the video track, decoded to RGB32.</summary>
    public static Mp4Reader OpenVideo(string path)
    {
        Check(MFStartup(MF_VERSION, 0));
        IntPtr attributes = IntPtr.Zero, reader = IntPtr.Zero, type = IntPtr.Zero, current = IntPtr.Zero;
        try
        {
            Check(MFCreateAttributes(out attributes, 1));
            SetUInt32(attributes, MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
            Check(MFCreateSourceReaderFromURL(path, attributes, out reader));

            Check(MFCreateMediaType(out type));
            SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGuid(type, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(reader)[7])(reader, FirstVideoStream, IntPtr.Zero, type));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(reader)[6])(reader, FirstVideoStream, &current));

            ulong size;
            var key = MF_MT_FRAME_SIZE;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Vtbl(current)[8])(current, &key, &size));
            var opened = new Mp4Reader(reader, FirstVideoStream, (int)(size >> 32), (int)(size & 0xFFFFFFFF));
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
            Release(current);
            Release(type);
            Release(attributes);
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
            var opened = new Mp4Reader(reader, FirstAudioStream, 0, 0);
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

    /// <summary>The next video frame, or null at the end.</summary>
    public Frame? ReadFrame()
    {
        while (ReadSample(out long time) is { } sample)
        {
            try
            {
                byte[]? pixels = null;
                WithBuffer(sample, (data, length) =>
                {
                    // The decoder's buffer may be padded to whole macroblocks; rows are width * 4 bytes apart.
                    pixels = new byte[Width * Height * 4];
                    Marshal.Copy((IntPtr)data, pixels, 0, Math.Min(pixels.Length, (int)length));
                });
                return new Frame(TimeSpan.FromTicks(time), pixels!);
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
            if (sample == IntPtr.Zero)
                continue; // e.g. a stream tick or a format change
            time = t;
            return sample;
        }
        return null;
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
