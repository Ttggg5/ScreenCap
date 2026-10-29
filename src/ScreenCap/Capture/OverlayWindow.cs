using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ScreenCap.Annotations;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;
using Path = System.Windows.Shapes.Path;

namespace ScreenCap.Capture;

internal enum OverlayAction
{
    Cancel,
    Copy,
    Save,
    Pin,
}

internal sealed record OverlayResult(OverlayAction Action, BitmapSource? Image, int ScreenX, int ScreenY)
{
    public static readonly OverlayResult Cancelled = new(OverlayAction.Cancel, null, 0, 0);
}

/// <summary>
/// Full virtual-screen window showing the frozen screenshot. All layout inside <see cref="_root"/>
/// is in physical pixels; chrome elements (toolbar, magnifier, labels) are scaled back up to DIPs.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private enum State { Detecting, Pressing, Selecting, Selected, Moving, Resizing, Drawing }

    private const double HandleSize = 7;  // DIP
    private const double DragThreshold = 3; // physical px
    private static readonly Color Accent = Color.FromRgb(0x1A, 0x9F, 0xFF);

    private readonly ScreenSnapshot _snap;
    private readonly WindowDetector _detector;

    private readonly Canvas _root = new();
    private readonly ScaleTransform _rootScale = new(1, 1);
    private readonly ScaleTransform _chromeScale = new(1, 1);
    private readonly RectangleGeometry _hole = new(new Rect());
    private readonly RectangleGeometry _annotationClip = new(new Rect());
    private readonly Rectangle _border;
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly AnnotationLayer _annotationLayer;
    private readonly Border _sizeLabel;
    private readonly TextBlock _sizeText;
    private readonly MagnifierControl _magnifier = new();
    private readonly SelectionToolbar _toolbar = new();
    private readonly UndoStack _annotations = new();

    private double _scale = 1;
    private State _state = State.Detecting;
    private Rect _sel = Rect.Empty;
    private Point _press;
    private Rect _origSel;
    private int _handle = -1;
    private Point _cursor;

    private ToolKind _tool = ToolKind.None;
    private Color _color;
    private double _size;
    private Annotation? _drawing;
    private TextBox? _textBox;
    private Point _textPos;

    public OverlayWindow(ScreenSnapshot snap, WindowDetector detector)
    {
        _snap = snap;
        _detector = detector;
        _color = _toolbar.SelectedColor;
        _size = _toolbar.SelectedSize;

        Title = "ScreenCap";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Focusable = true;

        _root.Width = snap.Width;
        _root.Height = snap.Height;
        _root.HorizontalAlignment = HorizontalAlignment.Left;
        _root.VerticalAlignment = VerticalAlignment.Top;
        _root.LayoutTransform = _rootScale;

        var background = new Image { Source = snap.Bitmap, Width = snap.Width, Height = snap.Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.NearestNeighbor);
        _root.Children.Add(background);

        _annotationLayer = new AnnotationLayer(_annotations.Items)
        {
            Width = snap.Width,
            Height = snap.Height,
            IsHitTestVisible = false,
            Clip = _annotationClip,
        };
        _root.Children.Add(_annotationLayer);

        _root.Children.Add(new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, snap.Width, snap.Height)), _hole),
            IsHitTestVisible = false,
        });

        _border = new Rectangle { Stroke = new SolidColorBrush(Accent), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _root.Children.Add(_border);

        for (int i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Rectangle
            {
                Fill = new SolidColorBrush(Accent),
                Stroke = Brushes.White,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            _root.Children.Add(_handles[i]);
        }

        _sizeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Consolas, Segoe UI") };
        _sizeLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Child = _sizeText,
            IsHitTestVisible = false,
            RenderTransform = _chromeScale,
            Visibility = Visibility.Collapsed,
        };
        _root.Children.Add(_sizeLabel);

        _magnifier.RenderTransform = _chromeScale;
        _root.Children.Add(_magnifier);

        _toolbar.RenderTransform = _chromeScale;
        _toolbar.Visibility = Visibility.Collapsed;
        _root.Children.Add(_toolbar);
        WireToolbar();

        Content = _root;

        SourceInitialized += (_, _) => ApplyBounds();
        DpiChanged += (_, e) =>
        {
            SetScale(e.NewDpi.DpiScaleX);
            Dispatcher.BeginInvoke(ApplyBounds); // WPF resizes the window on DPI change; undo that
        };
        Loaded += (_, _) =>
        {
            Activate();
            Keyboard.Focus(this);
            GetCursorPos(out var p);
            _cursor = ClampPoint(new Point(p.X - _snap.Left, p.Y - _snap.Top));
            DetectAt(_cursor);
            Refresh();
        };
    }

    public OverlayResult Result { get; private set; } = OverlayResult.Cancelled;

    private bool HasSelection => !_sel.IsEmpty && _sel.Width >= 1 && _sel.Height >= 1;

    // ---------------------------------------------------------------- layout / DPI

    private void ApplyBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, _snap.Left, _snap.Top, _snap.Width, _snap.Height, 0);
        SetScale(GetDpiForWindow(hwnd) / 96.0);
    }

    private void SetScale(double scale)
    {
        if (scale <= 0)
            scale = 1;
        _scale = scale;
        _rootScale.ScaleX = _rootScale.ScaleY = 1 / scale;
        _chromeScale.ScaleX = _chromeScale.ScaleY = scale;
        _border.StrokeThickness = Math.Max(1, Math.Round(scale));
        foreach (var h in _handles)
            h.StrokeThickness = Math.Max(1, Math.Round(scale));
        Refresh();
    }

    private void WireToolbar()
    {
        _toolbar.ToolChanged += tool =>
        {
            CommitText();
            _tool = tool;
            Refresh();
        };
        _toolbar.ColorChanged += c => _color = c;
        _toolbar.ThicknessChanged += s => _size = s;
        _toolbar.UndoRequested += Undo;
        _toolbar.RedoRequested += Redo;
        _toolbar.PinRequested += () => Finish(OverlayAction.Pin);
        _toolbar.SaveRequested += () => Finish(OverlayAction.Save);
        _toolbar.CopyRequested += () => Finish(OverlayAction.Copy);
        _toolbar.CancelRequested += Close;
    }

    // ---------------------------------------------------------------- mouse

    private Point ClampPoint(Point p) => new(
        Math.Clamp(Math.Floor(p.X), 0, _snap.Width - 1),
        Math.Clamp(Math.Floor(p.Y), 0, _snap.Height - 1));

    private Point MousePoint(MouseEventArgs e) => ClampPoint(e.GetPosition(_root));

    private bool IsOverChrome() =>
        (_toolbar.IsVisible && _toolbar.IsMouseOver) || (_textBox != null && _textBox.IsMouseOver);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || IsOverChrome())
            return;

        var p = MousePoint(e);
        if (_textBox != null)
        {
            CommitText();
            return;
        }

        switch (_state)
        {
            case State.Detecting:
                _press = p;
                _state = State.Pressing;
                CaptureMouse();
                break;

            case State.Selected:
                bool inside = _sel.Contains(p);
                if (e.ClickCount == 2 && inside && _tool == ToolKind.None)
                {
                    Finish(OverlayAction.Copy);
                    return;
                }

                int handle = HitHandle(p);
                if (handle >= 0)
                {
                    _handle = handle;
                    _origSel = _sel;
                    _press = p;
                    _state = State.Resizing;
                    CaptureMouse();
                }
                else if (inside && _tool == ToolKind.None)
                {
                    _origSel = _sel;
                    _press = p;
                    _state = State.Moving;
                    CaptureMouse();
                }
                else if (inside && _tool == ToolKind.Text)
                {
                    BeginText(p);
                }
                else if (inside)
                {
                    _drawing = CreateAnnotation(p);
                    _annotationLayer.Current = _drawing;
                    _state = State.Drawing;
                    CaptureMouse();
                }
                break;
        }
        Refresh();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = MousePoint(e);
        _cursor = p;

        switch (_state)
        {
            case State.Detecting:
                DetectAt(p);
                break;

            case State.Pressing:
                if (Math.Abs(p.X - _press.X) > DragThreshold || Math.Abs(p.Y - _press.Y) > DragThreshold)
                {
                    _state = State.Selecting;
                    _sel = Span(_press, p);
                }
                break;

            case State.Selecting:
                _sel = Span(_press, p);
                break;

            case State.Moving:
                _sel = new Rect(
                    Math.Clamp(_origSel.X + p.X - _press.X, 0, _snap.Width - _origSel.Width),
                    Math.Clamp(_origSel.Y + p.Y - _press.Y, 0, _snap.Height - _origSel.Height),
                    _origSel.Width, _origSel.Height);
                break;

            case State.Resizing:
                _sel = ResizeFrom(_origSel, _handle, p.X - _press.X, p.Y - _press.Y);
                break;

            case State.Drawing:
                _drawing?.Update(p);
                _annotationLayer.InvalidateVisual();
                break;

            case State.Selected:
                UpdateSelectedCursor(p);
                break;
        }
        Refresh();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
            ReleaseMouseCapture();

        switch (_state)
        {
            case State.Pressing:
                // A click without dragging accepts the auto-detected window.
                _state = HasSelection ? State.Selected : State.Detecting;
                break;

            case State.Selecting:
                _state = _sel.Width >= 2 && _sel.Height >= 2 ? State.Selected : State.Detecting;
                if (_state == State.Detecting)
                    DetectAt(_cursor);
                break;

            case State.Moving:
            case State.Resizing:
                _state = State.Selected;
                break;

            case State.Drawing:
                if (_drawing is { IsEmpty: false })
                    _annotations.Add(_drawing);
                _drawing = null;
                _annotationLayer.Current = null;
                _annotationLayer.InvalidateVisual();
                _state = State.Selected;
                break;
        }
        Refresh();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        e.Handled = true;
        if (_textBox != null)
            CommitText();
        else if (_state == State.Selected)
            ResetSelection();
        else
            Close();
    }

    private void UpdateSelectedCursor(Point p)
    {
        Cursor = HitHandle(p) switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            3 or 7 => Cursors.SizeWE,
            _ when !_sel.Contains(p) => Cursors.Arrow,
            _ => _tool switch
            {
                ToolKind.None => Cursors.SizeAll,
                ToolKind.Text => Cursors.IBeam,
                _ => Cursors.Cross,
            },
        };
    }

    // ---------------------------------------------------------------- keyboard

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (_textBox != null)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && ctrl))
            {
                CommitText();
                e.Handled = true;
            }
            return; // let the TextBox handle typing
        }

        bool selected = _state == State.Selected;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape when selected:
                ResetSelection();
                break;
            case Key.Escape:
                Close();
                break;
            case Key.Enter when selected:
            case Key.C when ctrl && selected:
                Finish(OverlayAction.Copy);
                break;
            case Key.C when !ctrl && !selected:
                ClipboardService.SetText(_magnifier.HexColor);
                Close();
                break;
            case Key.S when ctrl && selected:
                Finish(OverlayAction.Save);
                break;
            case Key.T when ctrl && selected:
                Finish(OverlayAction.Pin);
                break;
            case Key.Z when ctrl:
                Undo();
                break;
            case Key.Y when ctrl:
                Redo();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
                int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
                if (selected)
                    Nudge(dx, dy, shift);
                else
                    SetCursorPos((int)_cursor.X + _snap.Left + dx, (int)_cursor.Y + _snap.Top + dy);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void Nudge(int dx, int dy, bool resize)
    {
        if (resize)
        {
            double w = Math.Clamp(_sel.Width + dx, 1, _snap.Width - _sel.X);
            double h = Math.Clamp(_sel.Height + dy, 1, _snap.Height - _sel.Y);
            _sel = new Rect(_sel.X, _sel.Y, w, h);
        }
        else
        {
            _sel = new Rect(
                Math.Clamp(_sel.X + dx, 0, _snap.Width - _sel.Width),
                Math.Clamp(_sel.Y + dy, 0, _snap.Height - _sel.Height),
                _sel.Width, _sel.Height);
        }
        Refresh();
    }

    // ---------------------------------------------------------------- selection

    private void DetectAt(Point p) => _sel = _detector.HitTest((int)p.X, (int)p.Y) ?? Rect.Empty;

    private static Rect Span(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);

    private Rect ResizeFrom(Rect r, int handle, double dx, double dy)
    {
        double l = r.Left, t = r.Top, right = r.Right, b = r.Bottom;
        if (handle is 0 or 6 or 7) l += dx;
        if (handle is 0 or 1 or 2) t += dy;
        if (handle is 2 or 3 or 4) right += dx;
        if (handle is 4 or 5 or 6) b += dy;

        double x1 = Math.Clamp(Math.Min(l, right), 0, _snap.Width - 1);
        double y1 = Math.Clamp(Math.Min(t, b), 0, _snap.Height - 1);
        double x2 = Math.Clamp(Math.Max(l, right), x1 + 1, _snap.Width);
        double y2 = Math.Clamp(Math.Max(t, b), y1 + 1, _snap.Height);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private Point[] HandleCenters()
    {
        double l = _sel.Left, t = _sel.Top, r = _sel.Right, b = _sel.Bottom;
        double cx = (l + r) / 2, cy = (t + b) / 2;
        return [new(l, t), new(cx, t), new(r, t), new(r, cy), new(r, b), new(cx, b), new(l, b), new(l, cy)];
    }

    private int HitHandle(Point p)
    {
        if (!HasSelection)
            return -1;
        double tolerance = HandleSize * _scale;
        var centers = HandleCenters();
        for (int i = 0; i < centers.Length; i++)
        {
            if (Math.Abs(p.X - centers[i].X) <= tolerance && Math.Abs(p.Y - centers[i].Y) <= tolerance)
                return i;
        }
        return -1;
    }

    private void ResetSelection()
    {
        CommitText();
        _annotations.Clear();
        _annotationLayer.InvalidateVisual();
        _tool = ToolKind.None;
        _toolbar.ClearTool();
        _state = State.Detecting;
        Cursor = Cursors.Cross;
        DetectAt(_cursor);
        Refresh();
    }

    // ---------------------------------------------------------------- annotations

    private Annotation CreateAnnotation(Point p)
    {
        double thickness = _size * _scale;
        return _tool switch
        {
            ToolKind.Rectangle => new RectAnnotation(p, _color, thickness),
            ToolKind.Ellipse => new EllipseAnnotation(p, _color, thickness),
            ToolKind.Arrow => new ArrowAnnotation(p, _color, thickness),
            ToolKind.Mosaic => new MosaicAnnotation(p, _snap, Math.Max(4, (int)Math.Round(_size * 3 * _scale))),
            _ => new PenAnnotation(p, _color, thickness),
        };
    }

    private void BeginText(Point p)
    {
        double fontSize = (12 + _size * 2) * _scale;
        _textPos = p;
        _textBox = new TextBox
        {
            FontSize = fontSize,
            FontFamily = TextAnnotation.Font,
            Foreground = new SolidColorBrush(_color),
            CaretBrush = new SolidColorBrush(_color),
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = fontSize * 2,
            Cursor = Cursors.IBeam,
        };
        Canvas.SetLeft(_textBox, p.X);
        Canvas.SetTop(_textBox, p.Y);
        _root.Children.Add(_textBox);
        var box = _textBox;
        Dispatcher.BeginInvoke(() => box.Focus(), DispatcherPriority.Input);
    }

    private void CommitText()
    {
        var box = _textBox;
        if (box == null)
            return;
        _textBox = null;
        _root.Children.Remove(box);
        if (!string.IsNullOrWhiteSpace(box.Text))
        {
            // Offset by the TextBox border and internal text margin so the text stays where it was typed.
            var position = new Point(_textPos.X + 3, _textPos.Y + 1);
            var brush = (SolidColorBrush)box.Foreground;
            _annotations.Add(new TextAnnotation(box.Text, position, box.FontSize, brush.Color));
            _annotationLayer.InvalidateVisual();
        }
        Keyboard.Focus(this);
    }

    private void Undo()
    {
        CommitText();
        if (_annotations.Undo())
            _annotationLayer.InvalidateVisual();
    }

    private void Redo()
    {
        if (_annotations.Redo())
            _annotationLayer.InvalidateVisual();
    }

    // ---------------------------------------------------------------- finish

    private void Finish(OverlayAction action)
    {
        if (_state != State.Selected || !HasSelection)
            return;
        CommitText();
        var image = Compositor.Compose(_snap, _sel, _annotations.Items);
        Result = new OverlayResult(action, image, (int)_sel.X + _snap.Left, (int)_sel.Y + _snap.Top);
        Close();
    }

    // ---------------------------------------------------------------- rendering

    private void Refresh()
    {
        bool has = HasSelection;
        _hole.Rect = has ? _sel : new Rect(0, 0, 0, 0);
        _annotationClip.Rect = has ? _sel : new Rect(0, 0, 0, 0);

        // Border (stroke is drawn inside the shape, so expand it to sit just outside the selection)
        _border.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            double t = _border.StrokeThickness;
            Canvas.SetLeft(_border, _sel.X - t);
            Canvas.SetTop(_border, _sel.Y - t);
            _border.Width = _sel.Width + 2 * t;
            _border.Height = _sel.Height + 2 * t;
        }

        // Resize handles
        bool showHandles = has && _state is not (State.Detecting or State.Pressing);
        double hs = HandleSize * _scale;
        var centers = HandleCenters();
        for (int i = 0; i < _handles.Length; i++)
        {
            var h = _handles[i];
            h.Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
            if (!showHandles)
                continue;
            h.Width = h.Height = hs;
            Canvas.SetLeft(h, centers[i].X - hs / 2);
            Canvas.SetTop(h, centers[i].Y - hs / 2);
        }

        // Size label
        _sizeLabel.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            _sizeText.Text = $"{_sel.Width:0} × {_sel.Height:0}";
            _sizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelH = _sizeLabel.DesiredSize.Height * _scale;
            double y = _sel.Y - labelH - 4 * _scale;
            Canvas.SetLeft(_sizeLabel, _sel.X);
            Canvas.SetTop(_sizeLabel, y >= 0 ? y : _sel.Y + 4 * _scale);
        }

        // Magnifier
        bool showMagnifier = _state is State.Detecting or State.Pressing or State.Selecting or State.Resizing;
        _magnifier.Visibility = showMagnifier ? Visibility.Visible : Visibility.Collapsed;
        if (showMagnifier)
        {
            _magnifier.Update(_snap, (int)_cursor.X, (int)_cursor.Y);
            _magnifier.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _magnifier.DesiredSize.Width * _scale, h = _magnifier.DesiredSize.Height * _scale;
            double gap = 20 * _scale;
            double x = _cursor.X + gap + w > _snap.Width ? _cursor.X - gap - w : _cursor.X + gap;
            double y = _cursor.Y + gap + h > _snap.Height ? _cursor.Y - gap - h : _cursor.Y + gap;
            Canvas.SetLeft(_magnifier, Math.Max(0, x));
            Canvas.SetTop(_magnifier, Math.Max(0, y));
        }

        // Toolbar
        bool showToolbar = has && _state is State.Selected or State.Drawing;
        _toolbar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
        if (showToolbar)
        {
            _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _toolbar.DesiredSize.Width * _scale, h = _toolbar.DesiredSize.Height * _scale;
            double gap = 6 * _scale;
            double x = Math.Clamp(_sel.Right - w, 0, Math.Max(0, _snap.Width - w));
            double y = _sel.Bottom + gap;
            if (y + h > _snap.Height)
                y = _sel.Top - gap - h;
            if (y < 0)
                y = _sel.Bottom - gap - h;
            Canvas.SetLeft(_toolbar, x);
            Canvas.SetTop(_toolbar, Math.Max(0, y));
        }

        if (_state is State.Detecting or State.Pressing or State.Selecting)
            Cursor = Cursors.Cross;
    }
}
