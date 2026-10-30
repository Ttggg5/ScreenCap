using System.IO;
using System.Windows;
using ScreenCap.Services;

namespace ScreenCap.Recording;

internal enum RecordingFormat
{
    Gif,
    Mp4,
}

/// <summary>Coordinates one screen recording: chrome windows, recorder, then saving the file.</summary>
internal static class RecordingSession
{
    private const int GifFramesPerSecond = 10;
    private const int Mp4FramesPerSecond = 30;

    private static ScreenRecorder? _recorder;
    private static RecordingFrameWindow? _frame;
    private static RecordingControlWindow? _bar;
    private static string? _tempPath;
    private static RecordingFormat _format;
    private static string? _title;
    private static bool _stopping;

    /// <summary>True from the start of recording until the file has been saved or discarded.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>Format for the next recording; remembered across runs.</summary>
    public static RecordingFormat Format
    {
        get => AppSettings.RecordingFormat;
        set => AppSettings.RecordingFormat = value;
    }

    public static string DisplayName(RecordingFormat format) => format == RecordingFormat.Mp4 ? "MP4" : "GIF";

    /// <param name="title">Title of the recorded window, for the file name.</param>
    public static async void Start(Int32Rect region, string? title = null)
    {
        if (IsActive || region.Width < 1 || region.Height < 1)
            return;
        IsActive = true;
        _title = title;
        _stopping = false;

        // Give DWM a moment to remove the selection overlay so it doesn't end up in the first frame.
        await Task.Delay(150);

        _format = Format;
        string temp = Path.Combine(Path.GetTempPath(), $"ScreenCap_{Guid.NewGuid():N}.{_format.ToString().ToLowerInvariant()}");
        _tempPath = temp;
        int width = region.Width, height = region.Height;
        bool clicks = AppSettings.HighlightClicks;
        var recorder = _format == RecordingFormat.Mp4
            ? new ScreenRecorder(region, Mp4FramesPerSecond, () => new Mp4Encoder(temp, width, height, Mp4FramesPerSecond), clicks)
            : new ScreenRecorder(region, GifFramesPerSecond, () => new GifEncoder(
                new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16), width, height, ownsStream: true), clicks);
        try
        {
            await recorder.StartAsync();
        }
        catch (Exception ex)
        {
            if (File.Exists(temp))
                File.Delete(temp);
            IsActive = false;
            MessageBox.Show($"無法開始錄製：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _recorder = recorder;
        _frame = new RecordingFrameWindow(region);
        _bar = new RecordingControlWindow(region, () => _recorder?.Elapsed ?? TimeSpan.Zero);
        _bar.PauseRequested += TogglePause;
        _bar.StopRequested += Stop;
        _bar.CancelRequested += Cancel;
        _frame.Show();
        _bar.Show();
    }

    public static bool IsPaused => _recorder?.IsPaused == true;

    /// <summary>Pauses or resumes the recording in progress.</summary>
    public static void TogglePause()
    {
        if (_recorder == null || _stopping)
            return;
        if (_recorder.IsPaused)
            _recorder.Resume();
        else
            _recorder.Pause();
        _bar?.SetPaused(_recorder.IsPaused);
    }

    public static void Stop() => Finish(save: true);

    public static void Cancel() => Finish(save: false);

    /// <summary>Synchronously stops and discards a recording when the app exits.</summary>
    public static void Abort()
    {
        if (_recorder == null)
            return;
        try
        {
            _recorder.StopAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Exiting anyway; nothing to report.
        }
        if (_tempPath != null && File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    private static async void Finish(bool save)
    {
        if (!IsActive || _stopping || _recorder == null)
            return;
        _stopping = true;
        _bar?.ShowSaving();

        Exception? error = null;
        try
        {
            await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        _frame?.Close();
        _bar?.Close();
        _frame = null;
        _bar = null;
        _recorder = null;
        string temp = _tempPath!;

        try
        {
            if (error != null)
                MessageBox.Show($"錄製失敗：{error.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Error);
            else if (save && ImageExportService.SaveRecording(temp, _format == RecordingFormat.Mp4, _title) is { } path)
            {
                ClipboardService.SetFile(path);
                Notifier.Show($"{DisplayName(_format)} 已儲存並複製到剪貼簿：{Path.GetFileName(path)}");
            }
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
            IsActive = false;
        }
    }
}
