using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ScreenCap.Services;

internal static class ImageExportService
{
    /// <returns>true if the image was saved.</returns>
    public static bool Save(BitmapSource image, Window? owner = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "另存截圖",
            FileName = $"ScreenCap_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".png",
            Filter = "PNG 圖片 (*.png)|*.png|JPEG 圖片 (*.jpg)|*.jpg|BMP 圖片 (*.bmp)|*.bmp",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(owner) != true)
            return false;

        BitmapEncoder encoder = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(image));

        try
        {
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"無法儲存檔案：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
