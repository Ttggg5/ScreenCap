using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenCap.Services;

internal static class ClipboardService
{
    /// <summary>
    /// Copies an image as a DIB, which every app reads (Windows makes a bitmap from it for those asking for one).
    /// One with transparency also goes on as PNG, which keeps it in apps that read PNG (Office, browsers, chat
    /// apps); the DIB is then drawn on white, since apps without transparency would show black.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Clipboard.SetImage"/>: it also renders a GDI bitmap that is never freed, so every copy
    /// left a screenshot's worth of memory behind.
    /// </remarks>
    public static void SetImage(BitmapSource image)
    {
        var data = new DataObject();
        bool transparent = Beautifier.HasTransparency(image);
        data.SetData(DataFormats.Dib, new System.IO.MemoryStream(ToDib(transparent ? Beautifier.OnWhite(image) : image)), false);
        if (transparent)
        {
            var png = new System.IO.MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            data.SetData("PNG", png, false);
        }
        Retry(() => Clipboard.SetDataObject(data, true));
    }

    /// <summary>A packed DIB: a BITMAPINFOHEADER, then 32-bit pixels bottom-up, as CF_DIB holds them.</summary>
    internal static byte[] ToDib(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        const int headerSize = 40;
        var dib = new byte[headerSize + pixels.Length];
        var header = dib.AsSpan();
        BitConverter.TryWriteBytes(header[0..], headerSize);   // biSize
        BitConverter.TryWriteBytes(header[4..], w);            // biWidth
        BitConverter.TryWriteBytes(header[8..], h);            // biHeight: positive, bottom-up, which every reader supports
        BitConverter.TryWriteBytes(header[12..], (short)1);    // biPlanes
        BitConverter.TryWriteBytes(header[14..], (short)32);   // biBitCount; biCompression = BI_RGB (0)
        BitConverter.TryWriteBytes(header[20..], pixels.Length); // biSizeImage
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(pixels, y * stride, dib, headerSize + (h - 1 - y) * stride, stride);
        return dib;
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
            // Our own copy comes back as the DIB stream it was put on as, which GetImage doesn't read.
            var image = Clipboard.GetImage()
                ?? (Clipboard.GetData(DataFormats.Dib) is System.IO.MemoryStream dib ? FromDib(dib.ToArray()) : null);
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

    /// <summary>Decodes a packed DIB (CF_DIB) by giving it the file header that makes it a BMP file.</summary>
    internal static BitmapSource? FromDib(byte[] dib)
    {
        if (dib.Length < 40)
            return null;
        int headerSize = BitConverter.ToInt32(dib, 0);
        int bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int colorsUsed = BitConverter.ToInt32(dib, 32);
        // The pixels follow the header, the BI_BITFIELDS masks of a plain BITMAPINFOHEADER and the color table.
        int masks = headerSize == 40 && compression == 3 ? 12 : 0;
        int colors = (colorsUsed > 0 ? colorsUsed : bitCount <= 8 ? 1 << bitCount : 0) * 4;
        int offset = 14 + headerSize + masks + colors;

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
        BitConverter.TryWriteBytes(bmp.AsSpan(10), offset);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        try
        {
            var frame = new BmpBitmapDecoder(new System.IO.MemoryStream(bmp), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.FileFormatException or ArgumentException)
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
