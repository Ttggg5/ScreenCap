using System.Runtime.InteropServices;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Native;

internal static class CursorPainter
{
    /// <summary>
    /// Draws the current mouse cursor into a device context whose top-left corresponds to the
    /// screen point (<paramref name="originX"/>, <paramref name="originY"/>).
    /// </summary>
    public static void Draw(IntPtr dc, int originX, int originY)
    {
        var info = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref info) || (info.flags & CURSOR_SHOWING) == 0)
            return;
        if (!GetIconInfo(info.hCursor, out var icon))
            return;
        DeleteObject(icon.hbmMask);
        if (icon.hbmColor != IntPtr.Zero)
            DeleteObject(icon.hbmColor);

        DrawIconEx(dc, info.ptScreenPos.X - icon.xHotspot - originX, info.ptScreenPos.Y - icon.yHotspot - originY,
            info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
    }
}
