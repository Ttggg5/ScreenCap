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
    private readonly TextBox _folder = new() { IsReadOnly = true, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBox _pattern = new() { Text = AppSettings.FileNamePattern, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _patternPreview = Hint("");
    private readonly CheckBox _startup = new() { Content = "登入 Windows 時自動啟動 ScreenCap", IsChecked = StartupService.IsEnabled, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _theme = new();
    private readonly CheckBox _cursor = new() { Content = "截圖時包含滑鼠游標", IsChecked = AppSettings.CaptureCursor, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _clicks = new() { Content = "錄影時標示滑鼠點擊", IsChecked = AppSettings.HighlightClicks, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _keys = new() { Content = "錄影時顯示按下的按鍵（例如 Ctrl + C）", IsChecked = AppSettings.ShowKeystrokes, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _autoUpdate = new() { Content = "自動檢查更新（每天一次）", IsChecked = AppSettings.AutoCheckUpdates, Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _copyPath = new() { Content = "存檔後複製檔案路徑", IsChecked = AppSettings.CopyPathAfterSave, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _reveal = new() { Content = "存檔後在檔案總管中顯示檔案", IsChecked = AppSettings.RevealAfterSave, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _confirm = new();

    public SettingsWindow()
    {
        Title = "ScreenCap 設定";
        Width = 520;
        SizeToContent = SizeToContent.Height;
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

        var body = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };

        body.Children.Add(Heading("熱鍵"));
        body.Children.Add(Hint("點一下欄位後按下想要的按鍵組合；按 Backspace 或 Delete 停用該熱鍵。"));
        body.Children.Add(Row("截圖", _capture));
        body.Children.Add(Row("錄製螢幕", _record));
        body.Children.Add(Row("剪貼簿貼為貼圖", _pin));
        body.Children.Add(Row("再次開啟上一張截圖", _reopen));

        body.Children.Add(Heading("存檔"));
        body.Children.Add(Hint("另存新檔的預設位置；截圖時按 Ctrl+Shift+S 會直接存成 PNG 到這裡。"));
        var browse = new Button { Content = "瀏覽…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => BrowseFolder();
        var folderRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        body.Children.Add(folderRow);
        body.Children.Add(Row("檔名格式", _pattern));
        body.Children.Add(Hint("{date} 日期、{time} 時間、{title} 截圖的視窗標題、{n} 流水號（001、002…），也可以用 {yyyy-MM-dd} 這類日期格式。"));
        body.Children.Add(_patternPreview);
        _pattern.TextChanged += (_, _) => UpdatePatternPreview();
        UpdatePatternPreview();
        body.Children.Add(_copyPath);
        body.Children.Add(_reveal);

        body.Children.Add(Heading("截圖"));
        body.Children.Add(Row("Enter / 雙擊選區", _confirm));

        body.Children.Add(Heading("一般"));
        body.Children.Add(Row("外觀", _theme));
        body.Children.Add(_startup);
        body.Children.Add(_cursor);
        body.Children.Add(_clicks);
        body.Children.Add(_keys);
        body.Children.Add(_autoUpdate);
        body.Children.Add(Hint($"目前版本：{Update.UpdateService.CurrentVersion}。也可以從系統匣選單的「檢查更新…」立即檢查。"));

        var reset = new Button { Content = "還原預設" };
        reset.Click += (_, _) => ResetDefaults();
        var ok = new Button { Content = "確定", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) }.Themed(StyleProperty, "AccentButton");
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new DockPanel { Margin = new Thickness(0, 24, 0, 0), LastChildFill = false };
        buttons.Children.Add(reset);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        body.Children.Add(buttons);

        Content = body;
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
        _folder.Text = AppSettings.DefaultSaveFolder;
        _pattern.Text = FileNameTemplate.Default;
        _theme.SelectedIndex = (int)AppTheme.System;
        _cursor.IsChecked = false;
        _clicks.IsChecked = false;
        _keys.IsChecked = false;
        _autoUpdate.IsChecked = true;
        _copyPath.IsChecked = false;
        _reveal.IsChecked = false;
        _confirm.SelectedIndex = 0;
    }

    private void Apply()
    {
        var keys = new[] { _capture.Hotkey, _record.Hotkey, _pin.Hotkey, _reopen.Hotkey }.Where(k => !k.IsNone).ToList();
        if (keys.Distinct().Count() != keys.Count)
        {
            MessageBox.Show(this, "有兩個功能設定了相同的熱鍵，請改用不同的組合。", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppSettings.CaptureHotkey = _capture.Hotkey;
        AppSettings.RecordHotkey = _record.Hotkey;
        AppSettings.PinHotkey = _pin.Hotkey;
        AppSettings.ReopenHotkey = _reopen.Hotkey;
        AppSettings.SaveFolder = _folder.Text;
        AppSettings.FileNamePattern = Pattern;
        AppSettings.CaptureCursor = _cursor.IsChecked == true;
        AppSettings.HighlightClicks = _clicks.IsChecked == true;
        AppSettings.ShowKeystrokes = _keys.IsChecked == true;
        AppSettings.AutoCheckUpdates = _autoUpdate.IsChecked == true;
        AppSettings.CopyPathAfterSave = _copyPath.IsChecked == true;
        AppSettings.RevealAfterSave = _reveal.IsChecked == true;
        AppSettings.ConfirmAction = (Capture.OverlayAction)((ComboBoxItem)_confirm.SelectedItem).Tag;
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
