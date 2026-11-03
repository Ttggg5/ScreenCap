using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace ScreenCap.Services;

/// <summary>A background for beautified screenshots: a diagonal gradient, or transparent when both colors are.</summary>
internal sealed record BeautifyBackground(string Name, Color From, Color To)
{
    public bool IsTransparent => From.A == 0 && To.A == 0;

    public Brush CreateBrush() => IsTransparent
        ? Brushes.Transparent
        : new LinearGradientBrush(From, To, new Point(0, 0), new Point(1, 1));

    public static readonly BeautifyBackground[] All =
    [
        new("海洋", Color.FromRgb(0x21, 0x93, 0xB0), Color.FromRgb(0x6D, 0xD5, 0xED)),
        new("紫羅蘭", Color.FromRgb(0x66, 0x7E, 0xEA), Color.FromRgb(0x76, 0x4B, 0xA2)),
        new("晚霞", Color.FromRgb(0xF8, 0x7B, 0x7B), Color.FromRgb(0xFB, 0xD7, 0x86)),
        new("薄荷", Color.FromRgb(0x43, 0xC6, 0xAC), Color.FromRgb(0xF8, 0xFF, 0xAE)),
        new("蜜桃", Color.FromRgb(0xFF, 0x9A, 0x9E), Color.FromRgb(0xFA, 0xD0, 0xC4)),
        new("夜空", Color.FromRgb(0x23, 0x25, 0x26), Color.FromRgb(0x41, 0x43, 0x45)),
        new("淺灰", Color.FromRgb(0xE9, 0xEC, 0xEF), Color.FromRgb(0xF8, 0xF9, 0xFA)),
        new("白色", Colors.White, Colors.White),
        new("透明", Colors.Transparent, Colors.Transparent),
    ];

    public static BeautifyBackground FromIndex(int index) => All[Math.Clamp(index, 0, All.Length - 1)];
}

/// <param name="Padding">Margin around the screenshot, in DIPs (scaled to the display like the rest of the chrome).</param>
/// <param name="Radius">Corner radius of the screenshot, in DIPs.</param>
/// <param name="OuterRadius">Corner radius of the whole result, background included, in DIPs; outside it is transparent.</param>
internal sealed record BeautifyOptions(bool Enabled, int Background, int Padding, int Radius, bool Shadow, int OuterRadius = 0)
{
    public const int MaxPadding = 160;
    public const int MaxRadius = 40;
    public const int MaxOuterRadius = 80;

    public static readonly BeautifyOptions Default = new(false, 0, 48, 12, true);
}

/// <summary>Puts a screenshot on a background with margin, rounded corners and a drop shadow, for documents and posts.</summary>
internal static class Beautifier
{
    /// <summary>How far the screenshot sits from the edge of the result, in physical pixels.</summary>
    public static int PaddingPixels(BeautifyOptions options, double scale) => (int)Math.Round(Math.Clamp(options.Padding, 0, BeautifyOptions.MaxPadding) * scale);

    /// <param name="scale">Display scale, so the margin, corners and shadow look the same size at any DPI.</param>
    public static BitmapSource Apply(BitmapSource image, BeautifyOptions options, double scale)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        int pad = PaddingPixels(options, scale);
        // Corners can't be rounder than half the shorter side.
        double radius = Math.Min(Math.Clamp(options.Radius, 0, BeautifyOptions.MaxRadius) * scale, Math.Min(w, h) / 2.0);
        var background = BeautifyBackground.FromIndex(options.Background);
        var shot = new Rect(pad, pad, w, h);

        var all = new Rect(0, 0, w + 2 * pad, h + 2 * pad);
        double outer = Math.Min(Math.Clamp(options.OuterRadius, 0, BeautifyOptions.MaxOuterRadius) * scale, Math.Min(all.Width, all.Height) / 2.0);
        var root = new DrawingVisual();
        // Everything, the shadow and a screenshot without margin included, is cut to the outer corners.
        if (outer > 0)
            root.Clip = new RectangleGeometry(all, outer, outer);
        using (var dc = root.RenderOpen())
            dc.DrawRectangle(background.CreateBrush(), null, all);

        if (options.Shadow && pad > 0)
        {
            var shadow = new DrawingVisual
            {
                Effect = new BlurEffect { Radius = Math.Min(24 * scale, pad * 1.5), KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
                Opacity = background.IsTransparent ? 0.45 : 0.35,
            };
            using (var dc = shadow.RenderOpen())
            {
                var offset = shot;
                offset.Offset(0, Math.Min(6 * scale, pad / 3.0));
                dc.DrawRoundedRectangle(Brushes.Black, null, offset, radius, radius);
            }
            root.Children.Add(shadow);
        }

        var picture = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor); // drawn 1:1, keep it sharp
        using (var dc = picture.RenderOpen())
        {
            if (radius > 0)
                dc.PushClip(new RectangleGeometry(shot, radius, radius));
            dc.DrawImage(image, shot);
            if (radius > 0)
                dc.Pop();
        }
        root.Children.Add(picture);

        var result = new RenderTargetBitmap(w + 2 * pad, h + 2 * pad, 96, 96, PixelFormats.Pbgra32);
        result.Render(root);
        result.Freeze();
        return result;
    }

    /// <summary>True if some pixel is not fully opaque, e.g. a beautified screenshot on a transparent background.</summary>
    public static bool HasTransparency(BitmapSource image)
    {
        if (image.Format != PixelFormats.Pbgra32 && image.Format != PixelFormats.Bgra32)
            return false;
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 255)
                return true;
        }
        return false;
    }

    /// <summary>The image drawn on white, for formats and apps without transparency (JPG, BMP, the clipboard bitmap).</summary>
    public static BitmapSource OnWhite(BitmapSource image)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawImage(image, new Rect(0, 0, w, h));
        }
        var result = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }
}
