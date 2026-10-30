using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScreenCap.Recording;

namespace ScreenCap.Capture;

internal enum ToolKind
{
    None,
    Rectangle,
    Ellipse,
    Arrow,
    Line,
    Pen,
    Highlighter,
    Number,
    Text,
    Mosaic,
}

internal partial class SelectionToolbar : UserControl
{
    private readonly Dictionary<ToggleButton, ToolKind> _tools;
    private readonly ToggleButton[] _sizes;
    private readonly ColorPalette _palette = new();

    public SelectionToolbar()
    {
        InitializeComponent();
        _tools = new()
        {
            [RectangleTool] = ToolKind.Rectangle,
            [EllipseTool] = ToolKind.Ellipse,
            [ArrowTool] = ToolKind.Arrow,
            [LineTool] = ToolKind.Line,
            [PenTool] = ToolKind.Pen,
            [HighlighterTool] = ToolKind.Highlighter,
            [NumberTool] = ToolKind.Number,
            [TextTool] = ToolKind.Text,
            [MosaicTool] = ToolKind.Mosaic,
        };
        _sizes = [SizeSmall, SizeMedium, SizeLarge];
        SizeMedium.IsChecked = true;
        ((ToggleButton)Swatches.Children[0]).IsChecked = true;
        SetMosaicStrength(Annotations.MosaicAnnotation.DefaultStrength);
        SetMosaicStyle(blur: false);
        PaletteHost.Content = _palette;
        _palette.ColorPicked += color =>
        {
            ShowColor(color);
            ColorChanged?.Invoke(color);
        };
        RecordButton.ToolTip = $"錄製螢幕（{RecordingSession.DisplayName(RecordingSession.Format)}）";
    }

    public event Action<ToolKind>? ToolChanged;
    public event Action<Color>? ColorChanged;
    public event Action<double>? ThicknessChanged;

    /// <summary>Raised with the new strength and whether this is the first change of a new slider gesture.</summary>
    public event Action<int, bool>? MosaicStrengthChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? PinRequested;
    public event Action? SaveRequested;
    public event Action? CopyRequested;
    public event Action? CancelRequested;
    public event Action? RecordRequested;
    public event Action? OcrRequested;
    public event Action? ScrollCaptureRequested;

    /// <summary>Raised with true for blur, false for mosaic blocks.</summary>
    public event Action<bool>? MosaicStyleChanged;

    /// <summary>Raised when the eyedropper is turned on (true) or off (false).</summary>
    public event Action<bool>? EyedropperToggled;

    public bool MosaicBlur => BlurStyle.IsChecked == true;

    public void SetMosaicStyle(bool blur)
    {
        BlurStyle.IsChecked = blur;
        PixelateStyle.IsChecked = !blur;
    }

    private void OnMosaicStyleClick(object sender, RoutedEventArgs e)
    {
        bool blur = sender == BlurStyle;
        SetMosaicStyle(blur);
        MosaicStyleChanged?.Invoke(blur);
    }

    private void OnEyedropperClick(object sender, RoutedEventArgs e) => EyedropperToggled?.Invoke(EyedropperToggle.IsChecked == true);

    public void SetEyedropperActive(bool active) => EyedropperToggle.IsChecked = active;

    /// <summary>Selects a color picked from the screen, as if chosen in the palette.</summary>
    public void PickColor(Color color)
    {
        ShowColor(color);
        _palette.SetColor(color);
        ColorChanged?.Invoke(color);
    }

    /// <summary>Raised with the bold and background state when either text style toggle changes.</summary>
    public event Action<bool, bool>? TextStyleChanged;

    public bool TextBold => BoldToggle.IsChecked == true;
    public bool TextBackground => BackgroundToggle.IsChecked == true;

    public void SetTextStyle(bool bold, bool background)
    {
        BoldToggle.IsChecked = bold;
        BackgroundToggle.IsChecked = background;
    }

    private void OnTextStyleClick(object sender, RoutedEventArgs e) => TextStyleChanged?.Invoke(TextBold, TextBackground);

    /// <summary>Raised with the fill and dash state when either toggle changes.</summary>
    public event Action<bool, bool>? ShapeStyleChanged;

    /// <summary>Raised with the new opacity in percent and whether this is the first change of a new slider gesture.</summary>
    public event Action<int, bool>? OpacityChanged;

    public bool ShapeFilled => FillToggle.IsChecked == true;
    public bool ShapeDashed => DashToggle.IsChecked == true;
    public int OpacityPercent => (int)OpacitySlider.Value;

    public void SetShapeStyle(bool filled, bool dashed, int opacity)
    {
        FillToggle.IsChecked = filled;
        DashToggle.IsChecked = dashed;
        _updatingSlider = true;
        OpacitySlider.Value = opacity;
        OpacityValue.Text = $"{opacity}%";
        _updatingSlider = false;
    }

    private void OnShapeStyleClick(object sender, RoutedEventArgs e) => ShapeStyleChanged?.Invoke(ShapeFilled, ShapeDashed);

    private bool _opacityGestureStarted;

    private void OnOpacitySliderPressed(object sender, MouseButtonEventArgs e) => _opacityGestureStarted = true;

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue == null || _updatingSlider)
            return; // fired during InitializeComponent, or a programmatic update
        OpacityValue.Text = $"{OpacityPercent}%";
        OpacityChanged?.Invoke(OpacityPercent, _opacityGestureStarted);
        _opacityGestureStarted = false;
    }

    /// <summary>Editing an existing pin: recording makes no sense there.</summary>
    public void SetEditPinMode()
    {
        RecordButton.Visibility = Visibility.Collapsed;
        ScrollCaptureButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Recording-only toolbar: just start recording and cancel.</summary>
    public void SetRecordMode()
    {
        EditTools.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
        OptionsRow.Visibility = Visibility.Collapsed;
        RecordButton.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        RecordLabel.Visibility = Visibility.Visible;
        FormatOptions.Visibility = Visibility.Visible;
        ShowFormat(RecordingSession.Format);
    }

    private void ShowFormat(RecordingFormat format)
    {
        GifFormat.IsChecked = format == RecordingFormat.Gif;
        Mp4Format.IsChecked = format == RecordingFormat.Mp4;
        RecordButton.ToolTip = $"開始錄製 {RecordingSession.DisplayName(format)} (Enter)";
    }

    private void OnFormatClick(object sender, RoutedEventArgs e)
    {
        var format = sender == Mp4Format ? RecordingFormat.Mp4 : RecordingFormat.Gif;
        ShowFormat(format); // clicking the active format keeps it selected
        RecordingSession.Format = format;
    }

    /// <summary>The preset swatches plus the custom-color swatch.</summary>
    private IEnumerable<ToggleButton> ColorButtons => Swatches.Children.OfType<ToggleButton>().Append(CustomSwatch);

    public Color SelectedColor =>
        ColorButtons.FirstOrDefault(b => b.IsChecked == true) is { Background: SolidColorBrush brush } ? brush.Color : Colors.Red;

    /// <summary>Checks the preset matching <paramref name="color"/>, or shows it in the custom swatch.</summary>
    private void ShowColor(Color color)
    {
        var target = Swatches.Children.OfType<ToggleButton>().FirstOrDefault(b => ((SolidColorBrush)b.Background).Color == color);
        if (target == null)
        {
            CustomSwatch.Background = new SolidColorBrush(color);
            CustomSwatch.ToolTip = $"自訂顏色 {ColorPalette.ToHex(color)}";
            CustomSwatch.Visibility = Visibility.Visible;
            target = CustomSwatch;
        }
        foreach (var button in ColorButtons)
            button.IsChecked = button == target;
    }

    private void OnPaletteClick(object sender, RoutedEventArgs e)
    {
        if (PaletteToggle.IsChecked == true)
            _palette.SetColor(SelectedColor);
        PaletteHost.Visibility = PaletteToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public double SelectedSize => double.Parse((string)_sizes.First(b => b.IsChecked == true).Tag);

    public void ClearTool()
    {
        foreach (var button in _tools.Keys)
            button.IsChecked = false;
        OptionsRow.Visibility = Visibility.Collapsed;
        PaletteHost.Visibility = Visibility.Collapsed;
    }

    public int MosaicStrength => (int)MosaicSlider.Value;

    /// <summary>
    /// Shows the options row; mosaic shows a blur-strength slider instead of size, color and opacity,
    /// text adds bold / background toggles, and shapes add fill / dash toggles.
    /// </summary>
    public void SetOptionsVisible(bool visible, bool mosaic = false, bool text = false, bool fill = false, bool dash = false)
    {
        OptionsRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        TextOptions.Visibility = text && !mosaic ? Visibility.Visible : Visibility.Collapsed;
        ShapeOptions.Visibility = fill || dash ? Visibility.Visible : Visibility.Collapsed;
        FillToggle.Visibility = fill ? Visibility.Visible : Visibility.Collapsed;
        DashToggle.Visibility = dash ? Visibility.Visible : Visibility.Collapsed;
        PaletteHost.Visibility = visible && !mosaic && PaletteToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetMosaicStrength(int strength)
    {
        _updatingSlider = true;
        MosaicSlider.Value = strength;
        MosaicValue.Text = strength.ToString();
        _updatingSlider = false;
    }

    private bool _updatingSlider;
    private bool _sliderGestureStarted;

    private void OnMosaicSliderPressed(object sender, MouseButtonEventArgs e) => _sliderGestureStarted = true;

    private void OnMosaicSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MosaicValue == null || _updatingSlider)
            return; // fired during InitializeComponent, or a programmatic update
        MosaicValue.Text = MosaicStrength.ToString();
        MosaicStrengthChanged?.Invoke(MosaicStrength, _sliderGestureStarted);
        _sliderGestureStarted = false;
    }

    /// <summary>Reflects an existing annotation's style in the size and color pickers.</summary>
    public void SetStyle(Color color, double size)
    {
        ShowColor(color);
        _palette.SetColor(color);
        if (_sizes.FirstOrDefault(b => double.Parse((string)b.Tag) == size) is { } sizeButton)
        {
            foreach (var b in _sizes)
                b.IsChecked = b == sizeButton;
        }
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        var clicked = (ToggleButton)sender;
        foreach (var button in _tools.Keys)
        {
            if (button != clicked)
                button.IsChecked = false;
        }
        // The overlay shows the options that fit the tool.
        ToolChanged?.Invoke(clicked.IsChecked == true ? _tools[clicked] : ToolKind.None);
    }

    private void OnSizeClick(object sender, RoutedEventArgs e)
    {
        foreach (var button in _sizes)
            button.IsChecked = button == sender;
        ThicknessChanged?.Invoke(SelectedSize);
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        foreach (var button in ColorButtons)
            button.IsChecked = button == sender;
        _palette.SetColor(SelectedColor);
        ColorChanged?.Invoke(SelectedColor);
    }

    private void OnUndo(object sender, RoutedEventArgs e) => UndoRequested?.Invoke();
    private void OnRedo(object sender, RoutedEventArgs e) => RedoRequested?.Invoke();
    private void OnPin(object sender, RoutedEventArgs e) => PinRequested?.Invoke();
    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested?.Invoke();
    private void OnCopy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void OnRecord(object sender, RoutedEventArgs e) => RecordRequested?.Invoke();
    private void OnOcr(object sender, RoutedEventArgs e) => OcrRequested?.Invoke();
    private void OnScrollCapture(object sender, RoutedEventArgs e) => ScrollCaptureRequested?.Invoke();
}
