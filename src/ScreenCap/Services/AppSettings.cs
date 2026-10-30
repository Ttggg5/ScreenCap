using System.IO;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ScreenCap.Annotations;
using ScreenCap.Capture;
using ScreenCap.Recording;
using ScreenCap.Theme;

namespace ScreenCap.Services;

/// <summary>A global hotkey; <see cref="Key.None"/> means disabled.</summary>
internal readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public bool IsNone => Key == Key.None;

    public override string ToString()
    {
        if (IsNone)
            return "（無）";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (key - Key.NumPad0),
        Key.Snapshot => "PrintScreen",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.Oem1 => ";",
        Key.Oem2 => "/",
        Key.Oem3 => "`",
        Key.Oem4 => "[",
        Key.Oem5 => "\\",
        Key.Oem6 => "]",
        Key.Oem7 => "'",
        _ => key.ToString(),
    };

    /// <summary>Stored as "modifiers,key" numbers so the value doesn't depend on display names.</summary>
    public string Serialize() => $"{(int)Modifiers},{(int)Key}";

    public static Hotkey? Deserialize(string? value)
    {
        var parts = value?.Split(',');
        if (parts is not { Length: 2 } || !int.TryParse(parts[0], out int mods) || !int.TryParse(parts[1], out int key))
            return null;
        return new Hotkey((ModifierKeys)mods, (Key)key);
    }
}

/// <summary>User preferences, stored under HKCU\Software\ScreenCap.</summary>
internal static class AppSettings
{
    /// <summary>Registry key under HKCU; tests point it elsewhere so real settings are untouched.</summary>
    internal static string KeyPath { get; set; } = @"Software\ScreenCap";

    public static readonly Hotkey DefaultCaptureHotkey = new(ModifierKeys.None, Key.F1);
    public static readonly Hotkey DefaultRecordHotkey = new(ModifierKeys.Shift, Key.F1);
    public static readonly Hotkey DefaultPinHotkey = new(ModifierKeys.None, Key.F3);
    public static readonly Hotkey DefaultReopenHotkey = new(ModifierKeys.Control | ModifierKeys.Shift, Key.F1);

    public static string DefaultSaveFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    public static Hotkey CaptureHotkey
    {
        get => Hotkey.Deserialize(Get("CaptureHotkey")) ?? DefaultCaptureHotkey;
        set => Set("CaptureHotkey", value.Serialize());
    }

    public static Hotkey RecordHotkey
    {
        get => Hotkey.Deserialize(Get("RecordHotkey")) ?? DefaultRecordHotkey;
        set => Set("RecordHotkey", value.Serialize());
    }

    public static Hotkey PinHotkey
    {
        get => Hotkey.Deserialize(Get("PinHotkey")) ?? DefaultPinHotkey;
        set => Set("PinHotkey", value.Serialize());
    }

    public static Hotkey ReopenHotkey
    {
        get => Hotkey.Deserialize(Get("ReopenHotkey")) ?? DefaultReopenHotkey;
        set => Set("ReopenHotkey", value.Serialize());
    }

    /// <summary>Where quick save writes, and where save dialogs start. Falls back to Pictures if it no longer exists.</summary>
    public static string SaveFolder
    {
        get => Get("SaveFolder") is { } folder && Directory.Exists(folder) ? folder : DefaultSaveFolder;
        set => Set("SaveFolder", value);
    }

    public static RecordingFormat RecordingFormat
    {
        get => Enum.TryParse(Get("RecordingFormat"), out RecordingFormat format) ? format : RecordingFormat.Gif;
        set => Set("RecordingFormat", value.ToString());
    }

    public static AppTheme Theme
    {
        get => Enum.TryParse(Get("Theme"), out AppTheme mode) ? mode : AppTheme.System;
        set => Set("Theme", value.ToString());
    }

    /// <summary>File name pattern for saved captures and recordings; see <see cref="FileNameTemplate"/>.</summary>
    public static string FileNamePattern
    {
        get => Get("FileNamePattern") is { Length: > 0 } pattern ? pattern : FileNameTemplate.Default;
        set => Set("FileNamePattern", value);
    }

    /// <summary>After saving an image, put its path on the clipboard.</summary>
    public static bool CopyPathAfterSave
    {
        get => Get("CopyPathAfterSave") == "1";
        set => Set("CopyPathAfterSave", value ? "1" : "0");
    }

    /// <summary>After saving, select the file in Explorer.</summary>
    public static bool RevealAfterSave
    {
        get => Get("RevealAfterSave") == "1";
        set => Set("RevealAfterSave", value ? "1" : "0");
    }

    public static readonly OverlayAction[] ConfirmActions = [OverlayAction.Copy, OverlayAction.QuickSave, OverlayAction.Save, OverlayAction.Pin];

    /// <summary>What Enter and double-clicking the selection do.</summary>
    public static OverlayAction ConfirmAction
    {
        get => Enum.TryParse(Get("ConfirmAction"), out OverlayAction action) && ConfirmActions.Contains(action) ? action : OverlayAction.Copy;
        set => Set("ConfirmAction", value.ToString());
    }

    /// <summary>Mark mouse clicks with a ring in recordings.</summary>
    public static bool HighlightClicks
    {
        get => Get("HighlightClicks") == "1";
        set => Set("HighlightClicks", value ? "1" : "0");
    }

    /// <summary>Show the keys pressed (e.g. "Ctrl + C") at the bottom of recordings.</summary>
    public static bool ShowKeystrokes
    {
        get => Get("ShowKeystrokes") == "1";
        set => Set("ShowKeystrokes", value ? "1" : "0");
    }

    /// <summary>Font for text annotations; remembered from the last one chosen.</summary>
    public static string TextFont
    {
        get => Get("TextFont") is { Length: > 0 } font ? font : TextStyle.DefaultFontName;
        set => Set("TextFont", value);
    }

    /// <summary>Draw the mouse cursor into screenshots.</summary>
    public static bool CaptureCursor
    {
        get => Get("CaptureCursor") == "1";
        set => Set("CaptureCursor", value ? "1" : "0");
    }

    /// <summary>The magnifier copies RGB instead of HEX when pressing C.</summary>
    public static bool CopyColorAsRgb
    {
        get => Get("CopyColorAsRgb") == "1";
        set => Set("CopyColorAsRgb", value ? "1" : "0");
    }

    /// <summary>Colors picked from the custom palette, most recent first.</summary>
    public static IReadOnlyList<Color> RecentColors
    {
        get => (Get("RecentColors") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb) ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) : (Color?)null)
            .OfType<Color>()
            .ToList();
        set => Set("RecentColors", string.Join(",", value.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}")));
    }

    private static string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    private static void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value);
    }
}
