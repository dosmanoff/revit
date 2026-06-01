using ColumnReinforcement.Engine;

namespace ColumnReinforcement.PerBarTests;

/// <summary>
/// Pure-logic tests for SelectorListResolver — the resolver and the two built-in
/// parsers (ParseOutwardToken, ParseXyTokenInches). Touches no Revit-API types.
/// </summary>
public class SelectorListResolverTests
{
    // A canonical 8-bar 3x3 perimeter cage in canonical-frame inches, mirroring what
    // LongitudinalBarBuilder.LayoutRectangular would produce for a 12x12 column. Index
    // order matches the engine's enumeration: bottom face (y=yMin) 0..2, top face
    // (y=yMax) 3..5, then side bars (xMin, xMax) at the interior y row 6..7.
    //
    //                   x=-4.56     x=0       x=+4.56
    //   y=+4.56            3          4          5         (top face)
    //   y= 0               6                     7         (sides, interior)
    //   y=-4.56            0          1          2         (bottom face)
    //
    // Corners: 0, 2, 3, 5. Edges (non-corner perimeter): 1, 4, 6, 7.
    private static IReadOnlyList<(double x, double y)> Cage3x3()
    {
        const double e = 4.56;
        return new List<(double x, double y)>
        {
            (-e, -e), ( 0, -e), ( e, -e),     // 0..2 bottom face (y=yMin)
            (-e,  e), ( 0,  e), ( e,  e),     // 3..5 top face    (y=yMax)
            (-e,  0), ( e,  0),               // 6,7 side bars    (interior y)
        };
    }

    // ── Resolver dispatch ───────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullSpec_ReturnsAllDefault()
    {
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: null, defaultValue: true, SelectorListResolver.ParseOutwardToken);
        Assert.Equal(8, result.Length);
        Assert.All(result, v => Assert.True(v));
    }

    [Fact]
    public void Resolve_AllKeyword_OverridesEveryPosition()
    {
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: "all:inward", defaultValue: true, SelectorListResolver.ParseOutwardToken);
        Assert.All(result, v => Assert.False(v));
    }

    [Fact]
    public void Resolve_CornersKeyword_TargetsCornersOnly()
    {
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: "corners:inward", defaultValue: true, SelectorListResolver.ParseOutwardToken);
        // Corners 0, 2, 3, 5; edges 1, 4, 6, 7.
        Assert.False(result[0]); Assert.False(result[2]);
        Assert.False(result[3]); Assert.False(result[5]);
        Assert.True(result[1]); Assert.True(result[4]);
        Assert.True(result[6]); Assert.True(result[7]);
    }

    [Fact]
    public void Resolve_FaceKeyword_PlusXOnlyTargetsRightFace()
    {
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: "+x:inward", defaultValue: true, SelectorListResolver.ParseOutwardToken);
        // +X face = bars where x == max(=4.56): indices 2 (bottom-right), 5 (top-right), 7 (right side).
        Assert.False(result[2]); Assert.False(result[5]); Assert.False(result[7]);
        // The other five stay at default (true).
        Assert.True(result[0]); Assert.True(result[1]); Assert.True(result[3]);
        Assert.True(result[4]); Assert.True(result[6]);
    }

    [Fact]
    public void Resolve_IndexOverridesKeyword()
    {
        // Keyword sets all 8 bars true; index then forces bar 4 to false. Per the spec,
        // explicit index > keyword regardless of token order in the spec.
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: "4:inward all:outward", defaultValue: false, SelectorListResolver.ParseOutwardToken);
        Assert.False(result[4]);
        for (int i = 0; i < 8; i++) if (i != 4) Assert.True(result[i]);
    }

    [Fact]
    public void Resolve_UnknownValue_LeavesPositionAtDefault()
    {
        // "wat" isn't a recognised outward token → parser returns false → token skipped
        // → the position stays at the default. Other valid tokens in the same spec still apply.
        var result = SelectorListResolver.Resolve<bool>(
            Cage3x3(), spec: "all:outward corners:wat", defaultValue: false, SelectorListResolver.ParseOutwardToken);
        Assert.All(result, v => Assert.True(v));   // every position got "all:outward"
    }

    // ── Built-in parsers ────────────────────────────────────────────────────

    [Theory]
    [InlineData("true",    true)]  [InlineData("false",   false)]
    [InlineData("outward", true)]  [InlineData("inward",  false)]
    [InlineData("out",     true)]  [InlineData("in",      false)]
    [InlineData("YES",     true)]  [InlineData("No",      false)]
    [InlineData("1",       true)]  [InlineData("0",       false)]
    public void ParseOutwardToken_AcceptsKnownSynonyms(string token, bool expected)
    {
        Assert.True(SelectorListResolver.ParseOutwardToken(token, out bool v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("maybe")] [InlineData("")] [InlineData("on")]
    public void ParseOutwardToken_RejectsUnknown(string token)
    {
        Assert.False(SelectorListResolver.ParseOutwardToken(token, out _));
    }

    [Theory]
    [InlineData("(-3.56,-3.56)", -3.56, -3.56)]
    [InlineData("0,0",           0,     0)]
    [InlineData("(1.5, 2.0)",    1.5,   2.0)]    // tolerant of whitespace
    [InlineData("(+10.25,-7.5)", 10.25, -7.5)]
    public void ParseXyTokenInches_ParsesPair(string token, double xExpected, double yExpected)
    {
        Assert.True(SelectorListResolver.ParseXyTokenInches(token, out var v));
        Assert.Equal(xExpected, v.xIn, 6);
        Assert.Equal(yExpected, v.yIn, 6);
    }

    [Theory]
    [InlineData("(1)")]            // only one number
    [InlineData("(a,b)")]          // not numbers
    [InlineData("(1,2,3)")]        // three numbers
    [InlineData("")]
    public void ParseXyTokenInches_RejectsMalformed(string token)
    {
        Assert.False(SelectorListResolver.ParseXyTokenInches(token, out _));
    }
}
