using RevitActionRecorder.Recording;
using Xunit;

namespace RevitActionRecorder.Recording.Tests;

public class UndoTrackerTests
{
    private static void NoWarn(string _) { }

    [Fact]
    public void Undo_marks_last_committed_event()
    {
        var tracker = new UndoTracker();
        tracker.OnCommitted(1, ["Стена"]);
        tracker.OnCommitted(2, ["Перекрытие"]);

        var marked = tracker.OnUndone(["Перекрытие"], NoWarn);

        Assert.Equal([2L], marked);
        Assert.Equal([2L], tracker.UndoneSeqs.OrderBy(x => x));
    }

    [Fact]
    public void Redo_unmarks_the_event()
    {
        var tracker = new UndoTracker();
        tracker.OnCommitted(1, ["Стена"]);
        tracker.OnUndone(["Стена"], NoWarn);

        var restored = tracker.OnRedone(["Стена"], NoWarn);

        Assert.Equal([1L], restored);
        Assert.Empty(tracker.UndoneSeqs);
    }

    [Fact]
    public void Commit_after_undo_clears_redo_branch_but_keeps_undone_marks()
    {
        var tracker = new UndoTracker();
        tracker.OnCommitted(1, ["Стена"]);
        tracker.OnUndone(["Стена"], NoWarn);
        tracker.OnCommitted(2, ["Колонна"]);

        var restored = tracker.OnRedone(["Стена"], NoWarn);

        Assert.Empty(restored);
        Assert.Equal([1L], tracker.UndoneSeqs.OrderBy(x => x));
    }

    [Fact]
    public void Multi_undo_consumes_one_entry_per_name()
    {
        var tracker = new UndoTracker();
        tracker.OnCommitted(1, ["A"]);
        tracker.OnCommitted(2, ["B"]);
        tracker.OnCommitted(3, ["C"]);

        var marked = tracker.OnUndone(["C", "B"], NoWarn);

        Assert.Equal([3L, 2L], marked);
        Assert.Equal([2L, 3L], tracker.UndoneSeqs.OrderBy(x => x));
    }

    [Fact]
    public void Undo_without_recorded_commit_warns_and_marks_nothing()
    {
        var tracker = new UndoTracker();
        var warnings = new List<string>();

        var marked = tracker.OnUndone(["Чужая транзакция"], warnings.Add);

        Assert.Empty(marked);
        Assert.Single(warnings);
    }
}
