using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using ScreenCap.Recording;
using ScreenCap.Services;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

[Collection(nameof(SettingsCollection))]
public class AppSettingsTests : IDisposable
{
    public AppSettingsTests() => TestSettings.Clear();

    public void Dispose() => TestSettings.Clear();

    [Fact]
    public void Defaults_WhenNothingStored()
    {
        Assert.Equal(AppSettings.DefaultCaptureHotkey, AppSettings.CaptureHotkey);
        Assert.Equal(AppSettings.DefaultRecordHotkey, AppSettings.RecordHotkey);
        Assert.Equal(AppSettings.DefaultPinHotkey, AppSettings.PinHotkey);
        Assert.Equal(AppSettings.DefaultSaveFolder, AppSettings.SaveFolder);
        Assert.Equal(RecordingFormat.Gif, AppSettings.RecordingFormat);
        Assert.Empty(AppSettings.RecentColors);
        Assert.False(AppSettings.CaptureCursor);
        Assert.False(AppSettings.CopyColorAsRgb);
    }

    [Fact]
    public void Toggles_RoundTrip()
    {
        AppSettings.CaptureCursor = true;
        AppSettings.CopyColorAsRgb = true;
        Assert.True(AppSettings.CaptureCursor);
        Assert.True(AppSettings.CopyColorAsRgb);
        AppSettings.CaptureCursor = false;
        Assert.False(AppSettings.CaptureCursor);
    }

    [Fact]
    public void Values_RoundTrip()
    {
        var hotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.F9);
        AppSettings.CaptureHotkey = hotkey;
        AppSettings.PinHotkey = default; // disabled
        AppSettings.RecordingFormat = RecordingFormat.Mp4;
        AppSettings.SaveFolder = Path.GetTempPath();
        AppSettings.RecentColors = [Colors.Red, Color.FromRgb(0x12, 0x34, 0x56)];

        Assert.Equal(hotkey, AppSettings.CaptureHotkey);
        Assert.True(AppSettings.PinHotkey.IsNone);
        Assert.Equal(RecordingFormat.Mp4, AppSettings.RecordingFormat);
        Assert.Equal(Path.GetTempPath(), AppSettings.SaveFolder);
        Assert.Equal([Colors.Red, Color.FromRgb(0x12, 0x34, 0x56)], AppSettings.RecentColors);
    }

    [Fact]
    public void SaveAndCaptureOptions_RoundTrip()
    {
        Assert.Equal(Capture.OverlayAction.Copy, AppSettings.ConfirmAction);
        Assert.Equal(FileNameTemplate.Default, AppSettings.FileNamePattern);
        Assert.Equal(Annotations.TextStyle.DefaultFontName, AppSettings.TextFont);

        AppSettings.ConfirmAction = Capture.OverlayAction.QuickSave;
        AppSettings.CopyPathAfterSave = true;
        AppSettings.RevealAfterSave = true;
        AppSettings.HighlightClicks = true;
        AppSettings.TextFont = "Consolas";
        AppSettings.FileNamePattern = "{title}_{n}";

        Assert.Equal(Capture.OverlayAction.QuickSave, AppSettings.ConfirmAction);
        Assert.True(AppSettings.CopyPathAfterSave);
        Assert.True(AppSettings.RevealAfterSave);
        Assert.True(AppSettings.HighlightClicks);
        Assert.Equal("Consolas", AppSettings.TextFont);
        Assert.Equal("{title}_{n}", AppSettings.FileNamePattern);
    }

    [Fact]
    public void ConfirmAction_IgnoresActionsThatCannotBeTheDefault()
    {
        AppSettings.ConfirmAction = Capture.OverlayAction.Record;
        Assert.Equal(Capture.OverlayAction.Copy, AppSettings.ConfirmAction);
    }

    [Fact]
    public void SaveFolder_FallsBackWhenFolderNoLongerExists()
    {
        AppSettings.SaveFolder = Path.Combine(Path.GetTempPath(), "ScreenCap_missing_" + Guid.NewGuid());
        Assert.Equal(AppSettings.DefaultSaveFolder, AppSettings.SaveFolder);
    }
}

/// <summary>Tests sharing the settings registry key must not run in parallel.</summary>
[CollectionDefinition(nameof(SettingsCollection), DisableParallelization = true)]
public class SettingsCollection;
