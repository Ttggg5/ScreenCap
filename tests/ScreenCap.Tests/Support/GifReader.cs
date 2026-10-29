using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Tests.Support;

/// <summary>Decodes a GIF with WPF and composites the frames the way a viewer would (disposal "leave in place").</summary>
internal static class GifReader
{
    public sealed record Frame(byte[] Canvas, int DelayCs, int Left, int Top, int Width, int Height);

    public static (int Width, int Height, List<Frame> Frames) Decode(byte[] gif)
    {
        var decoder = new GifBitmapDecoder(new MemoryStream(gif), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var screen = (BitmapMetadata)decoder.Metadata;
        int width = Convert.ToInt32(screen.GetQuery("/logscrdesc/Width"));
        int height = Convert.ToInt32(screen.GetQuery("/logscrdesc/Height"));

        var canvas = new byte[width * height * 4];
        var frames = new List<Frame>();
        foreach (var frame in decoder.Frames)
        {
            var meta = (BitmapMetadata)frame.Metadata;
            int left = Convert.ToInt32(meta.GetQuery("/imgdesc/Left"));
            int top = Convert.ToInt32(meta.GetQuery("/imgdesc/Top"));
            int delay = Convert.ToInt32(meta.GetQuery("/grctlext/Delay"));

            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int s = (y * w + x) * 4;
                    if (pixels[s + 3] == 0)
                        continue; // transparent: previous frame shows through
                    int d = ((top + y) * width + left + x) * 4;
                    Array.Copy(pixels, s, canvas, d, 4);
                }
            }
            frames.Add(new Frame((byte[])canvas.Clone(), delay, left, top, w, h));
        }
        return (width, height, frames);
    }
}
