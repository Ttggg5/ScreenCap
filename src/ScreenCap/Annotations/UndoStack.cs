namespace ScreenCap.Annotations;

/// <summary>
/// Holds the annotations with snapshot-based undo/redo, so that adds, removals and later edits
/// (move, reshape, restyle) are all undoable. Call <see cref="Checkpoint"/> before mutating an annotation.
/// </summary>
internal sealed class UndoStack
{
    // Kept as the same instance so the render layer can hold on to it.
    private readonly List<Annotation> _items = new();
    private readonly Stack<List<Annotation>> _undo = new();
    private readonly Stack<List<Annotation>> _redo = new();

    public IReadOnlyList<Annotation> Items => _items;

    /// <summary>Records the current state as an undo step.</summary>
    public void Checkpoint()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
    }

    public void Add(Annotation annotation, bool checkpoint = true)
    {
        if (checkpoint)
            Checkpoint();
        _items.Add(annotation);
    }

    public void Remove(Annotation annotation)
    {
        Checkpoint();
        _items.Remove(annotation);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _undo.Clear();
        _redo.Clear();
    }

    private List<Annotation> Snapshot() => _items.Select(a => a.Clone()).ToList();

    private void Restore(List<Annotation> state)
    {
        _items.Clear();
        _items.AddRange(state);
    }
}
