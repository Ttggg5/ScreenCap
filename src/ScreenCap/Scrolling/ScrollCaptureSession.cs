using System.Windows;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Services;
using static ScreenCap.Native.NativeMethods;

namespace ScreenCap.Scrolling;

/// <summary>
/// Scrolling capture: scrolls the region with the mouse wheel, captures it once the content has
/// settled, and stitches the frames until the end is reached or the user presses Done.
/// </summary>
internal static class ScrollCaptureSession
{
    private const int MaxHeight = 30000;      // px; keeps memory and the result image reasonable
    private const double StepFraction = 0.6;  // aim to scroll this much of the region per step
    private const int MaxNotches = 10;

    private static bool _stop, _cancel;

    public static bool IsActive { get; private set; }

    /// <param name="title">Title of the captured window, for the file name.</param>
    public static async void Start(Int32Rect region, string? title = null)
    {
        if (IsActive)
            return;
        if (region.Width < 32 || region.Height < 80)
        {
            Notifier.Show("長截圖的範圍太小，請選取至少 32 × 80 像素的捲動區域。");
            return;
        }
        IsActive = true;
        _stop = _cancel = false;

        var frameWindow = new RecordingFrameWindow(region, Color.FromRgb(0x1E, 0x88, 0xE5));
        var bar = new ScrollControlWindow(region);
        bar.DoneRequested += () => _stop = true;
        bar.CancelRequested += () => _stop = _cancel = true;
        GetCursorPos(out var savedCursor);
        string? notice = null;
        ScrollStitcher? stitcher = null;
        try
        {
            frameWindow.Show();
            bar.Show();
            await Task.Delay(200); // let the selection overlay disappear

            stitcher = new ScrollStitcher(region.Width, region.Height, await CaptureSettled(region));
            int notches = 1, stillFrames = 0;
            double pixelsPerNotch = 0;
            while (!_stop)
            {
                Scroll(region, notches);
                var frame = await CaptureSettled(region);
                if (_stop)
                    break;

                var outcome = stitcher.Add(frame, (int)(pixelsPerNotch * notches));
                if (outcome == StitchOutcome.NoChange)
                {
                    // Nothing moved: the end of the content (wait one more step in case it is still loading).
                    if (++stillFrames >= 2)
                        break;
                    continue;
                }
                stillFrames = 0;
                if (outcome == StitchOutcome.Lost)
                {
                    notice = "畫面對不上，已在目前位置停止（捲動時內容可能有變動）。";
                    break;
                }

                pixelsPerNotch = (double)stitcher.LastOffset / notches;
                notches = Math.Clamp((int)(region.Height * StepFraction / pixelsPerNotch), 1, MaxNotches);
                bar.SetStatus($"長截圖中… 已擷取 {stitcher.Height:N0} px");
                if (stitcher.Height >= MaxHeight)
                {
                    notice = $"已達高度上限 {MaxHeight:N0} px。";
                    break;
                }
            }
        }
        finally
        {
            frameWindow.Close();
            bar.Close();
            SetCursorPos(savedCursor.X, savedCursor.Y);
            IsActive = false;
        }

        if (_cancel || stitcher == null)
            return;
        if (notice != null)
            Notifier.Show(notice);
        ScrollResultWindow.ShowFor(stitcher.ToBitmap(), title);
    }

    /// <summary>Wheel-scrolls the window under the center of the region.</summary>
    private static void Scroll(Int32Rect region, int notches)
    {
        SetCursorPos(region.X + region.Width / 2, region.Y + region.Height / 2);
        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, -WHEEL_DELTA * notches, IntPtr.Zero);
    }

    /// <summary>Captures until two consecutive frames are identical, so smooth scrolling has finished.</summary>
    private static async Task<byte[]> CaptureSettled(Int32Rect region)
    {
        var previous = ScreenCaptureService.CaptureRegion(region);
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(80);
            var current = ScreenCaptureService.CaptureRegion(region);
            if (current.AsSpan().SequenceEqual(previous))
                return current;
            previous = current;
        }
        return previous; // still animating (e.g. a video): use the latest
    }
}
