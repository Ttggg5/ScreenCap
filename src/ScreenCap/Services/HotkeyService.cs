using System.Windows.Input;
using System.Windows.Interop;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Services;

/// <summary>Registers system-wide hotkeys on a hidden message-only window.</summary>
internal sealed class HotkeyService : IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("ScreenCapHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HWND_MESSAGE,
        });
        _source.AddHook(WndProc);
    }

    /// <returns>false if the hotkey is already taken by another application. A disabled hotkey always succeeds.</returns>
    public bool Register(Hotkey hotkey, Action action)
    {
        if (hotkey.IsNone)
            return true;
        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (!RegisterHotKey(_source.Handle, id, (uint)hotkey.Modifiers | MOD_NOREPEAT, vk))
            return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (int id in _actions.Keys)
            UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
