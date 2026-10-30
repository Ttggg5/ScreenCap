using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ScreenCap.Services;

/// <summary>
/// Expands file name patterns such as "ScreenCap_{date}_{time}". Tokens:
/// {date} = yyyyMMdd, {time} = HHmmss, {title} = title of the captured window, {n} = sequence number (001, 002, …).
/// Anything else in braces is a date format, e.g. {yyyy-MM-dd}.
/// </summary>
internal static partial class FileNameTemplate
{
    public const string Default = "ScreenCap_{date}_{time}";
    private const string Fallback = "ScreenCap";
    private const int MaxTitleLength = 60;
    private const int MaxLength = 150;

    /// <summary>The file name (without extension) for one capture.</summary>
    public static string Expand(string pattern, DateTime time, string? title, int number = 1)
    {
        string name = TokenRegex().Replace(pattern, m => Token(m.Groups[1].Value, time, title, number));
        return Clean(name);
    }

    /// <summary>
    /// A path in <paramref name="folder"/> that doesn't exist yet: {n} counts up to the first free number;
    /// without {n}, "_2", "_3", … is appended when the name is taken.
    /// </summary>
    public static string UniquePath(string folder, string pattern, string extension, DateTime time, string? title, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (pattern.Contains("{n}", StringComparison.Ordinal))
        {
            for (int n = 1; n < 10000; n++)
            {
                string numbered = Path.Combine(folder, Expand(pattern, time, title, n) + extension);
                if (!exists(numbered))
                    return numbered;
            }
        }

        string name = Expand(pattern, time, title);
        string path = Path.Combine(folder, name + extension);
        for (int i = 2; exists(path); i++)
            path = Path.Combine(folder, $"{name}_{i}{extension}");
        return path;
    }

    private static string Token(string token, DateTime time, string? title, int number)
    {
        switch (token)
        {
            case "date":
                return time.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            case "time":
                return time.ToString("HHmmss", CultureInfo.InvariantCulture);
            case "title":
                string t = (title ?? "").Trim();
                return t.Length > MaxTitleLength ? t[..MaxTitleLength] : t;
            case "n":
                return number.ToString("000", CultureInfo.InvariantCulture);
        }
        try
        {
            // A single letter would be a standard format ("d" → 10/30/2026); only custom formats are meant here.
            string format = token.Length == 1 ? "%" + token : token;
            return time.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return "{" + token + "}";
        }
    }

    /// <summary>Replaces characters Windows doesn't allow and trims separators left over from empty tokens.</summary>
    private static string Clean(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string cleaned = new string(chars).Trim(' ', '.', '_', '-');
        if (cleaned.Length > MaxLength)
            cleaned = cleaned[..MaxLength].TrimEnd(' ', '.');
        return cleaned.Length == 0 ? Fallback : cleaned;
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();
}
