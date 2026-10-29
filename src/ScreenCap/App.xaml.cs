using System.Windows;
using System.Windows.Input;
using ScreenCap.Capture;
using ScreenCap.Pin;
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

        _tray = new TrayIconHost(CaptureSession.Start, PinFromClipboard, PinWindow.CloseAll, Shutdown);
        _hotkeys = new HotkeyService();

        var failed = new List<string>();
        if (!_hotkeys.Register(ModifierKeys.None, Key.F1, CaptureSession.Start))
            failed.Add("F1（截圖）");
        if (!_hotkeys.Register(ModifierKeys.None, Key.F3, PinFromClipboard))
            failed.Add("F3（貼圖）");

        if (failed.Count > 0)
            _tray.Notify("ScreenCap", $"熱鍵已被其他程式占用：{string.Join("、", failed)}。仍可從系統匣使用。");
        else
            _tray.Notify("ScreenCap", "已在背景執行。按 F1 截圖，F3 將剪貼簿圖片貼到桌面。");
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
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
