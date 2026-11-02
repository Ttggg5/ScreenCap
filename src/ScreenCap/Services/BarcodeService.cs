using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace ScreenCap.Services;

/// <summary>A decoded QR code or barcode.</summary>
/// <param name="Format">Display name of the symbology, e.g. "QR Code" or "EAN 13".</param>
internal readonly record struct BarcodeResult(string Format, string Text)
{
    /// <summary>True when the content is a web link that can be opened in the browser.</summary>
    public bool IsLink => Uri.TryCreate(Text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}

/// <summary>Finds and decodes QR codes and common barcodes in an image (ZXing.Net).</summary>
internal static class BarcodeService
{
    static BarcodeService()
    {
        // Codes without an encoding marker are guessed; the guess can be Shift_JIS or GB2312,
        // which .NET only knows once the code-page encodings are registered.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Symbologies looked for. Rarer 1D formats (Codabar, ITF, RSS, MSI) are left out: on a screenshot
    /// full of text and stripes they occasionally "decode" where there is no barcode at all.
    /// </summary>
    private static readonly BarcodeFormat[] Formats =
    [
        BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC, BarcodeFormat.PDF_417,
        BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A, BarcodeFormat.UPC_E,
        BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
    ];

    /// <returns>Every code found, top to bottom; empty if there is none.</returns>
    public static Task<IReadOnlyList<BarcodeResult>> DecodeAsync(BitmapSource image)
    {
        // Read the pixels here: the image may belong to the UI thread.
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);
        return Task.Run(() => Decode(pixels, width, height));
    }

    /// <summary>Selections up to this size are retried enlarged when nothing is found.</summary>
    private const int UpscaleBelow = 1200;
    private const int UpscaleFactor = 3;

    private static IReadOnlyList<BarcodeResult> Decode(byte[] bgra, int width, int height)
    {
        var codes = DecodeOnce(bgra, width, height);
        // The detector needs a few pixels per module; a small code on screen often has only one or two.
        if (codes.Count == 0 && Math.Max(width, height) <= UpscaleBelow)
            codes = DecodeOnce(Enlarge(bgra, width, height, UpscaleFactor), width * UpscaleFactor, height * UpscaleFactor);
        return codes;
    }

    /// <summary>Nearest-neighbor enlargement, so module edges stay sharp.</summary>
    private static byte[] Enlarge(byte[] bgra, int width, int height, int factor)
    {
        int outWidth = width * factor;
        var result = new byte[bgra.Length * factor * factor];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * factor * outWidth * 4;
            for (int x = 0; x < width; x++)
            {
                for (int k = 0; k < factor; k++)
                    Buffer.BlockCopy(bgra, (y * width + x) * 4, result, rowStart + (x * factor + k) * 4, 4);
            }
            for (int k = 1; k < factor; k++)
                Buffer.BlockCopy(result, rowStart, result, rowStart + k * outWidth * 4, outWidth * 4);
        }
        return result;
    }

    private static IReadOnlyList<BarcodeResult> DecodeOnce(byte[] bgra, int width, int height)
    {
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true, // light codes on a dark background, e.g. in dark mode
                PossibleFormats = Formats,
            },
        };
        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var results = reader.DecodeMultiple(source);
        if (results == null)
            return [];
        return results
            .Where(r => !string.IsNullOrEmpty(r.Text))
            .OrderBy(r => r.ResultPoints is { Length: > 0 } points ? points.Min(p => p.Y) : 0)
            .Select(r => new BarcodeResult(FormatName(r.BarcodeFormat), r.Text))
            .Distinct() // the same code can be found twice, e.g. once normal and once inverted
            .ToList();
    }

    internal static string FormatName(BarcodeFormat format) => format switch
    {
        BarcodeFormat.QR_CODE => "QR Code",
        BarcodeFormat.DATA_MATRIX => "Data Matrix",
        BarcodeFormat.AZTEC => "Aztec",
        BarcodeFormat.PDF_417 => "PDF417",
        _ => format.ToString().Replace('_', ' '), // EAN 13, CODE 128, ...
    };
}
