using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenCap.Services;
using ScreenCap.Theme;

namespace ScreenCap.Settings;

/// <summary>Preferences dialog: hotkeys, save folder, file names and start-at-sign-in.</summary>
internal sealed class SettingsWindow : Window
{

    private readonly HotkeyBox _capture = new(AppSettings.CaptureHotkey);
    private readonly HotkeyBox _record = new(AppSettings.RecordHotkey);
    private readonly HotkeyBox _pin = new(AppSettings.PinHotkey);
    private readonly HotkeyBox _reopen = new(AppSettings.ReopenHotkey);
    private readonly HotkeyBox _history = new(AppSettings.HistoryHotkey);
    private readonly HotkeyBox _ocr = new(AppSettings.OcrHotkey);
    private readonly HotkeyBox _barcode = new(AppSettings.BarcodeHotkey);
    private readonly TextBox _folder = new() { IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _pattern = new() { Text = AppSettings.FileNamePattern, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _patternPreview = Hint("");
    private readonly CheckBox _startup = new() { Content = "登入 Windows 時自動啟動 ScreenCap", IsChecked = StartupService.IsEnabled, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _theme = new();
    private readonly CheckBox _cursor = new() { Content = "截圖時包含滑鼠游標", IsChecked = AppSettings.CaptureCursor, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _clicks = new() { Content = "錄影時標示滑鼠點擊", IsChecked = AppSettings.HighlightClicks, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _keys = new() { Content = "錄影時顯示按下的按鍵（例如 Ctrl + C）", IsChecked = AppSettings.ShowKeystrokes, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _trim = new() { Content = "錄完後先開啟裁剪視窗，剪掉開頭與結尾", IsChecked = AppSettings.TrimAfterRecording, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _hardware = new() { Content = "使用 GPU 加速繪製（重新啟動 ScreenCap 後生效）", IsChecked = AppSettings.HardwareRendering, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _autoUpdate = new() { Content = "自動檢查更新（每天一次）", IsChecked = AppSettings.AutoCheckUpdates, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _copyPath = new() { Content = "存檔後複製檔案路徑", IsChecked = AppSettings.CopyPathAfterSave, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _reveal = new() { Content = "存檔後在檔案總管中顯示檔案", IsChecked = AppSettings.RevealAfterSave, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _confirm = new();
    private readonly ComboBox _historyLimit = new();

    public SettingsWindow()
    {
        Title = "ScreenCap 設定";
        Width = 760;
        Height = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);
        _folder.Text = AppSettings.SaveFolder;
        foreach (var (mode, name) in new[] { (AppTheme.System, "跟隨 Windows 設定"), (AppTheme.Light, "淺色"), (AppTheme.Dark, "深色") })
            _theme.Items.Add(new ComboBoxItem { Content = name, Tag = mode });
        _theme.SelectedIndex = (int)AppSettings.Theme;
        foreach (var action in AppSettings.ConfirmActions)
            _confirm.Items.Add(new ComboBoxItem { Content = ConfirmName(action), Tag = action });
        _confirm.SelectedIndex = Array.IndexOf(AppSettings.ConfirmActions, AppSettings.ConfirmAction);
        var limits = AppSettings.HistoryLimits.Union([AppSettings.HistoryLimit]).Order().ToList();
        foreach (int limit in limits)
            _historyLimit.Items.Add(new ComboBoxItem { Content = limit == 0 ? "不保留" : $"最近 {limit} 張", Tag = limit });
        _historyLimit.SelectedIndex = limits.IndexOf(AppSettings.HistoryLimit);

        var hotkeys = Page("熱鍵");
        hotkeys.Children.Add(Hint("點一下欄位後按下想要的按鍵組合；按 Backspace 或 Delete 停用該熱鍵。"));
        hotkeys.Children.Add(Row("截圖", _capture));
        hotkeys.Children.Add(Row("錄製螢幕", _record));
        hotkeys.Children.Add(Row("剪貼簿貼為貼圖", _pin));
        hotkeys.Children.Add(Row("再次開啟上一張截圖", _reopen));
        hotkeys.Children.Add(Row("截圖歷史庫", _history));
        hotkeys.Children.Add(Row("截圖辨識文字", _ocr));
        hotkeys.Children.Add(Row("截圖辨識 QR Code", _barcode));

        var saving = Page("存檔");
        saving.Children.Add(Hint("另存新檔的預設位置；截圖時按 Ctrl+Shift+S 會直接存成 PNG 到這裡。"));
        var browse = new Button { Content = "瀏覽…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => BrowseFolder();
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        saving.Children.Add(folderRow);
        saving.Children.Add(Row("檔名格式", _pattern));
        saving.Children.Add(Hint("{date} 日期、{time} 時間、{title} 截圖的視窗標題、{n} 流水號（001、002…），也可以用 {yyyy-MM-dd} 這類日期格式。"));
        saving.Children.Add(_patternPreview);
        _pattern.TextChanged += (_, _) => UpdatePatternPreview();
        UpdatePatternPreview();
        saving.Children.Add(_copyPath);
        saving.Children.Add(_reveal);

        var capture = Page("截圖");
        capture.Children.Add(Row("Enter / 雙擊選區", _confirm));
        capture.Children.Add(_cursor);
        capture.Children.Add(Heading("截圖歷史庫"));
        capture.Children.Add(Row("保留", _historyLimit));
        capture.Children.Add(Hint("複製、存檔或貼到桌面的截圖會自動保留一份，可從系統匣選單的「截圖歷史庫」找回。超過張數時刪除最舊的；改成「不保留」會刪除已保留的截圖。"));

        var recording = Page("錄影");
        recording.Children.Add(Hint("錄影的格式與聲音在框選錄影範圍時的工具列上選擇。"));
        recording.Children.Add(_clicks);
        recording.Children.Add(_keys);
        recording.Children.Add(_trim);

        var general = Page("一般");
        general.Children.Add(Row("外觀", _theme));
        general.Children.Add(_startup);
        general.Children.Add(_hardware);
        general.Children.Add(Hint("多螢幕或高解析度下截圖畫面不夠順時再開啟；會多用約 200 MB 記憶體。"));
        general.Children.Add(_autoUpdate);
        general.Children.Add(Hint($"目前版本：{Update.UpdateService.CurrentVersion}。也可以從系統匣選單的「檢查更新…」立即檢查。"));
        general.Children.Add(Heading("錯誤紀錄"));
        general.Children.Add(Hint("ScreenCap 發生錯誤或意外關閉時，會在這個資料夾留下紀錄；回報問題時附上紀錄有助於找出原因。紀錄只存在這台電腦上，不會自動上傳。"));
        var logs = new Button { Content = "開啟紀錄資料夾", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        logs.Click += (_, _) => CrashLog.OpenFolder();
        general.Children.Add(logs);

        var nav = new StackPanel { Width = 168, Margin = new Thickness(12, 16, 0, 16) };
        foreach (var (glyph, page) in new[] { ("\uE765", hotkeys), ("\uE74E", saving), ("\uE722", capture), ("\uE714", recording), ("\uE713", general) })
        {
            var item = NavItem(glyph, (string)page.Tag, page);
            _nav.Add((item, page));
            nav.Children.Add(item);
        }

        var reset = new Button { Content = "還原預設" };
        reset.Click += (_, _) => ResetDefaults();
        var ok = new Button { Content = "確定", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) }.Themed(StyleProperty, "AccentButton");
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new DockPanel { Margin = new Thickness(24, 14, 24, 18), LastChildFill = false };
        buttons.Children.Add(reset);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        // The buttons stay at the bottom; only a page that doesn't fit scrolls.
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons }
            .Themed(Border.BorderBrushProperty, "Brush.Divider");

        var pageHost = new ScrollViewer
        {
            Content = _page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(24, 20, 24, 16),
            Focusable = false,
        };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(nav, Dock.Left);
        root.Children.Add(footer);
        root.Children.Add(nav);
        root.Children.Add(pageHost);
        Content = root;
        ShowPage(hotkeys);
    }

    private readonly ContentControl _page = new() { Focusable = false };
    private readonly List<(Border Item, StackPanel Page)> _nav = new();

    /// <summary>A settings page starting with its title; the title is also its name in the side list.</summary>
    private static StackPanel Page(string title)
    {
        var page = new StackPanel { Tag = title };
        var heading = Heading(title);
        heading.FontSize = 20;
        heading.Margin = new Thickness(0, 0, 0, 10);
        page.Children.Add(heading);
        return page;
    }

    private Border NavItem(string glyph, string text, StackPanel page)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        label.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var item = new Border
        {
            Child = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        item.MouseLeftButtonUp += (_, _) => ShowPage(page);
        item.MouseEnter += (_, _) => StyleNav();
        item.MouseLeave += (_, _) => StyleNav();
        return item;
    }

    private void ShowPage(StackPanel page)
    {
        _page.Content = page;
        StyleNav();
    }

    /// <summary>Highlights the current page in the side list, and the one under the mouse.</summary>
    private void StyleNav()
    {
        foreach (var (item, page) in _nav)
        {
            bool current = _page.Content == page;
            if (current)
                item.Themed(Border.BackgroundProperty, "Brush.AccentSoft");
            else if (item.IsMouseOver)
                item.Themed(Border.BackgroundProperty, "Brush.Hover");
            else
                item.Background = Brushes.Transparent;
            var text = (TextBlock)((StackPanel)item.Child).Children[1];
            text.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 14, 0, 4),
    };

    private static TextBlock Hint(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    }.Themed(TextBlock.ForegroundProperty, "Brush.Muted");

    private static Grid Row(string label, Control editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(editor, 1);
        grid.Children.Add(text);
        grid.Children.Add(editor);
        return grid;
    }

    private static string ConfirmName(Capture.OverlayAction action) => action switch
    {
        Capture.OverlayAction.QuickSave => "快速存檔",
        Capture.OverlayAction.Save => "另存新檔",
        Capture.OverlayAction.Pin => "貼到桌面",
        _ => "複製到剪貼簿",
    };

    private string Pattern => string.IsNullOrWhiteSpace(_pattern.Text) ? FileNameTemplate.Default : _pattern.Text.Trim();

    private void UpdatePatternPreview() =>
        _patternPreview.Text = $"範例：{FileNameTemplate.Expand(Pattern, DateTime.Now, "Google Chrome")}.png";

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "選擇存檔資料夾", InitialDirectory = _folder.Text };
        if (dialog.ShowDialog(this) == true)
            _folder.Text = dialog.FolderName;
    }

    private void ResetDefaults()
    {
        _capture.Hotkey = AppSettings.DefaultCaptureHotkey;
        _record.Hotkey = AppSettings.DefaultRecordHotkey;
        _pin.Hotkey = AppSettings.DefaultPinHotkey;
        _reopen.Hotkey = AppSettings.DefaultReopenHotkey;
        _history.Hotkey = AppSettings.DefaultHistoryHotkey;
        _ocr.Hotkey = AppSettings.DefaultOcrHotkey;
        _barcode.Hotkey = AppSettings.DefaultBarcodeHotkey;
        _folder.Text = AppSettings.DefaultSaveFolder;
        _pattern.Text = FileNameTemplate.Default;
        _theme.SelectedIndex = (int)AppTheme.System;
        _cursor.IsChecked = false;
        _clicks.IsChecked = false;
        _keys.IsChecked = false;
        _trim.IsChecked = true;
        _autoUpdate.IsChecked = true;
        _hardware.IsChecked = false;
        _copyPath.IsChecked = false;
        _reveal.IsChecked = false;
        _confirm.SelectedIndex = 0;
        _historyLimit.SelectedItem = _historyLimit.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == AppSettings.DefaultHistoryLimit);
    }

    private void Apply()
    {
        var keys = new[] { _capture.Hotkey, _record.Hotkey, _pin.Hotkey, _reopen.Hotkey, _history.Hotkey, _ocr.Hotkey, _barcode.Hotkey }.Where(k => !k.IsNone).ToList();
        if (keys.Distinct().Count() != keys.Count)
        {
            ShowPage(_nav[0].Page);
            MessageBox.Show(this, "有兩個功能設定了相同的熱鍵，請改用不同的組合。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        int historyLimit = (int)((ComboBoxItem)_historyLimit.SelectedItem).Tag;
        int kept = ScreenshotHistory.Entries.Count;
        if (historyLimit < kept
            && MessageBox.Show(this, historyLimit == 0
                    ? $"不保留截圖歷史會刪除歷史庫中的 {kept} 張截圖。要繼續嗎？"
                    : $"歷史庫中有 {kept} 張截圖，改成保留 {historyLimit} 張會刪除最舊的 {kept - historyLimit} 張。要繼續嗎？",
                "ScreenCap", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;

        AppSettings.CaptureHotkey = _capture.Hotkey;
        AppSettings.RecordHotkey = _record.Hotkey;
        AppSettings.PinHotkey = _pin.Hotkey;
        AppSettings.ReopenHotkey = _reopen.Hotkey;
        AppSettings.HistoryHotkey = _history.Hotkey;
        AppSettings.OcrHotkey = _ocr.Hotkey;
        AppSettings.BarcodeHotkey = _barcode.Hotkey;
        AppSettings.SaveFolder = _folder.Text;
        AppSettings.FileNamePattern = Pattern;
        AppSettings.CaptureCursor = _cursor.IsChecked == true;
        AppSettings.HighlightClicks = _clicks.IsChecked == true;
        AppSettings.ShowKeystrokes = _keys.IsChecked == true;
        AppSettings.TrimAfterRecording = _trim.IsChecked == true;
        AppSettings.AutoCheckUpdates = _autoUpdate.IsChecked == true;
        AppSettings.HardwareRendering = _hardware.IsChecked == true;
        AppSettings.CopyPathAfterSave = _copyPath.IsChecked == true;
        AppSettings.RevealAfterSave = _reveal.IsChecked == true;
        AppSettings.ConfirmAction = (Capture.OverlayAction)((ComboBoxItem)_confirm.SelectedItem).Tag;
        AppSettings.HistoryLimit = historyLimit;
        ScreenshotHistory.Trim();
        var theme = (AppTheme)((ComboBoxItem)_theme.SelectedItem).Tag;
        if (theme != AppSettings.Theme)
        {
            AppSettings.Theme = theme;
            ThemeManager.Apply();
        }
        if (_startup.IsChecked != StartupService.IsEnabled)
            StartupService.SetEnabled(_startup.IsChecked == true);
        DialogResult = true;
    }

    /// <summary>A read-only box that records the next key combination pressed in it.</summary>
    private sealed class HotkeyBox : TextBox
    {
        private Hotkey _hotkey;

        public HotkeyBox(Hotkey hotkey)
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            Cursor = Cursors.Hand;
            Hotkey = hotkey;
            // Implicit styles only match the exact type, so pick up the themed TextBox style explicitly.
            SetResourceReference(StyleProperty, typeof(TextBox));
        }

        public Hotkey Hotkey
        {
            get => _hotkey;
            set
            {
                _hotkey = value;
                Text = value.ToString();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            if (key is Key.Tab or Key.Escape or Key.Enter && modifiers == ModifierKeys.None)
                return; // keep dialog navigation working
            e.Handled = true;

            if (key is Key.Back or Key.Delete && modifiers == ModifierKeys.None)
            {
                Hotkey = default;
                return;
            }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            {
                // Preview the modifiers held so far, e.g. "Ctrl+Shift+…".
                Text = modifiers == ModifierKeys.None ? _hotkey.ToString() : new Hotkey(modifiers, Key.A).ToString()[..^1] + "…";
                return;
            }
            Hotkey = new Hotkey(modifiers, key);
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            // Releasing modifiers without a key restores the current value.
            Text = _hotkey.ToString();
        }
    }
}
