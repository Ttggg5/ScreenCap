using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;
using ScreenCap.Theme;

namespace ScreenCap.Barcode;

/// <summary>Lists the QR codes and barcodes found in an image, each with copy, and open for links.</summary>
internal sealed class BarcodeResultWindow : Window
{
    private readonly BitmapSource _image;
    private readonly StackPanel _codes = new();
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };

    private BarcodeResultWindow(BitmapSource image)
    {
        _image = image;
        Title = "ScreenCap QR Code / 條碼";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinWidth = 360;
        MaxHeight = 600;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // above pins
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);
        _status.Themed(TextBlock.ForegroundProperty, "Brush.Muted");

        var close = new Button { Content = "關閉", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = _codes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Loaded += (_, _) => Decode();
    }

    public static void ShowFor(BitmapSource image)
    {
        var window = new BarcodeResultWindow(image);
        window.Show();
        window.Activate();
    }

    private async void Decode()
    {
        _status.Text = "辨識中…";
        try
        {
            var codes = await BarcodeService.DecodeAsync(_image);
            foreach (var code in codes)
                _codes.Children.Add(CodeRow(code));
            _status.Text = codes.Count switch
            {
                0 => "沒有找到 QR Code 或條碼",
                1 => $"找到 1 個 {codes[0].Format}",
                _ => $"找到 {codes.Count} 個 QR Code / 條碼",
            };
        }
        catch (Exception ex)
        {
            _status.Text = $"辨識失敗：{ex.Message}";
        }
    }

    private UIElement CodeRow(BarcodeResult code)
    {
        var format = new TextBlock { Text = code.Format, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) }
            .Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        var content = new TextBox
        {
            Text = code.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var copy = new Button { Content = "複製", MinWidth = 70, Margin = new Thickness(8, 0, 0, 0) }.Themed(StyleProperty, "AccentButton");
        copy.Click += (_, _) =>
        {
            ClipboardService.SetText(code.Text);
            _status.Text = $"已複製 {code.Format} 的內容";
        };

        var line = new DockPanel();
        DockPanel.SetDock(copy, Dock.Right);
        line.Children.Add(copy);
        if (code.IsLink)
        {
            var open = new Button { Content = "開啟", MinWidth = 70, Margin = new Thickness(8, 0, 0, 0), ToolTip = "在瀏覽器中開啟連結" };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo(code.Text.Trim()) { UseShellExecute = true });
            DockPanel.SetDock(open, Dock.Right);
            line.Children.Add(open);
        }
        line.Children.Add(content);

        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(format);
        row.Children.Add(line);
        return row;
    }
}
