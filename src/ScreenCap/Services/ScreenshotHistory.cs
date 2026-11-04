using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace ScreenCap.Services;

/// <summary>A screenshot kept in the history.</summary>
/// <param name="File">File name in the history folder.</param>
/// <param name="Width">Size in pixels; 0 if unknown.</param>
internal sealed record HistoryEntry(string File, DateTime Time, int Width, int Height);

/// <summary>
/// Keeps a copy of every finished screenshot as a PNG in the local app data folder, newest first,
/// up to <see cref="AppSettings.HistoryLimit"/>. The folder is the whole record: each file is named by its time.
/// </summary>
internal static class ScreenshotHistory
{
    /// <summary>Where the screenshots are kept; tests point it elsewhere.</summary>
    internal static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCap", "History");

    private const string TimeFormat = "yyyyMMdd-HHmmss-fff";
    /// <summary>Kept the window titles in earlier versions; deleted when found.</summary>
    private const string OldIndexName = "index.json";
    private static readonly Lock Gate = new();
    private static List<HistoryEntry>? _entries;

    /// <summary>Raised after an entry is added or removed, possibly on a background thread.</summary>
    public static event Action? Changed;

    /// <summary>Newest first.</summary>
    public static IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (Gate)
                return Load().ToList();
        }
    }

    public static string PathOf(HistoryEntry entry) => Path.Combine(Folder, entry.File);

    /// <summary>Stores a copy of <paramref name="image"/>; does nothing if the history is turned off.</summary>
    /// <remarks>Never throws: a full disk must not break the capture itself.</remarks>
    public static Task AddAsync(BitmapSource image)
    {
        if (AppSettings.HistoryLimit <= 0)
            return Task.CompletedTask;
        // Encoding happens in the background, which needs an image not tied to this thread.
        var frozen = image;
        if (!frozen.IsFrozen)
        {
            frozen = new WriteableBitmap(image);
            frozen.Freeze();
        }
        var time = DateTime.Now;
        return Task.Run(() =>
        {
            try
            {
                Add(frozen, time);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keeping the history is best effort.
            }
        });
    }

    private static void Add(BitmapSource image, DateTime time)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        lock (Gate)
        {
            var entries = Load();
            Directory.CreateDirectory(Folder);
            string name = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
            string file = name + ".png";
            for (int n = 2; File.Exists(Path.Combine(Folder, file)); n++)
                file = $"{name}-{n}.png";
            using (var stream = File.Create(Path.Combine(Folder, file)))
                encoder.Save(stream);
            entries.Insert(0, new HistoryEntry(file, time, image.PixelWidth, image.PixelHeight));
            TrimLocked(entries, AppSettings.HistoryLimit);
        }
        Changed?.Invoke();
    }

    /// <summary>Removes the oldest screenshots beyond the limit, e.g. after lowering it in the settings.</summary>
    public static void Trim()
    {
        lock (Gate)
        {
            var entries = Load();
            if (!TrimLocked(entries, AppSettings.HistoryLimit))
                return;
        }
        Changed?.Invoke();
    }

    public static void Delete(HistoryEntry entry)
    {
        lock (Gate)
        {
            var entries = Load();
            if (entries.RemoveAll(e => e.File == entry.File) == 0)
                return;
            DeleteFile(PathOf(entry));
        }
        Changed?.Invoke();
    }

    /// <summary>The full image, read into memory so the file isn't kept open.</summary>
    public static BitmapSource LoadImage(HistoryEntry entry) => Decode(PathOf(entry), null);

    /// <summary>A small copy fitting in <paramref name="width"/> × <paramref name="height"/>, for previews.</summary>
    public static BitmapSource LoadThumbnail(HistoryEntry entry, int width, int height)
    {
        // Decode straight to the small size; only one side can be given, the other follows the aspect ratio.
        bool wide = entry.Width * height >= entry.Height * width;
        if (entry.Width <= width && entry.Height <= height)
            return Decode(PathOf(entry), null);
        return Decode(PathOf(entry), wide ? (width, 0) : (0, height));
    }

    private static BitmapSource Decode(string path, (int Width, int Height)? size)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        if (size is { } s)
        {
            image.DecodePixelWidth = s.Width;
            image.DecodePixelHeight = s.Height;
        }
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Forgets the loaded list, so the next access reads the folder again (for tests).</summary>
    internal static void Reset()
    {
        lock (Gate)
            _entries = null;
    }

    private static List<HistoryEntry> Load()
    {
        if (_entries != null)
            return _entries;
        var entries = new List<HistoryEntry>();
        if (Directory.Exists(Folder))
        {
            DeleteFile(Path.Combine(Folder, OldIndexName));
            DeleteFile(Path.Combine(Folder, OldIndexName + ".tmp"));
            foreach (var path in Directory.EnumerateFiles(Folder, "*.png"))
                entries.Add(FromFile(path));
        }
        // At the same time, "name-2.png" was saved after "name.png".
        entries.Sort((a, b) => a.Time != b.Time ? b.Time.CompareTo(a.Time)
            : a.File.Length != b.File.Length ? b.File.Length.CompareTo(a.File.Length) : string.CompareOrdinal(b.File, a.File));
        return _entries = entries;
    }

    private static HistoryEntry FromFile(string path)
    {
        string file = Path.GetFileName(path);
        if (file.Length < TimeFormat.Length || !DateTime.TryParseExact(file[..TimeFormat.Length], TimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            time = File.GetLastWriteTime(path); // not named by us
        int width = 0, height = 0;
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            (width, height) = (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // Size stays unknown.
        }
        return new HistoryEntry(file, time, width, height);
    }

    /// <returns>True if something was removed.</returns>
    private static bool TrimLocked(List<HistoryEntry> entries, int limit)
    {
        bool removed = false;
        while (entries.Count > Math.Max(limit, 0))
        {
            DeleteFile(PathOf(entries[^1]));
            entries.RemoveAt(entries.Count - 1);
            removed = true;
        }
        return removed;
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is picked up again the next time the folder is read.
        }
    }
}
