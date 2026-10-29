using System.Drawing;
using System.Drawing.Drawing2D;
using ScreenCap.Services;
using Forms = System.Windows.Forms;
using static ScreenCap.Native.NativeMethods;

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

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(0x1A, 0x9F, 0xFF));
            using var path = RoundedRect(new Rectangle(1, 1, 30, 30), 7);
            g.FillPath(background, path);

            // Corner brackets of a selection frame
            using var pen = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, new Point[] { new(8, 14), new(8, 8), new(14, 8) });
            g.DrawLines(pen, new Point[] { new(18, 24), new(24, 24), new(24, 18) });
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
