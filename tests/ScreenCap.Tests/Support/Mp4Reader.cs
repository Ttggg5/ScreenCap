using System.Runtime.InteropServices;
using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Tests.Support;

/// <summary>Decodes a video to top-down BGRA frames, or its audio to PCM, with the Media Foundation source reader.</summary>
internal static unsafe class Mp4Reader
{
    private static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    private const uint FirstVideoStream = 0xFFFFFFFC;
    private const uint FirstAudioStream = 0xFFFFFFFD;
    private const uint EndOfStream = 0x2;

    public sealed record Frame(TimeSpan Time, byte[] Bgra);

    [DllImport("mfreadwrite.dll")]
    private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IntPtr reader);

    public static (int Width, int Height, List<Frame> Frames) Read(string path)
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
            int width = (int)(size >> 32), height = (int)(size & 0xFFFFFFFF);

            var frames = new List<Frame>();
            while (true)
            {
                uint index, flags;
                long time;
                IntPtr sample;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Vtbl(reader)[9])(
                    reader, FirstVideoStream, 0, &index, &flags, &time, &sample));
                if ((flags & EndOfStream) != 0)
                    break;
                if (sample == IntPtr.Zero)
                    continue;
                try
                {
                    frames.Add(new Frame(TimeSpan.FromTicks(time), CopyFrame(sample, width, height)));
                }
                finally
                {
                    Release(sample);
                }
            }
            return (width, height, frames);
        }
        finally
        {
            Release(current);
            Release(type);
            Release(reader);
            Release(attributes);
            MFShutdown();
        }
    }

    /// <summary>Decodes the audio track to interleaved 16-bit samples in the <see cref="ScreenCap.Recording.AudioMixer"/> format.</summary>
    /// <returns>Null if the file has no audio track.</returns>
    public static short[]? ReadAudio(string path)
    {
        Check(MFStartup(MF_VERSION, 0));
        IntPtr reader = IntPtr.Zero, type = IntPtr.Zero;
        try
        {
            Check(MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader));
            Check(MFCreateMediaType(out type));
            SetGuid(type, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            SetGuid(type, MF_MT_SUBTYPE, MFAudioFormat_PCM);
            SetUInt32(type, MF_MT_AUDIO_NUM_CHANNELS, ScreenCap.Recording.AudioMixer.Channels);
            SetUInt32(type, MF_MT_AUDIO_SAMPLES_PER_SECOND, ScreenCap.Recording.AudioMixer.SampleRate);
            SetUInt32(type, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(reader)[7])(reader, FirstAudioStream, IntPtr.Zero, type) < 0)
                return null;

            var samples = new List<short>();
            while (true)
            {
                uint index, flags;
                long time;
                IntPtr sample;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Vtbl(reader)[9])(
                    reader, FirstAudioStream, 0, &index, &flags, &time, &sample));
                if ((flags & EndOfStream) != 0)
                    break;
                if (sample == IntPtr.Zero)
                    continue;
                try
                {
                    IntPtr buffer;
                    Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(sample)[41])(sample, &buffer)); // ConvertToContiguousBuffer
                    try
                    {
                        byte* data;
                        uint max, length;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer)[3])(buffer, &data, &max, &length));
                        samples.AddRange(new ReadOnlySpan<short>(data, (int)length / 2));
                        Unlock(buffer);
                    }
                    finally
                    {
                        Release(buffer);
                    }
                }
                finally
                {
                    Release(sample);
                }
            }
            return samples.ToArray();
        }
        finally
        {
            Release(type);
            Release(reader);
            MFShutdown();
        }
    }

    private static byte[] CopyFrame(IntPtr sample, int width, int height)
    {
        IntPtr buffer;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Vtbl(sample)[41])(sample, &buffer)); // ConvertToContiguousBuffer
        try
        {
            byte* data = Lock(buffer);
            try
            {
                // The decoder's buffer may be padded to whole macroblocks; rows are width * 4 bytes apart.
                var pixels = new byte[width * height * 4];
                Marshal.Copy((IntPtr)data, pixels, 0, pixels.Length);
                return pixels;
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
}
