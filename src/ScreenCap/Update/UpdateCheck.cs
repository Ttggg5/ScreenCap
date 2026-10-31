using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ScreenCap.Services;

namespace ScreenCap.Update;

/// <summary>Checks for updates shortly after startup and then once a day, and on request from the tray menu.</summary>
internal static class UpdateCheck
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Poll = TimeSpan.FromHours(1);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private static DispatcherTimer? _timer;
    private static bool _checking;

    public static void StartAutomatic()
    {
        _timer = new DispatcherTimer { Interval = FirstDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Poll; // the computer may sleep, so poll and compare with the last check
            if (AppSettings.AutoCheckUpdates && DateTime.UtcNow - AppSettings.LastUpdateCheck >= Interval)
                Check(manual: false);
        };
        _timer.Start();
    }

    /// <param name="manual">From the menu: also report "up to date" and errors, and show skipped versions.</param>
    public static async void Check(bool manual)
    {
        if (_checking)
            return;
        _checking = true;
        UpdateInfo? update;
        try
        {
            update = await UpdateService.CheckAsync();
            AppSettings.LastUpdateCheck = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Offline etc.: an automatic check simply tries again later.
            if (manual)
                MessageBox.Show($"無法檢查更新：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            _checking = false;
        }

        if (update == null)
        {
            if (manual)
                MessageBox.Show($"目前已是最新版本（{UpdateService.CurrentVersion}）。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (manual)
            UpdateWindow.ShowFor(update);
        else if (AppSettings.SkippedUpdate != update.Tag)
            Notifier.Show($"有新版本 ScreenCap {update.Version}，點這裡查看並更新。", () => UpdateWindow.ShowFor(update));
    }
}
