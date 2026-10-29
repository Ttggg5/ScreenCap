using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ScreenCap.Services;

/// <summary>A recognized word and its horizontal extent, in image pixels.</summary>
internal readonly record struct OcrWordBox(string Text, double Left, double Right, double Height);

/// <summary>Text recognition with the OCR engine built into Windows.</summary>
internal static class OcrService
{
    /// <summary>Text this small is upscaled first; Windows OCR is much more accurate on larger glyphs.</summary>
    private const int UpscaleBelow = 1200;

    /// <summary>OCR languages installed on this PC.</summary>
    public static IReadOnlyList<Language> Languages => OcrEngine.AvailableRecognizerLanguages.ToList();

    /// <summary>The language used when none is chosen: the first of the user's languages that has OCR.</summary>
    public static Language? DefaultLanguage => OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage;

    /// <returns>The recognized text, one line per text line.</returns>
    public static async Task<string> RecognizeAsync(BitmapSource image, Language? language = null)
    {
        var engine = (language != null ? OcrEngine.TryCreateFromLanguage(language) : null)
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("這台電腦沒有可用的文字辨識語言，請在 Windows 設定的「語言」中安裝語言套件。");

        using var bitmap = ToSoftwareBitmap(Prepare(image));
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(line => JoinWords(line.Words
            .Select(w => new OcrWordBox(w.Text, w.BoundingRect.X, w.BoundingRect.X + w.BoundingRect.Width, w.BoundingRect.Height)))));
    }

    /// <summary>Upscales small images and shrinks ones beyond the engine's size limit.</summary>
    private static BitmapSource Prepare(BitmapSource image)
    {
        int longest = Math.Max(image.PixelWidth, image.PixelHeight);
        double scale = longest > OcrEngine.MaxImageDimension ? (double)OcrEngine.MaxImageDimension / longest
            : longest < UpscaleBelow && longest * 2 <= OcrEngine.MaxImageDimension ? 2
            : 1;
        if (scale == 1)
            return image;
        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8,
            bgra.PixelWidth, bgra.PixelHeight, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>
    /// Joins OCR words into a line. The engine reports each CJK character as its own word, so next to
    /// CJK text a space is only kept where the gap is clearly wider than normal letter spacing
    /// ("截圖工具", but "ScreenCap 文字" stays apart). Other words are always separated by a space.
    /// </summary>
    internal static string JoinWords(IEnumerable<OcrWordBox> words)
    {
        const double SpaceGap = 0.25; // fraction of the text height that counts as a real space

        var line = new StringBuilder();
        OcrWordBox? previous = null;
        foreach (var word in words.Where(w => w.Text.Length > 0))
        {
            if (previous is { } prev)
            {
                bool cjk = IsCjk(line[^1]) || IsCjk(word.Text[0]);
                double gap = word.Left - prev.Right;
                if (!cjk || gap > SpaceGap * Math.Max(prev.Height, word.Height))
                    line.Append(' ');
            }
            line.Append(word.Text);
            previous = word;
        }
        return line.ToString();
    }

    private static bool IsCjk(char c) =>
        c is >= '\u2E80' and <= '\u9FFF'   // radicals, CJK punctuation, kana, bopomofo, ideographs
          or >= '\uAC00' and <= '\uD7AF'   // Hangul
          or >= '\uF900' and <= '\uFAFF'   // compatibility ideographs
          or >= '\uFE30' and <= '\uFE4F'   // CJK compatibility forms
          or >= '\uFF00' and <= '\uFFEF';  // full-width forms (，！？)
}
