namespace RevitActionRecorder.Recording;

/// <summary>
/// Сопоставление undo/redo-шагов с ранее записанными doc_changed-событиями.
/// Зеркалит стек Undo Revit: commit кладёт запись, undo снимает верхушку,
/// redo возвращает, новый commit после undo сбрасывает redo-ветку (как в Revit).
/// Отменённые seq копятся в UndoneSeqs и при финализации помечаются undone:true.
/// Работает best-effort: расхождение имён логируется, но не роняет запись.
/// </summary>
internal sealed class UndoTracker
{
    private readonly List<(long Seq, IReadOnlyList<string> Names)> _committed = new();
    private readonly List<(long Seq, IReadOnlyList<string> Names)> _redoable = new();
    private readonly HashSet<long> _undoneSeqs = new();

    public IReadOnlySet<long> UndoneSeqs => _undoneSeqs;

    public void OnCommitted(long seq, IReadOnlyList<string> transactionNames)
    {
        _committed.Add((seq, transactionNames));
        _redoable.Clear();
    }

    /// <summary>Возвращает seq-номера событий, отменённых этим undo-шагом.</summary>
    public IReadOnlyList<long> OnUndone(IReadOnlyList<string> undoneNames, Action<string> warn)
    {
        var marked = new List<long>();
        int remaining = Math.Max(1, undoneNames.Count);

        while (remaining > 0 && _committed.Count > 0)
        {
            var top = _committed[^1];
            _committed.RemoveAt(_committed.Count - 1);
            _redoable.Add(top);
            _undoneSeqs.Add(top.Seq);
            marked.Add(top.Seq);
            remaining -= Math.Max(1, top.Names.Count);

            if (undoneNames.Count > 0 && top.Names.Count > 0 && !undoneNames.Contains(top.Names[0]))
                warn($"undo name mismatch: popped '{top.Names[0]}', event names [{string.Join("; ", undoneNames)}]");
        }

        if (remaining > 0 && marked.Count == 0)
            warn($"undo of [{string.Join("; ", undoneNames)}] has no recorded commit (action predates recording?)");

        return marked;
    }

    /// <summary>Возвращает seq-номера событий, возвращённых этим redo-шагом.</summary>
    public IReadOnlyList<long> OnRedone(IReadOnlyList<string> redoneNames, Action<string> warn)
    {
        var restored = new List<long>();
        int remaining = Math.Max(1, redoneNames.Count);

        while (remaining > 0 && _redoable.Count > 0)
        {
            var top = _redoable[^1];
            _redoable.RemoveAt(_redoable.Count - 1);
            _committed.Add(top);
            _undoneSeqs.Remove(top.Seq);
            restored.Add(top.Seq);
            remaining -= Math.Max(1, top.Names.Count);
        }

        if (remaining > 0 && restored.Count == 0)
            warn($"redo of [{string.Join("; ", redoneNames)}] has no matching undone commit");

        return restored;
    }
}
