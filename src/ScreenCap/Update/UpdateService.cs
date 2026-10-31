using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace ScreenCap.Update;

/// <summary>A newer release on GitHub.</summary>
internal sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Finds the latest GitHub release and replaces the running single-file exe with it. A running exe can't be
/// deleted but can be renamed, so it moves aside to <c>.old</c>, the new file takes its place, and the
/// restarted app removes the old one.
/// </summary>
internal static class UpdateService
{
    public const string Repository = "Ttggg5/ScreenCap";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases/latest";
    private const string AssetName = "ScreenCap.exe";

    /// <summary>Command-line switch of the restarted app, followed by the id of the process it replaces.</summary>
    public const string UpdatedSwitch = "--updated";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion => Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// Only the published single-file exe can replace itself; a development build runs from a folder of
    /// DLLs (its assembly has a location on disk).
    /// </summary>
    public static bool CanSelfUpdate => string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location) && Environment.ProcessPath != null;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ScreenCap/{CurrentVersion}"); // required by the GitHub API
        return client;
    }

    /// <returns>The latest release if it is newer than this version and has the exe attached; otherwise null.</returns>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(cancel), CurrentVersion);
    }

    /// <summary>Reads a GitHub release (REST API JSON).</summary>
    public static UpdateInfo? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var release = doc.RootElement;
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (ParseVersion(tag) is not { } version || version <= current)
            return null;

        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                continue;
            string? digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new UpdateInfo(
                version,
                tag,
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                release.GetProperty("html_url").GetString() ?? ReleasesPage,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.GetProperty("size").GetInt64(),
                digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null);
        }
        return null; // a release without the exe, e.g. still being uploaded
    }

    /// <summary>"v1.2" or "1.2.3" → 1.2.0 / 1.2.3; null if it isn't a version.</summary>
    public static Version? ParseVersion(string tag)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        int suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            text = text[..suffix];
        return Version.TryParse(text.Contains('.') ? text : text + ".0", out var version) ? Normalize(version) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Downloads the new exe next to the running one and checks its size and SHA-256.</summary>
    /// <param name="progress">Fraction downloaded, 0 to 1.</param>
    /// <returns>Path of the verified download.</returns>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken cancel = default)
    {
        string target = Environment.ProcessPath! + ".new"; // same folder, so it can be moved into place
        try
        {
            using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read, percent = -1;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    int now = update.Size > 0 ? (int)Math.Min(100, done * 100 / update.Size) : 0;
                    if (now != percent) // each report updates the UI, so only whole percents
                        progress?.Report((percent = now) / 100.0);
                }
            }
            Verify(target, update.Size, update.Sha256);
            return target;
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <exception cref="InvalidDataException">The file isn't the one the release describes.</exception>
    public static void Verify(string path, long size, string? sha256)
    {
        long length = new FileInfo(path).Length;
        if (length != size)
            throw new InvalidDataException($"下載的檔案大小不符（{length:N0} / {size:N0} 位元組）。");
        if (sha256 is null)
            return;
        using var stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("下載的檔案驗證失敗（SHA-256 不符），可能已損毀。");
    }

    /// <summary>Puts <paramref name="newFile"/> in place of <paramref name="exePath"/>, keeping the old one as <c>.old</c>.</summary>
    /// <remarks>If the new file can't be moved in, the old exe is put back.</remarks>
    public static void ReplaceExecutable(string exePath, string newFile)
    {
        string old = exePath + ".old";
        if (File.Exists(old))
            File.Delete(old); // left over from an earlier update
        File.Move(exePath, old);
        try
        {
            File.Move(newFile, exePath);
        }
        catch
        {
            File.Move(old, exePath);
            throw;
        }
    }

    /// <summary>Starts the updated exe, which waits for this process to exit before taking over.</summary>
    public static void Restart() =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{UpdatedSwitch} {Environment.ProcessId}") { UseShellExecute = false });

    /// <summary>Removes the previous exe and any unfinished download; called by the app at startup.</summary>
    public static void CleanUp(string exePath)
    {
        TryDelete(exePath + ".old");
        TryDelete(exePath + ".new");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
