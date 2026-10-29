using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Capture;

/// <summary>Pixel magnifier shown next to the cursor, with coordinates and color under the cursor.</summary>
internal sealed class MagnifierControl : Border
{
    private readonly PixelView _view = new();
    private readonly TextBlock _position = CreateText();
    private readonly TextBlock _color = CreateText();
    private readonly Border _swatch = new() { Width = 12, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0) };

    public MagnifierControl()
    {
        IsHitTestVisible = false;
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(1);

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        colorRow.Children.Add(_swatch);
        colorRow.Children.Add(_color);

        var hint = CreateText();
        hint.Text = "C：複製色值";
        hint.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

        var info = new StackPanel { Margin = new Thickness(6, 4, 6, 5) };
        info.Children.Add(_position);
        info.Children.Add(colorRow);
        info.Children.Add(hint);

        var stack = new StackPanel();
        stack.Children.Add(new Border { CornerRadius = new CornerRadius(5, 5, 0, 0), ClipToBounds = true, Child = _view });
        stack.Children.Add(info);
        Child = stack;
    }

    public string HexColor { get; private set; } = "#000000";

    public void Update(ScreenSnapshot snapshot, int x, int y)
    {
        var c = snapshot.GetPixel(x, y);
        HexColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _position.Text = $"({x + snapshot.Left}, {y + snapshot.Top})";
        _color.Text = $"{HexColor}  RGB({c.R}, {c.G}, {c.B})";
        _swatch.Background = new SolidColorBrush(c);
        _view.Set(snapshot, x, y);
    }

    private static TextBlock CreateText() => new()
    {
        Foreground = Brushes.White,
        FontSize = 12,
        FontFamily = new FontFamily("Consolas, Microsoft JhengHei UI"),
    };

    private sealed class PixelView : FrameworkElement
    {
        private const int Cells = 15;
        private const double CellSize = 8;
        private static readonly Brush Band = new SolidColorBrush(Color.FromArgb(0x50, 0x1A, 0x9F, 0xFF));

        private ScreenSnapshot? _snapshot;
        private int _x, _y;

        public PixelView()
        {
            Width = Height = Cells * CellSize;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        }

        public void Set(ScreenSnapshot snapshot, int x, int y)
        {
            _snapshot = snapshot;
            _x = x;
            _y = y;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Cells * CellSize;
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, size, size));
            if (_snapshot == null)
                return;

            int half = Cells / 2;
            int x0 = _x - half, y0 = _y - half;
            int sx = Math.Max(0, x0), sy = Math.Max(0, y0);
            int ex = Math.Min(_snapshot.Width, x0 + Cells), ey = Math.Min(_snapshot.Height, y0 + Cells);
            if (ex > sx && ey > sy)
            {
                var crop = new CroppedBitmap(_snapshot.Bitmap, new Int32Rect(sx, sy, ex - sx, ey - sy));
                dc.DrawImage(crop, new Rect((sx - x0) * CellSize, (sy - y0) * CellSize, (ex - sx) * CellSize, (ey - sy) * CellSize));
            }

            double c = half * CellSize;
            dc.DrawRectangle(Band, null, new Rect(0, c, size, CellSize));
            dc.DrawRectangle(Band, null, new Rect(c, 0, CellSize, size));
            dc.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(c - 1, c - 1, CellSize + 2, CellSize + 2));
            dc.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(c, c, CellSize, CellSize));
        }
    }
}
