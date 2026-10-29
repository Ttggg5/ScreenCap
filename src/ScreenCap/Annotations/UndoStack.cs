namespace ScreenCap.Annotations;

/// <summary>Holds committed annotations with undo/redo support.</summary>
internal sealed class UndoStack
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<Annotation> _redo = new();

    public IReadOnlyList<Annotation> Items => _items;

    public void Add(Annotation annotation)
    {
        _items.Add(annotation);
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_items.Count == 0)
            return false;
        _redo.Push(_items[^1]);
        _items.RemoveAt(_items.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _items.Add(_redo.Pop());
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _redo.Clear();
    }
}
