using System.Windows.Input;

namespace ScreenCap.Services;

internal static class KeyEventExtensions
{
    /// <summary>
    /// The key actually pressed. With an IME active (e.g. Chinese input), plain keys such as letters,
    /// digits and punctuation arrive as <see cref="Key.ImeProcessed"/>; Alt combinations as <see cref="Key.System"/>.
    /// </summary>
    public static Key RealKey(this KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key,
    };
}
