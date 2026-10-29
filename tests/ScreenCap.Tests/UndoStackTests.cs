using System.Windows;
using System.Windows.Media;
using ScreenCap.Annotations;
using ScreenCap.Tests.Support;

namespace ScreenCap.Tests;

public class UndoStackTests
{
    private static RectAnnotation Rect(double x) => new(new Point(x, 0), Colors.Red, 4, 1);

    [Fact]
    public void UndoRedo_AddAndRemove() => Sta.Run(() =>
    {
        var stack = new UndoStack();
        var a = Rect(0);
        stack.Add(a);
        stack.Add(Rect(10));
        stack.Remove(a);
        Assert.Single(stack.Items);

        Assert.True(stack.Undo());
        Assert.Equal(2, stack.Items.Count);
        Assert.True(stack.Undo());
        Assert.Single(stack.Items);
        Assert.True(stack.Redo());
        Assert.Equal(2, stack.Items.Count);
    });

    [Fact]
    public void Checkpoint_MakesEditsUndoable() => Sta.Run(() =>
    {
        var stack = new UndoStack();
        var rect = Rect(0);
        rect.Update(new Point(20, 20));
        stack.Add(rect);

        stack.Checkpoint();
        rect.MoveFrom(rect.Clone(), new Vector(100, 0));
        rect.Color = Colors.Blue;

        stack.Undo();
        var restored = stack.Items.Single();
        Assert.Equal(0, restored.Bounds.X);
        Assert.Equal(Colors.Red, restored.Color);
        Assert.NotSame(rect, restored); // undo restores clones
    });

    [Fact]
    public void NewChange_ClearsRedo_AndEmptyStacksReturnFalse() => Sta.Run(() =>
    {
        var stack = new UndoStack();
        Assert.False(stack.Undo());
        stack.Add(Rect(0));
        stack.Undo();
        stack.Add(Rect(5));
        Assert.False(stack.Redo());
    });

    [Fact]
    public void Clear_DropsHistory() => Sta.Run(() =>
    {
        var stack = new UndoStack();
        stack.Add(Rect(0));
        stack.Clear();
        Assert.Empty(stack.Items);
        Assert.False(stack.Undo());
    });

    [Fact]
    public void Items_IsTheSameListInstanceAfterUndo() => Sta.Run(() =>
    {
        var stack = new UndoStack();
        var items = stack.Items;
        stack.Add(Rect(0));
        stack.Undo();
        stack.Redo();
        Assert.Same(items, stack.Items); // the render layer holds on to this list
        Assert.Single(items);
    });
}
