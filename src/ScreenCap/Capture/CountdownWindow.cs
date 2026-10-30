using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Capture;

/// <summary>
/// Small countdown badge in the bottom-right corner before a delayed capture. It never takes focus
/// (so menus and tooltips stay open), is excluded from capture, and cancels when clicked.
/// </summary>
internal sealed class CountdownWindow : Window
{
    private readonly TextBlock _number = new()
    {
        FontSize = 34,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly DispatcherTimer _timer;
    private int _remaining;

    private CountdownWindow(int seconds)
    {
        _remaining = seconds;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Hand;
        ToolTip = "點一下取消";

        var hint = new TextBlock
        {
            Text = "秒後截圖 · 點此取消",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            FontFamily = new FontFamily("Microsoft JhengHei UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var stack = new StackPanel { Margin = new Thickness(22, 10, 22, 12) };
        stack.Children.Add(_number);
        stack.Children.Add(hint);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 16, 16),
            Child = stack,
        };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea; // primary monitor, DIPs
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        };
        MouseLeftButtonDown += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        _number.Text = _remaining.ToString();
    }

    private event Action? Elapsed;

    /// <summary>Counts down <paramref name="seconds"/>, then calls <paramref name="elapsed"/> unless cancelled.</summary>
    public static CountdownWindow Start(int seconds, Action elapsed)
    {
        var window = new CountdownWindow(seconds);
        window.Elapsed += elapsed;
        window.Show();
        window._timer.Start();
        return window;
    }

    private async void Tick()
    {
        if (--_remaining > 0)
        {
            _number.Text = _remaining.ToString();
            return;
        }
        _timer.Stop();
        Close();
        await Task.Delay(150); // let DWM remove the badge before the screen is frozen
        Elapsed?.Invoke();
    }
}
