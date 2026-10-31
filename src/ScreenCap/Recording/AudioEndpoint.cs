using System.Runtime.InteropServices;
using static ScreenCap.Recording.MediaFoundation;

namespace ScreenCap.Recording;

/// <summary>
/// Captures the default speakers (loopback, i.e. what is playing) or microphone with WASAPI in shared mode,
/// converted by Windows to <see cref="AudioMixer.SampleRate"/> Hz 16-bit stereo. COM objects are called
/// through their vtables like <see cref="MediaFoundation"/>. Use on one MTA thread.
/// </summary>
internal sealed unsafe class AudioEndpoint : IDisposable
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("bcde0395-e52f-467c-8e3d-c4579291692e");
    private static readonly Guid IID_IMMDeviceEnumerator = new("a95664d2-9614-4f35-a746-de8db63617e6");
    private static readonly Guid IID_IAudioClient = new("1cb9ad4c-dbfa-4c32-b178-c2f568a703b2");
    private static readonly Guid IID_IAudioCaptureClient = new("c8adbd64-e71e-48a0-a4de-185c395cd317");

    private const int eRender = 0, eCapture = 1, eConsole = 0;
    private const uint CLSCTX_ALL = 0x17;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const long BufferDuration = 2_000_000; // 200 ms, in 100 ns units

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, IntPtr* instance);

    private IntPtr _client, _capture;

    /// <param name="loopback">Capture what the default speakers play instead of the default microphone.</param>
    /// <exception cref="COMException">No such device, or it can't be opened.</exception>
    public AudioEndpoint(bool loopback)
    {
        IntPtr enumerator = IntPtr.Zero, device = IntPtr.Zero;
        try
        {
            var clsid = CLSID_MMDeviceEnumerator;
            var iid = IID_IMMDeviceEnumerator;
            Check(CoCreateInstance(&clsid, IntPtr.Zero, CLSCTX_ALL, &iid, &enumerator));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)Vtbl(enumerator)[4])(
                enumerator, loopback ? eRender : eCapture, eConsole, &device)); // GetDefaultAudioEndpoint

            var clientIid = IID_IAudioClient;
            IntPtr client;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)Vtbl(device)[3])(
                device, &clientIid, CLSCTX_ALL, IntPtr.Zero, &client)); // Activate
            _client = client;

            var format = new WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = AudioMixer.Channels,
                nSamplesPerSec = AudioMixer.SampleRate,
                nAvgBytesPerSec = AudioMixer.SampleRate * AudioMixer.Channels * 2,
                nBlockAlign = AudioMixer.Channels * 2,
                wBitsPerSample = 16,
            };
            uint flags = AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY | (loopback ? AUDCLNT_STREAMFLAGS_LOOPBACK : 0);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, WAVEFORMATEX*, Guid*, int>)Vtbl(_client)[3])(
                _client, AUDCLNT_SHAREMODE_SHARED, flags, BufferDuration, 0, &format, null)); // Initialize

            var captureIid = IID_IAudioCaptureClient;
            IntPtr capture;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(_client)[14])(_client, &captureIid, &capture)); // GetService
            _capture = capture;

            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(_client)[10])(_client)); // Start
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    /// <summary>Returns the interleaved samples captured since the last call (possibly none).</summary>
    /// <remarks>Loopback delivers nothing at all while no sound is playing.</remarks>
    public short[] Read()
    {
        var samples = new List<short>();
        while (true)
        {
            uint packet;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Vtbl(_capture)[5])(_capture, &packet)); // GetNextPacketSize
            if (packet == 0)
                return samples.ToArray();

            byte* data;
            uint frames, flags;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, uint*, ulong*, ulong*, int>)Vtbl(_capture)[3])(
                _capture, &data, &frames, &flags, null, null)); // GetBuffer
            try
            {
                int count = (int)frames * AudioMixer.Channels;
                if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                    samples.AddRange(new short[count]);
                else
                    samples.AddRange(new ReadOnlySpan<short>(data, count));
            }
            finally
            {
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)Vtbl(_capture)[4])(_capture, frames)); // ReleaseBuffer
            }
        }
    }

    public void Dispose()
    {
        if (_client != IntPtr.Zero)
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(_client)[11])(_client); // Stop
        Release(_capture);
        _capture = IntPtr.Zero;
        Release(_client);
        _client = IntPtr.Zero;
    }
}
