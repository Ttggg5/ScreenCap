using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Recording;
using ScreenCap.Services;

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
    Spotlight,
}

/// <summary>Range of the size slider for one tool, in DIPs.</summary>
/// <param name="Label">What the slider adjusts: stroke width, or the size of text and markers.</param>
internal readonly record struct SizeRange(double Min, double Max, double Default, string Label)
{
    public static SizeRange For(ToolKind tool) => tool switch
    {
        ToolKind.Highlighter => new(4, 40, 16, "粗細"),
        ToolKind.Number => new(16, 64, 32, "大小"),   // circle diameter
        ToolKind.Text => new(8, 96, 20, "大小"),      // font size
        _ => new(1, 20, 4, "粗細"),                  // stroke width
    };
}

internal partial class SelectionToolbar : UserControl
{
    private readonly Dictionary<ToggleButton, ToolKind> _tools;
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
            [SpotlightTool] = ToolKind.Spotlight,
        };
        WireSliders(new()
        {
            [MosaicSlider] = () => _sliderGestureStarted = true,
            [OpacitySlider] = () => _opacityGestureStarted = true,
            [SizeSlider] = () => _sizeGestureStarted = true,
            [DimSlider] = () => _dimGestureStarted = true,
        });
        // Typed values apply like a slider change; each is its own undo step.
        LinkNumber(MosaicValue, MosaicSlider, () => _sliderGestureStarted = true);
        LinkNumber(OpacityValue, OpacitySlider, () => _opacityGestureStarted = true);
        LinkNumber(SizeValue, SizeSlider, () => _sizeGestureStarted = true);
        LinkNumber(DimValue, DimSlider, () => _dimGestureStarted = true);
        SetSize(ToolKind.Pen, SizeRange.For(ToolKind.Pen).Default);
        SetSpotlightDim(Annotations.SpotlightAnnotation.DefaultDim);
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
        SetTextStyle(TextStyle.Default with { FontName = AppSettings.TextFont });
    }

    /// <summary>
    /// Each press on a slider starts a new undo step, and a press on the track jumps there and keeps dragging.
    /// </summary>
    private void WireSliders(Dictionary<Slider, Action> pressed)
    {
        // Preview events tunnel down from the toolbar, so this runs before the slider jumps to the click.
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            for (var element = e.OriginalSource as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
            {
                if (element is Slider slider && pressed.TryGetValue(slider, out var markGesture))
                {
                    markGesture();
                    return;
                }
            }
        }), handledEventsToo: true);

        // IsMoveToPointEnabled only moves the value; hand the press on to the thumb so the drag continues.
        foreach (var slider in pressed.Keys)
        {
            slider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                if (slider.Template.FindName("PART_Track", slider) is not Track { Thumb: { } thumb } || thumb.IsMouseOver)
                    return;
                slider.UpdateLayout(); // put the thumb under the cursor first
                thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left)
                {
                    RoutedEvent = MouseLeftButtonDownEvent,
                    Source = thumb,
                });
            }), handledEventsToo: true);
        }
    }

    /// <summary>Raised when typing in a number box ends with Enter or Esc.</summary>
    public event Action? NumberEditingFinished;

    private void LinkNumber(NumberBox box, Slider slider, Action newGesture)
    {
        box.ValueCommitted += value =>
        {
            newGesture();
            slider.Value = value; // raises the slider's ValueChanged, which updates the annotation
        };
        box.EditingFinished += () => NumberEditingFinished?.Invoke();
    }

    public event Action<ToolKind>? ToolChanged;
    public event Action<Color>? ColorChanged;
    /// <summary>Raised with the new size and whether this is the first change of a new slider gesture.</summary>
    public event Action<double, bool>? ThicknessChanged;

    /// <summary>Raised with the new dim level in percent and whether this is the first change of a new slider gesture.</summary>
    public event Action<int, bool>? SpotlightDimChanged;

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

    /// <summary>Raised when the font or any text style toggle changes.</summary>
    public event Action<TextStyle>? TextStyleChanged;

    public TextStyle CurrentTextStyle => new(
        BoldToggle.IsChecked == true, BackgroundToggle.IsChecked == true,
        ItalicToggle.IsChecked == true, UnderlineToggle.IsChecked == true,
        (FontBox.SelectedItem as ComboBoxItem)?.Tag as string ?? TextStyle.DefaultFontName);

    private bool _updatingFont;

    public void SetTextStyle(TextStyle style)
    {
        BoldToggle.IsChecked = style.Bold;
        BackgroundToggle.IsChecked = style.Background;
        ItalicToggle.IsChecked = style.Italic;
        UnderlineToggle.IsChecked = style.Underline;
        _updatingFont = true;
        FontBox.SelectedItem = FontItem(style.FontName);
        _updatingFont = false;
    }

    private void OnTextStyleClick(object sender, RoutedEventArgs e) => TextStyleChanged?.Invoke(CurrentTextStyle);

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFont || FontBox.SelectedItem == null)
            return;
        AppSettings.TextFont = CurrentTextStyle.FontName; // remembered for the next capture
        TextStyleChanged?.Invoke(CurrentTextStyle);
    }

    /// <summary>The item for a font, added if the full list hasn't been loaded yet.</summary>
    private ComboBoxItem FontItem(string name)
    {
        var item = FontBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == name);
        if (item == null)
        {
            item = CreateFontItem(name, name);
            FontBox.Items.Add(item);
        }
        return item;
    }

    private static ComboBoxItem CreateFontItem(string name, string displayName) => new()
    {
        Content = displayName,
        Tag = name,
        FontFamily = TextStyle.FamilyFor(name), // preview in the font itself
        FontSize = 13,
    };

    private bool _fontsLoaded;

    /// <summary>Lists the installed fonts on first use; loading them for every capture would slow the overlay down.</summary>
    private void OnFontDropDownOpened(object? sender, EventArgs e)
    {
        if (_fontsLoaded)
            return;
        _fontsLoaded = true;
        var language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
        var fonts = Fonts.SystemFontFamilies
            .Select(f => (Name: f.Source, Display: f.FamilyNames.TryGetValue(language, out var local) ? local : f.Source))
            .DistinctBy(f => f.Name)
            .OrderBy(f => f.Display, StringComparer.CurrentCulture)
            .ToList();
        string current = CurrentTextStyle.FontName;
        _updatingFont = true;
        FontBox.Items.Clear();
        foreach (var (name, display) in fonts)
            FontBox.Items.Add(CreateFontItem(name, display));
        FontBox.SelectedItem = FontItem(current);
        _updatingFont = false;
    }

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
        OpacityValue.Value = opacity;
        _updatingSlider = false;
    }

    private void OnShapeStyleClick(object sender, RoutedEventArgs e) => ShapeStyleChanged?.Invoke(ShapeFilled, ShapeDashed);

    private bool _opacityGestureStarted;

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue == null || _updatingSlider)
            return; // fired during InitializeComponent, or a programmatic update
        OpacityValue.Value = OpacityPercent;
        OpacityChanged?.Invoke(OpacityPercent, _opacityGestureStarted);
        _opacityGestureStarted = false;
    }

    /// <summary>Shows which button Enter triggers.</summary>
    public void SetConfirmAction(OverlayAction action)
    {
        CopyButton.ToolTip = action == OverlayAction.Copy ? "複製到剪貼簿 (Enter / Ctrl+C)" : "複製到剪貼簿 (Ctrl+C)";
        SaveButton.ToolTip = action == OverlayAction.Save ? "另存新檔 (Enter / Ctrl+S)" : "另存新檔 (Ctrl+S)";
        PinButton.ToolTip = action == OverlayAction.Pin ? "貼到桌面 (Enter / Ctrl+T)" : "貼到桌面 (Ctrl+T)";
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

    public double SelectedSize => SizeSlider.Value;

    /// <summary>Shows the size slider with the range and value for <paramref name="tool"/>.</summary>
    public void SetSize(ToolKind tool, double size)
    {
        var range = SizeRange.For(tool);
        _updatingSlider = true;
        SizeSlider.Minimum = range.Min;
        SizeSlider.Maximum = range.Max;
        SizeSlider.Value = Math.Clamp(size, range.Min, range.Max);
        SizeLabel.Text = range.Label;
        SizeValue.Minimum = range.Min;
        SizeValue.Maximum = range.Max;
        SizeValue.Value = SizeSlider.Value;
        _updatingSlider = false;
    }

    private bool _sizeGestureStarted;

    private void OnSizeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SizeValue == null || _updatingSlider)
            return; // fired during InitializeComponent, or a programmatic update
        SizeValue.Value = SelectedSize;
        ThicknessChanged?.Invoke(SelectedSize, _sizeGestureStarted);
        _sizeGestureStarted = false;
    }

    public int SpotlightDim => (int)DimSlider.Value;

    public void SetSpotlightDim(int percent)
    {
        _updatingSlider = true;
        DimSlider.Value = percent;
        DimValue.Value = percent;
        _updatingSlider = false;
    }

    private bool _dimGestureStarted;

    private void OnDimSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DimValue == null || _updatingSlider)
            return;
        DimValue.Value = SpotlightDim;
        SpotlightDimChanged?.Invoke(SpotlightDim, _dimGestureStarted);
        _dimGestureStarted = false;
    }

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
    public void SetOptionsVisible(bool visible, bool mosaic = false, bool text = false, bool fill = false, bool dash = false, bool spotlight = false)
    {
        OptionsRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        SpotlightOptions.Visibility = spotlight ? Visibility.Visible : Visibility.Collapsed;
        mosaic |= spotlight; // neither has size, color or opacity
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
        MosaicValue.Value = strength;
        _updatingSlider = false;
    }

    private bool _updatingSlider;
    private bool _sliderGestureStarted;

    private void OnMosaicSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MosaicValue == null || _updatingSlider)
            return; // fired during InitializeComponent, or a programmatic update
        MosaicValue.Value = MosaicStrength;
        MosaicStrengthChanged?.Invoke(MosaicStrength, _sliderGestureStarted);
        _sliderGestureStarted = false;
    }

    /// <summary>Reflects an existing annotation's color in the color pickers.</summary>
    public void SetColor(Color color)
    {
        ShowColor(color);
        _palette.SetColor(color);
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
