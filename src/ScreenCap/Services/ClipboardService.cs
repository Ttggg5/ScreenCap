using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Services;

internal static class ClipboardService
{
    /// <summary>
    /// Copies an image. One with transparency also goes on as PNG, which keeps it in apps that read PNG
    /// (Office, browsers, chat apps); the plain bitmap others read is drawn on white, since they would show black.
    /// </summary>
    public static void SetImage(BitmapSource image)
    {
        if (!Beautifier.HasTransparency(image))
        {
            Retry(() => Clipboard.SetImage(image));
            return;
        }
        var png = new System.IO.MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        var data = new DataObject();
        data.SetImage(Beautifier.OnWhite(image));
        data.SetData("PNG", png, false);
        Retry(() => Clipboard.SetDataObject(data, true));
    }

    public static void SetText(string text) => Retry(() => Clipboard.SetText(text));

    /// <summary>Puts a file on the clipboard so it can be pasted into chat apps and Explorer.</summary>
    public static void SetFile(string path) => Retry(() => Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { path }));

    /// <summary>
    /// Reads an image from the clipboard. Alpha is dropped because many apps put
    /// 32-bit DIBs with an all-zero alpha channel, which WPF would show as fully transparent.
    /// </summary>
    public static BitmapSource? GetImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
                return null;
            var image = Clipboard.GetImage();
            if (image == null)
                return null;
            var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    // The clipboard can be briefly locked by another process.
    private static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }
}
