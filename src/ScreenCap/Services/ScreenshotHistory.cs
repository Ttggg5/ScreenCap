using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace ScreenCap.Services;

/// <summary>A screenshot kept in the history.</summary>
/// <param name="File">File name in the history folder.</param>
/// <param name="Title">Title of the captured window, if known.</param>
/// <param name="Width">Size in pixels; 0 if unknown.</param>
/// <param name="Text">Text recognized in it, for searching; null until recognized.</param>
internal sealed record HistoryEntry(string File, DateTime Time, string? Title, int Width, int Height, string? Text = null);

/// <summary>
/// Keeps a copy of every finished screenshot as a PNG in the local app data folder, newest first,
/// up to <see cref="AppSettings.HistoryLimit"/>. An index file remembers the window titles.
/// </summary>
internal static class ScreenshotHistory
{
    /// <summary>Where the screenshots are kept; tests point it elsewhere.</summary>
    internal static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCap", "History");

    private const string IndexName = "index.json";
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
    public static Task AddAsync(BitmapSource image, string? title = null)
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
                Add(frozen, title, time);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keeping the history is best effort.
            }
        });
    }

    private static void Add(BitmapSource image, string? title, DateTime time)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        lock (Gate)
        {
            var entries = Load();
            Directory.CreateDirectory(Folder);
            string name = time.ToString("yyyyMMdd-HHmmss-fff");
            string file = name + ".png";
            for (int n = 2; File.Exists(Path.Combine(Folder, file)); n++)
                file = $"{name}-{n}.png";
            using (var stream = File.Create(Path.Combine(Folder, file)))
                encoder.Save(stream);
            entries.Insert(0, new HistoryEntry(file, time, string.IsNullOrWhiteSpace(title) ? null : title, image.PixelWidth, image.PixelHeight));
            TrimLocked(entries, AppSettings.HistoryLimit);
            SaveIndex(entries);
        }
        Changed?.Invoke();
        _ = IndexTextAsync();
    }

    // ---------------------------------------------------------------- text search

    private static Task? _indexing;

    /// <summary>True while text is being recognized in screenshots that don't have it yet.</summary>
    public static bool IsIndexing
    {
        get
        {
            lock (Gate)
                return _indexing != null;
        }
    }

    /// <summary>Recognizes the text in every screenshot that hasn't been yet, one at a time, in the background.</summary>
    /// <returns>Completes when no screenshot is left, or when recognition is unavailable.</returns>
    public static Task IndexTextAsync()
    {
        lock (Gate)
            return _indexing ??= Task.Run(IndexPendingAsync);
    }

    private static async Task IndexPendingAsync()
    {
        Changed?.Invoke(); // IsIndexing turned on
        try
        {
            while (true)
            {
                HistoryEntry? next;
                lock (Gate)
                {
                    next = Load().FirstOrDefault(e => e.Text == null);
                    // Decided under the lock, so a screenshot added meanwhile starts a new run.
                    if (next == null)
                    {
                        _indexing = null;
                        break;
                    }
                }
                string text;
                try
                {
                    text = await OcrService.RecognizeAsync(LoadImage(next));
                }
                catch (InvalidOperationException)
                {
                    // No OCR language installed: try again next time.
                    lock (Gate)
                        _indexing = null;
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
                {
                    text = ""; // unreadable file: don't retry it forever
                }
                lock (Gate)
                {
                    var entries = Load();
                    int index = entries.FindIndex(e => e.File == next.File);
                    if (index >= 0)
                    {
                        entries[index] = entries[index] with { Text = text };
                        try
                        {
                            SaveIndex(entries);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Kept in memory; written with the next change.
                        }
                    }
                }
            }
        }
        catch
        {
            lock (Gate)
                _indexing = null;
            throw;
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// True if every word of <paramref name="query"/> appears in the window title or the recognized text.
    /// Spaces are ignored when comparing, since OCR puts them unreliably between CJK characters.
    /// </summary>
    public static bool Matches(HistoryEntry entry, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return true;
        string haystack = WithoutSpaces($"{entry.Title}\n{entry.Text}");
        return words.All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>The part of the recognized text around the first word of <paramref name="query"/>, on one line.</summary>
    /// <returns>Null if the text doesn't contain it.</returns>
    public static string? Snippet(HistoryEntry entry, string query, int length = 40)
    {
        var word = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (word == null || string.IsNullOrEmpty(entry.Text))
            return null;
        string text = string.Join(" ", entry.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        int at = text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
        if (at < 0)
            return null;
        int start = Math.Max(0, Math.Min(at - length / 3, text.Length - length));
        int end = Math.Min(text.Length, start + length);
        return (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
    }

    private static string WithoutSpaces(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>Removes the oldest screenshots beyond the limit, e.g. after lowering it in the settings.</summary>
    public static void Trim()
    {
        lock (Gate)
        {
            var entries = Load();
            if (!TrimLocked(entries, AppSettings.HistoryLimit))
                return;
            SaveIndex(entries);
        }
        Changed?.Invoke();
    }

    public static void Delete(HistoryEntry entry)
    {
        lock (Gate)
        {
            var entries = Load();
            // By file: the caller's copy may predate its recognized text.
            if (entries.RemoveAll(e => e.File == entry.File) == 0)
                return;
            DeleteFile(entry);
            SaveIndex(entries);
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            var entries = Load();
            foreach (var entry in entries)
                DeleteFile(entry);
            entries.Clear();
            SaveIndex(entries);
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

    /// <summary>Forgets the loaded index, so the next access reads the folder again (for tests).</summary>
    internal static void Reset()
    {
        lock (Gate)
            _entries = null;
    }

    private static List<HistoryEntry> Load()
    {
        if (_entries != null)
            return _entries;
        List<HistoryEntry>? entries = null;
        try
        {
            entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(Path.Combine(Folder, IndexName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // No index yet, or it is damaged: rebuilt from the files below.
        }
        entries = entries?.Where(e => File.Exists(Path.Combine(Folder, e.File))).ToList() ?? [];
        // Pick up PNGs missing from the index, e.g. after a crash while saving it.
        if (Directory.Exists(Folder))
        {
            var known = entries.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(Folder, "*.png"))
            {
                if (!known.Contains(Path.GetFileName(path)))
                    entries.Add(FromFile(path));
            }
        }
        entries.Sort((a, b) => b.Time.CompareTo(a.Time));
        return _entries = entries;
    }

    private static HistoryEntry FromFile(string path)
    {
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
        return new HistoryEntry(Path.GetFileName(path), File.GetLastWriteTime(path), null, width, height);
    }

    /// <returns>True if something was removed.</returns>
    private static bool TrimLocked(List<HistoryEntry> entries, int limit)
    {
        bool removed = false;
        while (entries.Count > Math.Max(limit, 0))
        {
            DeleteFile(entries[^1]);
            entries.RemoveAt(entries.Count - 1);
            removed = true;
        }
        return removed;
    }

    private static void DeleteFile(HistoryEntry entry)
    {
        try
        {
            File.Delete(PathOf(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is picked up again the next time the folder is read.
        }
    }

    private static void SaveIndex(List<HistoryEntry> entries)
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, IndexName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(entries));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
