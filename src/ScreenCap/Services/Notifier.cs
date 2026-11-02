namespace ScreenCap.Services;

/// <summary>Shows tray balloon notifications; wired up by the app at startup.</summary>
internal static class Notifier
{
    /// <summary>Shows a title, message, and an optional action run when the notification is clicked.</summary>
    public static Action<string, string, Action?>? Handler { get; set; }

    /// <param name="onClick">Run if the user clicks the notification.</param>
    public static void Show(string message, Action? onClick = null) => Handler?.Invoke("ScreenCap", message, onClick);

    private const int PreviewLength = 60;

    /// <summary>The start of <paramref name="text"/> on one line, short enough for a notification.</summary>
    public static string Preview(string text)
    {
        string line = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length <= PreviewLength ? line : line[..PreviewLength] + "…";
    }
}
