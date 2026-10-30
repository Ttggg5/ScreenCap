using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ScreenCap.Services;

internal static class ImageExportService
{
    /// <summary>A free file name in <paramref name="folder"/> following the user's naming pattern.</summary>
    private static string NewPath(string folder, string extension, string? title) =>
        FileNameTemplate.UniquePath(folder, AppSettings.FileNamePattern, extension, DateTime.Now, title);

    /// <param name="title">Title of the captured window, for the {title} token.</param>
    /// <returns>true if the image was saved.</returns>
    public static bool Save(BitmapSource image, Window? owner = null, string? title = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "另存截圖",
            FileName = Path.GetFileNameWithoutExtension(NewPath(AppSettings.SaveFolder, ".png", title)),
            DefaultExt = ".png",
            Filter = "PNG 圖片 (*.png)|*.png|JPEG 圖片 (*.jpg)|*.jpg|BMP 圖片 (*.bmp)|*.bmp",
            InitialDirectory = AppSettings.SaveFolder,
        };
        return dialog.ShowDialog(owner) == true && Write(image, dialog.FileName) && AfterSave(dialog.FileName, copyPath: true);
    }

    /// <summary>Saves a PNG straight to the configured folder without asking.</summary>
    /// <returns>The saved path, or null if it failed.</returns>
    public static string? QuickSave(BitmapSource image, string? title = null)
    {
        string path = NewPath(AppSettings.SaveFolder, ".png", title);
        return Write(image, path) && AfterSave(path, copyPath: true) ? path : null;
    }

    private static bool Write(BitmapSource image, string path)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        encoder.Frames.Add(BitmapFrame.Create(image));

        try
        {
            using var stream = File.Create(path);
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
    public static string? SaveRecording(string sourcePath, bool mp4, string? title = null)
    {
        string folder = mp4 ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : AppSettings.SaveFolder;
        string extension = mp4 ? ".mp4" : ".gif";
        var dialog = new SaveFileDialog
        {
            Title = mp4 ? "儲存 MP4" : "儲存 GIF",
            FileName = Path.GetFileNameWithoutExtension(NewPath(folder, extension, title)),
            DefaultExt = extension,
            Filter = mp4 ? "MP4 影片 (*.mp4)|*.mp4" : "GIF 動畫 (*.gif)|*.gif",
            InitialDirectory = folder,
        };
        if (dialog.ShowDialog() != true)
            return null;

        try
        {
            File.Copy(sourcePath, dialog.FileName, overwrite: true);
            // The recording itself goes on the clipboard, so its path isn't copied.
            AfterSave(dialog.FileName, copyPath: false);
            return dialog.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex);
            return null;
        }
    }

    /// <summary>Copies the path and/or selects the file in Explorer, as configured.</summary>
    /// <returns>Always true, so it can be chained after a successful save.</returns>
    private static bool AfterSave(string path, bool copyPath)
    {
        if (copyPath && AppSettings.CopyPathAfterSave)
            ClipboardService.SetText(path);
        if (AppSettings.RevealAfterSave)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Explorer unavailable; the file is saved regardless.
            }
        }
        return true;
    }

    private static void ShowError(Exception ex) =>
        MessageBox.Show($"無法儲存檔案：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
}
