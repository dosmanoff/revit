using RevitActionRecorder.Model;
using RevitActionRecorder.Recording;
using Xunit;

namespace RevitActionRecorder.Recording.Tests;

public class CommandTrackerTests
{
    [Fact]
    public void Correlates_a_recent_command_and_reports_its_age()
    {
        var tracker = new CommandTracker();
        tracker.Record(new CommandRef { Id = "ID_ANNOTATIONS_DIMENSION_ALIGNED", Title = "Aligned Dimension" });

        var correlated = tracker.TryCorrelate(30000);

        Assert.NotNull(correlated);
        Assert.Equal("Aligned Dimension", correlated!.Title);
        Assert.NotNull(correlated.AgeMs);
        Assert.True(correlated.AgeMs >= 0);
    }

    [Fact]
    public void Command_stays_sticky_for_repeated_transactions()
    {
        // Инструмент Revit остаётся активным и порождает серию транзакций: клик по «Tag by Category»
        // дал на живой сессии четыре пары Tag+Move за 20 секунд.
        var tracker = new CommandTracker();
        tracker.Record(new CommandRef { Id = "ID_BUTTON_TAG", Title = "Tag by Category" });

        Assert.NotNull(tracker.TryCorrelate(30000));
        Assert.NotNull(tracker.TryCorrelate(30000));
        Assert.NotNull(tracker.TryCorrelate(30000));
    }

    [Fact]
    public void Threshold_cuts_off_stale_commands()
    {
        var tracker = new CommandTracker();
        tracker.Record(new CommandRef { Id = "ID_BUTTON_TAG", Title = "Tag by Category" });
        Thread.Sleep(20);

        Assert.Null(tracker.TryCorrelate(1));
    }

    [Fact]
    public void Own_ribbon_buttons_never_stick()
    {
        var tracker = new CommandTracker();
        tracker.Record(new CommandRef
        {
            Id = "CustomCtrl_%CustomCtrl_%Smart Tools%Action Recorder%RAR_StartRecording",
            Title = "Start recording",
        });

        Assert.Null(tracker.TryCorrelate(30000));
    }
}
