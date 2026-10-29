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
    Record,
    QuickSave,
    Ocr,
}

internal enum OverlayMode
{
    /// <summary>Normal screenshot with annotation tools.</summary>
    Capture,

    /// <summary>Selecting a region to record rather than to screenshot.</summary>
    Record,

    /// <summary>Annotating an existing pin; the selection starts on the pinned image.</summary>
    EditPin,
}

/// <param name="Width">Selection size in physical pixels (used for recording).</param>
internal sealed record OverlayResult(OverlayAction Action, BitmapSource? Image, int ScreenX, int ScreenY, int Width = 0, int Height = 0)
{
    public static readonly OverlayResult Cancelled = new(OverlayAction.Cancel, null, 0, 0);
}

/// <summary>
/// Full virtual-screen window showing the frozen screenshot. All layout inside <see cref="_root"/>
/// is in physical pixels; chrome elements (toolbar, magnifier, labels) are scaled back up to DIPs.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private enum State { Detecting, Pressing, Selecting, Selected, Moving, Resizing, Drawing, AnnotationMoving, AnnotationResizing }

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
    private readonly AnnotationAdorner _adorner = new() { IsHitTestVisible = false, HandleSize = HandleSize };
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
    private int _mosaicStrength = MosaicAnnotation.DefaultStrength;
    private Annotation? _drawing;

    // Editing of existing annotations
    private Annotation? _selected;
    private Annotation? _editOrigin; // state of _selected when the drag started
    private int _editHandle = -1;
    private bool _editDirty;         // an undo checkpoint has been taken for the current drag

    private TextBox? _textBox;
    private Point _textPos;
    private double _textSize;
    private bool _textReplacing;     // re-editing an existing text annotation

    private bool _textBold;
    private bool _textBackground;

    private int _historyIndex = -1; // position while stepping through earlier capture regions with , and .

    private readonly OverlayMode _mode;

    public OverlayWindow(ScreenSnapshot snap, WindowDetector detector, OverlayMode mode = OverlayMode.Capture, Rect? initialSelection = null)
    {
        _mode = mode;
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

        _adorner.Width = snap.Width;
        _adorner.Height = snap.Height;
        _root.Children.Add(_adorner);

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
            if (initialSelection is { } initial)
            {
                _sel = initial;
                _state = State.Selected;
                UpdateSelectedCursor(_cursor);
            }
            else
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
        _adorner.Scale = scale;
        _adorner.InvalidateVisual();
        Refresh();
    }

    private void WireToolbar()
    {
        _toolbar.ToolChanged += tool =>
        {
            CommitText();
            _tool = tool;
            Select(null);
            Refresh();
        };
        _toolbar.ColorChanged += c =>
        {
            _color = c;
            StyleTextBox();
            Restyle(a => a.Color = c);
        };
        _toolbar.ThicknessChanged += s =>
        {
            _size = s;
            if (_textBox != null)
            {
                _textSize = s;
                _textBox.FontSize = TextAnnotation.FontSizeFor(s, _scale);
            }
            Restyle(a => a.Size = s);
        };
        _toolbar.MosaicStrengthChanged += (strength, newGesture) =>
        {
            _mosaicStrength = strength;
            if (_selected is not MosaicAnnotation mosaic)
                return;
            // One undo step per slider drag, not per tick.
            if (newGesture)
                _annotations.Checkpoint();
            mosaic.Strength = strength;
            InvalidateAnnotations();
        };
        _toolbar.UndoRequested += Undo;
        _toolbar.RedoRequested += Redo;
        _toolbar.PinRequested += () => Finish(OverlayAction.Pin);
        _toolbar.SaveRequested += () => Finish(OverlayAction.Save);
        _toolbar.CopyRequested += () => Finish(OverlayAction.Copy);
        _toolbar.CancelRequested += Close;
        _toolbar.TextStyleChanged += (bold, background) =>
        {
            _textBold = bold;
            _textBackground = background;
            StyleTextBox();
            if (_selected is TextAnnotation)
            {
                Restyle(a =>
                {
                    var text = (TextAnnotation)a;
                    text.Bold = bold;
                    text.Background = background;
                });
            }
        };
        _toolbar.RecordRequested += () => Finish(OverlayAction.Record);
        _toolbar.OcrRequested += () => Finish(OverlayAction.Ocr);
        if (_mode == OverlayMode.Record)
            _toolbar.SetRecordMode();
        else if (_mode == OverlayMode.EditPin)
            _toolbar.SetEditPinMode();
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
        if (Keyboard.FocusedElement is TextBox field && field != _textBox)
            FocusOverlay(); // leave the palette's HEX box so overlay shortcuts work again

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

                // Existing annotations take priority over the selection and the current tool.
                int annotationHandle = HitAnnotationHandle(p);
                if (annotationHandle >= 0)
                {
                    BeginAnnotationEdit(State.AnnotationResizing, p, annotationHandle);
                    break;
                }
                if (HitAnnotation(p) is { } hit)
                {
                    if (e.ClickCount == 2 && hit is TextAnnotation text)
                        EditText(text);
                    else
                    {
                        Select(hit);
                        BeginAnnotationEdit(State.AnnotationMoving, p, -1);
                    }
                    break;
                }
                Select(null);

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

            case State.AnnotationMoving:
            case State.AnnotationResizing:
                DragSelectedAnnotation(p - _press);
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

            case State.AnnotationMoving:
            case State.AnnotationResizing:
                _editOrigin = null;
                _state = State.Selected;
                break;

            case State.Drawing:
                var drawn = _drawing;
                _drawing = null;
                _annotationLayer.Current = null;
                _state = State.Selected;
                if (drawn is { IsEmpty: false })
                {
                    _annotations.Add(drawn);
                    Select(drawn); // keep the new shape adjustable right away
                }
                InvalidateAnnotations();
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
        else if (_selected != null)
        {
            Select(null);
            Refresh();
        }
        else if (_state == State.Selected && _mode != OverlayMode.EditPin)
            ResetSelection();
        else
            Close();
    }

    private void UpdateSelectedCursor(Point p)
    {
        int annotationHandle = HitAnnotationHandle(p);
        if (annotationHandle >= 0)
        {
            Cursor = _selected is ArrowAnnotation ? Cursors.SizeAll : HandleCursor(annotationHandle);
            return;
        }
        if (HitAnnotation(p) != null)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        int handle = HitHandle(p);
        Cursor = handle >= 0 ? HandleCursor(handle) : !_sel.Contains(p) ? Cursors.Arrow : _tool switch
        {
            ToolKind.None => Cursors.SizeAll,
            ToolKind.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }

    private static Cursor HandleCursor(int handle) => handle switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    // ---------------------------------------------------------------- keyboard

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // Typing in a toolbar field (the palette's HEX box); Esc returns to the overlay.
        if (Keyboard.FocusedElement is TextBox field && field != _textBox)
        {
            if (e.Key == Key.Escape)
            {
                FocusOverlay();
                e.Handled = true;
            }
            return;
        }

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
        var key = e.RealKey();
        switch (key)
        {
            case Key.Escape when _selected != null:
                Select(null);
                Refresh();
                break;
            case Key.Delete or Key.Back when _selected != null:
                _annotations.Remove(_selected);
                Select(null);
                InvalidateAnnotations();
                Refresh();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _selected != null && selected:
                var step = new Vector(key == Key.Left ? -1 : key == Key.Right ? 1 : 0, key == Key.Up ? -1 : key == Key.Down ? 1 : 0);
                var before = _selected.Clone();
                _annotations.Checkpoint();
                _selected.MoveFrom(before, step);
                InvalidateAnnotations();
                break;
            case Key.Escape when selected && _mode != OverlayMode.EditPin:
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
            case Key.S when ctrl && shift && selected:
                Finish(OverlayAction.QuickSave);
                break;
            case Key.S when ctrl && selected:
                Finish(OverlayAction.Save);
                break;
            case Key.OemComma or Key.OemPeriod when !ctrl && _mode != OverlayMode.EditPin && _annotations.Items.Count == 0
                                                    && _state is State.Detecting or State.Selected:
                StepHistory(key == Key.OemComma ? -1 : 1);
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
                int dx = key == Key.Left ? -1 : key == Key.Right ? 1 : 0;
                int dy = key == Key.Up ? -1 : key == Key.Down ? 1 : 0;
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

    /// <summary>
    /// Moves keyboard focus back to the overlay. Focusing a window restores its remembered focused
    /// element, so that is reset first.
    /// </summary>
    private void FocusOverlay()
    {
        FocusManager.SetFocusedElement(this, this);
        Keyboard.Focus(this);
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

    /// <summary>Jumps to an earlier (-1) or later (+1) capture region; past the newest goes back to detection.</summary>
    private void StepHistory(int direction)
    {
        var history = CaptureSession.History;
        if (history.Count == 0)
            return;
        int index;
        if (_historyIndex < 0)
        {
            if (direction > 0)
                return; // already past the newest
            index = history.Count - 1;
        }
        else
            index = Math.Max(0, _historyIndex + direction);
        if (index >= history.Count)
        {
            _historyIndex = -1;
            ResetSelection();
            return;
        }

        var r = history[index];
        var rect = new Rect(r.X - _snap.Left, r.Y - _snap.Top, r.Width, r.Height);
        rect.Intersect(new Rect(0, 0, _snap.Width, _snap.Height));
        if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
            return; // that region is off-screen now (e.g. a monitor was unplugged)

        _historyIndex = index;
        _sel = rect;
        _state = State.Selected;
        UpdateSelectedCursor(_cursor);
        Refresh();
    }

    private void DetectAt(Point p) => _sel = _detector.HitTest((int)p.X, (int)p.Y) ?? Rect.Empty;

    private static Rect Span(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);

    private Rect ResizeFrom(Rect r, int handle, double dx, double dy)
    {
        var resized = RectHandles.Resize(r, handle, new Vector(dx, dy));
        double x1 = Math.Clamp(resized.Left, 0, _snap.Width - 1);
        double y1 = Math.Clamp(resized.Top, 0, _snap.Height - 1);
        double x2 = Math.Clamp(resized.Right, x1 + 1, _snap.Width);
        double y2 = Math.Clamp(resized.Bottom, y1 + 1, _snap.Height);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    private Point[] HandleCenters() => RectHandles.Points(_sel);

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
        Select(null);
        _annotations.Clear();
        InvalidateAnnotations();
        _tool = ToolKind.None;
        _toolbar.ClearTool();
        _state = State.Detecting;
        _historyIndex = -1;
        Cursor = Cursors.Cross;
        DetectAt(_cursor);
        Refresh();
    }

    // ---------------------------------------------------------------- annotations

    private Annotation CreateAnnotation(Point p) => _tool switch
    {
        ToolKind.Rectangle => new RectAnnotation(p, _color, _size, _scale),
        ToolKind.Ellipse => new EllipseAnnotation(p, _color, _size, _scale),
        ToolKind.Arrow => new ArrowAnnotation(p, _color, _size, _scale),
        ToolKind.Highlighter => new HighlighterAnnotation(p, _color, _size, _scale),
        ToolKind.Number => new NumberAnnotation(p, NextNumber(), _color, _size, _scale),
        ToolKind.Mosaic => new MosaicAnnotation(p, _snap, _mosaicStrength, _scale),
        _ => new PenAnnotation(p, _color, _size, _scale),
    };

    private int NextNumber() => _annotations.Items.OfType<NumberAnnotation>().Select(n => n.Number).DefaultIfEmpty(0).Max() + 1;

    private void InvalidateAnnotations()
    {
        _annotationLayer.InvalidateVisual();
        _adorner.InvalidateVisual();
    }

    private void Select(Annotation? annotation)
    {
        _selected = annotation;
        _adorner.Target = annotation;
        _adorner.InvalidateVisual();
        // New shapes continue with the selected shape's style.
        if (annotation is MosaicAnnotation mosaic)
        {
            _mosaicStrength = mosaic.Strength;
            _toolbar.SetMosaicStrength(_mosaicStrength);
        }
        else if (annotation != null)
        {
            _color = annotation.Color;
            _size = annotation.Size;
            _toolbar.SetStyle(_color, _size);
            if (annotation is TextAnnotation text)
            {
                _textBold = text.Bold;
                _textBackground = text.Background;
                _toolbar.SetTextStyle(_textBold, _textBackground);
            }
        }
        bool mosaicOptions = annotation is MosaicAnnotation || (annotation == null && _tool == ToolKind.Mosaic);
        bool textOptions = annotation is TextAnnotation || (annotation == null && _tool == ToolKind.Text);
        _toolbar.SetOptionsVisible(annotation != null || _tool != ToolKind.None, mosaicOptions, textOptions);
    }

    /// <summary>Topmost annotation under the point, only within the visible selection.</summary>
    private Annotation? HitAnnotation(Point p)
    {
        if (!_sel.Contains(p))
            return null;
        double tolerance = 4 * _scale;
        var items = _annotations.Items;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (items[i].HitTest(p, tolerance))
                return items[i];
        }
        return null;
    }

    private int HitAnnotationHandle(Point p)
    {
        if (_selected == null)
            return -1;
        double tolerance = HandleSize * _scale;
        var handles = _selected.Handles;
        for (int i = 0; i < handles.Length; i++)
        {
            if (Math.Abs(p.X - handles[i].X) <= tolerance && Math.Abs(p.Y - handles[i].Y) <= tolerance)
                return i;
        }
        return -1;
    }

    private void BeginAnnotationEdit(State state, Point p, int handle)
    {
        _state = state;
        _press = p;
        _editHandle = handle;
        _editOrigin = _selected!.Clone();
        _editDirty = false;
        CaptureMouse();
    }

    private void DragSelectedAnnotation(Vector delta)
    {
        if (_selected == null || _editOrigin == null)
            return;
        if (!_editDirty)
        {
            if (delta.Length < 1)
                return; // a plain click only selects; don't record an undo step
            _annotations.Checkpoint();
            _editDirty = true;
        }

        if (_state == State.AnnotationMoving)
            _selected.MoveFrom(_editOrigin, delta);
        else
            _selected.DragHandleFrom(_editOrigin, _editHandle, delta);
        InvalidateAnnotations();
    }

    /// <summary>Applies a toolbar style change to the selected annotation as an undoable step.</summary>
    private void Restyle(Action<Annotation> change)
    {
        if (_selected == null)
            return;
        _annotations.Checkpoint();
        change(_selected);
        InvalidateAnnotations();
        Refresh();
    }

    private void EditText(TextAnnotation text)
    {
        // Removing records the undo step; committing the edit adds the replacement without another one.
        _annotations.Remove(text);
        Select(null);
        InvalidateAnnotations();
        _color = text.Color;
        _size = text.Size;
        _textBold = text.Bold;
        _textBackground = text.Background;
        _toolbar.SetStyle(_color, _size);
        _toolbar.SetTextStyle(_textBold, _textBackground);
        _toolbar.SetOptionsVisible(true, text: true);
        BeginText(new Point(text.Position.X - 3, text.Position.Y - 1), text.Text, replacing: true);
    }

    private void BeginText(Point p, string text = "", bool replacing = false)
    {
        double fontSize = TextAnnotation.FontSizeFor(_size, _scale);
        _textPos = p;
        _textSize = _size;
        _textReplacing = replacing;
        _textBox = new TextBox
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = TextAnnotation.Font,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = fontSize * 2,
            Cursor = Cursors.IBeam,
        };
        StyleTextBox();
        Canvas.SetLeft(_textBox, p.X);
        Canvas.SetTop(_textBox, p.Y);
        _root.Children.Add(_textBox);
        var box = _textBox;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }, DispatcherPriority.Input);
    }

    /// <summary>Previews the current color, weight and background in the text being typed.</summary>
    private void StyleTextBox()
    {
        if (_textBox == null)
            return;
        var text = new SolidColorBrush(_textBackground ? TextAnnotation.ContrastColor(_color) : _color);
        _textBox.Foreground = _textBox.CaretBrush = text;
        _textBox.Background = _textBackground ? new SolidColorBrush(_color) : Brushes.Transparent;
        _textBox.FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal;
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
            var annotation = new TextAnnotation(box.Text, position, _color, _textSize, _scale, _textBold, _textBackground);
            _annotations.Add(annotation, checkpoint: !_textReplacing);
            Select(annotation);
            InvalidateAnnotations();
        }
        Keyboard.Focus(this);
        Refresh();
    }

    private void Undo()
    {
        CommitText();
        // Undo restores clones, so the selected instance is no longer part of the list.
        Select(null);
        if (_annotations.Undo())
            InvalidateAnnotations();
        Refresh();
    }

    private void Redo()
    {
        Select(null);
        if (_annotations.Redo())
            InvalidateAnnotations();
        Refresh();
    }

    // ---------------------------------------------------------------- finish

    private void Finish(OverlayAction action)
    {
        if (_state != State.Selected || !HasSelection)
            return;
        int x = (int)_sel.X + _snap.Left, y = (int)_sel.Y + _snap.Top;

        if (_mode != OverlayMode.EditPin)
            CaptureSession.AddHistory(new Int32Rect(x, y, (int)_sel.Width, (int)_sel.Height));

        // In record mode every confirm action (Enter, double-click, ...) starts recording.
        if (_mode == OverlayMode.Record || action == OverlayAction.Record)
        {
            Result = new OverlayResult(OverlayAction.Record, null, x, y, (int)_sel.Width, (int)_sel.Height);
            Close();
            return;
        }

        CommitText();
        var image = Compositor.Compose(_snap, _sel, _annotations.Items);
        Result = new OverlayResult(action, image, x, y);
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
        bool showToolbar = has && _state is State.Selected or State.Drawing or State.AnnotationMoving or State.AnnotationResizing;
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
