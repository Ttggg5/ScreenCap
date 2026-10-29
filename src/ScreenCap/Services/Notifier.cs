namespace ScreenCap.Services;

/// <summary>Shows tray balloon notifications; wired up by the app at startup.</summary>
internal static class Notifier
{
    public static Action<string, string>? Handler { get; set; }

    public static void Show(string message) => Handler?.Invoke("ScreenCap", message);
}
