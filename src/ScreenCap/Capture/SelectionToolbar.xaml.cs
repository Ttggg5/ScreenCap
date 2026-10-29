using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
    }

    public event Action<ToolKind>? ToolChanged;
    public event Action<Color>? ColorChanged;
    public event Action<double>? ThicknessChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? PinRequested;
    public event Action? SaveRequested;
    public event Action? CopyRequested;
    public event Action? CancelRequested;

    public Color SelectedColor => ((SolidColorBrush)Swatches.Children.OfType<ToggleButton>().First(b => b.IsChecked == true).Background).Color;

    public double SelectedSize => double.Parse((string)_sizes.First(b => b.IsChecked == true).Tag);

    public void ClearTool()
    {
        foreach (var button in _tools.Keys)
            button.IsChecked = false;
        OptionsRow.Visibility = Visibility.Collapsed;
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
        OptionsRow.Visibility = tool == ToolKind.None ? Visibility.Collapsed : Visibility.Visible;
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
}
