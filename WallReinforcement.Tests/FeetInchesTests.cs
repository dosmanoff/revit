using System;
using WallReinforcement.Geometry;
using Xunit;

namespace WallReinforcement.Tests;

public class FeetInchesTests
{
    [Theory]
    [InlineData("3\"", 3.0)]
    [InlineData("1'", 12.0)]
    [InlineData("1'-3\"", 15.0)]
    [InlineData("1'3\"", 15.0)]
    [InlineData("2'-0\"", 24.0)]
    [InlineData("3 1/2\"", 3.5)]
    [InlineData("1'-3 1/2\"", 15.5)]
    [InlineData("0'-6\"", 6.0)]
    public void ParseToInches_KnownForms(string s, double expectedInches)
    {
        Assert.Equal(expectedInches, FeetInches.ParseToInches(s), 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("3/0\"")]   // zero denominator
    public void ParseToInches_Garbage_Throws(string s)
    {
        Assert.Throws<FormatException>(() => FeetInches.ParseToInches(s));
    }
}
