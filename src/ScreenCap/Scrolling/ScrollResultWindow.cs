using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Ocr;
using ScreenCap.Services;
using ScreenCap.Theme;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Scrolling;

/// <summary>Preview of a scrolling capture with copy, save and text recognition.</summary>
internal sealed class ScrollResultWindow : Window
{
    private readonly BitmapSource _image;
    private readonly Image _view;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _status;

    private ScrollResultWindow(BitmapSource image)
    {
        _image = image;
        Title = "ScreenCap 長截圖";
        Width = 760;
        Height = 760;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);

        // Fills the available width, but never beyond 100%; the height follows the aspect ratio.
        _view = new Image { Source = image, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);
        _scroller = new ScrollViewer
        {
            Content = _view,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8),
        }.Themed(BackgroundProperty, "Brush.Card");
        var frame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _scroller, BorderThickness = new Thickness(1) }
            .Themed(Border.BorderBrushProperty, "Brush.Border");

        _status = new TextBlock { Text = $"{image.PixelWidth:N0} × {image.PixelHeight:N0} 像素", VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "Brush.Muted");

        var copy = new Button { Content = "複製", MinWidth = 90, ToolTip = "Ctrl+C" }.Themed(StyleProperty, "AccentButton");
        copy.Click += (_, _) => Copy();
        var save = new Button { Content = "另存新檔…", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ctrl+S" };
        save.Click += (_, _) => ImageExportService.Save(_image, this);
        var ocr = new Button { Content = "文字辨識", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        ocr.Click += (_, _) => OcrResultWindow.ShowFor(_image);
        var close = new Button { Content = "關閉", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(ocr);
        buttons.Children.Add(close);
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(frame);
        Content = root;

        SourceInitialized += (_, _) => LimitToActualSize();
        DpiChanged += (_, _) => LimitToActualSize();
        PreviewKeyDown += (_, e) =>
        {
            bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
            if (ctrl && e.Key == Key.C)
            {
                Copy();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.S)
            {
                ImageExportService.Save(_image, this);
                e.Handled = true;
            }
        };
    }

    public static void ShowFor(BitmapSource image)
    {
        var window = new ScrollResultWindow(image);
        window.Show();
        window.Activate();
    }

    /// <summary>Caps the preview at 100% (one image pixel per screen pixel) on the current monitor.</summary>
    private void LimitToActualSize()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = hwnd == IntPtr.Zero ? 1 : GetDpiForWindow(hwnd) / 96.0;
        _view.MaxWidth = _image.PixelWidth / scale;
    }

    private void Copy()
    {
        ClipboardService.SetImage(_image);
        _status.Text = $"{_image.PixelWidth:N0} × {_image.PixelHeight:N0} 像素 · 已複製到剪貼簿";
    }
}
