using System.Runtime.InteropServices;
using System.Windows.Input;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Native;

/// <summary>
/// Watches the keyboard during a recording (low-level hook, so it sees keys sent to any app) and keeps
/// the latest key combination, e.g. "Ctrl + C", for <see cref="KeyPainter"/> to draw into the frames.
/// Must be created on a thread with a message loop (the UI thread).
/// </summary>
internal sealed class KeystrokeMonitor : IDisposable
{
    private readonly LowLevelKeyboardProc _proc; // kept alive while hooked
    private readonly IntPtr _hook;
    private readonly Lock _lock = new();
    private string? _text;
    private long _time;

    public KeystrokeMonitor()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    /// <summary>The latest combination and when it was pressed (<see cref="Environment.TickCount64"/>).</summary>
    public (string? Text, long Time) Latest
    {
        get
        {
            lock (_lock)
                return (_text, _time);
        }
    }

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && (int)message is WM_KEYDOWN or WM_SYSKEYDOWN)
        {
            int vk = Marshal.ReadInt32(data); // KBDLLHOOKSTRUCT.vkCode
            if (Describe(KeyInterop.KeyFromVirtualKey(vk), CurrentModifiers()) is { } text)
            {
                lock (_lock)
                {
                    _text = text;
                    _time = Environment.TickCount64;
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private static ModifierKeys CurrentModifiers()
    {
        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var modifiers = ModifierKeys.None;
        if (Down(VK_CONTROL)) modifiers |= ModifierKeys.Control;
        if (Down(VK_MENU)) modifiers |= ModifierKeys.Alt;
        if (Down(VK_SHIFT)) modifiers |= ModifierKeys.Shift;
        if (Down(VK_LWIN) || Down(VK_RWIN)) modifiers |= ModifierKeys.Windows;
        return modifiers;
    }

    /// <summary>
    /// "Ctrl + Shift + S" style text for a key press; null for a modifier on its own, which only shows
    /// as part of the next combination.
    /// </summary>
    internal static string? Describe(Key key, ModifierKeys modifiers)
    {
        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return null;
        string name = key switch
        {
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Capital => "CapsLock",
            _ => new Hotkey(ModifierKeys.None, key).ToString(),
        };
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        return string.Join(" + ", parts);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWindowsHookEx(_hook);
    }
}

/// <summary>Draws the latest key combination as a dark label at the bottom center of a recording frame.</summary>
internal sealed class KeyPainter : IDisposable
{
    private const long ShowMs = 1500;
    private const double FontDip = 18, PaddingDip = 10, MarginDip = 24;

    private readonly double _scale;
    private readonly IntPtr _font, _brush;

    public KeyPainter(double scale)
    {
        _scale = scale;
        _font = CreateFont(-(int)Math.Round(FontDip * scale), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, "Microsoft JhengHei UI");
        _brush = CreateSolidBrush(0x00202020);
    }

    /// <summary>Draws <paramref name="text"/> if it was pressed within the last 1.5 s.</summary>
    public void Draw(IntPtr hdc, int width, int height, string? text, long pressedAt, long now)
    {
        if (text == null || now - pressedAt > ShowMs)
            return;
        var oldFont = SelectObject(hdc, _font);
        GetTextExtentPoint32(hdc, text, text.Length, out var size);
        int pad = (int)Math.Round(PaddingDip * _scale), margin = (int)Math.Round(MarginDip * _scale);
        int boxW = size.cx + 2 * pad, boxH = size.cy + pad;
        int left = (width - boxW) / 2, top = height - margin - boxH;
        if (top < 0)
            top = Math.Max(0, height - boxH); // tiny region: hug the bottom

        var oldBrush = SelectObject(hdc, _brush);
        var oldPen = SelectObject(hdc, GetStockObject(NULL_PEN));
        int radius = (int)Math.Round(8 * _scale);
        RoundRect(hdc, left, top, left + boxW, top + boxH, radius, radius);
        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);

        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, 0x00FFFFFF);
        TextOut(hdc, left + pad, top + pad / 2, text, text.Length);
        SelectObject(hdc, oldFont);
    }

    public void Dispose()
    {
        DeleteObject(_font);
        DeleteObject(_brush);
    }
}
