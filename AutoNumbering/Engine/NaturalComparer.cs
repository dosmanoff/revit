using System.Text.RegularExpressions;

namespace AutoNumbering.Engine;

/// <summary>
/// Sorts strings so "Level 2" comes before "Level 10" (numeric segments compared numerically).
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    private NaturalComparer() { }

    public int Compare(string? x, string? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var xParts = Tokenize(x);
        var yParts = Tokenize(y);

        int len = Math.Min(xParts.Count, yParts.Count);
        for (int i = 0; i < len; i++)
        {
            int cmp = ComparePart(xParts[i], yParts[i]);
            if (cmp != 0) return cmp;
        }

        return xParts.Count.CompareTo(yParts.Count);
    }

    private static List<string> Tokenize(string s) =>
        Regex.Split(s, @"(\d+)").Where(p => p.Length > 0).ToList();

    private static int ComparePart(string a, string b)
    {
        if (long.TryParse(a, out long aNum) && long.TryParse(b, out long bNum))
            return aNum.CompareTo(bNum);
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }
}
