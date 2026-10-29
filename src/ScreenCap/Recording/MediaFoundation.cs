using System.Runtime.InteropServices;

namespace ScreenCap.Recording;

/// <summary>
/// Minimal Media Foundation interop for the sink writer. COM objects are kept as raw pointers and
/// called through their vtables, which avoids declaring the large IMFAttributes-derived interfaces.
/// </summary>
internal static unsafe class MediaFoundation
{
    public const uint MF_VERSION = 0x00020070;
    public const uint COINIT_MULTITHREADED = 0;

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MF_MT_MPEG2_PROFILE = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00aa00389b71");
    public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");
    public static readonly Guid CODECAPI_AVEncCommonQuality = new("fcbf57a3-7ea5-4b0c-9644-69b40c39c391");

    public const uint MFVideoInterlace_Progressive = 2;
    public const uint eAVEncH264VProfile_High = 100;
    public const uint eAVEncCommonRateControlMode_Quality = 3;

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("mfplat.dll")]
    public static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    public static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [DllImport("mfplat.dll")]
    public static extern int MFCreateMediaType(out IntPtr mediaType);

    [DllImport("mfplat.dll")]
    public static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);

    [DllImport("mfplat.dll")]
    public static extern int MFCreateSample(out IntPtr sample);

    [DllImport("mfreadwrite.dll")]
    public static extern int MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);

    public static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static void** Vtbl(IntPtr obj) => *(void***)obj;

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(obj)[2])(obj);
    }

    // IMFAttributes (also the base of IMFMediaType and IMFSample)
    public static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(attributes)[21])(attributes, &key, value));

    public static void SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(attributes)[22])(attributes, &key, value));

    public static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[24])(attributes, &key, &value));

    public static void SetPair(IntPtr attributes, Guid key, uint high, uint low) =>
        SetUInt64(attributes, key, ((ulong)high << 32) | low);

    // IMFMediaBuffer
    public static byte* Lock(IntPtr buffer)
    {
        byte* data;
        uint max, current;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, int>)Vtbl(buffer)[3])(buffer, &data, &max, &current));
        return data;
    }

    public static void Unlock(IntPtr buffer) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(buffer)[4])(buffer));

    public static void SetCurrentLength(IntPtr buffer, uint length) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(buffer)[6])(buffer, length));

    // IMFSample (after the 30 IMFAttributes methods)
    public static void SetSampleTime(IntPtr sample, long time) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[36])(sample, time));

    public static void SetSampleDuration(IntPtr sample, long duration) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[38])(sample, duration));

    public static void AddBuffer(IntPtr sample, IntPtr buffer) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(sample)[42])(sample, buffer));

    // IMFSinkWriter
    public static uint AddStream(IntPtr writer, IntPtr mediaType)
    {
        uint index;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(writer)[3])(writer, mediaType, &index));
        return index;
    }

    public static int SetInputMediaType(IntPtr writer, uint stream, IntPtr mediaType, IntPtr encodingParameters) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(writer)[4])(writer, stream, mediaType, encodingParameters);

    public static void BeginWriting(IntPtr writer) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[5])(writer));

    public static void WriteSample(IntPtr writer, uint stream, IntPtr sample) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Vtbl(writer)[6])(writer, stream, sample));

    public static void FinalizeWriter(IntPtr writer) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[11])(writer));
}
