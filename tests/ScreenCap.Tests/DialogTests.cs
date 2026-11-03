using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenCap.Tests.Support;
using ScreenCap.Theme;

namespace ScreenCap.Tests;

public class DialogTests
{
    [Fact]
    public void Choices_AreTheButtonsInOrder()
    {
        Assert.Equal([MessageBoxResult.OK], Dialog.Choices(MessageBoxButton.OK));
        Assert.Equal([MessageBoxResult.OK, MessageBoxResult.Cancel], Dialog.Choices(MessageBoxButton.OKCancel));
        Assert.Equal([MessageBoxResult.Yes, MessageBoxResult.No], Dialog.Choices(MessageBoxButton.YesNo));
        Assert.Equal([MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel], Dialog.Choices(MessageBoxButton.YesNoCancel));
    }

    /// <summary>Shows a dialog and, once it is up, does <paramref name="act"/> with it and its buttons.</summary>
    private static MessageBoxResult ShowAnd(Action<Window, List<Button>> act, MessageBoxButton buttons, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            var dialog = PresentationSource.CurrentSources.OfType<HwndSource>().Select(s => s.RootVisual).OfType<Dialog>().Single();
            act(dialog, Descendants(dialog).OfType<Button>().ToList());
        });
        return Dialog.Show("要繼續嗎？", buttons, MessageBoxImage.Question, defaultResult);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var below in Descendants(child))
                yield return below;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void ClickingAButton_IsTheAnswer() => Sta.Run(() =>
    {
        var result = ShowAnd((_, buttons) =>
        {
            Assert.Equal(["是", "否", "取消"], buttons.Select(b => (string)b.Content));
            Click(buttons[1]);
        }, MessageBoxButton.YesNoCancel);

        Assert.Equal(MessageBoxResult.No, result);
    });

    [Fact]
    public void TheDefaultButton_IsTheOneEnterPresses() => Sta.Run(() =>
    {
        ShowAnd((dialog, buttons) =>
        {
            Assert.Equal([false, true], buttons.Select(b => b.IsDefault));
            Assert.True(buttons[1].IsKeyboardFocused);
            dialog.Close();
        }, MessageBoxButton.OKCancel, MessageBoxResult.Cancel);

        ShowAnd((dialog, buttons) =>
        {
            Assert.Equal([true, false], buttons.Select(b => b.IsDefault)); // the first, when not given
            dialog.Close();
        }, MessageBoxButton.YesNo);
    });

    [Theory]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void ClosingTheWindow_IsTheSafeAnswer(MessageBoxButton buttons, MessageBoxResult expected) => Sta.Run(() =>
    {
        var result = ShowAnd((dialog, all) =>
        {
            Assert.Single(all, b => b.IsCancel); // Esc presses it
            dialog.Close();
        }, buttons);

        Assert.Equal(expected, result);
    });
}
