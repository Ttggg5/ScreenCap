using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScreenCap.Theme;

/// <summary>
/// A message box in the app's light or dark theme, used like <see cref="MessageBox.Show(string)"/>;
/// the system one is always light. The title is always "ScreenCap".
/// </summary>
internal sealed class Dialog : Window
{
    private MessageBoxResult _result;

    private Dialog(string text, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult)
    {
        Title = "ScreenCap";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        ThemeManager.Attach(this);

        var choices = Choices(buttons);
        // Closing the window (or Esc) is the safe answer: cancel, or no, or the only button there is.
        _result = choices.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : choices.Contains(MessageBoxResult.No) ? MessageBoxResult.No
            : choices[0];
        if (!choices.Contains(defaultResult))
            defaultResult = choices[0];

        var message = new DockPanel { Margin = new Thickness(24, 22, 24, 22) };
        if (Glyph(image) is { } found)
        {
            var icon = new TextBlock
            {
                Text = found.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 28,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Top,
            }.Themed(TextBlock.ForegroundProperty, found.Brush);
            DockPanel.SetDock(icon, Dock.Left);
            message.Children.Add(icon);
        }
        message.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 240,
            MaxWidth = 420,
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "Brush.Text"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 16, 24, 16) };
        foreach (var choice in choices)
        {
            var button = new Button { Content = Label(choice), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            if (choice == defaultResult)
            {
                button.IsDefault = true;
                button.Themed(StyleProperty, "AccentButton");
                Loaded += (_, _) => button.Focus();
            }
            if (choice == _result)
                button.IsCancel = true;
            button.Click += (_, _) =>
            {
                _result = choice;
                Close();
            };
            row.Children.Add(button);
        }
        // As in Windows 11 dialogs: the message on the lighter surface, the buttons on the window color below it.
        var footer = new Border { Child = row, BorderThickness = new Thickness(0, 1, 0, 0) }
            .Themed(Border.BorderBrushProperty, "Brush.Divider");
        var body = new Border { Child = message }.Themed(Border.BackgroundProperty, "Brush.Surface");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
    }

    /// <summary>Shows the message and waits for an answer.</summary>
    /// <param name="owner">The window it belongs to, kept above; null for a window of its own.</param>
    /// <param name="defaultResult">The button Enter presses; the first one if not given.</param>
    public static MessageBoxResult Show(Window? owner, string text, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var dialog = new Dialog(text, buttons, image, defaultResult);
        if (owner is { IsVisible: true } && owner.WindowStyle != WindowStyle.None)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // Over the full-screen capture overlay, or with no window at all: on its own, and above pins.
            if (owner is { IsVisible: true })
                dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
            dialog.ShowInTaskbar = owner == null;
        }
        dialog.ShowDialog();
        return dialog._result;
    }

    public static MessageBoxResult Show(string text, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(null, text, buttons, image, defaultResult);

    internal static MessageBoxResult[] Choices(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
        MessageBoxButton.YesNo => [MessageBoxResult.Yes, MessageBoxResult.No],
        MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
        _ => [MessageBoxResult.OK],
    };

    private static string Label(MessageBoxResult choice) => choice switch
    {
        MessageBoxResult.Cancel => "取消",
        MessageBoxResult.Yes => "是",
        MessageBoxResult.No => "否",
        _ => "確定",
    };

    /// <returns>The Segoe Fluent Icons glyph and the brush to draw it with; null for no icon.</returns>
    private static (string Glyph, string Brush)? Glyph(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => ("", "Brush.Danger"),        // ErrorBadge
        MessageBoxImage.Warning => ("", "Brush.Warning"),     // Warning
        MessageBoxImage.Question => ("", "Brush.Accent"),     // Unknown (a question mark in a circle)
        MessageBoxImage.Information => ("", "Brush.Accent"),  // Info
        _ => null,
    };
}
