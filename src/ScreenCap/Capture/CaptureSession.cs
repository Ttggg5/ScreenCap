using System.Windows;
using System.Windows.Media.Imaging;
using ScreenCap.Ocr;
using ScreenCap.Pin;
using ScreenCap.Recording;
using ScreenCap.Scrolling;
using ScreenCap.Services;

namespace ScreenCap.Capture;

/// <summary>Coordinates one capture: freeze the screen, show the overlay, then act on the result.</summary>
internal static class CaptureSession
{
    private const int MaxHistory = 30;
    private static readonly List<Int32Rect> _history = new();

    public static bool IsActive { get; private set; }

    /// <summary>Earlier capture regions in screen pixels, oldest first.</summary>
    public static IReadOnlyList<Int32Rect> History => _history;

    public static void AddHistory(Int32Rect region)
    {
        _history.Remove(region); // re-capturing a region moves it to the end
        _history.Add(region);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }

    private static bool _countingDown;

    /// <summary>Takes a screenshot after a countdown, e.g. to capture an open menu or tooltip.</summary>
    public static void StartDelayed(int seconds)
    {
        if (IsActive || RecordingSession.IsActive || ScrollCaptureSession.IsActive || _countingDown)
            return;
        _countingDown = true;
        var countdown = CountdownWindow.Start(seconds, () => Start());
        countdown.Closed += (_, _) => _countingDown = false; // finished or cancelled by a click
    }

    /// <param name="recordMode">Select a region to record instead of taking a screenshot.</param>
    public static void Start(bool recordMode = false)
    {
        if (IsActive || RecordingSession.IsActive || ScrollCaptureSession.IsActive)
            return;
        IsActive = true;
        try
        {
            var snapshot = ScreenCaptureService.CaptureVirtualScreen(AppSettings.CaptureCursor);
            var detector = WindowDetector.Snapshot(snapshot);
            var overlay = new OverlayWindow(snapshot, detector, recordMode ? OverlayMode.Record : OverlayMode.Capture);
            overlay.Closed += (_, _) =>
            {
                IsActive = false;
                HandleResult(overlay.Result);
            };
            overlay.Show();
        }
        catch
        {
            IsActive = false;
            throw;
        }
    }

    /// <summary>
    /// Opens the editor on an image (a pin) shown at its screen position on top of the current screen.
    /// <paramref name="done"/> receives the edited image and its new top-left, or null if cancelled.
    /// </summary>
    public static async void EditImage(BitmapSource image, int screenX, int screenY, Action<BitmapSource?, int, int> done)
    {
        if (IsActive || RecordingSession.IsActive || ScrollCaptureSession.IsActive)
        {
            done(null, screenX, screenY);
            return;
        }
        IsActive = true;
        try
        {
            await Task.Delay(120); // let DWM remove the hidden pin from the screen
            var screen = ScreenCaptureService.CaptureVirtualScreen();
            int w = image.PixelWidth, h = image.PixelHeight;
            if (w > screen.Width || h > screen.Height)
            {
                // The editor works on the screen, so the part that doesn't fit would be lost.
                IsActive = false;
                Notifier.Show("貼圖比螢幕還大，無法編輯標註。");
                done(null, screenX, screenY);
                return;
            }

            // Keep the image on screen so all of it can be edited.
            int x = Math.Clamp(screenX - screen.Left, 0, screen.Width - w);
            int y = Math.Clamp(screenY - screen.Top, 0, screen.Height - h);
            var snapshot = screen.WithImage(image, x, y);

            var overlay = new OverlayWindow(snapshot, WindowDetector.Snapshot(snapshot), OverlayMode.EditPin, new Rect(x, y, w, h));
            overlay.Closed += (_, _) =>
            {
                IsActive = false;
                var result = overlay.Result;
                if (result.Image == null)
                {
                    done(null, screenX, screenY);
                    return;
                }
                Output(result);
                done(result.Image, result.ScreenX, result.ScreenY);
            };
            overlay.Show();
        }
        catch
        {
            IsActive = false;
            done(null, screenX, screenY);
            throw;
        }
    }

    private static void HandleResult(OverlayResult result)
    {
        if (result.Action == OverlayAction.Record)
            RecordingSession.Start(new Int32Rect(result.ScreenX, result.ScreenY, result.Width, result.Height));
        else if (result.Action == OverlayAction.ScrollCapture)
            ScrollCaptureSession.Start(new Int32Rect(result.ScreenX, result.ScreenY, result.Width, result.Height));
        else if (result.Action == OverlayAction.Pin && result.Image != null)
            PinWindow.ShowAt(result.Image, result.ScreenX, result.ScreenY);
        else
            Output(result);
    }

    /// <summary>Copies or saves the result image; other actions are handled by the caller.</summary>
    private static void Output(OverlayResult result)
    {
        if (result.Image == null)
            return;
        switch (result.Action)
        {
            case OverlayAction.Copy:
                ClipboardService.SetImage(result.Image);
                break;
            case OverlayAction.Save:
                ImageExportService.Save(result.Image);
                break;
            case OverlayAction.QuickSave:
                QuickSave(result.Image);
                break;
            case OverlayAction.Ocr:
                OcrResultWindow.ShowFor(result.Image);
                break;
        }
    }

    public static void QuickSave(BitmapSource image)
    {
        if (ImageExportService.QuickSave(image) is { } path)
            Notifier.Show($"已儲存：{path}");
    }
}
