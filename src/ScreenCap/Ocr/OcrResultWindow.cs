using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Services;
using Language = Windows.Globalization.Language;

namespace ScreenCap.Ocr;

/// <summary>Shows the text recognized in an image, editable, with copy and a language switch.</summary>
internal sealed class OcrResultWindow : Window
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68));

    private readonly BitmapSource _image;
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 14,
        Padding = new Thickness(6),
        IsReadOnly = true,
    };
    private readonly ComboBox _language = new() { MinWidth = 160, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _copy = new() { Content = "複製", MinWidth = 80, Padding = new Thickness(12, 4, 12, 4), IsEnabled = false, ToolTip = "Ctrl+Enter：複製並關閉" };
    private int _run; // ignores results of a recognition superseded by a language change

    private OcrResultWindow(BitmapSource image)
    {
        _image = image;
        Title = "ScreenCap 文字辨識";
        Width = 560;
        Height = 420;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // above pins
        FontFamily = new FontFamily("Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;

        var languages = OcrService.Languages;
        var initial = OcrService.DefaultLanguage;
        foreach (var language in languages)
            _language.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language });
        _language.SelectedIndex = Math.Max(0, languages.ToList().FindIndex(l => l.LanguageTag == initial?.LanguageTag));
        _language.SelectionChanged += (_, _) => Recognize();

        _copy.Click += (_, _) => Copy();
        var close = new Button { Content = "關閉", MinWidth = 80, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock { Text = "辨識語言", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        top.Children.Add(label);
        top.Children.Add(_language);
        top.LastChildFill = false;

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_copy, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_copy);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_text);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            // Ctrl+Enter copies and closes, for a quick keyboard-only flow.
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && _copy.IsEnabled)
            {
                Copy();
                Close();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => Recognize();
    }

    public static void ShowFor(BitmapSource image)
    {
        var window = new OcrResultWindow(image);
        window.Show();
        window.Activate();
    }

    private async void Recognize()
    {
        int run = ++_run;
        _text.IsReadOnly = true;
        _copy.IsEnabled = false;
        _status.Text = "辨識中…";
        var language = (_language.SelectedItem as ComboBoxItem)?.Tag as Language;
        try
        {
            string text = await OcrService.RecognizeAsync(_image, language);
            if (run != _run)
                return;
            _text.Text = text;
            _text.IsReadOnly = false;
            bool found = text.Length > 0;
            _copy.IsEnabled = found;
            _status.Text = found ? $"辨識出 {text.Split(Environment.NewLine).Length} 行，可修改後再複製" : "沒有辨識到文字";
            _text.Focus();
            _text.SelectAll();
        }
        catch (Exception ex)
        {
            if (run != _run)
                return;
            _text.Text = "";
            _status.Text = $"辨識失敗：{ex.Message}";
        }
    }

    private void Copy()
    {
        ClipboardService.SetText(_text.Text);
        _status.Text = "已複製到剪貼簿";
    }
}
