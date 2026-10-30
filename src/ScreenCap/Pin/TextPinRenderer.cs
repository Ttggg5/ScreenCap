using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Annotations;
using ScreenCap.Capture;

namespace ScreenCap.Pin;

/// <summary>
/// Turns clipboard text into an image to pin: a color code (#1E88E5, rgb(30, 136, 229), …) becomes a
/// color swatch; any other text is drawn on a card.
/// </summary>
internal static partial class TextPinRenderer
{
    private const int MaxChars = 5000;
    private const double MaxTextWidth = 560, MaxTextHeight = 1600, Padding = 12, TextSize = 14; // DIP
    private const double SwatchWidth = 150, SwatchHeight = 84;                                  // DIP
    private static readonly FontFamily Font = new("Microsoft JhengHei UI, Segoe UI");
    private static readonly Color CardColor = Color.FromRgb(0xFB, 0xFB, 0xFB);
    private static readonly Color TextColor = Color.FromRgb(0x1F, 0x1F, 0x1F);

    /// <param name="scale">DPI scale of the monitor the pin appears on.</param>
    /// <returns>null for empty text.</returns>
    public static BitmapSource? FromText(string? text, double scale)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return ParseColor(text) is { } color ? RenderColor(color, scale) : RenderText(text, scale);
    }

    /// <summary>
    /// Recognizes "#RGB", "#RRGGBB", "RRGGBB" (with at least one letter, so plain numbers stay text),
    /// "rgb(r, g, b)" and "r, g, b".
    /// </summary>
    public static Color? ParseColor(string text)
    {
        string s = text.Trim();
        if (s.Length is 0 or > 40)
            return null;
        if (s.StartsWith('#') || (s.Length == 6 && s.Any(char.IsAsciiLetter)))
            return ColorPalette.TryParseHex(s, out var hex) ? hex : null;

        var m = RgbRegex().Match(s);
        if (!m.Success)
            return null;
        var parts = new byte[3];
        for (int i = 0; i < 3; i++)
        {
            int value = int.Parse(m.Groups[i + 1].Value, CultureInfo.InvariantCulture);
            if (value > 255)
                return null;
            parts[i] = (byte)value;
        }
        return Color.FromRgb(parts[0], parts[1], parts[2]);
    }

    public static BitmapSource RenderColor(Color color, double scale)
    {
        var label = Format($"{ColorPalette.ToHex(color)}\n{color.R}, {color.G}, {color.B}", TextAnnotation.ContrastColor(color), SwatchWidth, scale);
        label.TextAlignment = TextAlignment.Center;
        return Render(SwatchWidth, SwatchHeight, scale, dc =>
        {
            dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, SwatchWidth, SwatchHeight));
            dc.DrawText(label, new Point(0, (SwatchHeight - label.Height) / 2));
        });
    }

    public static BitmapSource RenderText(string text, double scale)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").TrimEnd();
        if (normalized.Length > MaxChars)
            normalized = normalized[..MaxChars] + "…";
        var formatted = Format(normalized, TextColor, MaxTextWidth, scale);
        double width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 2 * Padding;
        double height = Math.Ceiling(formatted.Height) + 2 * Padding;
        return Render(width, height, scale, dc =>
        {
            dc.DrawRectangle(new SolidColorBrush(CardColor), null, new Rect(0, 0, width, height));
            dc.DrawText(formatted, new Point(Padding, Padding));
        });
    }

    private static FormattedText Format(string text, Color color, double maxWidth, double scale)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), TextSize, new SolidColorBrush(color), scale)
        {
            LineHeight = TextSize * 1.45,
            MaxTextHeight = MaxTextHeight,
            Trimming = TextTrimming.CharacterEllipsis,
            MaxTextWidth = maxWidth,
        };
        return formatted;
    }

    /// <summary>Draws a DIP-sized card into a 96-DPI bitmap of physical pixels, like other captures.</summary>
    private static BitmapSource Render(double width, double height, double scale, Action<DrawingContext> draw)
    {
        int pw = (int)Math.Ceiling(width * scale), ph = (int)Math.Ceiling(height * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            draw(dc);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        return opaque;
    }

    [GeneratedRegex(@"^(?:rgba?\s*\()?\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbRegex();
}
