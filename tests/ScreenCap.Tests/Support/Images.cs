using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Tests.Support;

internal static class Images
{
    /// <summary>Top-down BGRA buffer filled with one color.</summary>
    public static byte[] Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    public static void FillRect(byte[] pixels, int width, Int32Rect rect, Color color)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (int x = rect.X; x < rect.X + rect.Width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = color.B;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.R;
                pixels[i + 3] = 255;
            }
        }
    }

    public static BitmapSource ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static ScreenSnapshot Snapshot(int width, int height, Color color, int left = 0, int top = 0)
    {
        var pixels = Solid(width, height, color);
        return new ScreenSnapshot(left, top, width, height, pixels, width * 4, ToBitmap(pixels, width, height));
    }

    /// <summary>Copies any bitmap out as top-down BGRA.</summary>
    public static byte[] Bgra(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }

    public static Color PixelAt(byte[] bgra, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return Color.FromArgb(bgra[i + 3], bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    public static Color PixelAt(BitmapSource source, int x, int y) => PixelAt(Bgra(source), source.PixelWidth, x, y);

    /// <summary>Largest per-channel difference between two colors.</summary>
    public static int Distance(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
