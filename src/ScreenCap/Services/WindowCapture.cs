using System.Runtime.InteropServices;
using System.Windows;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Services;

/// <summary>The content of one window, in physical pixels, whatever covers it on the screen.</summary>
/// <param name="Bounds">The visible frame in screen coordinates (without the invisible resize borders).</param>
/// <param name="Pixels">BGRX pixel data, top-down, Bounds.Width * 4 bytes per row.</param>
/// <param name="CornerRadius">Radius of the rounded corners Windows 11 draws, 0 for square corners.</param>
internal sealed record WindowImage(Int32Rect Bounds, byte[] Pixels, double CornerRadius);

internal static class WindowCapture
{
    /// <summary>
    /// Asks the window to draw itself into a bitmap, so the parts covered by other windows are captured too.
    /// </summary>
    /// <returns>Null if the window could not be drawn (it reported failure or drew nothing but black).</returns>
    public static WindowImage? Capture(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var window))
            return null;
        // Extended frame bounds exclude the invisible resize borders of Win10/11 windows.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) != 0)
            frame = window;
        int ww = window.Right - window.Left, wh = window.Bottom - window.Top;
        int fx = frame.Left - window.Left, fy = frame.Top - window.Top;
        int fw = Math.Min(frame.Right - frame.Left, ww - fx), fh = Math.Min(frame.Bottom - frame.Top, wh - fy);
        if (ww <= 0 || wh <= 0 || fw <= 0 || fh <= 0 || fx < 0 || fy < 0)
            return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hbmp = CreateCompatibleBitmap(screenDc, ww, wh);
        IntPtr old = SelectObject(memDc, hbmp);
        try
        {
            // PW_RENDERFULLCONTENT also gets DirectX / Chromium / UWP content, which the plain call leaves black.
            if (!PrintWindow(hwnd, memDc, PW_RENDERFULLCONTENT))
                return null;
            SelectObject(memDc, old);
            var header = new BITMAPINFOHEADER { biSize = 40, biWidth = ww, biHeight = -wh, biPlanes = 1, biBitCount = 32 };
            var all = new byte[ww * wh * 4];
            GetDIBits(memDc, hbmp, 0, (uint)wh, all, ref header, 0);

            var pixels = new byte[fw * fh * 4];
            for (int y = 0; y < fh; y++)
                Buffer.BlockCopy(all, ((fy + y) * ww + fx) * 4, pixels, y * fw * 4, fw * 4);
            if (IsBlack(pixels))
                return null;
            return new WindowImage(new Int32Rect(frame.Left, frame.Top, fw, fh), pixels, CornerRadius(hwnd));
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hbmp);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    internal static bool IsBlack(byte[] bgrx)
    {
        for (int i = 0; i < bgrx.Length; i += 4)
        {
            if (bgrx[i] != 0 || bgrx[i + 1] != 0 || bgrx[i + 2] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Windows 11 rounds the corners of normal top-level windows, but PrintWindow draws them square;
    /// this is the radius to cut them back to what the screen shows.
    /// </summary>
    private static double CornerRadius(IntPtr hwnd)
    {
        if (Environment.OSVersion.Version.Build < 22000 || IsZoomed(hwnd))
            return 0;
        if ((GetWindowLong(hwnd, GWL_STYLE) & (WS_CAPTION | WS_THICKFRAME)) == 0)
            return 0; // popups, splash screens, … are not rounded by default
        int preference = 0;
        DwmGetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, out preference, sizeof(int));
        double scale = GetDpiForWindow(hwnd) / 96.0;
        return preference switch
        {
            DWMWCP_DONOTROUND => 0,
            DWMWCP_ROUNDSMALL => 4 * scale,
            _ => 8 * scale,
        };
    }
}
