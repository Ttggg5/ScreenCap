using System.Windows;
using ScreenCap.Capture;
using ScreenCap.Pin;
using ScreenCap.Recording;
using ScreenCap.Services;
using ScreenCap.Settings;
using ScreenCap.Theme;
using ScreenCap.Tray;
using ScreenCap.Update;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIconHost? _tray;
    private HotkeyService? _hotkeys;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool updated = WaitForReplacedProcess(e.Args);
        _singleInstance = new Mutex(true, "ScreenCap.SingleInstance.7F3C2A1E", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ThemeManager.Initialize(this);

        _tray = new TrayIconHost(new TrayActions(
            Capture: () => CaptureSession.Start(),
            DelayedCapture: CaptureSession.StartDelayed,
            ReopenLast: CaptureSession.ReopenLast,
            ToggleRecording: ToggleRecording,
            PinFromClipboard: PinFromClipboard,
            CloseAllPins: PinWindow.CloseAll,
            ReleasePins: PinWindow.ReleaseAllClickThrough,
            OpenSettings: OpenSettings,
            CheckForUpdates: () => UpdateCheck.Check(manual: true),
            Exit: Shutdown));
        Notifier.Handler = _tray.Notify;
        _hotkeys = new HotkeyService();

        if (Environment.ProcessPath is { } exe)
            UpdateService.CleanUp(exe);
        UpdateCheck.StartAutomatic();

        var failed = RegisterHotkeys();
        if (failed.Count > 0)
            Notifier.Show($"熱鍵已被其他程式占用：{string.Join("、", failed)}。可在系統匣的「設定」中更換。");
        else if (updated)
            Notifier.Show($"已更新到 ScreenCap {UpdateService.CurrentVersion}。");
        else
            Notifier.Show($"已在背景執行。{AppSettings.CaptureHotkey} 截圖、{AppSettings.RecordHotkey} 錄製螢幕、{AppSettings.PinHotkey} 將剪貼簿圖片貼到桌面。右鍵系統匣圖示可開啟設定與使用說明。");
    }

    /// <summary>
    /// After a self-update, the new exe is started with the id of the old process: wait for it to exit,
    /// so it releases the single-instance mutex and its exe file.
    /// </summary>
    /// <returns>True if this run follows an update.</returns>
    private static bool WaitForReplacedProcess(string[] args)
    {
        if (args.Length < 2 || args[0] != UpdateService.UpdatedSwitch || !int.TryParse(args[1], out int pid))
            return false;
        try
        {
            using var old = System.Diagnostics.Process.GetProcessById(pid);
            old.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        return true;
    }

    /// <returns>Descriptions of the hotkeys that could not be registered.</returns>
    private List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        _hotkeys!.UnregisterAll();
        if (!_hotkeys.Register(AppSettings.CaptureHotkey, () => CaptureSession.Start()))
            failed.Add($"{AppSettings.CaptureHotkey}（截圖）");
        if (!_hotkeys.Register(AppSettings.RecordHotkey, ToggleRecording))
            failed.Add($"{AppSettings.RecordHotkey}（錄製螢幕）");
        if (!_hotkeys.Register(AppSettings.PinHotkey, PinFromClipboard))
            failed.Add($"{AppSettings.PinHotkey}（貼圖）");
        if (!_hotkeys.Register(AppSettings.ReopenHotkey, CaptureSession.ReopenLast))
            failed.Add($"{AppSettings.ReopenHotkey}（再次開啟上一張截圖）");
        return failed;
    }

    private bool _settingsOpen;

    private void OpenSettings()
    {
        if (_settingsOpen)
            return;
        _settingsOpen = true;
        // Release the hotkeys so they can be typed into the dialog.
        _hotkeys!.UnregisterAll();
        try
        {
            new SettingsWindow().ShowDialog();
        }
        finally
        {
            _settingsOpen = false;
            var failed = RegisterHotkeys();
            if (failed.Count > 0)
                MessageBox.Show($"以下熱鍵已被其他程式占用，無法使用：\n{string.Join("\n", failed)}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Starts selecting a region to record, or stops the recording in progress.</summary>
    private static void ToggleRecording()
    {
        if (RecordingSession.IsActive)
            RecordingSession.Stop();
        else
            CaptureSession.Start(recordMode: true);
    }

    /// <summary>Pins the clipboard image; text is pinned as an image too, and color codes as a swatch.</summary>
    private void PinFromClipboard()
    {
        GetCursorPos(out var p);
        var image = ClipboardService.GetImage();
        if (image == null)
        {
            double scale = GetDpiForMonitor(MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
            image = TextPinRenderer.FromText(ClipboardService.GetText(), scale);
        }
        if (image == null)
        {
            Notifier.Show("剪貼簿中沒有圖片或文字。");
            return;
        }
        PinWindow.ShowAt(image, p.X, p.Y);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RecordingSession.Abort();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
