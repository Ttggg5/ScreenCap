using System.Reflection;
using System.Windows.Controls;
using ScreenCap.Help;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class HelpWindowTests
{
    [Theory]
    [InlineData("Ctrl + Shift + F1", "ctrl+shift", true)]     // spaces inside a word are optional
    [InlineData("Ctrl + Shift + F1", "CTRL SHIFT f1", true)]  // every word, any case
    [InlineData("Ctrl + Shift + F1", "ctrl alt", false)]      // every word must appear
    [InlineData("拖曳畫出矩形框", "矩形", true)]
    [InlineData("拖曳畫出矩形框", "矩 形", true)]
    [InlineData("($10.5)", "(", true)]                         // typed characters are literal, not patterns
    [InlineData("anything", "   ", false)]                     // nothing typed matches nothing
    public void Matches_AllWordsIgnoringCaseAndSpaces(string text, string query, bool expected)
    {
        Assert.Equal(expected, HelpWindow.Matches(text, HelpWindow.Terms(query)));
    }

    [Fact]
    public void Search_ShowsMatchingPassages_AndClearingItShowsTheTopicAgain() => Sta.Run(() =>
    {
        var window = (HelpWindow)Activator.CreateInstance(typeof(HelpWindow), nonPublic: true)!;
        const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
        var search = (TextBox)typeof(HelpWindow).GetField("_search", any)!.GetValue(window)!;
        var body = (StackPanel)typeof(HelpWindow).GetField("_body", any)!.GetValue(window)!;
        string Texts() => string.Join("\n", body.Children.OfType<TextBlock>().Select(t => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text));

        Assert.StartsWith("快速開始", Texts());

        search.Text = "長截圖 30,000";
        Assert.Contains("找到 1 筆", Texts());
        Assert.Contains("長截圖", Texts());

        search.Text = "沒有這個字zzz";
        Assert.Contains("找不到", Texts());

        search.Text = "";
        Assert.StartsWith("快速開始", Texts());
        window.Close();
    });
}
