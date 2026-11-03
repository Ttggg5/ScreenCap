using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenCap.Barcode;
using ScreenCap.Ocr;
using ScreenCap.Pin;
using ScreenCap.Services;
using ScreenCap.Theme;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.History;

/// <summary>Browses the recent screenshots as thumbnails: pin, copy, save, recognize or delete them.</summary>
internal sealed class HistoryWindow : Window
{
    private const int ThumbWidth = 220, ThumbHeight = 132;

    private static HistoryWindow? _instance;

    private readonly WrapPanel _tiles = new();
    private readonly ScrollViewer _scroller;
    private readonly TextBox _search = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
    private readonly Button _clear = new() { Content = "全部清除…", Margin = new Thickness(8, 0, 0, 0) };
    /// <summary>Thumbnails by file name, kept across refreshes.</summary>
    private readonly Dictionary<string, BitmapSource> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(HistoryEntry Entry, Border Tile)> _shown = new();
    private HistoryEntry? _selected;

    private HistoryWindow()
    {
        Title = "ScreenCap 截圖歷史庫";
        Width = 1000;
        Height = 680;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; // above pins
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);
        _status.Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        _empty.Themed(TextBlock.ForegroundProperty, "Brush.Muted");

        var searchLabel = new TextBlock { Text = "搜尋標題或截圖中的文字", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        _search.TextChanged += (_, _) => Refresh();
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(searchLabel);
        top.Children.Add(_search);

        _scroller = new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6), Focusable = false };
        var frame = new Grid();
        frame.Children.Add(_scroller);
        frame.Children.Add(_empty);
        var border = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(1), Child = frame }
            .Themed(Border.BorderBrushProperty, "Brush.Border")
            .Themed(BackgroundProperty, "Brush.Surface");

        var folder = new Button { Content = "開啟資料夾" };
        folder.Click += (_, _) => OpenFolder();
        _clear.Click += (_, _) => ClearAll();
        var close = new Button { Content = "關閉", MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(_clear, Dock.Right);
        DockPanel.SetDock(folder, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(_clear);
        bottom.Children.Add(folder);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(border);
        Content = root;

        ScreenshotHistory.Changed += OnHistoryChanged;
        Closed += (_, _) =>
        {
            MemoryRelease.Schedule(); // the thumbnails
            ScreenshotHistory.Changed -= OnHistoryChanged;
            _instance = null;
        };
        Refresh();
        _ = ScreenshotHistory.IndexTextAsync(); // catch up, e.g. after installing an OCR language
    }

    public static void ShowWindow()
    {
        if (_instance == null)
        {
            _instance = new HistoryWindow();
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized)
            _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private void OnHistoryChanged() => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var all = ScreenshotHistory.Entries;
        string query = _search.Text.Trim();
        var entries = query.Length == 0 ? all : all.Where(e => ScreenshotHistory.Matches(e, query)).ToList();

        // Forget thumbnails of deleted screenshots.
        var files = all.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _thumbnails.Keys.Where(f => !files.Contains(f)).ToList())
            _thumbnails.Remove(file);

        _tiles.Children.Clear();
        _shown.Clear();
        foreach (var entry in entries)
        {
            var tile = Tile(entry, query.Length > 0 ? ScreenshotHistory.Snippet(entry, query) : null);
            _shown.Add((entry, tile));
            _tiles.Children.Add(tile);
        }
        // Entries are replaced when their text is recognized, so they are matched by file.
        _selected = entries.FirstOrDefault(e => e.File == _selected?.File);
        UpdateSelection();

        _clear.IsEnabled = all.Count > 0;
        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = AppSettings.HistoryLimit <= 0 && all.Count == 0
            ? "截圖歷史已關閉。可在「設定」中選擇要保留幾張最近的截圖。"
            : all.Count == 0 ? "還沒有截圖。截圖完成後會自動保留一份在這裡。" : "沒有符合的截圖。";
        int pending = all.Count(e => e.Text == null);
        string indexing = ScreenshotHistory.IsIndexing && pending > 0 ? $"正在辨識截圖中的文字以供搜尋（還有 {pending} 張）。" : "";
        _status.Text = query.Length == 0
            ? $"共 {all.Count} 張截圖（最多保留 {AppSettings.HistoryLimit} 張）。{indexing}雙擊可貼到桌面，右鍵有更多動作。"
            : $"找到 {entries.Count} 張，共 {all.Count} 張截圖。{indexing}";
    }

    /// <param name="snippet">Matching part of the recognized text, shown under the caption.</param>
    private Border Tile(HistoryEntry entry, string? snippet)
    {
        var image = new Image { Stretch = Stretch.Uniform, Width = ThumbWidth, Height = ThumbHeight };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        if (_thumbnails.TryGetValue(entry.File, out var cached))
            image.Source = cached;
        else
            LoadThumbnail(entry, image);

        var caption = new TextBlock
        {
            Text = entry.Title ?? "截圖",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
        };
        string size = entry.Width > 0 ? $" · {entry.Width} × {entry.Height}" : "";
        var details = new TextBlock { Text = $"{entry.Time:yyyy/MM/dd HH:mm:ss}{size}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }
            .Themed(TextBlock.ForegroundProperty, "Brush.Muted");

        var content = new StackPanel { Width = ThumbWidth };
        content.Children.Add(image);
        content.Children.Add(caption);
        content.Children.Add(details);
        if (snippet != null)
        {
            content.Children.Add(new TextBlock { Text = snippet, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, Margin = new Thickness(0, 4, 0, 0) }
                .Themed(TextBlock.ForegroundProperty, "Brush.AccentStrong"));
        }

        var tile = new Border
        {
            Child = content,
            Padding = new Thickness(8),
            Margin = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            ToolTip = entry.Title,
            ContextMenu = Menu(entry),
        }.Themed(BackgroundProperty, "Brush.Card");
        tile.MouseLeftButtonDown += (_, e) =>
        {
            Select(entry);
            if (e.ClickCount == 2)
                Pin(entry);
        };
        tile.MouseRightButtonDown += (_, _) => Select(entry);
        return tile;
    }

    private async void LoadThumbnail(HistoryEntry entry, Image target)
    {
        try
        {
            var thumbnail = await Task.Run(() => ScreenshotHistory.LoadThumbnail(entry, ThumbWidth, ThumbHeight));
            _thumbnails[entry.File] = thumbnail;
            target.Source = thumbnail;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // Deleted or damaged meanwhile: the tile stays blank.
        }
    }

    private ContextMenu Menu(HistoryEntry entry)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("貼到桌面", "Enter", () => Pin(entry)));
        menu.Items.Add(MenuItem("複製", "Ctrl+C", () => Copy(entry)));
        menu.Items.Add(MenuItem("另存新檔…", "Ctrl+S", () => Save(entry)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("文字辨識", "O", () => WithImage(entry, OcrResultWindow.ShowFor)));
        menu.Items.Add(MenuItem("辨識 QR Code / 條碼", "Q", () => WithImage(entry, BarcodeResultWindow.ShowFor)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("在檔案總管中顯示", "", () => Reveal(entry)));
        menu.Items.Add(MenuItem("刪除", "Delete", () => ScreenshotHistory.Delete(entry)));
        return menu;
    }

    private static MenuItem MenuItem(string header, string gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    private void Select(HistoryEntry entry)
    {
        _selected = entry;
        UpdateSelection();
        Keyboard.Focus(this); // out of the search box, so the keys act on the selection
    }

    private void UpdateSelection()
    {
        foreach (var (entry, tile) in _shown)
        {
            if (entry.File == _selected?.File)
                tile.Themed(Border.BorderBrushProperty, "Brush.Accent");
            else
                tile.BorderBrush = Brushes.Transparent;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_search.IsKeyboardFocusWithin && e.Key is not (Key.Down or Key.Enter))
            return; // typing in the search box
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        bool none = Keyboard.Modifiers == ModifierKeys.None;
        switch (e.Key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down when none:
                Move(e.Key);
                break;
            case Key.Home when none && _shown.Count > 0:
                SelectAndShow(0);
                break;
            case Key.End when none && _shown.Count > 0:
                SelectAndShow(_shown.Count - 1);
                break;
            case Key.Enter when none && _selected != null:
                Pin(_selected);
                break;
            case Key.C when ctrl && _selected != null:
                Copy(_selected);
                break;
            case Key.S when ctrl && _selected != null:
                Save(_selected);
                break;
            case Key.F when ctrl:
                _search.Focus();
                _search.SelectAll();
                break;
            case Key.O when none && _selected != null:
                WithImage(_selected, OcrResultWindow.ShowFor);
                break;
            case Key.Q when none && _selected != null:
                WithImage(_selected, BarcodeResultWindow.ShowFor);
                break;
            case Key.Delete when none && _selected != null:
                DeleteSelected();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Arrow keys move the selection; up and down by a row of the wrapped tiles.</summary>
    private void Move(Key key)
    {
        if (_shown.Count == 0)
            return;
        int index = _shown.FindIndex(s => s.Entry.File == _selected?.File);
        if (index < 0)
        {
            SelectAndShow(0);
            return;
        }
        double tileWidth = _shown[0].Tile.ActualWidth + _shown[0].Tile.Margin.Left + _shown[0].Tile.Margin.Right;
        int perRow = tileWidth > 0 ? Math.Max(1, (int)(_tiles.ActualWidth / tileWidth)) : 1;
        int step = key switch { Key.Left => -1, Key.Right => 1, Key.Up => -perRow, _ => perRow };
        SelectAndShow(Math.Clamp(index + step, 0, _shown.Count - 1));
    }

    private void SelectAndShow(int index)
    {
        Select(_shown[index].Entry);
        _shown[index].Tile.BringIntoView();
    }

    /// <summary>Deletes the selected screenshot and selects the next one, so several can be deleted in a row.</summary>
    private void DeleteSelected()
    {
        int index = _shown.FindIndex(s => s.Entry.File == _selected?.File);
        var next = index + 1 < _shown.Count ? _shown[index + 1].Entry : index > 0 ? _shown[index - 1].Entry : null;
        ScreenshotHistory.Delete(_selected!);
        _selected = next;
        Refresh();
    }

    /// <summary>Loads the full image for an action; reports it if the file is gone.</summary>
    private void WithImage(HistoryEntry entry, Action<BitmapSource> action)
    {
        BitmapSource image;
        try
        {
            image = ScreenshotHistory.LoadImage(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _status.Text = $"無法讀取這張截圖：{ex.Message}";
            return;
        }
        action(image);
    }

    private void Pin(HistoryEntry entry) => WithImage(entry, image =>
    {
        GetCursorPos(out var p);
        PinWindow.ShowAt(image, p.X, p.Y, entry.Title);
    });

    private void Copy(HistoryEntry entry) => WithImage(entry, image =>
    {
        ClipboardService.SetImage(image);
        _status.Text = "已複製到剪貼簿";
    });

    private void Save(HistoryEntry entry) => WithImage(entry, image => ImageExportService.Save(image, this, entry.Title));

    private static void Reveal(HistoryEntry entry) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ScreenshotHistory.PathOf(entry)}\"") { UseShellExecute = false });

    private static void OpenFolder()
    {
        Directory.CreateDirectory(ScreenshotHistory.Folder);
        Process.Start(new ProcessStartInfo(ScreenshotHistory.Folder) { UseShellExecute = true });
    }

    private void ClearAll()
    {
        int count = ScreenshotHistory.Entries.Count;
        if (Dialog.Show(this, $"要刪除歷史庫中全部 {count} 張截圖嗎？已另存的檔案不受影響。",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK)
            ScreenshotHistory.Clear();
    }
}
