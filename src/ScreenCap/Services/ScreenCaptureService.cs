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

    public BitmapSource Crop(Rect r)
    {
        var rect = new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        var cropped = new CroppedBitmap(Bitmap, rect);
        cropped.Freeze();
        return cropped;
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
