using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenCap.Recording;

namespace ScreenCap.Scrolling;

/// <summary>Status bar next to the region during a scrolling capture, with Done and Cancel.</summary>
internal sealed class ScrollControlWindow : RecordingChromeWindow
{
    private const double BarWidth = 300, BarHeight = 40, Gap = 8; // DIP

    private readonly Int32Rect _region;
    private readonly TextBlock _status;

    public ScrollControlWindow(Int32Rect region)
    {
        _region = region;
        _status = new TextBlock
        {
            Text = "長截圖中…",
            Foreground = Brushes.White,
            FontSize = 13,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var done = CreateButton("完成", Color.FromRgb(0x1E, 0x88, 0xE5));
        var cancel = CreateButton("取消", Color.FromRgb(0x55, 0x55, 0x55));
        done.Click += (_, _) => DoneRequested?.Invoke();
        cancel.Click += (_, _) => CancelRequested?.Invoke();

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
        right.Children.Add(done);
        right.Children.Add(cancel);
        var grid = new Grid();
        grid.Children.Add(_status);
        grid.Children.Add(right);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(8),
            Child = grid,
        };
        FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI");
        Width = BarWidth;
        Height = BarHeight;
    }

    public event Action? DoneRequested;
    public event Action? CancelRequested;

    public void SetStatus(string text) => _status.Text = text;

    protected override void Place() => PlaceBar(_region, BarWidth, BarHeight, Gap);
}
