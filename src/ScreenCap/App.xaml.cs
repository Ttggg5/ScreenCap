using System.Windows;
using ScreenCap.Capture;
using ScreenCap.Pin;
using ScreenCap.Recording;
using ScreenCap.Services;
using ScreenCap.Settings;
using ScreenCap.Theme;
using ScreenCap.Tray;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIconHost? _tray;
    private HotkeyService? _hotkeys;

    protected override void OnStartup(StartupEventArgs e)
    {
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
            ToggleRecording: ToggleRecording,
            PinFromClipboard: PinFromClipboard,
            CloseAllPins: PinWindow.CloseAll,
            ReleasePins: PinWindow.ReleaseAllClickThrough,
            OpenSettings: OpenSettings,
            Exit: Shutdown));
        Notifier.Handler = _tray.Notify;
        _hotkeys = new HotkeyService();

        var failed = RegisterHotkeys();
        if (failed.Count > 0)
            Notifier.Show($"熱鍵已被其他程式占用：{string.Join("、", failed)}。可在系統匣的「設定」中更換。");
        else
            Notifier.Show($"已在背景執行。{AppSettings.CaptureHotkey} 截圖、{AppSettings.RecordHotkey} 錄製螢幕、{AppSettings.PinHotkey} 將剪貼簿圖片貼到桌面。右鍵系統匣圖示可開啟設定與使用說明。");
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

    private void PinFromClipboard()
    {
        var image = ClipboardService.GetImage();
        if (image == null)
        {
            Notifier.Show("剪貼簿中沒有圖片。");
            return;
        }
        GetCursorPos(out var p);
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
