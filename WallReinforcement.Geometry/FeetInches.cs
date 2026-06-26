using System.Globalization;
using System.Text.RegularExpressions;

namespace WallReinforcement.Geometry;

/// <summary>
/// Pure parser for architectural feet-inches strings → total inches. No Revit dependency, so the
/// (bug-prone) string parsing is unit-testable. Accepted forms: <c>"1'-3""</c>, <c>"1'3""</c>,
/// <c>"1'-3 1/2""</c>, <c>"3""</c>, <c>"3 1/2""</c>, <c>"1'"</c>. Whitespace and the dash between
/// feet and inches are optional.
/// </summary>
public static class FeetInches
{
    private static readonly Regex Rx = new(
        @"^\s*(?:(?<ft>\d+(?:\.\d+)?)\s*['′])?\s*[-\s]*\s*" +
        @"(?:(?<inWhole>\d+(?:\.\d+)?)?\s*(?:(?<inNum>\d+)\s*/\s*(?<inDen>\d+))?\s*[""″])?\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parse a feet-inches string and return total inches. Throws <see cref="FormatException"/> if
    /// the string matches nothing meaningful or has a zero fraction denominator.
    /// </summary>
    public static double ParseToInches(string s)
    {
        var m = Rx.Match(s);
        if (!m.Success || (!m.Groups["ft"].Success && !m.Groups["inWhole"].Success && !m.Groups["inNum"].Success))
            throw new FormatException($"Cannot parse '{s}' as feet-inches (expected forms like \"1'-3\\\"\" or \"3 1/2\\\"\").");

        double ft = m.Groups["ft"].Success ? double.Parse(m.Groups["ft"].Value, CultureInfo.InvariantCulture) : 0;
        double inWhole = m.Groups["inWhole"].Success ? double.Parse(m.Groups["inWhole"].Value, CultureInfo.InvariantCulture) : 0;
        double inFrac = 0;
        if (m.Groups["inNum"].Success && m.Groups["inDen"].Success)
        {
            double num = double.Parse(m.Groups["inNum"].Value, CultureInfo.InvariantCulture);
            double den = double.Parse(m.Groups["inDen"].Value, CultureInfo.InvariantCulture);
            if (den == 0) throw new FormatException($"Cannot parse '{s}' — fraction denominator is zero.");
            inFrac = num / den;
        }

        return ft * 12.0 + inWhole + inFrac;
    }
}
