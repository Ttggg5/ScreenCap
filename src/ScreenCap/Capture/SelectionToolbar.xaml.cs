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
    Text,
    Mosaic,
}

internal partial class SelectionToolbar : UserControl
{
    private readonly Dictionary<ToggleButton, ToolKind> _tools;
    private readonly ToggleButton[] _sizes;

    public SelectionToolbar()
    {
        InitializeComponent();
        _tools = new()
        {
            [RectangleTool] = ToolKind.Rectangle,
            [EllipseTool] = ToolKind.Ellipse,
            [ArrowTool] = ToolKind.Arrow,
            [PenTool] = ToolKind.Pen,
            [TextTool] = ToolKind.Text,
            [MosaicTool] = ToolKind.Mosaic,
        };
        _sizes = [SizeSmall, SizeMedium, SizeLarge];
        SizeMedium.IsChecked = true;
        ((ToggleButton)Swatches.Children[0]).IsChecked = true;
        SetMosaicStrength(Annotations.MosaicAnnotation.DefaultStrength);
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

    public Color SelectedColor => ((SolidColorBrush)Swatches.Children.OfType<ToggleButton>().First(b => b.IsChecked == true).Background).Color;

    public double SelectedSize => double.Parse((string)_sizes.First(b => b.IsChecked == true).Tag);

    public void ClearTool()
    {
        foreach (var button in _tools.Keys)
            button.IsChecked = false;
        OptionsRow.Visibility = Visibility.Collapsed;
    }

    public int MosaicStrength => (int)MosaicSlider.Value;

    /// <summary>Shows the options row; mosaic shows a blur-strength slider instead of size and color.</summary>
    public void SetOptionsVisible(bool visible, bool mosaic = false)
    {
        OptionsRow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MosaicOptions.Visibility = mosaic ? Visibility.Visible : Visibility.Collapsed;
        StrokeOptions.Visibility = mosaic ? Visibility.Collapsed : Visibility.Visible;
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
        var swatches = Swatches.Children.OfType<ToggleButton>().ToList();
        if (swatches.FirstOrDefault(b => ((SolidColorBrush)b.Background).Color == color) is { } swatch)
        {
            foreach (var b in swatches)
                b.IsChecked = b == swatch;
        }
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
        SetOptionsVisible(tool != ToolKind.None, tool == ToolKind.Mosaic);
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
        foreach (var button in Swatches.Children.OfType<ToggleButton>())
            button.IsChecked = button == sender;
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
