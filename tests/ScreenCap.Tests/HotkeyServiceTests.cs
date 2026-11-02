using System.Runtime.InteropServices;
using System.Windows.Input;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class HotkeyServiceTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_HOTKEY = 0x0312;

    [Fact]
    public void StillWorks_AfterAFullCollection() => Sta.Run(() =>
    {
        using var hotkeys = new HotkeyService();
        int pressed = 0;
        // Unlikely to be taken by another app.
        Assert.True(hotkeys.Register(new Hotkey(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.F24), () => pressed++));

        MemoryRelease.Collect(); // what runs after every capture

        SendMessage(hotkeys.Handle, WM_HOTKEY, 1, 0); // the first registered id
        Assert.Equal(1, pressed);
    });
}
