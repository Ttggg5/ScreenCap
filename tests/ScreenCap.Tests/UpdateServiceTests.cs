using System.IO;
using System.Security.Cryptography;
using System.Text;
using ScreenCap.Update;

namespace ScreenCap.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ScreenCap.Tests_").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Release(string tag, bool draft = false, bool prerelease = false, string asset = "ScreenCap.exe", string? digest = "sha256:ABCDEF") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/Ttggg5/ScreenCap/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "body": "修正\n- 一些問題",
          "assets": [
            { "name": "notes.txt", "size": 1, "browser_download_url": "https://example.com/notes.txt", "digest": null },
            { "name": "{{asset}}", "size": 1234, "browser_download_url": "https://example.com/{{asset}}", "digest": {{(digest == null ? "null" : $"\"{digest}\"")}} }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V2", "2.0.0")]
    [InlineData("v1.3.0-beta.1", "1.3.0")]
    [InlineData("v1.2.3.4", "1.2.3")]
    public void ParseVersion_ReadsTags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void ParseVersion_RejectsNonVersions(string tag) => Assert.Null(UpdateService.ParseVersion(tag));

    [Fact]
    public void ParseRelease_NewerVersion_ReturnsTheExeAsset()
    {
        var update = UpdateService.ParseRelease(Release("v1.2.0"), new Version(1, 1, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 0), update.Version);
        Assert.Equal("v1.2.0", update.Tag);
        Assert.Equal("https://example.com/ScreenCap.exe", update.DownloadUrl);
        Assert.Equal(1234, update.Size);
        Assert.Equal("ABCDEF", update.Sha256);
        Assert.Contains("一些問題", update.Notes);
        Assert.EndsWith("/tag/v1.2.0", update.PageUrl);
    }

    [Theory]
    [InlineData("v1.1.0")] // same
    [InlineData("v1.0.9")] // older
    public void ParseRelease_NotNewer_ReturnsNull(string tag) =>
        Assert.Null(UpdateService.ParseRelease(Release(tag), new Version(1, 1, 0)));

    [Fact]
    public void ParseRelease_IgnoresDraftsPrereleasesAndReleasesWithoutTheExe()
    {
        var current = new Version(1, 0, 0);
        Assert.Null(UpdateService.ParseRelease(Release("v2.0.0", draft: true), current));
        Assert.Null(UpdateService.ParseRelease(Release("v2.0.0", prerelease: true), current));
        Assert.Null(UpdateService.ParseRelease(Release("v2.0.0", asset: "ScreenCap.zip"), current));
    }

    [Fact]
    public void ParseRelease_WithoutDigest_HasNoHash() =>
        Assert.Null(UpdateService.ParseRelease(Release("v2.0.0", digest: null), new Version(1, 0, 0))!.Sha256);

    [Fact]
    public void ReleaseNotes_ShowWithoutMarkdownSymbols() =>
        Assert.Equal("新功能\n\n• 錄進聲音\n  • 巢狀 F1\n一般文字",
            UpdateWindow.PlainText("## 新功能\r\n\r\n- 錄進**聲音**\r\n  * 巢狀 `F1`\r\n一般文字"));

    [Fact]
    public void Verify_ChecksSizeAndSha256()
    {
        string path = Path.Combine(_dir, "new.exe");
        var data = Encoding.ASCII.GetBytes("new version");
        File.WriteAllBytes(path, data);
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        UpdateService.Verify(path, data.Length, hash);
        UpdateService.Verify(path, data.Length, null);
        Assert.Throws<InvalidDataException>(() => UpdateService.Verify(path, data.Length + 1, hash));
        Assert.Throws<InvalidDataException>(() => UpdateService.Verify(path, data.Length, new string('0', 64)));
    }

    [Fact]
    public void ReplaceExecutable_MovesTheOldOneAside()
    {
        string exe = Path.Combine(_dir, "ScreenCap.exe"), download = exe + ".new";
        File.WriteAllText(exe, "old");
        File.WriteAllText(exe + ".old", "from an earlier update");
        File.WriteAllText(download, "new");

        UpdateService.ReplaceExecutable(exe, download);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(exe + ".old"));
        Assert.False(File.Exists(download));

        UpdateService.CleanUp(exe);
        Assert.False(File.Exists(exe + ".old"));
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void ReplaceExecutable_PutsTheOldOneBack_IfTheNewOneCantBeMovedIn()
    {
        string exe = Path.Combine(_dir, "ScreenCap.exe");
        File.WriteAllText(exe, "old");

        Assert.ThrowsAny<IOException>(() => UpdateService.ReplaceExecutable(exe, Path.Combine(_dir, "missing.new")));

        Assert.Equal("old", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + ".old"));
    }

    [Fact]
    public void ReplaceExecutable_WorksWhileTheExeIsRunning()
    {
        // A running exe is open without delete sharing; renaming it must still work.
        string exe = Path.Combine(_dir, "ScreenCap.exe"), download = exe + ".new";
        File.WriteAllText(exe, "old");
        File.WriteAllText(download, "new");
        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            UpdateService.ReplaceExecutable(exe, download);

        Assert.Equal("new", File.ReadAllText(exe));
    }
}
