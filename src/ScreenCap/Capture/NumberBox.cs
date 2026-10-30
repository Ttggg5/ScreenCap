using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScreenCap.Capture;

/// <summary>
/// Editable number next to a slider: type a value and press Enter (or click elsewhere) to apply it,
/// Esc to undo the typing, ↑ / ↓ to step. Values are clamped to the slider's range.
/// </summary>
internal sealed class NumberBox : TextBox
{
    private double _value;

    public NumberBox()
    {
        // Implicit styles only match the exact type, so pick up the themed TextBox style explicitly.
        SetResourceReference(StyleProperty, typeof(TextBox));
        Width = 42;
        Height = 22;
        Padding = new Thickness(2, 1, 4, 1);
        FontFamily = new FontFamily("Consolas");
        FontSize = 12;
        TextAlignment = TextAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        InputMethod.SetIsInputMethodEnabled(this, false); // digits only; no IME composition
        ToolTip = "可直接輸入數值，按 Enter 套用";
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Step { get; set; } = 1;

    /// <summary>Shown after the number, e.g. "%".</summary>
    public string Suffix { get; set; } = "";

    /// <summary>Raised when the user applies a typed or stepped value.</summary>
    public event Action<double>? ValueCommitted;

    /// <summary>Raised when editing ends with Enter or Esc, so the owner can take keyboard focus back.</summary>
    public event Action? EditingFinished;

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = Format(value);
        }
    }

    private string Format(double value) => value.ToString("0", CultureInfo.InvariantCulture) + Suffix;

    /// <summary>Parses the typed text; the suffix is optional.</summary>
    internal static double? Parse(string text, string suffix)
    {
        string s = text.Trim();
        if (suffix.Length > 0 && s.EndsWith(suffix, StringComparison.Ordinal))
            s = s[..^suffix.Length].TrimEnd();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;
    }

    private void Commit()
    {
        if (Parse(Text, Suffix) is not { } typed)
        {
            Text = Format(_value); // not a number: show the current value again
            return;
        }
        double value = Math.Clamp(Math.Round(typed / Step) * Step, Minimum, Maximum);
        bool changed = value != _value;
        Value = value;
        if (changed)
            ValueCommitted?.Invoke(value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = _value.ToString("0", CultureInfo.InvariantCulture); // edit the bare number
        Dispatcher.BeginInvoke(SelectAll);
    }

    // The first click selects the whole number so typing replaces it; letting the TextBox handle that
    // click would place the caret there instead. Later clicks position the caret as usual.
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (IsKeyboardFocusWithin)
            return;
        Focus();
        SelectAll();
        e.Handled = true;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Commit();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        // Only digits (and a decimal point); anything else would just be rejected on commit.
        e.Handled = !e.Text.All(c => char.IsAsciiDigit(c) || c == '.');
        base.OnPreviewTextInput(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                EditingFinished?.Invoke();
                e.Handled = true;
                break;
            case Key.Escape:
                Text = Format(_value); // undo the typing; losing focus then commits the unchanged value
                EditingFinished?.Invoke();
                e.Handled = true;
                break;
            case Key.Up or Key.Down:
                Commit();
                double stepped = Math.Clamp(_value + (e.Key == Key.Up ? Step : -Step), Minimum, Maximum);
                if (stepped != _value)
                {
                    Value = stepped;
                    ValueCommitted?.Invoke(stepped);
                }
                Text = _value.ToString("0", CultureInfo.InvariantCulture);
                SelectAll();
                e.Handled = true;
                break;
        }
    }
}
