using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using ScreenCap.Services;

namespace ScreenCap.Tests.Support;

/// <summary>Points the app's settings at a throwaway registry key so tests never touch the user's settings.</summary>
internal static class TestSettings
{
    public const string KeyPath = @"Software\ScreenCap.Tests";

    [ModuleInitializer]
    internal static void Initialize()
    {
        AppSettings.KeyPath = KeyPath;
        // Never read or write the user's real screenshot history or stamps.
        ScreenshotHistory.Folder = Path.Combine(Path.GetTempPath(), "ScreenCap.Tests.History");
        StampLibrary.Folder = Path.Combine(Path.GetTempPath(), "ScreenCap.Tests.Stamps");
        Clear();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Clear();
    }

    public static void Clear() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
}
