using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Services;
using ScreenCap.Theme;

namespace ScreenCap.Update;

/// <summary>Announces a new release with its notes, and downloads and installs it.</summary>
internal sealed class UpdateWindow : Window
{
    private static UpdateWindow? _open;

    private readonly UpdateInfo _update;
    private readonly ProgressBar _progress = new() { Height = 6, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _install = new() { MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
    private readonly Button _later = new() { Content = "稍後", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
    private readonly Button _skip = new() { Content = "略過這個版本" };
    private CancellationTokenSource? _download;

    private UpdateWindow(UpdateInfo update)
    {
        _update = update;
        Title = "ScreenCap 更新";
        Width = 520;
        Height = 460;
        MinWidth = 400;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // above pins
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);
        _status.Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        _install.Themed(StyleProperty, "AccentButton");

        var heading = new TextBlock
        {
            Text = $"有新版本：ScreenCap {update.Version}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        };
        var current = new TextBlock { Text = $"目前版本：{UpdateService.CurrentVersion}", Margin = new Thickness(0, 4, 0, 10) }
            .Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(update.Notes) ? "（沒有更新說明）" : PlainText(update.Notes),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            FontSize = 13,
        };

        if (UpdateService.CanSelfUpdate)
        {
            _install.Content = "立即更新";
            _status.Text = "更新時會下載新版並重新啟動 ScreenCap，目前開著的貼圖會關閉。";
            _install.Click += (_, _) => Install();
        }
        else
        {
            _install.Content = "前往下載頁面";
            _status.Text = "目前執行的不是發佈的單一執行檔（例如從原始碼執行），無法自動更新，請從下載頁面取得新版。";
            _install.Click += (_, _) => OpenPage();
        }
        _later.Click += (_, _) => Close();
        _skip.Click += (_, _) =>
        {
            AppSettings.SkippedUpdate = update.Tag;
            Close();
        };

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_later, Dock.Right);
        DockPanel.SetDock(_install, Dock.Right);
        buttons.Children.Add(_skip);
        buttons.Children.Add(_later);
        buttons.Children.Add(_install);

        var top = new StackPanel();
        top.Children.Add(heading);
        top.Children.Add(current);
        var bottom = new StackPanel();
        bottom.Children.Add(_progress);
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(notes);
        Content = root;

        Closed += (_, _) =>
        {
            _download?.Cancel();
            _open = null;
        };
    }

    /// <summary>Shows the window for <paramref name="update"/>, or brings the open one to the front.</summary>
    public static void ShowFor(UpdateInfo update)
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }
        _open = new UpdateWindow(update);
        _open.Show();
        _open.Activate();
    }

    /// <summary>Release notes are Markdown; drops the markup that would otherwise show as symbols.</summary>
    internal static string PlainText(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Trim().Split('\n').Select(line =>
        {
            string text = line.Replace("**", "").Replace("`", "");
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith('#'))
                return trimmed.TrimStart('#').Trim();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                return text[..(text.Length - trimmed.Length)] + "• " + trimmed[2..];
            return text;
        });
        return string.Join("\n", lines);
    }

    private void OpenPage() =>
        Process.Start(new ProcessStartInfo(_update.PageUrl) { UseShellExecute = true });

    private async void Install()
    {
        if (RecordingSession.IsActive)
        {
            MessageBox.Show(this, "正在錄製螢幕，請先停止錄製再更新。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _install.IsEnabled = _skip.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "下載中…";
        _download = new CancellationTokenSource();
        var progress = new Progress<double>(p =>
        {
            _progress.Value = p;
            _status.Text = $"下載中… {p:P0}（共 {_update.Size / 1048576.0:0} MB）";
        });
        try
        {
            string file = await UpdateService.DownloadAsync(_update, progress, _download.Token);
            _status.Text = "安裝中…";
            UpdateService.ReplaceExecutable(Environment.ProcessPath!, file);
            try
            {
                UpdateService.Restart();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"新版已安裝，但無法自動重新啟動（{ex.Message}），請手動開啟 ScreenCap。", "ScreenCap",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            // The window was closed during the download.
        }
        catch (Exception ex)
        {
            _progress.Visibility = Visibility.Collapsed;
            _install.IsEnabled = _skip.IsEnabled = true;
            _status.Text = $"更新失敗：{ex.Message}";
            if (MessageBox.Show(this, $"更新失敗：{ex.Message}\n\n要開啟下載頁面手動下載嗎？", "ScreenCap",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                OpenPage();
        }
    }
}
