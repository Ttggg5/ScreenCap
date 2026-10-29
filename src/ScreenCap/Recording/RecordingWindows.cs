using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Recording;

/// <summary>Base for the recording chrome: topmost, never activated, and excluded from screen capture.</summary>
internal abstract class RecordingChromeWindow : Window
{
    protected RecordingChromeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SourceInitialized += (_, _) =>
        {
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | ExtraExStyle;
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);
            // Keep the chrome out of the recording even where it overlaps the region (Windows 10 2004+).
            SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
    }

    protected IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected double DpiScale => GetDpiForWindow(Handle) / 96.0;

    protected virtual int ExtraExStyle => 0;

    /// <summary>Positions the window in physical pixels.</summary>
    protected abstract void Place();
}

/// <summary>Red frame drawn just outside the recorded region. Click-through.</summary>
internal sealed class RecordingFrameWindow : RecordingChromeWindow
{
    private const double Thickness = 2; // DIP
    private readonly Int32Rect _region;
    private readonly Border _border = new() { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) };

    public RecordingFrameWindow(Int32Rect region)
    {
        _region = region;
        Content = _border;
    }

    protected override int ExtraExStyle => WS_EX_TRANSPARENT;

    protected override void Place()
    {
        // Move onto the target monitor first so the DPI is known, then size precisely.
        SetWindowPos(Handle, HWND_TOPMOST, _region.X, _region.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        double scale = DpiScale;
        int t = Math.Max(1, (int)Math.Round(Thickness * scale));
        _border.BorderThickness = new Thickness(t / scale);
        SetWindowPos(Handle, HWND_TOPMOST, _region.X - t, _region.Y - t, _region.Width + 2 * t, _region.Height + 2 * t, SWP_NOACTIVATE);
    }
}

/// <summary>Small bar next to the region with the elapsed time and stop/cancel buttons.</summary>
internal sealed class RecordingControlWindow : RecordingChromeWindow
{
    private const double BarWidth = 236, BarHeight = 40, Gap = 8; // DIP

    private readonly Int32Rect _region;
    private readonly Func<TimeSpan> _elapsed;
    private readonly TextBlock _time;
    private readonly TextBlock _status;
    private readonly Button _stop, _cancel;
    private readonly Ellipse _dot;
    private readonly DispatcherTimer _timer;

    public RecordingControlWindow(Int32Rect region, Func<TimeSpan> elapsed)
    {
        _region = region;
        _elapsed = elapsed;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)), Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { Text = "00:00", Foreground = Brushes.White, FontFamily = new FontFamily("Consolas"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock { Text = "錄製中", Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _stop = CreateButton("停止", Color.FromRgb(0xE5, 0x39, 0x35));
        _cancel = CreateButton("取消", Color.FromRgb(0x55, 0x55, 0x55));
        _stop.Click += (_, _) => StopRequested?.Invoke();
        _cancel.Click += (_, _) => CancelRequested?.Invoke();

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_dot);
        left.Children.Add(_time);
        left.Children.Add(_status);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0) };
        right.Children.Add(_stop);
        right.Children.Add(_cancel);
        var grid = new Grid();
        grid.Children.Add(left);
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

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, (_, _) => Tick(), Dispatcher);
        Closed += (_, _) => _timer.Stop();
    }

    public event Action? StopRequested;
    public event Action? CancelRequested;

    public void ShowSaving()
    {
        _timer.Stop();
        _status.Text = "處理中…";
        _stop.IsEnabled = _cancel.IsEnabled = false;
        _dot.Fill = Brushes.Gray;
    }

    private void Tick()
    {
        var t = _elapsed();
        _time.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        _dot.Opacity = t.Milliseconds < 500 ? 1 : 0.35; // blink
    }

    protected override void Place()
    {
        SetWindowPos(Handle, HWND_TOPMOST, _region.X, _region.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        double scale = DpiScale;
        int w = (int)Math.Round(BarWidth * scale), h = (int)Math.Round(BarHeight * scale), gap = (int)Math.Round(Gap * scale);

        // Below the region, right-aligned; above it if there's no room; otherwise inside the top edge.
        int screenTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int screenBottom = screenTop + GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int screenLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int x = Math.Max(screenLeft, _region.X + _region.Width - w);
        int y = _region.Y + _region.Height + gap;
        if (y + h > screenBottom)
            y = _region.Y - gap - h;
        if (y < screenTop)
            y = _region.Y + gap;
        SetWindowPos(Handle, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);
    }

    private static Button CreateButton(string text, Color background)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 3, 12, 4));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        return new Button
        {
            Content = text,
            Template = template,
            Background = new SolidColorBrush(background),
            Foreground = Brushes.White,
            FontSize = 13,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }
}
