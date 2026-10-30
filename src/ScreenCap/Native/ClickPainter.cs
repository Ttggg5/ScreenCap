using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Native;

/// <summary>
/// Marks mouse clicks in recordings: a ring around the cursor while a button is held (amber for left,
/// blue for right), which widens briefly after release so even short clicks show up.
/// </summary>
internal sealed class ClickPainter : IDisposable
{
    private const double RadiusDip = 16, ThicknessDip = 3;
    private const long LingerMs = 250;

    private readonly int _radius;
    private readonly IntPtr _leftPen, _rightPen;
    private bool _down, _right;
    private long? _releasedAt;

    /// <param name="scale">DPI scale of the recorded monitor.</param>
    public ClickPainter(double scale)
    {
        _radius = (int)Math.Round(RadiusDip * scale);
        int thickness = Math.Max(1, (int)Math.Round(ThicknessDip * scale));
        _leftPen = CreatePen(PS_SOLID, thickness, Rgb(0xFF, 0xB3, 0x00));
        _rightPen = CreatePen(PS_SOLID, thickness, Rgb(0x1E, 0x88, 0xE5));
    }

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | g << 8 | b << 16); // COLORREF is 0x00BBGGRR

    /// <summary>Draws the ring, if any, onto a bitmap whose top-left is at screen point (<paramref name="originX"/>, <paramref name="originY"/>).</summary>
    public void Draw(IntPtr hdc, int originX, int originY)
    {
        bool swapped = GetSystemMetrics(SM_SWAPBUTTON) != 0;
        // The low bit catches a click that started and ended between two frames.
        bool primary = (GetAsyncKeyState(swapped ? VK_RBUTTON : VK_LBUTTON) & 0x8001) != 0;
        bool secondary = (GetAsyncKeyState(swapped ? VK_LBUTTON : VK_RBUTTON) & 0x8001) != 0;
        if (GetCursorPos(out var p))
            Draw(hdc, p.X - originX, p.Y - originY, primary, secondary, Environment.TickCount64);
    }

    /// <summary>Draws for the given button state and cursor position (relative to the bitmap) at time <paramref name="now"/> (ms).</summary>
    internal void Draw(IntPtr hdc, int x, int y, bool primary, bool secondary, long now)
    {
        int radius;
        if (primary || secondary)
        {
            _down = true;
            _right = secondary && !primary;
            radius = _radius;
        }
        else
        {
            if (_down)
            {
                _down = false;
                _releasedAt = now;
            }
            if (_releasedAt is not { } released || now - released > LingerMs)
                return;
            long since = now - released;
            radius = (int)(_radius * (1 + 0.6 * since / LingerMs));
        }

        var oldPen = SelectObject(hdc, _right ? _rightPen : _leftPen);
        var oldBrush = SelectObject(hdc, GetStockObject(NULL_BRUSH));
        Ellipse(hdc, x - radius, y - radius, x + radius, y + radius);
        SelectObject(hdc, oldBrush);
        SelectObject(hdc, oldPen);
    }

    public void Dispose()
    {
        DeleteObject(_leftPen);
        DeleteObject(_rightPen);
    }
}
