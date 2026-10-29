using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenCap.Services;
using ScreenCap.Theme;

namespace ScreenCap.Capture;

/// <summary>
/// Custom color picker shown under the toolbar: a saturation/brightness square, a hue bar,
/// a HEX box and the recently picked colors.
/// </summary>
internal sealed class ColorPalette : UserControl
{
    private const double SquareWidth = 160, SquareHeight = 110, HueWidth = 16;
    private const int RecentCount = 10;

    private readonly SolidColorBrush _hueBrush = new();
    private readonly SolidColorBrush _previewBrush = new();
    private readonly Ellipse _squareMarker = new() { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Rectangle _hueMarker = new() { Width = HueWidth + 4, Height = 4, Stroke = Brushes.White, StrokeThickness = 1.5, Fill = Brushes.Transparent, IsHitTestVisible = false };
    private readonly TextBox _hex = new()
    {
        Width = 72,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Padding = new Thickness(2, 1, 2, 1),
        MaxLength = 7,
        CharacterCasing = CharacterCasing.Upper,
        VerticalContentAlignment = VerticalAlignment.Center,
    };
    private readonly WrapPanel _recent = new() { Width = 5 * 18 };

    private double _hue, _saturation = 1, _value = 1;

    public ColorPalette()
    {
        // Saturation goes left → right, brightness top → bottom.
        var square = new Grid { Width = SquareWidth, Height = SquareHeight, Cursor = Cursors.Cross };
        square.Clip = new RectangleGeometry(new Rect(0, 0, SquareWidth, SquareHeight), 4, 4);
        square.Children.Add(new Rectangle { Fill = _hueBrush });
        square.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        square.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        var squareCanvas = new Canvas();
        squareCanvas.Children.Add(_squareMarker);
        square.Children.Add(squareCanvas);
        HookDrag(square, p =>
        {
            _saturation = Math.Clamp(p.X / SquareWidth, 0, 1);
            _value = 1 - Math.Clamp(p.Y / SquareHeight, 0, 1);
        });

        var hueStops = new GradientStopCollection();
        for (int i = 0; i <= 6; i++)
            hueStops.Add(new GradientStop(FromHsv(i * 60 % 360, 1, 1), i / 6.0));
        var hue = new Grid { Width = HueWidth, Height = SquareHeight, Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0) };
        hue.Children.Add(new Rectangle { Fill = new LinearGradientBrush(hueStops, 90), RadiusX = 2, RadiusY = 2 });
        var hueCanvas = new Canvas { ClipToBounds = false };
        hueCanvas.Children.Add(_hueMarker);
        hue.Children.Add(hueCanvas);
        HookDrag(hue, p => _hue = Math.Clamp(p.Y / SquareHeight, 0, 1) * 359.9);

        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyHex();
                // Hand the keyboard back to the overlay. Focusing a window restores its remembered
                // focused element (this box), so reset that first.
                if (Window.GetWindow(this) is { } window)
                {
                    FocusManager.SetFocusedElement(window, window);
                    Keyboard.Focus(window);
                }
                e.Handled = true;
            }
        };
        _hex.LostKeyboardFocus += (_, _) => ApplyHex();

        var label = new TextBlock { Text = "最近使用", FontSize = 11, Margin = new Thickness(0, 8, 0, 3) }.Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal };
        hexRow.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = _previewBrush, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) }
            .Themed(Border.BorderBrushProperty, "Brush.Divider"));
        hexRow.Children.Add(_hex);
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        side.Children.Add(hexRow);
        side.Children.Add(label);
        side.Children.Add(_recent);

        var root = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 2) };
        root.Children.Add(square);
        root.Children.Add(hue);
        root.Children.Add(side);
        Content = root;

        LoadRecent();
        SetColor(Color.FromRgb(0xE5, 0x39, 0x35));
    }

    /// <summary>Raised when the user finishes choosing a color (mouse released, Enter, or a recent color clicked).</summary>
    public event Action<Color>? ColorPicked;

    public Color Color => FromHsv(_hue, _saturation, _value);

    /// <summary>Shows <paramref name="color"/> in the picker without raising <see cref="ColorPicked"/>.</summary>
    public void SetColor(Color color)
    {
        (_hue, _saturation, _value) = ToHsv(color);
        UpdateVisuals();
    }

    private void HookDrag(Panel area, Action<Point> update)
    {
        area.Background = Brushes.Transparent;
        area.MouseLeftButtonDown += (_, e) =>
        {
            area.CaptureMouse();
            update(e.GetPosition(area));
            UpdateVisuals();
            e.Handled = true;
        };
        area.MouseMove += (_, e) =>
        {
            if (!area.IsMouseCaptured)
                return;
            update(e.GetPosition(area));
            UpdateVisuals();
        };
        area.MouseLeftButtonUp += (_, e) =>
        {
            if (!area.IsMouseCaptured)
                return;
            area.ReleaseMouseCapture();
            Pick(Color);
            e.Handled = true;
        };
    }

    private void UpdateVisuals()
    {
        _hueBrush.Color = FromHsv(_hue, 1, 1);
        _previewBrush.Color = Color;
        Canvas.SetLeft(_squareMarker, _saturation * SquareWidth - _squareMarker.Width / 2);
        Canvas.SetTop(_squareMarker, (1 - _value) * SquareHeight - _squareMarker.Height / 2);
        _squareMarker.Stroke = _value > 0.6 && _saturation < 0.4 ? Brushes.Black : Brushes.White;
        Canvas.SetLeft(_hueMarker, -2);
        Canvas.SetTop(_hueMarker, _hue / 360 * SquareHeight - _hueMarker.Height / 2);
        if (!_hex.IsKeyboardFocused)
            _hex.Text = ToHex(Color);
    }

    private void ApplyHex()
    {
        if (TryParseHex(_hex.Text, out var color))
        {
            if (color == Color && _hex.Text == ToHex(color))
                return; // nothing changed (e.g. focus left without editing)
            SetColor(color);
            Pick(color);
        }
        _hex.Text = ToHex(Color);
    }

    private void Pick(Color color)
    {
        AddRecent(color);
        ColorPicked?.Invoke(color);
    }

    // ---------------------------------------------------------------- recent colors

    private void LoadRecent()
    {
        _recent.Children.Clear();
        foreach (var color in AppSettings.RecentColors.Take(RecentCount))
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 4, 4),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = ToHex(color),
            }.Themed(Border.BorderBrushProperty, "Brush.Divider");
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                SetColor(color);
                Pick(color);
                e.Handled = true;
            };
            _recent.Children.Add(swatch);
        }
    }

    private void AddRecent(Color color)
    {
        var colors = AppSettings.RecentColors.Where(c => c != color).Prepend(color).Take(RecentCount).ToList();
        AppSettings.RecentColors = colors;
        LoadRecent();
    }

    // ---------------------------------------------------------------- color math

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static bool TryParseHex(string text, out Color color)
    {
        color = default;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? _hue // gray: keep the current hue so the square doesn't jump
            : max == r ? 60 * ((g - b) / d % 6)
            : max == g ? 60 * ((b - r) / d + 2)
            : 60 * ((r - g) / d + 4);
        if (h < 0)
            h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
