using ScreenCap.Services;

namespace ScreenCap.Tests;

public class FileNameTemplateTests
{
    private static readonly DateTime Time = new(2026, 10, 30, 14, 5, 9);

    [Fact]
    public void Default_MatchesTheOldFixedName() =>
        Assert.Equal("ScreenCap_20261030_140509", FileNameTemplate.Expand(FileNameTemplate.Default, Time, null));

    [Theory]
    [InlineData("{yyyy-MM-dd} {HH.mm}", "2026-10-30 14.05")]
    [InlineData("截圖_{title}_{time}", "截圖_Notepad_140509")]
    [InlineData("shot{n}", "shot007")]
    [InlineData("{d}", "30")] // a single letter is a custom format, not the short-date pattern
    public void Expand_ReplacesTokens(string pattern, string expected) =>
        Assert.Equal(expected, FileNameTemplate.Expand(pattern, Time, "Notepad", 7));

    [Fact]
    public void Expand_ReplacesCharactersWindowsDoesNotAllow() =>
        Assert.Equal("a_b_c_ - Chrome", FileNameTemplate.Expand("{title}", Time, "a/b:c? - Chrome"));

    [Fact]
    public void Expand_TrimsSeparatorsLeftByAMissingTitle_AndNeverReturnsEmpty()
    {
        Assert.Equal("140509", FileNameTemplate.Expand("{title}_{time}", Time, null));
        Assert.Equal("ScreenCap", FileNameTemplate.Expand("{title}", Time, "  "));
    }

    [Fact]
    public void UniquePath_CountsUpTheSequenceNumber()
    {
        var taken = new HashSet<string> { @"C:\out\shot_001.png", @"C:\out\shot_002.png" };
        Assert.Equal(@"C:\out\shot_003.png", FileNameTemplate.UniquePath(@"C:\out", "shot_{n}", ".png", Time, null, taken.Contains));
    }

    [Fact]
    public void UniquePath_WithoutSequence_AppendsASuffixWhenTaken()
    {
        var taken = new HashSet<string> { @"C:\out\a.png", @"C:\out\a_2.png" };
        Assert.Equal(@"C:\out\a_3.png", FileNameTemplate.UniquePath(@"C:\out", "a", ".png", Time, null, taken.Contains));
        Assert.Equal(@"C:\out\b.png", FileNameTemplate.UniquePath(@"C:\out", "b", ".png", Time, null, taken.Contains));
    }
}
