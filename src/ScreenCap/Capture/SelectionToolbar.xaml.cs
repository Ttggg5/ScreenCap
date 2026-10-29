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

    /// <summary>Editing an existing pin: recording makes no sense there.</summary>
    public void SetEditPinMode() => RecordButton.Visibility = Visibility.Collapsed;

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
    /// Shows the options row; mosaic shows a blur-strength slider instead of size and color,
    /// and text adds bold / background toggles.
    /// </summary>
    public void SetOptionsVisible(bool visible, bool mosaic = false, bool text = false)
    {
        OptionsRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
        TextOptions.Visibility = text && !mosaic ? Visibility.Visible : Visibility.Collapsed;
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
        var tool = clicked.IsChecked == true ? _tools[clicked] : ToolKind.None;
        SetOptionsVisible(tool != ToolKind.None, tool == ToolKind.Mosaic, tool == ToolKind.Text);
        ToolChanged?.Invoke(tool);
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
}
