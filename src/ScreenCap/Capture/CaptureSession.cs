using ScreenCap.Pin;
using ScreenCap.Services;

namespace ScreenCap.Capture;

/// <summary>Coordinates one capture: freeze the screen, show the overlay, then act on the result.</summary>
internal static class CaptureSession
{
    public static bool IsActive { get; private set; }

    public static void Start()
    {
        if (IsActive)
            return;
        IsActive = true;
        try
        {
            var snapshot = ScreenCaptureService.CaptureVirtualScreen();
            var detector = WindowDetector.Snapshot(snapshot);
            var overlay = new OverlayWindow(snapshot, detector);
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

    private static void HandleResult(OverlayResult result)
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
            case OverlayAction.Pin:
                PinWindow.ShowAt(result.Image, result.ScreenX, result.ScreenY);
                break;
        }
    }
}
