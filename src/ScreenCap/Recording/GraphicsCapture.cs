using System.Runtime.InteropServices;
using System.Windows;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Recording;

/// <summary>
/// Captures a region of one monitor with Windows.Graphics.Capture. DWM hands over each new frame as a GPU
/// texture, and only the recorded region is copied back to memory; BitBlt instead makes DWM copy the screen
/// for every call, which is slow for large regions. Use from one thread.
/// </summary>
internal sealed unsafe class GraphicsCapture : IDisposable
{
    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint D3D11_SDK_VERSION = 7;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IID_IGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid IID_IGraphicsCaptureSession3 = new("f2cdd966-22ae-5ea1-9596-3a289344c3be");

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_BOX
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public byte* Data;
        public uint RowPitch, DepthPitch;
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels,
        uint sdkVersion, out IntPtr device, out uint featureLevel, out IntPtr context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    private readonly Int32Rect _region;   // within the monitor
    private IntPtr _device, _context, _staging;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly GraphicsCaptureItem _item;
    private Direct3D11CaptureFramePool _pool;
    private readonly GraphicsCaptureSession _session;
    private Windows.Graphics.SizeInt32 _poolSize;
    private bool _hasFrame;

    private GraphicsCapture(Int32Rect regionOnMonitor, IntPtr device, IntPtr context, IDirect3DDevice winrtDevice, GraphicsCaptureItem item)
    {
        _region = regionOnMonitor;
        _device = device;
        _context = context;
        _winrtDevice = winrtDevice;
        _item = item;
        _poolSize = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        _session = _pool.CreateCaptureSession(item);
        _session.IsCursorCaptureEnabled = false; // drawn by the recorder, with the click highlight under it
        _staging = CreateStaging(device, regionOnMonitor.Width, regionOnMonitor.Height);
    }

    /// <summary>Starts capturing <paramref name="region"/> (screen pixels).</summary>
    /// <returns>
    /// Null if it can't be used: the region spans several monitors, Windows.Graphics.Capture is missing, or
    /// (Windows 10) the yellow capture border can't be turned off, which would show around the monitor.
    /// </returns>
    public static GraphicsCapture? TryStart(Int32Rect region)
    {
        if (!GraphicsCaptureSession.IsSupported())
            return null;
        var rect = new RECT { Left = region.X, Top = region.Y, Right = region.X + region.Width, Bottom = region.Y + region.Height };
        IntPtr monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return null;
        var m = info.rcMonitor;
        if (rect.Left < m.Left || rect.Top < m.Top || rect.Right > m.Right || rect.Bottom > m.Bottom)
            return null;

        IntPtr device = IntPtr.Zero, context = IntPtr.Zero;
        GraphicsCapture? capture = null;
        try
        {
            if (D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, D3D11_CREATE_DEVICE_BGRA_SUPPORT, IntPtr.Zero, 0,
                    D3D11_SDK_VERSION, out device, out _, out context) < 0)
                return null;
            var winrtDevice = CreateWinRTDevice(device);
            var item = CreateItemForMonitor(monitor);
            capture = new GraphicsCapture(new Int32Rect(region.X - m.Left, region.Y - m.Top, region.Width, region.Height), device, context, winrtDevice, item);
            if (!capture.TryHideBorder())
            {
                capture.Dispose();
                return null;
            }
            capture._session.StartCapture();
            return capture;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException)
        {
            if (capture != null)
                capture.Dispose();
            else
            {
                Release(context);
                Release(device);
            }
            return null;
        }
    }

    /// <summary>Copies the latest frame's region into <paramref name="bits"/> (top-down BGRA, width × 4 bytes per row).</summary>
    /// <returns>False if no frame has arrived yet.</returns>
    public bool CopyTo(byte* bits)
    {
        // Frames only arrive when something on the monitor changes; keep the newest, or the last one if none.
        Direct3D11CaptureFrame? latest = null;
        while (_pool.TryGetNextFrame() is { } frame)
        {
            latest?.Dispose();
            latest = frame;
        }
        if (latest != null)
        {
            using (latest)
                CopyFrame(latest);
        }
        if (!_hasFrame)
            return false;

        D3D11_MAPPED_SUBRESOURCE mapped;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)Vtbl(_context)[14])(
            _context, _staging, 0, D3D11_MAP_READ, 0, &mapped)); // Map
        try
        {
            int rowBytes = _region.Width * 4;
            for (int y = 0; y < _region.Height; y++)
                Buffer.MemoryCopy(mapped.Data + (long)y * mapped.RowPitch, bits + (long)y * rowBytes, rowBytes, rowBytes);
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Vtbl(_context)[15])(_context, _staging, 0); // Unmap
        }
        return true;
    }

    private void CopyFrame(Direct3D11CaptureFrame frame)
    {
        // The monitor changed resolution or orientation: frames come in the new size from the next one on.
        if (frame.ContentSize.Width != _poolSize.Width || frame.ContentSize.Height != _poolSize.Height)
        {
            _poolSize = frame.ContentSize;
            _pool.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
        }
        if (_region.X + _region.Width > frame.ContentSize.Width || _region.Y + _region.Height > frame.ContentSize.Height)
            return; // the region is no longer on the monitor; the last frame stays

        IntPtr texture = GetTexture(frame.Surface);
        try
        {
            var box = new D3D11_BOX
            {
                Left = (uint)_region.X,
                Top = (uint)_region.Y,
                Front = 0,
                Right = (uint)(_region.X + _region.Width),
                Bottom = (uint)(_region.Y + _region.Height),
                Back = 1,
            };
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, D3D11_BOX*, void>)Vtbl(_context)[46])(
                _context, _staging, 0, 0, 0, 0, texture, 0, &box); // CopySubresourceRegion
            _hasFrame = true;
        }
        finally
        {
            Release(texture);
        }
    }

    /// <summary>Turns off the yellow border around a captured monitor; only Windows 11 can (IGraphicsCaptureSession3).</summary>
    private bool TryHideBorder()
    {
        IntPtr session = MarshalInspectable<object>.FromManaged(_session);
        try
        {
            var iid = IID_IGraphicsCaptureSession3;
            if (Marshal.QueryInterface(session, in iid, out IntPtr session3) < 0)
                return false;
            try
            {
                // IInspectable's 6 methods, then get_IsBorderRequired, put_IsBorderRequired.
                return ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Vtbl(session3)[7])(session3, 0) >= 0;
            }
            finally
            {
                Marshal.Release(session3);
            }
        }
        finally
        {
            Marshal.Release(session);
        }
    }

    private static IDirect3DDevice CreateWinRTDevice(IntPtr device)
    {
        var iid = IID_IDXGIDevice;
        Check(Marshal.QueryInterface(device, in iid, out IntPtr dxgi));
        try
        {
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out IntPtr inspectable));
            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
            }
            finally
            {
                Marshal.Release(inspectable);
            }
        }
        finally
        {
            Marshal.Release(dxgi);
        }
    }

    private static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Check(WindowsCreateString(name, name.Length, out IntPtr className));
        try
        {
            var iid = IID_IGraphicsCaptureItemInterop;
            Check(RoGetActivationFactory(className, ref iid, out IntPtr interop));
            try
            {
                var itemIid = IID_IGraphicsCaptureItem;
                IntPtr item;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Vtbl(interop)[4])(interop, monitor, &itemIid, &item)); // CreateForMonitor
                try
                {
                    return GraphicsCaptureItem.FromAbi(item);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static IntPtr GetTexture(IDirect3DSurface surface)
    {
        IntPtr abi = MarshalInspectable<object>.FromManaged(surface);
        try
        {
            var iid = IID_IDirect3DDxgiInterfaceAccess;
            Check(Marshal.QueryInterface(abi, in iid, out IntPtr access));
            try
            {
                var textureIid = IID_ID3D11Texture2D;
                IntPtr texture;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Vtbl(access)[3])(access, &textureIid, &texture)); // GetInterface
                return texture;
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static IntPtr CreateStaging(IntPtr device, int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleCount = 1,
            Usage = D3D11_USAGE_STAGING,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
        };
        IntPtr texture;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)Vtbl(device)[5])(device, &desc, IntPtr.Zero, &texture)); // CreateTexture2D
        return texture;
    }

    private static void** Vtbl(IntPtr obj) => *(void***)obj;

    private static void Check(int hr)
    {
        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);
    }

    private static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero)
            Marshal.Release(obj);
    }

    public void Dispose()
    {
        _session.Dispose();
        _pool.Dispose();
        (_winrtDevice as IDisposable)?.Dispose();
        Release(_staging);
        Release(_context);
        Release(_device);
        _staging = _context = _device = IntPtr.Zero;
    }
}
