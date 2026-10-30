using ScreenCap.Capture;

namespace ScreenCap.Tests;

public class NumberBoxTests
{
    [Theory]
    [InlineData("12", "", 12.0)]
    [InlineData(" 55% ", "%", 55.0)]
    [InlineData("55", "%", 55.0)]  // suffix is optional
    [InlineData("7.5", "", 7.5)]
    public void Parse_ReadsNumbers(string text, string suffix, double expected) =>
        Assert.Equal(expected, NumberBox.Parse(text, suffix));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("%")]
    public void Parse_RejectsOtherText(string text) => Assert.Null(NumberBox.Parse(text, "%"));

    [Fact]
    public void TypedValue_IsClampedAndRaisedOnce_WhenFocusLeaves() => Support.Sta.Run(() =>
    {
        var box = new NumberBox { Minimum = 10, Maximum = 90, Suffix = "%", Value = 60 };
        var raised = new List<double>();
        box.ValueCommitted += raised.Add;
        var window = new System.Windows.Window { Content = new System.Windows.Controls.StackPanel { Children = { box, new System.Windows.Controls.Button() } }, ShowActivated = false, Left = -10000 };
        window.Show();
        try
        {
            box.Focus();
            box.Text = "150";
            ((System.Windows.Controls.StackPanel)window.Content).Children[1].Focus(); // leaving commits
            Assert.Equal([90.0], raised);
            Assert.Equal("90%", box.Text);

            box.Focus();
            box.Text = "oops";
            ((System.Windows.Controls.StackPanel)window.Content).Children[1].Focus();
            Assert.Single(raised);          // invalid text changes nothing...
            Assert.Equal("90%", box.Text);  // ...and shows the value again
        }
        finally
        {
            window.Close();
        }
    });
}
