using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ScreenCap.Capture;
using ScreenCap.Ocr;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Pin;

/// <summary>A borderless always-on-top window showing a pinned image.</summary>
internal sealed class PinWindow : Window
{
    private const double ShadowMargin = 10; // DIP around the image for the drop shadow
    private const int MaxClosed = 20;
    private static readonly List<PinWindow> OpenPins = new();

    /// <summary>Recently closed pins, newest last, so they can be brought back.</summary>
    private static readonly List<ClosedPin> ClosedPins = new();

    private sealed record ClosedPin(BitmapSource Image, int ScreenX, int ScreenY, double Zoom, double Opacity, string? Title);

    /// <summary>All pins are temporarily hidden (from the tray menu).</summary>
    public static bool AreHidden { get; private set; }

    public static bool HasOpenPins => OpenPins.Count > 0;
    public static bool CanRestore => ClosedPins.Count > 0;

    private BitmapSource _image;
    private readonly string? _title; // of the captured window, for file names
    private readonly Image _view;
    private int _screenX, _screenY;
    private double _zoom = 1;
    private double _dpiScale = 1;
    private bool _clickThrough;
    private bool _editing;

    private PinWindow(BitmapSource image, int screenX, int screenY, string? title)
    {
        _image = image;
        _title = title;
        _screenX = screenX;
        _screenY = screenY;

        Title = "ScreenCap 貼圖";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        UseLayoutRounding = true;

        _view = new Image { Source = image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

        Content = new Border
        {
            Margin = new Thickness(ShadowMargin),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x1A, 0x9F, 0xFF)),
            Child = _view,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.45, Color = Colors.Black },
        };
        ContextMenu = BuildMenu();

        SourceInitialized += (_, _) => Place();
        // Nothing inside is focusable, so without this key presses never reach the window.
        Activated += (_, _) => Keyboard.Focus(this);
        DpiChanged += (_, e) =>
        {
            _dpiScale = e.NewDpi.DpiScaleX;
            // WPF rescales the window after this event; restore our size once it has done so.
            Dispatcher.BeginInvoke(ApplySize);
        };
    }

    /// <param name="title">Title of the window the image came from, used when saving.</param>
    public static void ShowAt(BitmapSource image, int screenX, int screenY, string? title = null) =>
        Open(new PinWindow(image, screenX, screenY, title));

    private static void Open(PinWindow pin)
    {
        if (AreHidden)
            SetHidden(false); // a new pin should be seen, along with the others
        OpenPins.Add(pin);
        pin.Closing += (_, _) => pin.Remember();
        pin.Closed += (_, _) => OpenPins.Remove(pin);
        pin.Show();
        pin.Activate();
    }

    /// <summary>Hides or shows every pin without closing them.</summary>
    public static void SetHidden(bool hidden)
    {
        AreHidden = hidden;
        foreach (var pin in OpenPins.Where(p => !p._editing))
        {
            if (hidden)
                pin.Hide();
            else
                pin.Show();
        }
    }

    /// <summary>Reopens the most recently closed pin where it was, at the same zoom and opacity.</summary>
    public static void RestoreLast()
    {
        if (ClosedPins.Count == 0)
            return;
        var closed = ClosedPins[^1];
        ClosedPins.RemoveAt(ClosedPins.Count - 1);
        var pin = new PinWindow(closed.Image, closed.ScreenX, closed.ScreenY, closed.Title) { _zoom = closed.Zoom, Opacity = closed.Opacity };
        Open(pin);
    }

    private void Remember()
    {
        if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
            return;
        GetWindowRect(Handle, out var rect);
        ClosedPins.Add(new ClosedPin(_image, rect.Left + Inset, rect.Top + Inset, _zoom, Opacity, _title));
        if (ClosedPins.Count > MaxClosed)
            ClosedPins.RemoveAt(0);
    }

    public static void CloseAll()
    {
        foreach (var pin in OpenPins.ToArray())
            pin.Close();
    }

    public static void ReleaseAllClickThrough()
    {
        foreach (var pin in OpenPins)
            pin.SetClickThrough(false);
    }

    /// <summary>Positions the window so the image's top-left lands on the given physical screen point.</summary>
    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Physical-pixel gap between the window edge and the image (shadow margin + border).</summary>
    private int Inset => (int)Math.Round((ShadowMargin + 1) * _dpiScale);

    /// <summary>Positions the window so the image's top-left lands on the given physical screen point.</summary>
    private void Place()
    {
        // Move first so the window picks up the DPI of the target monitor.
        SetWindowPos(Handle, IntPtr.Zero, _screenX, _screenY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        _dpiScale = GetDpiForWindow(Handle) / 96.0;
        SetWindowPos(Handle, IntPtr.Zero, _screenX - Inset, _screenY - Inset, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        ApplySize();
    }

    /// <summary>
    /// Sizes the window explicitly in physical pixels (keeping its top-left fixed); the image stretches to fill it.
    /// SizeToContent is not used because it fights with the SetWindowPos placement and can blow the window up.
    /// </summary>
    private void ApplySize()
    {
        int w = (int)Math.Round(_image.PixelWidth * _zoom) + 2 * Inset;
        int h = (int)Math.Round(_image.PixelHeight * _zoom) + 2 * Inset;
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("編輯標註", "Space", EditAnnotations));
        menu.Items.Add(MenuItem("文字辨識", "O", () => OcrResultWindow.ShowFor(_image)));
        menu.Items.Add(MenuItem("複製", "Ctrl+C", Copy));
        menu.Items.Add(MenuItem("另存新檔…", "Ctrl+S", Save));
        menu.Items.Add(MenuItem("快速存檔", "Ctrl+Shift+S", () => CaptureSession.QuickSave(_image, _title)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("向左旋轉", "1", () => Transform(new RotateTransform(-90))));
        menu.Items.Add(MenuItem("向右旋轉", "2", () => Transform(new RotateTransform(90))));
        menu.Items.Add(MenuItem("水平翻轉", "3", () => Transform(new ScaleTransform(-1, 1))));
        menu.Items.Add(MenuItem("垂直翻轉", "4", () => Transform(new ScaleTransform(1, -1))));
        menu.Items.Add(MenuItem("原始大小", "Ctrl+0", ResetView));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("滑鼠穿透", "X", () => SetClickThrough(true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("關閉", "Esc", Close));
        return menu;
    }

    private static MenuItem MenuItem(string header, string gesture, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetImage(BitmapSource image)
    {
        _image = image;
        _view.Source = image;
    }

    /// <summary>Rotates or flips the pinned image itself, so copy and save use the new orientation.</summary>
    private void Transform(Transform transform)
    {
        var transformed = new TransformedBitmap(_image, transform);
        transformed.Freeze();
        SetImage(transformed);
        ApplySize();
    }

    /// <summary>Lets clicks pass through to the windows below; undone from the tray menu.</summary>
    private void SetClickThrough(bool enabled)
    {
        if (_clickThrough == enabled)
            return;
        _clickThrough = enabled;
        int style = GetWindowLong(Handle, GWL_EXSTYLE);
        SetWindowLong(Handle, GWL_EXSTYLE, enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT);
        if (enabled)
            Notifier.Show("貼圖已開啟滑鼠穿透，可從系統匣選單「解除所有貼圖的滑鼠穿透」恢復。");
    }

    /// <summary>Hides the pin and reopens it in the capture editor at 100% so annotations can be added.</summary>
    private void EditAnnotations()
    {
        if (_editing)
            return;
        _editing = true;
        GetWindowRect(Handle, out var rect);
        Hide();
        CaptureSession.EditImage(_image, rect.Left + Inset, rect.Top + Inset, (edited, x, y) =>
        {
            _editing = false;
            if (edited != null)
            {
                SetImage(edited);
                _zoom = 1;
                _screenX = x;
                _screenY = y;
            }
            Show();
            if (edited != null)
                Place();
            Activate();
        });
    }

    private void Copy() => ClipboardService.SetImage(_image);

    private void Save() => ImageExportService.Save(_image, this, _title);

    private void ResetView()
    {
        _zoom = 1;
        Opacity = 1;
        ApplySize();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
            Close();
        else
            DragMove();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int direction = Math.Sign(e.Delta);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Opacity = Math.Clamp(Opacity + direction * 0.1, 0.2, 1);
        }
        else
        {
            _zoom = Math.Clamp(direction > 0 ? _zoom * 1.1 : _zoom / 1.1, 0.1, 8);
            ApplySize();
        }
        e.Handled = true;
    }

    // Preview: with an IME active the bubbling KeyDown is already marked handled.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.RealKey())
        {
            case Key.Escape:
                Close();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.S when ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                CaptureSession.QuickSave(_image, _title);
                break;
            case Key.S when ctrl:
                Save();
                break;
            case Key.Space:
                EditAnnotations();
                break;
            case Key.D1 or Key.NumPad1:
                Transform(new RotateTransform(-90));
                break;
            case Key.D2 or Key.NumPad2:
                Transform(new RotateTransform(90));
                break;
            case Key.D3 or Key.NumPad3:
                Transform(new ScaleTransform(-1, 1));
                break;
            case Key.D4 or Key.NumPad4:
                Transform(new ScaleTransform(1, -1));
                break;
            case Key.O when !ctrl:
                OcrResultWindow.ShowFor(_image);
                break;
            case Key.X when !ctrl:
                SetClickThrough(true);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                ResetView();
                break;
        }
    }
}
