using System.Diagnostics;
using System.Windows.Media.Imaging;
using ScreenCap.Services;

namespace ScreenCap.Barcode;

/// <summary>
/// The QR code hotkey: decodes the selected region and copies the content right away, without opening
/// a window. Clicking the notification opens the result window, e.g. to open a link.
/// </summary>
internal static class QuickBarcode
{
    public static async void Run(BitmapSource image)
    {
        try
        {
            var codes = await BarcodeService.DecodeAsync(image);
            if (codes.Count == 0)
            {
                Notifier.Show("沒有找到 QR Code 或條碼。");
                return;
            }
            ClipboardService.SetText(string.Join(Environment.NewLine, codes.Select(c => c.Text)));
            string what = codes.Count == 1 ? codes[0].Format : $"{codes.Count} 個 QR Code / 條碼";
            string message = $"已複製 {what} 的內容：{Notifier.Preview(codes[0].Text)}";
            // A single link opens straight from the notification; otherwise the window lists every code.
            if (codes is [{ IsLink: true } link])
                Notifier.Show(message + "\n點這裡開啟連結。", () => Process.Start(new ProcessStartInfo(link.Text.Trim()) { UseShellExecute = true }));
            else
                Notifier.Show(message, () => BarcodeResultWindow.ShowFor(image));
        }
        catch (Exception ex)
        {
            Notifier.Show($"QR Code 辨識失敗：{ex.Message}");
        }
    }
}
