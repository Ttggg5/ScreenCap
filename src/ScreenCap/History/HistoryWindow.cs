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
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
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

        _scroller = new ScrollViewer { Content = _tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6), Focusable = false };
        var frame = new Grid();
        frame.Children.Add(_scroller);
        frame.Children.Add(_empty);
        var border = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, BorderThickness = new Thickness(1), Child = frame }
            .Themed(Border.BorderBrushProperty, "Brush.Border")
            .Themed(BackgroundProperty, "Brush.Surface");

        var folder = new Button { Content = "開啟資料夾" };
        folder.Click += (_, _) => OpenFolder();
        var close = new Button { Content = "關閉", MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(folder, Dock.Right);
        bottom.Children.Add(close);
        bottom.Children.Add(folder);
        bottom.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(bottom, Dock.Bottom);
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
        var entries = ScreenshotHistory.Entries;

        // Forget thumbnails of deleted screenshots.
        var files = entries.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in _thumbnails.Keys.Where(f => !files.Contains(f)).ToList())
            _thumbnails.Remove(file);

        _tiles.Children.Clear();
        _shown.Clear();
        foreach (var entry in entries)
        {
            var tile = Tile(entry);
            _shown.Add((entry, tile));
            _tiles.Children.Add(tile);
        }
        _selected = entries.FirstOrDefault(e => e.File == _selected?.File);
        UpdateSelection();

        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = AppSettings.HistoryLimit <= 0
            ? "截圖歷史已關閉。可在「設定」中選擇要保留幾張最近的截圖。"
            : "還沒有截圖。截圖完成後會自動保留一份在這裡。";
        _status.Text = $"共 {entries.Count} 張截圖（最多保留 {AppSettings.HistoryLimit} 張）。雙擊可貼到桌面，右鍵有更多動作。";
    }

    private Border Tile(HistoryEntry entry)
    {
        var image = new Image { Stretch = Stretch.Uniform, Width = ThumbWidth, Height = ThumbHeight };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        if (_thumbnails.TryGetValue(entry.File, out var cached))
            image.Source = cached;
        else
            LoadThumbnail(entry, image);

        var caption = new TextBlock
        {
            Text = $"{entry.Time:yyyy/MM/dd HH:mm:ss}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 13,
        };

        var content = new StackPanel { Width = ThumbWidth };
        content.Children.Add(image);
        content.Children.Add(caption);
        if (entry.Width > 0)
        {
            content.Children.Add(new TextBlock { Text = $"{entry.Width} × {entry.Height}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }
                .Themed(TextBlock.ForegroundProperty, "Brush.Muted"));
        }

        var tile = new Border
        {
            Child = content,
            Padding = new Thickness(8),
            Margin = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
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
        Keyboard.Focus(this); // so the keys act on the selection
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
        PinWindow.ShowAt(image, p.X, p.Y);
    });

    private void Copy(HistoryEntry entry) => WithImage(entry, image =>
    {
        ClipboardService.SetImage(image);
        _status.Text = "已複製到剪貼簿";
    });

    private void Save(HistoryEntry entry) => WithImage(entry, image => ImageExportService.Save(image, this));

    private static void Reveal(HistoryEntry entry) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ScreenshotHistory.PathOf(entry)}\"") { UseShellExecute = false });

    private static void OpenFolder()
    {
        Directory.CreateDirectory(ScreenshotHistory.Folder);
        Process.Start(new ProcessStartInfo(ScreenshotHistory.Folder) { UseShellExecute = true });
    }
}
