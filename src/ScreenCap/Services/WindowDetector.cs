using System.Runtime.InteropServices;
using System.Windows;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Services;

/// <summary>
/// Snapshot of top-level windows (in Z-order) taken before the overlay is shown,
/// used to auto-highlight the window or child control under the cursor.
/// All rectangles are relative to the virtual screen origin.
/// </summary>
internal sealed class WindowDetector
{
    private readonly List<(IntPtr Hwnd, Rect Bounds)> _windows = new();
    private readonly Dictionary<IntPtr, List<Rect>> _children = new();
    private readonly int _originX, _originY;
    private readonly Rect _screen;

    private WindowDetector(ScreenSnapshot snap)
    {
        _originX = snap.Left;
        _originY = snap.Top;
        _screen = new Rect(0, 0, snap.Width, snap.Height);
    }

    public static WindowDetector Snapshot(ScreenSnapshot snap)
    {
        var detector = new WindowDetector(snap);
        EnumWindows((hwnd, _) =>
        {
            if (IsCandidate(hwnd) && detector.TryGetBounds(hwnd, out var bounds))
                detector._windows.Add((hwnd, bounds));
            return true;
        }, IntPtr.Zero);
        return detector;
    }

    /// <summary>Returns the smallest window/control rectangle under the given point.</summary>
    public Rect? HitTest(int x, int y)
    {
        var p = new Point(x + 0.5, y + 0.5);
        foreach (var (hwnd, bounds) in _windows)
        {
            if (!bounds.Contains(p))
                continue;

            var best = bounds;
            foreach (var child in GetChildren(hwnd, bounds))
            {
                if (child.Contains(p) && child.Width * child.Height < best.Width * best.Height)
                    best = child;
            }
            best.Intersect(_screen);
            return best.IsEmpty || best.Width < 1 || best.Height < 1 ? null : best;
        }
        return null;
    }

    /// <summary>Title of the topmost titled window under the center of <paramref name="selection"/>, for file names.</summary>
    public string? TitleAt(Rect selection)
    {
        var center = new Point(selection.X + selection.Width / 2, selection.Y + selection.Height / 2);
        foreach (var (hwnd, bounds) in _windows)
        {
            if (!bounds.Contains(center))
                continue;
            int length = GetWindowTextLength(hwnd);
            if (length == 0)
                continue; // untitled popups, toolbars, …
            var buffer = new char[length + 1];
            string title = new string(buffer, 0, GetWindowText(hwnd, buffer, buffer.Length));
            if (title.Length > 0)
                return title == "Program Manager" ? null : title; // the desktop
        }
        return null;
    }

    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            return false;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0)
            return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        return true;
    }

    private bool TryGetBounds(IntPtr hwnd, out Rect bounds)
    {
        // Extended frame bounds exclude the invisible resize borders of Win10/11 windows.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0
            && !GetWindowRect(hwnd, out r))
        {
            bounds = Rect.Empty;
            return false;
        }
        return TryToRelative(r, out bounds);
    }

    private bool TryToRelative(RECT r, out Rect rect)
    {
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0)
        {
            rect = Rect.Empty;
            return false;
        }
        rect = new Rect(r.Left - _originX, r.Top - _originY, w, h);
        return true;
    }

    private List<Rect> GetChildren(IntPtr hwnd, Rect parentBounds)
    {
        if (_children.TryGetValue(hwnd, out var list))
            return list;

        list = new List<Rect>();
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (IsWindowVisible(child) && GetWindowRect(child, out var r) && TryToRelative(r, out var rect))
            {
                rect.Intersect(parentBounds);
                if (!rect.IsEmpty && rect.Width >= 4 && rect.Height >= 4)
                    list.Add(rect);
            }
            return true;
        }, IntPtr.Zero);
        _children[hwnd] = list;
        return list;
    }
}
