using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Pin;

/// <summary>A borderless always-on-top window showing a pinned image.</summary>
internal sealed class PinWindow : Window
{
    private const double ShadowMargin = 10; // DIP around the image for the drop shadow
    private static readonly List<PinWindow> OpenPins = new();

    private readonly BitmapSource _image;
    private readonly Image _view;
    private readonly int _screenX, _screenY;
    private double _zoom = 1;
    private double _dpiScale = 1;

    private PinWindow(BitmapSource image, int screenX, int screenY)
    {
        _image = image;
        _screenX = screenX;
        _screenY = screenY;

        Title = "ScreenCap 貼圖";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
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
            Background = Brushes.White,
            Child = _view,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.45, Color = Colors.Black },
        };
        ContextMenu = BuildMenu();

        SourceInitialized += (_, _) => Place();
        DpiChanged += (_, e) =>
        {
            _dpiScale = e.NewDpi.DpiScaleX;
            ApplySize();
        };
    }

    public static void ShowAt(BitmapSource image, int screenX, int screenY)
    {
        var pin = new PinWindow(image, screenX, screenY);
        OpenPins.Add(pin);
        pin.Closed += (_, _) => OpenPins.Remove(pin);
        pin.Show();
        pin.Activate();
    }

    public static void CloseAll()
    {
        foreach (var pin in OpenPins.ToArray())
            pin.Close();
    }

    /// <summary>Positions the window so the image's top-left lands on the given physical screen point.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        const uint flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;
        // Move first so the window picks up the DPI of the target monitor, then offset by the shadow margin.
        SetWindowPos(hwnd, IntPtr.Zero, _screenX, _screenY, 0, 0, flags);
        _dpiScale = GetDpiForWindow(hwnd) / 96.0;
        ApplySize();
        int offset = (int)Math.Round((ShadowMargin + 1) * _dpiScale);
        SetWindowPos(hwnd, IntPtr.Zero, _screenX - offset, _screenY - offset, 0, 0, flags);
    }

    private void ApplySize()
    {
        _view.Width = _image.PixelWidth * _zoom / _dpiScale;
        _view.Height = _image.PixelHeight * _zoom / _dpiScale;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("複製", "Ctrl+C", Copy));
        menu.Items.Add(MenuItem("另存新檔…", "Ctrl+S", Save));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("原始大小", "Ctrl+0", ResetView));
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

    private void Copy() => ClipboardService.SetImage(_image);

    private void Save() => ImageExportService.Save(_image, this);

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.S when ctrl:
                Save();
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                ResetView();
                break;
        }
    }
}
