using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Native;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Services;

/// <summary>A frozen copy of the whole virtual screen, in physical pixels.</summary>
internal sealed class ScreenSnapshot
{
    public ScreenSnapshot(int left, int top, int width, int height, byte[] pixels, int stride, BitmapSource bitmap)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Pixels = pixels;
        Stride = stride;
        Bitmap = bitmap;
    }

    /// <summary>Screen coordinate of the virtual screen origin (may be negative).</summary>
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRX pixel data, top-down.</summary>
    public byte[] Pixels { get; }
    public int Stride { get; }
    public BitmapSource Bitmap { get; }

    public Color GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = y * Stride + x * 4;
        return Color.FromRgb(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    /// <summary>
    /// A 1 × 1 stand-in for annotations that are stored without their screen (mosaics and magnifiers show
    /// pixels from it), so the stored annotations don't keep a whole screen in memory.
    /// </summary>
    public static ScreenSnapshot Detached { get; } = CreateDetached();

    private static ScreenSnapshot CreateDetached()
    {
        var pixels = new byte[4];
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr32, null, pixels, 4);
        bitmap.Freeze();
        return new ScreenSnapshot(0, 0, 1, 1, pixels, 4, bitmap);
    }

    /// <summary>Returns a copy with <paramref name="image"/> painted at (x, y) in snapshot coordinates.</summary>
    public ScreenSnapshot WithImage(BitmapSource image, int x, int y)
    {
        var pixels = (byte[])Pixels.Clone();
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        int w = Math.Min(converted.PixelWidth, Width - x), h = Math.Min(converted.PixelHeight, Height - y);
        if (x >= 0 && y >= 0 && w > 0 && h > 0)
            converted.CopyPixels(new Int32Rect(0, 0, w, h), pixels, Stride, y * Stride + x * 4);

        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, pixels, Stride);
        bitmap.Freeze();
        return new ScreenSnapshot(Left, Top, Width, Height, pixels, Stride, bitmap);
    }

    /// <summary>
    /// Returns a copy with a captured window painted over its place on the screen. Outside its rounded
    /// corners the screen behind it stays, blended at the edge like DWM does.
    /// </summary>
    public ScreenSnapshot WithWindow(WindowImage window)
    {
        var pixels = (byte[])Pixels.Clone();
        PaintWindow(pixels, Stride, Width, Height, window.Pixels, window.Bounds.Width, window.Bounds.Height,
            window.Bounds.X - Left, window.Bounds.Y - Top, window.CornerRadius);
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgr32, null, pixels, Stride);
        bitmap.Freeze();
        return new ScreenSnapshot(Left, Top, Width, Height, pixels, Stride, bitmap);
    }

    /// <summary>Paints a w × h BGRX image at (x, y), clipped to the destination, with its corners rounded by <paramref name="radius"/>.</summary>
    internal static void PaintWindow(byte[] dest, int destStride, int destWidth, int destHeight,
        byte[] src, int w, int h, int x, int y, double radius)
    {
        int x0 = Math.Max(0, -x), y0 = Math.Max(0, -y);
        int x1 = Math.Min(w, destWidth - x), y1 = Math.Min(h, destHeight - y);
        for (int sy = y0; sy < y1; sy++)
        {
            int srcRow = sy * w * 4, destRow = (sy + y) * destStride + x * 4;
            for (int sx = x0; sx < x1; sx++)
            {
                double coverage = CornerCoverage(sx, sy, w, h, radius);
                if (coverage <= 0)
                    continue;
                int s = srcRow + sx * 4, d = destRow + sx * 4;
                for (int c = 0; c < 3; c++)
                    dest[d + c] = coverage >= 1 ? src[s + c] : (byte)Math.Round(src[s + c] * coverage + dest[d + c] * (1 - coverage));
            }
        }
    }

    /// <summary>How much of pixel (x, y) lies inside a w × h rectangle with rounded corners: 0 to 1.</summary>
    private static double CornerCoverage(int x, int y, int w, int h, double radius)
    {
        if (radius <= 0)
            return 1;
        // Distance from the pixel center to the center of the corner's circle, when the pixel is in a corner square.
        double cx = x + 0.5 < radius ? radius - (x + 0.5) : x + 0.5 > w - radius ? x + 0.5 - (w - radius) : 0;
        double cy = y + 0.5 < radius ? radius - (y + 0.5) : y + 0.5 > h - radius ? y + 0.5 - (h - radius) : 0;
        if (cx <= 0 || cy <= 0)
            return 1;
        double distance = Math.Sqrt(cx * cx + cy * cy);
        return Math.Clamp(radius - distance + 0.5, 0, 1);
    }

    public BitmapSource Crop(Rect r)
    {
        var rect = new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        var cropped = new CroppedBitmap(Bitmap, rect);
        cropped.Freeze();
        return cropped;
    }
}

/// <summary>
/// A frozen screen kept for later (the last capture, to reopen it), compressed in the background: a whole
/// screen is tens of MB as pixels (and as many again as a bitmap), but screens compress to a few MB.
/// </summary>
internal sealed class StoredSnapshot
{
    private readonly Lock _lock = new();
    private readonly int _left, _top, _width, _height, _stride;
    private ScreenSnapshot? _raw;      // until compressed
    private byte[]? _compressed;

    public StoredSnapshot(ScreenSnapshot snapshot)
    {
        _raw = snapshot;
        (_left, _top, _width, _height, _stride) = (snapshot.Left, snapshot.Top, snapshot.Width, snapshot.Height, snapshot.Stride);
        Compressing = Task.Run(Compress);
    }

    /// <summary>Done once the pixels are compressed and the snapshot itself is let go.</summary>
    public Task Compressing { get; }

    /// <summary>Compressed size in bytes, 0 until compressed.</summary>
    public int CompressedLength => _compressed?.Length ?? 0;

    private void Compress()
    {
        ScreenSnapshot? raw;
        lock (_lock)
            raw = _raw;
        if (raw == null)
            return;
        using var buffer = new MemoryStream();
        using (var deflate = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw.Pixels);
        lock (_lock)
        {
            _compressed = buffer.ToArray();
            _raw = null;
        }
    }

    /// <summary>The screen again: the snapshot itself if it isn't compressed yet, else decompressed into a new one.</summary>
    public ScreenSnapshot Restore()
    {
        byte[] compressed;
        lock (_lock)
        {
            if (_raw != null)
                return _raw;
            compressed = _compressed!;
        }
        var pixels = new byte[_stride * _height];
        using (var inflate = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            inflate.ReadExactly(pixels);
        var bitmap = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr32, null, pixels, _stride);
        bitmap.Freeze();
        return new ScreenSnapshot(_left, _top, _width, _height, pixels, _stride, bitmap);
    }
}

internal static class ScreenCaptureService
{
    /// <summary>Captures a screen region (physical pixels) as top-down BGRA, without the cursor.</summary>
    public static byte[] CaptureRegion(Int32Rect region)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hbmp = CreateCompatibleBitmap(screenDc, region.Width, region.Height);
        IntPtr old = SelectObject(memDc, hbmp);
        try
        {
            BitBlt(memDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, SRCCOPY);
            SelectObject(memDc, old);
            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = region.Width, biHeight = -region.Height, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[region.Width * region.Height * 4];
            GetDIBits(memDc, hbmp, 0, (uint)region.Height, pixels, ref header, 0);
            return pixels;
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <param name="includeCursor">Draw the mouse cursor into the capture.</param>
    public static ScreenSnapshot CaptureVirtualScreen(bool includeCursor = false)
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hbmp = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, hbmp);
        try
        {
            BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY | CAPTUREBLT);
            if (includeCursor)
                CursorPainter.Draw(memDc, x, y);
            SelectObject(memDc, old); // GetDIBits requires the bitmap to be deselected

            var header = new BITMAPINFOHEADER
            {
                biSize = 40,
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };
            int stride = w * 4;
            var pixels = new byte[stride * h];
            GetDIBits(memDc, hbmp, 0, (uint)h, pixels, ref header, 0);

            var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            bitmap.Freeze();
            return new ScreenSnapshot(x, y, w, h, pixels, stride, bitmap);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
