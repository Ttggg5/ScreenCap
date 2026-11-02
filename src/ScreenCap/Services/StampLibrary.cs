using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Annotations;

namespace ScreenCap.Services;

/// <summary>The user's own stamp pictures, kept as PNGs in the local app data folder.</summary>
internal static class StampLibrary
{
    /// <summary>Where the pictures are kept; tests point it elsewhere.</summary>
    internal static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCap", "Stamps");

    /// <summary>Pictures are shrunk to this longest side: plenty for a stamp, and keeps the files small.</summary>
    internal const int MaxSide = 512;

    private const string IdPrefix = "file:";
    private static List<Stamp>? _custom;

    /// <summary>Oldest first, so new ones are added at the end of the list.</summary>
    public static IReadOnlyList<Stamp> Custom => Load().ToList();

    /// <summary>A built-in or custom stamp by <see cref="Stamp.Id"/>, or null if it no longer exists.</summary>
    public static Stamp? Find(string? id) =>
        Stamp.BuiltIn.FirstOrDefault(s => s.Id == id) ?? Load().FirstOrDefault(s => s.Id == id);

    /// <summary>Stores a copy of <paramref name="image"/> as a new stamp.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static Stamp Add(BitmapSource image)
    {
        double fit = Math.Min(1, (double)MaxSide / Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource stored = fit < 1 ? new TransformedBitmap(image, new ScaleTransform(fit, fit)) : image;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(stored));

        var custom = Load(); // before writing, or reading the folder would list the new file already
        Directory.CreateDirectory(Folder);
        string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        string file = name + ".png";
        for (int n = 2; File.Exists(Path.Combine(Folder, file)); n++)
            file = $"{name}-{n}.png";
        using (var stream = File.Create(Path.Combine(Folder, file)))
            encoder.Save(stream);

        var stamp = FromFile(Path.Combine(Folder, file))!;
        custom.Add(stamp);
        return stamp;
    }

    public static void Remove(Stamp stamp)
    {
        var custom = Load();
        if (custom.RemoveAll(s => s.Id == stamp.Id) == 0)
            return;
        try
        {
            File.Delete(Path.Combine(Folder, stamp.Id[IdPrefix.Length..]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Shows up again next time; nothing else depends on it.
        }
    }

    /// <summary>Forgets the loaded pictures, so the next access reads the folder again (for tests).</summary>
    internal static void Reset() => _custom = null;

    private static List<Stamp> Load()
    {
        if (_custom != null)
            return _custom;
        var custom = new List<Stamp>();
        if (Directory.Exists(Folder))
        {
            foreach (var path in Directory.EnumerateFiles(Folder, "*.png").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (FromFile(path) is { } stamp)
                    custom.Add(stamp);
            }
        }
        return _custom = custom;
    }

    /// <returns>Null if the file can't be read as an image.</returns>
    private static Stamp? FromFile(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file open
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return new Stamp(IdPrefix + Path.GetFileName(path), "自訂圖片", StampStyle.Image, Image: image);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
