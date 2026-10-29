using System.Drawing;
using ScreenCap.Help;
using ScreenCap.Services;
using Forms = System.Windows.Forms;

namespace ScreenCap.Tray;

internal sealed class TrayIconHost : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    public TrayIconHost(Action capture, Action pinFromClipboard, Action closeAllPins, Action exit)
    {
        _icon = CreateIcon();

        var menu = new Forms.ContextMenuStrip();
        // Wait for the menu to disappear so it doesn't end up in the screenshot.
        menu.Items.Add("截圖 (F1)", null, async (_, _) =>
        {
            await Task.Delay(250);
            capture();
        });
        menu.Items.Add("剪貼簿貼為貼圖 (F3)", null, (_, _) => pinFromClipboard());
        menu.Items.Add("關閉所有貼圖", null, (_, _) => closeAllPins());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var startup = new Forms.ToolStripMenuItem("開機時啟動") { CheckOnClick = true };
        menu.Opening += (_, _) => startup.Checked = StartupService.IsEnabled;
        startup.CheckedChanged += (_, _) =>
        {
            if (startup.Checked != StartupService.IsEnabled)
                StartupService.SetEnabled(startup.Checked);
        };
        menu.Items.Add(startup);
        menu.Items.Add("使用說明", null, (_, _) => HelpWindow.ShowManual());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "ScreenCap — F1 截圖",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                capture();
        };
    }

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
