using System.Runtime;
using System.Windows.Threading;

namespace ScreenCap.Services;

/// <summary>
/// Gives memory back to Windows once a capture, pin or recording is done. Each capture holds a copy of the
/// whole screen (tens of MB on large monitors), and a tray app sits idle so long that the GC would keep
/// that memory reserved for a long time; Task Manager then shows hundreds of MB for an idle app.
/// </summary>
internal static class MemoryRelease
{
    /// <summary>Waits for the window to be torn down, and for back-to-back captures to settle.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    private static DispatcherTimer? _timer;

    /// <summary>Collects a little later; calls in quick succession collect once.</summary>
    public static void Schedule()
    {
        if (_timer == null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = Delay };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Collect();
            };
        }
        _timer.Stop();
        _timer.Start(); // restart the delay
    }

    /// <summary>A full, compacting collection that also returns the freed pages to the OS.</summary>
    public static void Collect()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce; // the screen copies live there
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers(); // bitmaps free their native pixel memory in finalizers
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
