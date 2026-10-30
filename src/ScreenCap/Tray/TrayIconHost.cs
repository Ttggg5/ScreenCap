using System.Drawing;
using ScreenCap.Help;
using ScreenCap.Pin;
using ScreenCap.Recording;
using ScreenCap.Services;
using ScreenCap.Theme;
using Forms = System.Windows.Forms;

namespace ScreenCap.Tray;

internal sealed record TrayActions(
    Action Capture,
    Action<int> DelayedCapture,
    Action ToggleRecording,
    Action PinFromClipboard,
    Action CloseAllPins,
    Action ReleasePins,
    Action OpenSettings,
    Action Exit);

internal sealed class TrayIconHost : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public TrayIconHost(TrayActions actions)
    {
        _icon = CreateIcon();

        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Forms.Padding(0, 4, 0, 4),
            Font = new Font("Microsoft JhengHei UI", 9.5f), // has both CJK and Latin glyphs, so sizes match
        };
        menu.Opening += (_, _) =>
        {
            var renderer = new TrayMenuRenderer(ThemeManager.IsDark);
            menu.Renderer = renderer;
            menu.BackColor = renderer.Background;
            foreach (Forms.ToolStripItem item in menu.Items)
                item.Padding = item is Forms.ToolStripSeparator ? item.Padding : new Forms.Padding(0, 5, 12, 5);
        };
        menu.HandleCreated += (_, _) => ThemeManager.RoundCorners(menu.Handle);
        // Wait for the menu to disappear so it doesn't end up in the screenshot.
        var capture = new Forms.ToolStripMenuItem();
        capture.Click += async (_, _) =>
        {
            await Task.Delay(250);
            actions.Capture();
        };
        var record = new Forms.ToolStripMenuItem();
        record.Click += async (_, _) =>
        {
            await Task.Delay(250);
            actions.ToggleRecording();
        };
        var pause = new Forms.ToolStripMenuItem();
        pause.Click += (_, _) => RecordingSession.TogglePause();
        var pin = new Forms.ToolStripMenuItem();
        pin.Click += (_, _) => actions.PinFromClipboard();
        menu.Opening += (_, _) =>
        {
            capture.Text = WithHotkey("截圖", AppSettings.CaptureHotkey);
            record.Text = WithHotkey(RecordingSession.IsActive ? "停止錄製" : "錄製螢幕", AppSettings.RecordHotkey);
            pause.Visible = RecordingSession.IsActive;
            pause.Text = RecordingSession.IsPaused ? "繼續錄製" : "暫停錄製";
            pin.Text = WithHotkey("剪貼簿貼為貼圖", AppSettings.PinHotkey);
        };
        menu.Items.Add(capture);
        var delayed = new Forms.ToolStripMenuItem("延遲截圖");
        foreach (int seconds in new[] { 3, 5, 10 })
            delayed.DropDownItems.Add($"{seconds} 秒", null, (_, _) => actions.DelayedCapture(seconds));
        delayed.DropDown.HandleCreated += (_, _) => ThemeManager.RoundCorners(delayed.DropDown.Handle);
        delayed.DropDownOpening += (_, _) =>
        {
            var renderer = new TrayMenuRenderer(ThemeManager.IsDark);
            delayed.DropDown.Renderer = renderer;
            delayed.DropDown.BackColor = renderer.Background;
            delayed.DropDown.Padding = new Forms.Padding(0, 4, 0, 4);
            ((Forms.ToolStripDropDownMenu)delayed.DropDown).ShowImageMargin = false;
            foreach (Forms.ToolStripItem item in delayed.DropDownItems)
                item.Padding = new Forms.Padding(0, 5, 12, 5);
        };
        menu.Items.Add(delayed);
        menu.Items.Add(record);
        menu.Items.Add(pause);
        menu.Items.Add(pin);
        var hidePins = new Forms.ToolStripMenuItem();
        hidePins.Click += (_, _) => PinWindow.SetHidden(!PinWindow.AreHidden);
        var restorePin = new Forms.ToolStripMenuItem("還原上次關閉的貼圖");
        restorePin.Click += (_, _) => PinWindow.RestoreLast();
        menu.Opening += (_, _) =>
        {
            hidePins.Text = PinWindow.AreHidden ? "顯示所有貼圖" : "隱藏所有貼圖";
            hidePins.Enabled = PinWindow.HasOpenPins;
            restorePin.Enabled = PinWindow.CanRestore;
        };
        menu.Items.Add(hidePins);
        menu.Items.Add(restorePin);
        menu.Items.Add("關閉所有貼圖", null, (_, _) => actions.CloseAllPins());
        menu.Items.Add("解除所有貼圖的滑鼠穿透", null, (_, _) => actions.ReleasePins());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var startup = new Forms.ToolStripMenuItem("開機時啟動") { CheckOnClick = true };
        menu.Opening += (_, _) => startup.Checked = StartupService.IsEnabled;
        startup.CheckedChanged += (_, _) =>
        {
            if (startup.Checked != StartupService.IsEnabled)
                StartupService.SetEnabled(startup.Checked);
        };
        menu.Items.Add(startup);
        menu.Items.Add("設定…", null, (_, _) => actions.OpenSettings());
        menu.Items.Add("使用說明", null, (_, _) => HelpWindow.ShowManual());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => actions.Exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "ScreenCap",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                actions.Capture();
        };
    }

    private static string WithHotkey(string text, Hotkey hotkey) => hotkey.IsNone ? text : $"{text} ({hotkey})";

    public void Notify(string title, string message) =>
        _notifyIcon.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);

    /// <summary>Loads the app icon at the tray's small-icon size so it isn't downscaled blurrily.</summary>
    private static Icon CreateIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ScreenCap.ico"));
        using var stream = resource!.Stream;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
