using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Ocr;

/// <summary>
/// The text-recognition hotkey: recognizes the text in the selected region and copies it right away,
/// without opening a window. Clicking the notification opens the full result window to edit it.
/// </summary>
internal static class QuickOcr
{
    public static async void Run(BitmapSource image)
    {
        void OpenWindow() => OcrResultWindow.ShowFor(image);
        try
        {
            string text = await OcrService.RecognizeAsync(image);
            if (text.Length == 0)
            {
                Notifier.Show("沒有辨識到文字。點這裡可換個辨識語言再試一次。", OpenWindow);
                return;
            }
            ClipboardService.SetText(text);
            int lines = text.Split(Environment.NewLine).Length;
            Notifier.Show($"已複製 {lines} 行文字：{Notifier.Preview(text)}", OpenWindow);
        }
        catch (Exception ex)
        {
            Notifier.Show($"文字辨識失敗：{ex.Message}");
        }
    }
}
