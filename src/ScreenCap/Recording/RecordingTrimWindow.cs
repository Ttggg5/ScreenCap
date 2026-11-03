using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenCap.Services;
using ScreenCap.Theme;

namespace ScreenCap.Recording;

/// <summary>
/// Shown after a recording stops: drag the ends of the timeline to cut off the start and end
/// (stopping the recording, or the first seconds, are often not wanted), then save.
/// </summary>
internal sealed class RecordingTrimWindow : Window
{
    private readonly string _path;
    private readonly RecordingFormat _format;
    private readonly int _mp4FramesPerSecond;
    private readonly TaskCompletionSource<string?> _result = new();
    private readonly CancellationTokenSource _loading = new();

    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _placeholder;
    private readonly TrimBar _bar = new() { Height = 40, IsEnabled = false };
    private readonly TextBlock _info;
    private readonly Button _save, _discard, _play;
    private readonly CheckBox _showNextTime;

    private RecordingTimeline? _timeline;
    /// <summary>Frame boundaries: every frame's start, then the end of the last one.</summary>
    private List<TimeSpan> _bounds = [];
    private int _start, _end; // indices into _bounds
    private bool _editingEnd;
    private bool _finished;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private DateTime _playStarted;
    private TimeSpan _playFrom;

    private RecordingTrimWindow(string path, RecordingFormat format, int mp4FramesPerSecond)
    {
        _path = path;
        _format = format;
        _mp4FramesPerSecond = mp4FramesPerSecond;
        Title = $"ScreenCap 裁剪錄影（{RecordingSession.DisplayName(format)}）";
        Width = 760;
        Height = 620;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);

        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
        _placeholder = new TextBlock { Text = "正在讀取錄影…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "Brush.Muted");
        var previewGrid = new Grid { Margin = new Thickness(8) };
        previewGrid.Children.Add(_preview);
        previewGrid.Children.Add(_placeholder);
        var frame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = previewGrid, BorderThickness = new Thickness(1) }
            .Themed(Border.BorderBrushProperty, "Brush.Border")
            .Themed(BackgroundProperty, "Brush.Card");

        _bar.Changed += OnBarChanged;
        _play = new Button { Content = "▶", Width = 40, Margin = new Thickness(0, 0, 10, 0), IsEnabled = false, ToolTip = "播放保留的部分 (空白鍵)" };
        _play.Click += (_, _) => TogglePlay();
        var timeline = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_play, Dock.Left);
        timeline.Children.Add(_play);
        timeline.Children.Add(_bar);

        _info = new TextBlock { Margin = new Thickness(0, 8, 0, 0), FontFamily = new FontFamily("Consolas, Microsoft JhengHei UI") }
            .Themed(TextBlock.ForegroundProperty, "Brush.Text");
        var hint = new TextBlock
        {
            Text = "拖曳時間軸兩端選擇要保留的部分；← / → 逐格微調目前的那一端，Home / End 跳到最前或最後。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        }.Themed(TextBlock.ForegroundProperty, "Brush.Muted");

        _showNextTime = new CheckBox { Content = "錄完後顯示這個視窗", IsChecked = AppSettings.TrimAfterRecording, VerticalAlignment = VerticalAlignment.Center };
        _showNextTime.Click += (_, _) => AppSettings.TrimAfterRecording = _showNextTime.IsChecked == true;
        _save = new Button { Content = "儲存…", MinWidth = 90, IsDefault = true, ToolTip = "Enter" }.Themed(StyleProperty, "AccentButton");
        _save.Click += (_, _) => Save();
        _discard = new Button { Content = "捨棄", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        _discard.Click += (_, _) => Discard();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_save);
        buttons.Children.Add(_discard);
        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_showNextTime);

        var details = new StackPanel();
        details.Children.Add(timeline);
        details.Children.Add(_info);
        details.Children.Add(hint);
        details.Children.Add(bottom);
        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(details, Dock.Bottom);
        root.Children.Add(details);
        root.Children.Add(frame);
        Content = root;

        _timer.Tick += (_, _) => PlayTick();
        PreviewKeyDown += OnKey;
        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, e) =>
        {
            if (!_finished && !ConfirmDiscard())
                e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _loading.Cancel();
            _result.TrySetResult(null);
        };
    }

    /// <summary>Lets the user trim a finished recording.</summary>
    /// <returns>The file to save: <paramref name="path"/> itself, or a trimmed copy next to it; null to discard the recording.</returns>
    public static Task<string?> ShowAsync(string path, RecordingFormat format, int mp4FramesPerSecond)
    {
        var window = new RecordingTrimWindow(path, format, mp4FramesPerSecond);
        window.Show();
        window.Activate();
        return window._result.Task;
    }

    private async Task LoadAsync()
    {
        try
        {
            var token = _loading.Token;
            _timeline = await Task.Run(() => RecordingTrimmer.Load(_path, _format, token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _placeholder.Text = $"無法讀取錄影，只能直接儲存：{ex.Message}";
            return;
        }

        _bounds = [.. _timeline.FrameTimes, _timeline.Duration];
        _start = 0;
        _end = _bounds.Count - 1;
        _placeholder.Visibility = Visibility.Collapsed;
        _bar.Duration = _timeline.Duration;
        _bar.IsEnabled = _play.IsEnabled = _timeline.FrameTimes.Count > 1;
        ShowRange();
        ShowFrameAt(TimeSpan.Zero);
    }

    private TimeSpan Start => _bounds[_start];
    private TimeSpan End => _bounds[_end];
    private bool IsTrimmed => _timeline != null && (_start > 0 || _end < _bounds.Count - 1);

    private void OnBarChanged(bool end, TimeSpan time)
    {
        StopPlaying();
        _editingEnd = end;
        int nearest = Nearest(time);
        if (end)
            _end = Math.Clamp(nearest, _start + 1, _bounds.Count - 1);
        else
            _start = Math.Clamp(nearest, 0, _end - 1);
        ShowRange();
        ShowEditedEnd();
    }

    private int Nearest(TimeSpan time)
    {
        int index = _bounds.BinarySearch(time);
        if (index >= 0)
            return index;
        index = ~index;
        if (index == 0)
            return 0;
        if (index >= _bounds.Count)
            return _bounds.Count - 1;
        return time - _bounds[index - 1] <= _bounds[index] - time ? index - 1 : index;
    }

    /// <summary>The first kept frame when moving the start, the last kept one when moving the end.</summary>
    private void ShowEditedEnd() => ShowFrameAt(_editingEnd ? _bounds[_end - 1] : Start);

    private void ShowFrameAt(TimeSpan time)
    {
        if (_timeline?.ThumbnailAt(time) is { } image)
            _preview.Source = image;
    }

    private void ShowRange()
    {
        _bar.Start = Start;
        _bar.End = End;
        _bar.EditingEnd = _editingEnd;
        string length = Format(End - Start);
        _info.Text = IsTrimmed
            ? $"保留 {Format(Start)} – {Format(End)}　長度 {length}（原本 {Format(_timeline!.Duration)}）"
            : $"長度 {length}";
    }

    private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_timeline == null || !_bar.IsEnabled || Keyboard.FocusedElement is CheckBox && e.Key == Key.Space)
            return;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                StopPlaying();
                int step = e.Key == Key.Left ? -1 : 1;
                if (_editingEnd)
                    _end = Math.Clamp(_end + step, _start + 1, _bounds.Count - 1);
                else
                    _start = Math.Clamp(_start + step, 0, _end - 1);
                break;
            case Key.Home or Key.End:
                StopPlaying();
                if (_editingEnd)
                    _end = e.Key == Key.Home ? _start + 1 : _bounds.Count - 1;
                else
                    _start = e.Key == Key.Home ? 0 : _end - 1;
                break;
            case Key.Space:
                TogglePlay();
                e.Handled = true;
                return;
            default:
                return;
        }
        e.Handled = true;
        ShowRange();
        ShowEditedEnd();
    }

    // ---------------------------------------------------------------- playing

    private void TogglePlay()
    {
        if (_timer.IsEnabled)
        {
            StopPlaying();
            return;
        }
        _playFrom = Start;
        _playStarted = DateTime.UtcNow;
        _play.Content = "❚❚";
        _timer.Start();
    }

    private void StopPlaying()
    {
        if (!_timer.IsEnabled)
            return;
        _timer.Stop();
        _play.Content = "▶";
        _bar.Playhead = null;
        ShowEditedEnd();
    }

    private void PlayTick()
    {
        var at = _playFrom + (DateTime.UtcNow - _playStarted);
        if (at >= End)
        {
            StopPlaying();
            return;
        }
        _bar.Playhead = at;
        ShowFrameAt(at);
    }

    // ---------------------------------------------------------------- result

    private async void Save()
    {
        StopPlaying();
        if (!IsTrimmed)
        {
            Finish(_path);
            return;
        }

        _save.IsEnabled = _discard.IsEnabled = _bar.IsEnabled = _play.IsEnabled = false;
        _info.Text = "正在裁剪…";
        string trimmed = Path.Combine(Path.GetDirectoryName(_path)!, Path.GetFileNameWithoutExtension(_path) + "_trimmed" + Path.GetExtension(_path));
        TimeSpan start = Start, end = End;
        try
        {
            await Task.Run(() => RecordingTrimmer.Trim(_path, trimmed, _format, start, end, _mp4FramesPerSecond));
            Finish(trimmed);
        }
        catch (Exception ex)
        {
            if (File.Exists(trimmed))
                File.Delete(trimmed);
            MessageBox.Show(this, $"裁剪失敗，將儲存完整的錄影：{ex.Message}", "ScreenCap", MessageBoxButton.OK, MessageBoxImage.Warning);
            Finish(_path);
        }
    }

    private void Discard()
    {
        if (ConfirmDiscard())
            Finish(null);
    }

    private bool ConfirmDiscard() =>
        MessageBox.Show(this, "要捨棄這段錄影嗎？捨棄後無法復原。", "ScreenCap", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void Finish(string? path)
    {
        _finished = true;
        _result.TrySetResult(path);
        Close();
    }
}

/// <summary>Timeline with two handles for the kept range; drag a handle, or click the track to move the nearer one.</summary>
internal sealed class TrimBar : FrameworkElement
{
    private const double HandleWidth = 10;
    private bool? _dragging; // true: the end handle

    public TimeSpan Duration { get; set; }
    public TimeSpan Start { get => _start; set { _start = value; InvalidateVisual(); } }
    public TimeSpan End { get => _end; set { _end = value; InvalidateVisual(); } }
    public bool EditingEnd { get => _editingEnd; set { _editingEnd = value; InvalidateVisual(); } }
    public TimeSpan? Playhead { get => _playhead; set { _playhead = value; InvalidateVisual(); } }
    private TimeSpan _start, _end;
    private TimeSpan? _playhead;
    private bool _editingEnd;

    /// <summary>A handle was moved: (true for the end handle, the time under the cursor).</summary>
    public event Action<bool, TimeSpan>? Changed;

    public TrimBar()
    {
        Cursor = Cursors.SizeWE;
        Focusable = false;
        IsEnabledChanged += (_, _) => InvalidateVisual();
    }

    private double TrackWidth => Math.Max(1, ActualWidth - 2 * HandleWidth);

    private double X(TimeSpan t) => HandleWidth + (Duration <= TimeSpan.Zero ? 0 : t / Duration * TrackWidth);

    private TimeSpan TimeAt(double x) => Duration * Math.Clamp((x - HandleWidth) / TrackWidth, 0, 1);

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, mid = h / 2;
        double x0 = X(Start), x1 = X(End);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, h)); // hit-testable everywhere
        dc.DrawRoundedRectangle(Res("Brush.Control"), new Pen(Res("Brush.Border"), 1), new Rect(HandleWidth, mid - 8, TrackWidth, 16), 4, 4);
        if (!IsEnabled)
            return;
        dc.DrawRectangle(Res("Brush.AccentSoft"), null, new Rect(x0, mid - 8, Math.Max(0, x1 - x0), 16));
        var accent = Res("Brush.Accent");
        foreach (var (x, isEnd) in new[] { (x0, false), (x1, true) })
        {
            var handle = new Rect(isEnd ? x : x - HandleWidth, 2, HandleWidth, h - 4);
            dc.DrawRoundedRectangle(accent, isEnd == EditingEnd ? new Pen(Res("Brush.Text"), 1.5) : null, handle, 3, 3);
        }
        if (Playhead is { } p)
            dc.DrawRectangle(Res("Brush.Text"), null, new Rect(X(p) - 1, 2, 2, h - 4));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled)
            return;
        double x = e.GetPosition(this).X;
        // The nearer handle; past the end handle always the end, before the start always the start.
        double d0 = Math.Abs(x - (X(Start) - HandleWidth / 2)), d1 = Math.Abs(x - (X(End) + HandleWidth / 2));
        _dragging = x > X(End) || (x >= X(Start) && d1 < d0);
        CaptureMouse();
        Changed?.Invoke(_dragging.Value, TimeAt(x));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging is { } end)
            Changed?.Invoke(end, TimeAt(e.GetPosition(this).X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = null;
        ReleaseMouseCapture();
    }
}
