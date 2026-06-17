using StairsReinforcement.Geometry;
using Xunit;

namespace StairsReinforcement.Tests;

public class BarSplitterTests
{
    [Fact]
    public void ShortRun_NotSplit()
    {
        var segs = BarSplitter.Split(length: 20, maxLen: 40, lap: 2);
        Assert.Single(segs);
        Assert.Equal(0, segs[0].Start, 6);
        Assert.Equal(20, segs[0].End, 6);
    }

    [Fact]
    public void LongRun_SplitsIntoEqualSegmentsUnderMax()
    {
        const double length = 100, maxLen = 40, lap = 2;
        var segs = BarSplitter.Split(length, maxLen, lap);

        Assert.True(segs.Count >= 3);
        foreach (var s in segs)
            Assert.True(s.Length <= maxLen + 1e-6, $"segment {s.Length} exceeds max {maxLen}");

        // Equal lengths.
        double first = segs[0].Length;
        foreach (var s in segs)
            Assert.Equal(first, s.Length, 6);

        // Consecutive segments overlap by exactly the lap; chain covers the whole run.
        for (int i = 1; i < segs.Count; i++)
            Assert.Equal(lap, segs[i - 1].End - segs[i].Start, 6);
        Assert.Equal(0, segs[0].Start, 6);
        Assert.Equal(length, segs[^1].End, 6);
    }

    [Fact]
    public void ZeroLength_Empty()
    {
        Assert.Empty(BarSplitter.Split(0, 40, 2));
    }

    [Fact]
    public void LapLargerThanMax_StillTerminates()
    {
        var segs = BarSplitter.Split(length: 100, maxLen: 10, lap: 50);
        Assert.NotEmpty(segs);
        foreach (var s in segs)
            Assert.True(s.Length <= 10 + 1e-6);
    }
}
