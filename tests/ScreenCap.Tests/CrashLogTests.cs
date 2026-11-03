using System.IO;
using ScreenCap.Services;

namespace ScreenCap.Tests;

public class CrashLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ScreenCap.Tests.Logs." + Guid.NewGuid().ToString("N"));
    private readonly string _original = CrashLog.Folder;

    public CrashLogTests()
    {
        CrashLog.Folder = _folder;
        CrashLog.ResetStorm();
    }

    public void Dispose()
    {
        CrashLog.Folder = _original;
        CrashLog.ResetStorm();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    /// <summary>An exception with a stack trace, as one that was actually thrown.</summary>
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message, new FormatException("inner cause"));
        }
        catch (Exception e)
        {
            return e;
        }
    }

    [Fact]
    public void Write_SavesTheExceptionAndTheVersion()
    {
        string? path = CrashLog.Write(Thrown("something broke"), "介面執行緒", fatal: false);

        Assert.NotNull(path);
        Assert.StartsWith("error-", Path.GetFileName(path));
        string text = File.ReadAllText(path);
        Assert.Contains("ScreenCap 錯誤紀錄", text);
        Assert.Contains($"版本：{Update.UpdateService.CurrentVersion}", text);
        Assert.Contains("發生於：介面執行緒", text);
        Assert.Contains("InvalidOperationException: something broke", text);
        Assert.Contains("inner cause", text);
        Assert.Contains(nameof(Thrown), text); // the stack trace
    }

    [Fact]
    public void Write_Fatal_IsACrashLog()
    {
        string? path = CrashLog.Write(Thrown("fatal"), "背景執行緒", fatal: true);

        Assert.StartsWith("crash-", Path.GetFileName(path));
        Assert.Contains("ScreenCap 當機紀錄", File.ReadAllText(path!));
    }

    [Fact]
    public void Write_TheSameExceptionOnlyOnce()
    {
        // A UI-thread exception that is let through is raised again as a fatal one.
        var e = Thrown("once");
        Assert.NotNull(CrashLog.Write(e, "介面執行緒", fatal: false));
        Assert.Null(CrashLog.Write(e, "介面執行緒", fatal: true));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Write_KeepsEachLog_AndOnlyTheNewest()
    {
        var paths = Enumerable.Range(0, CrashLog.MaxFiles + 5).Select(i => CrashLog.Write(Thrown($"error {i}"), "test", fatal: false)).ToList();

        Assert.Equal(paths.Count, paths.Distinct().Count()); // none overwrote another, even in the same millisecond
        var kept = Directory.GetFiles(_folder);
        Assert.Equal(CrashLog.MaxFiles, kept.Length);
        Assert.Contains(paths[^1], kept);
        Assert.DoesNotContain(paths[0], kept);
    }

    [Fact]
    public void Write_DoesNotThrow_WhenTheFolderCannotBeCreated()
    {
        Directory.CreateDirectory(_folder);
        string file = Path.Combine(_folder, "a file");
        File.WriteAllText(file, "");
        CrashLog.Folder = Path.Combine(file, "Logs");

        Assert.Null(CrashLog.Write(Thrown("nowhere"), "test", fatal: true));
    }

    [Fact]
    public void TakeUnreportedCrash_ReturnsEachCrashOnce()
    {
        Assert.Null(CrashLog.TakeUnreportedCrash());

        CrashLog.Write(Thrown("handled"), "test", fatal: false);
        Assert.Null(CrashLog.TakeUnreportedCrash()); // the app kept running: nothing to tell at the next start

        string? first = CrashLog.Write(Thrown("first"), "test", fatal: true);
        Assert.Equal(first, CrashLog.TakeUnreportedCrash());
        Assert.Null(CrashLog.TakeUnreportedCrash());

        Thread.Sleep(5); // a later timestamp
        string? second = CrashLog.Write(Thrown("second"), "test", fatal: true);
        Assert.Equal(second, CrashLog.TakeUnreportedCrash());
        Assert.Null(CrashLog.TakeUnreportedCrash());
    }

    [Fact]
    public void IsStorm_AfterManyErrorsInAShortTime()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < CrashLog.StormCount - 1; i++)
            Assert.False(CrashLog.IsStorm(start.AddSeconds(i)));
        Assert.True(CrashLog.IsStorm(start.AddSeconds(CrashLog.StormCount - 1)));
    }

    [Fact]
    public void IsStorm_NotForErrorsSpreadOut()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < CrashLog.StormCount * 3; i++)
            Assert.False(CrashLog.IsStorm(start + CrashLog.StormWindow * i / 2 + TimeSpan.FromSeconds(i)));
    }
}
