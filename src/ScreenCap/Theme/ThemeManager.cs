using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Theme;

internal enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Light/dark theming. Colors live in a swappable palette dictionary referenced by DynamicResource keys
/// (see Styles.xaml), so switching updates every open window immediately.
/// </summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _palette;

    public static bool IsDark { get; private set; }

    /// <summary>Raised after the theme switched, for UI that isn't driven by resources (e.g. the tray menu).</summary>
    public static event Action? Changed;

    public static void Initialize(Application app)
    {
        _palette = new ResourceDictionary();
        app.Resources.MergedDictionaries.Add(_palette);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ScreenCap;component/Theme/Styles.xaml"),
        });
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && AppSettings.Theme == AppTheme.System)
                app.Dispatcher.BeginInvoke(Apply);
        };
    }

    /// <summary>Re-reads the theme setting (and the Windows app theme) and applies it.</summary>
    public static void Apply()
    {
        if (_palette == null)
            return;
        IsDark = AppSettings.Theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => SystemUsesDarkTheme(),
        };
        _palette.Clear();
        foreach (var (key, color) in IsDark ? Dark : Light)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _palette[key] = brush;
        }
        foreach (Window window in Application.Current.Windows)
            ApplyTitleBar(window);
        Changed?.Invoke();
    }

    public static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <summary>Themes a regular (title-bar) window: background, text color and a matching title bar.</summary>
    public static void Attach(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Window");
        window.SetResourceReference(Window.ForegroundProperty, "Brush.Text");
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    private static void ApplyTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || window.WindowStyle == WindowStyle.None)
            return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    /// <summary>Asks Windows 11 for rounded corners on a popup window (no effect on Windows 10).</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>Shorthand for <see cref="FrameworkElement.SetResourceReference"/> in code-built UI.</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }

    private static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // Windows 11 Fluent-inspired palettes.
    private static readonly (string Key, Color Color)[] Light =
    [
        ("Brush.Window", C(0xFFF3F3F3)),
        ("Brush.Surface", C(0xFFFFFFFF)),
        ("Brush.Toolbar", C(0xF7FCFCFC)),
        ("Brush.Card", C(0xFFF0F0F0)),
        ("Brush.Border", C(0xFFE5E5E5)),
        ("Brush.Divider", C(0x1F000000)),
        ("Brush.Text", C(0xFF1A1A1A)),
        ("Brush.Muted", C(0xFF616161)),
        ("Brush.Disabled", C(0xFFA0A0A0)),
        ("Brush.Hover", C(0x0F000000)),
        ("Brush.Pressed", C(0x1A000000)),
        ("Brush.Control", C(0xFFFBFBFB)),
        ("Brush.ControlHover", C(0xFFF5F5F5)),
        ("Brush.ControlPressed", C(0xFFEFEFEF)),
        ("Brush.ControlBorder", C(0xFFD4D4D4)),
        ("Brush.Input", C(0xFFFFFFFF)),
        ("Brush.Accent", C(0xFF005FB8)),
        ("Brush.AccentHover", C(0xFF1A6FC2)),
        ("Brush.AccentText", C(0xFFFFFFFF)),
        ("Brush.AccentSoft", C(0xFFDCEBFA)),
        ("Brush.AccentStrong", C(0xFF005FB8)),
        ("Brush.Danger", C(0xFFC42B1C)),
        ("Brush.Success", C(0xFF0F7B0F)),
        ("Brush.Menu", C(0xFFF9F9F9)),
        ("Brush.ScrollThumb", C(0xFFB0B0B0)),
    ];

    private static readonly (string Key, Color Color)[] Dark =
    [
        ("Brush.Window", C(0xFF202020)),
        ("Brush.Surface", C(0xFF2B2B2B)),
        ("Brush.Toolbar", C(0xF52B2B2B)),
        ("Brush.Card", C(0xFF333333)),
        ("Brush.Border", C(0xFF3A3A3A)),
        ("Brush.Divider", C(0x26FFFFFF)),
        ("Brush.Text", C(0xFFFFFFFF)),
        ("Brush.Muted", C(0xFFB4B4B4)),
        ("Brush.Disabled", C(0xFF6E6E6E)),
        ("Brush.Hover", C(0x14FFFFFF)),
        ("Brush.Pressed", C(0x0CFFFFFF)),
        ("Brush.Control", C(0xFF2D2D2D)),
        ("Brush.ControlHover", C(0xFF353535)),
        ("Brush.ControlPressed", C(0xFF292929)),
        ("Brush.ControlBorder", C(0xFF454545)),
        ("Brush.Input", C(0xFF1C1C1C)),
        ("Brush.Accent", C(0xFF60CDFF)),
        ("Brush.AccentHover", C(0xFF7AD5FF)),
        ("Brush.AccentText", C(0xFF000000)),
        ("Brush.AccentSoft", C(0xFF1F3B50)),
        ("Brush.AccentStrong", C(0xFF60CDFF)),
        ("Brush.Danger", C(0xFFFF99A4)),
        ("Brush.Success", C(0xFF6CCB5F)),
        ("Brush.Menu", C(0xFF2C2C2C)),
        ("Brush.ScrollThumb", C(0xFF6A6A6A)),
    ];
}
