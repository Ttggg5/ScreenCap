using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ScreenCap.Services;

/// <summary>
/// Writes unhandled exceptions to text files in %LocalAppData%\ScreenCap\Logs, so a crash leaves something to look at
/// (and to attach to a bug report) instead of the tray icon just disappearing.
/// </summary>
/// <remarks>
/// An exception on the UI thread is logged and the app keeps running: most come from one action (a click, a hotkey)
/// and the next one works. A burst of them means it is stuck failing (e.g. in a render loop), and it is let crash.
/// An exception on any other thread always ends the process; it is logged as a crash and reported then, or at the
/// next start if no message could be shown.
/// </remarks>
internal static class CrashLog
{
    internal static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCap", "Logs");

    /// <summary>The newest logs kept; older ones are deleted when a new one is written.</summary>
    internal const int MaxFiles = 20;

    /// <summary>This many UI-thread errors within <see cref="StormWindow"/> are treated as a crash.</summary>
    internal const int StormCount = 5;
    internal static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(10);

    private const string CrashPrefix = "crash-";
    private const string ErrorPrefix = "error-";
    /// <summary>Holds the name of the last crash log the user was told about.</summary>
    private const string ReportedFile = "reported.txt";

    private static readonly Queue<DateTime> RecentErrors = new();
    private static readonly object Gate = new();
    /// <summary>The exception last written, so one that is let through the dispatcher isn't logged again as fatal.</summary>
    private static Exception? _lastLogged;

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Nothing is waiting for the task, so nothing is broken: just keep the trace.
            Write(e.Exception, "未觀察的背景工作", fatal: false);
            e.SetObserved();
        };
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsStorm(DateTime.UtcNow))
        {
            // Let it crash: OnFatalException writes the log and tells the user.
            return;
        }
        e.Handled = true;
        if (Write(e.Exception, "介面執行緒", fatal: false) is { } path)
            Notifier.Show("發生未預期的錯誤，已寫入錯誤紀錄。點這裡查看。", () => Reveal(path));
    }

    private static void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
            exception = new Exception(e.ExceptionObject?.ToString() ?? "Unknown error");
        string? path = Write(exception, Thread.CurrentThread.IsBackground ? "背景執行緒" : "介面執行緒", fatal: true);
        if (path == null || !e.IsTerminating)
            return;
        try
        {
            var answer = MessageBox.Show(
                $"ScreenCap 發生錯誤，必須關閉。\n\n錯誤紀錄已存到：\n{path}\n\n要開啟紀錄所在的資料夾嗎？",
                "ScreenCap", MessageBoxButton.YesNo, MessageBoxImage.Error);
            MarkReported(path);
            if (answer == MessageBoxResult.Yes)
                Reveal(path);
        }
        catch
        {
            // No message now: the next start says so instead.
        }
    }

    /// <summary>Records a UI-thread error at <paramref name="now"/>.</summary>
    /// <returns>True if there have been too many in a short time to keep going.</returns>
    internal static bool IsStorm(DateTime now)
    {
        lock (Gate)
        {
            RecentErrors.Enqueue(now);
            while (RecentErrors.Count > 0 && now - RecentErrors.Peek() > StormWindow)
                RecentErrors.Dequeue();
            return RecentErrors.Count >= StormCount;
        }
    }

    internal static void ResetStorm()
    {
        lock (Gate)
            RecentErrors.Clear();
    }

    /// <summary>Writes a log for <paramref name="exception"/>; never throws.</summary>
    /// <param name="source">Where it was caught, shown in the log.</param>
    /// <param name="fatal">Whether the process is ending: crash logs are reported at the next start.</param>
    /// <returns>The log's path, or null if it was already written or could not be.</returns>
    internal static string? Write(Exception exception, string source, bool fatal)
    {
        lock (Gate)
        {
            if (ReferenceEquals(exception, _lastLogged))
                return null;
            _lastLogged = exception;
        }
        try
        {
            Directory.CreateDirectory(Folder);
            var now = DateTime.Now;
            byte[] text = new UTF8Encoding(false).GetBytes(Format(exception, source, fatal, now));
            string stem = $"{(fatal ? CrashPrefix : ErrorPrefix)}{now:yyyyMMdd-HHmmss-fff}";
            for (int n = 0; ; n++)
            {
                // Two in the same millisecond (e.g. on two threads) each get their own file.
                string path = Path.Combine(Folder, n == 0 ? $"{stem}.txt" : $"{stem}-{n}.txt");
                try
                {
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        file.Write(text);
                }
                catch (IOException) when (File.Exists(path) && n < 100)
                {
                    continue;
                }
                Prune();
                return path;
            }
        }
        catch
        {
            return null;
        }
    }

    internal static string Format(Exception exception, string source, bool fatal, DateTime time)
    {
        var text = new StringBuilder();
        text.AppendLine(fatal ? "ScreenCap 當機紀錄" : "ScreenCap 錯誤紀錄");
        text.AppendLine($"時間：{time:yyyy-MM-dd HH:mm:ss.fff zzz}");
        text.AppendLine($"版本：{Safe(() => Update.UpdateService.CurrentVersion.ToString())}");
        text.AppendLine($"系統：{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
        text.AppendLine($".NET：{RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"繪製：{Safe(() => AppSettings.HardwareRendering ? "GPU 加速" : "軟體")}");
        text.AppendLine($"發生於：{source}");
        text.AppendLine();
        text.AppendLine(exception.ToString());
        return text.ToString();
    }

    /// <summary>A detail for the log that must not keep the log from being written.</summary>
    private static string Safe(Func<string> detail)
    {
        try
        {
            return detail();
        }
        catch (Exception e)
        {
            return $"（無法取得：{e.GetType().Name}）";
        }
    }

    /// <summary>Deletes all but the newest <see cref="MaxFiles"/> logs.</summary>
    private static void Prune()
    {
        foreach (var old in Logs().Skip(MaxFiles))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The logs, newest first.</summary>
    private static IEnumerable<FileInfo> Logs()
    {
        var folder = new DirectoryInfo(Folder);
        if (!folder.Exists)
            return [];
        // The names sort by time; the prefix differs, so compare from the timestamp on.
        return folder.EnumerateFiles("*.txt")
            .Where(f => f.Name.StartsWith(CrashPrefix) || f.Name.StartsWith(ErrorPrefix))
            .OrderByDescending(f => f.Name[(f.Name.IndexOf('-') + 1)..], StringComparer.Ordinal);
    }

    /// <summary>
    /// The newest crash log the user hasn't been told about (when the process ended without showing a message),
    /// marked as told; null if there is none.
    /// </summary>
    public static string? TakeUnreportedCrash()
    {
        try
        {
            var newest = Logs().FirstOrDefault(f => f.Name.StartsWith(CrashPrefix));
            if (newest == null)
                return null;
            string reported = Path.Combine(Folder, ReportedFile);
            if (File.Exists(reported) && string.CompareOrdinal(File.ReadAllText(reported).Trim(), newest.Name) >= 0)
                return null;
            MarkReported(newest.FullName);
            return newest.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void MarkReported(string path)
    {
        try
        {
            File.WriteAllText(Path.Combine(Folder, ReportedFile), Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Shows the log selected in File Explorer.</summary>
    public static void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            // Not worth another error report.
        }
    }

    /// <summary>Opens the log folder in File Explorer, creating it if needed.</summary>
    public static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo(Folder) { UseShellExecute = true });
    }
}
