using System.Windows;
using System.Windows.Input;
using ScreenCap.Capture;
using ScreenCap.Pin;
using ScreenCap.Recording;
using ScreenCap.Services;
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

        _tray = new TrayIconHost(() => CaptureSession.Start(), ToggleRecording, PinFromClipboard, PinWindow.CloseAll, Shutdown);
        RecordingSession.Notify = _tray.Notify;
        _hotkeys = new HotkeyService();

        var failed = new List<string>();
        if (!_hotkeys.Register(ModifierKeys.None, Key.F1, () => CaptureSession.Start()))
            failed.Add("F1（截圖）");
        if (!_hotkeys.Register(ModifierKeys.Shift, Key.F1, ToggleRecording))
            failed.Add("Shift+F1（錄製螢幕）");
        if (!_hotkeys.Register(ModifierKeys.None, Key.F3, PinFromClipboard))
            failed.Add("F3（貼圖）");

        if (failed.Count > 0)
            _tray.Notify("ScreenCap", $"熱鍵已被其他程式占用：{string.Join("、", failed)}。仍可從系統匣使用。");
        else
            _tray.Notify("ScreenCap", "已在背景執行。F1 截圖、Shift+F1 錄製螢幕、F3 將剪貼簿圖片貼到桌面。右鍵系統匣圖示可開啟使用說明。");
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
            _tray?.Notify("ScreenCap", "剪貼簿中沒有圖片。");
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
