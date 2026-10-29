using System.Windows.Input;
using ScreenCap.Services;

namespace ScreenCap.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData(ModifierKeys.None, Key.F1, "F1")]
    [InlineData(ModifierKeys.Shift, Key.F1, "Shift+F1")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Shift, Key.D1, "Ctrl+Shift+1")]
    [InlineData(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, Key.A, "Ctrl+Alt+Shift+Win+A")]
    [InlineData(ModifierKeys.Alt, Key.Snapshot, "Alt+PrintScreen")]
    [InlineData(ModifierKeys.Control, Key.OemComma, "Ctrl+,")]
    [InlineData(ModifierKeys.None, Key.NumPad5, "Num 5")]
    public void ToString_UsesReadableNames(ModifierKeys modifiers, Key key, string expected) =>
        Assert.Equal(expected, new Hotkey(modifiers, key).ToString());

    [Fact]
    public void None_IsDisabled()
    {
        Assert.True(default(Hotkey).IsNone);
        Assert.Equal("（無）", default(Hotkey).ToString());
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var hotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F12);
        Assert.Equal(hotkey, Hotkey.Deserialize(hotkey.Serialize()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F1")]
    [InlineData("1,2,3")]
    [InlineData("a,b")]
    public void Deserialize_RejectsInvalidValues(string? value) => Assert.Null(Hotkey.Deserialize(value));
}
