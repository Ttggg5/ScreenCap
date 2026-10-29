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
            ShowError(ex);
            return false;
        }
    }

    /// <summary>Asks where to save a recording (GIF or MP4) and copies it there.</summary>
    /// <returns>The saved path, or null if cancelled or failed.</returns>
    public static string? SaveRecording(string sourcePath, bool mp4)
    {
        var dialog = new SaveFileDialog
        {
            Title = mp4 ? "儲存 MP4" : "儲存 GIF",
            FileName = $"ScreenCap_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = mp4 ? ".mp4" : ".gif",
            Filter = mp4 ? "MP4 影片 (*.mp4)|*.mp4" : "GIF 動畫 (*.gif)|*.gif",
            InitialDirectory = Environment.GetFolderPath(mp4 ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog() != true)
            return null;

        try
        {
            File.Copy(sourcePath, dialog.FileName, overwrite: true);
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex);
            return null;
        }
    }

    private static void ShowError(Exception ex) =>
        MessageBox.Show($"無法儲存檔案：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
}
