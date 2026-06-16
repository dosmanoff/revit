using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StairsReinforcement.Config;

public enum UnitSystem
{
    /// <summary>Plain numbers in JSON / CSV are millimetres.</summary>
    Metric,
    /// <summary>Plain numbers are inches. String values may use feet-inches syntax.</summary>
    Imperial,
}

/// <summary>
/// A length value in a config / CSV cell. Either a plain number (interpreted per the
/// row's unit system) or a feet-inches string (parsed unambiguously regardless of units).
///
/// Accepted string forms: <c>"1'-3""</c>, <c>"1'3""</c>, <c>"1'-3 1/2""</c>, <c>"3""</c>,
/// <c>"3 1/2""</c>, <c>"1'"</c>. Whitespace and the dash between feet and inches are optional.
/// Ported verbatim from SlabReinforcement.Config.Length (itself from WallReinforcement).
/// </summary>
[JsonConverter(typeof(LengthJsonConverter))]
public readonly struct Length
{
    public double? Number { get; }
    public string? Text   { get; }

    public Length(double number) { Number = number; Text = null; }
    public Length(string text)   { Number = null;   Text = text; }

    public double ToFeet(UnitSystem sys)
    {
        if (Text is not null)
        {
            double totalInches = ParseFeetInches(Text);
            return UnitUtils.ConvertToInternalUnits(totalInches, UnitTypeId.Inches);
        }

        double n = Number ?? 0;
        ForgeTypeId unit = sys == UnitSystem.Metric ? UnitTypeId.Millimeters : UnitTypeId.Inches;
        return UnitUtils.ConvertToInternalUnits(n, unit);
    }

    private static readonly Regex FeetInchesRx = new(
        @"^\s*(?:(?<ft>\d+(?:\.\d+)?)\s*['′])?\s*[-\s]*\s*" +
        @"(?:(?<inWhole>\d+(?:\.\d+)?)?\s*(?:(?<inNum>\d+)\s*/\s*(?<inDen>\d+))?\s*[""″])?\s*$",
        RegexOptions.Compiled);

    /// <summary>Parses a feet-inches string and returns total inches. Pure — no Revit dependency.</summary>
    public static double ParseFeetInches(string s)
    {
        var m = FeetInchesRx.Match(s);
        if (!m.Success || (!m.Groups["ft"].Success && !m.Groups["inWhole"].Success && !m.Groups["inNum"].Success))
            throw new FormatException($"Cannot parse '{s}' as feet-inches (expected forms like \"1'-3\\\"\" or \"3 1/2\\\"\").");

        double ft = m.Groups["ft"].Success ? double.Parse(m.Groups["ft"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        double inWhole = m.Groups["inWhole"].Success ? double.Parse(m.Groups["inWhole"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        double inFrac = 0;
        if (m.Groups["inNum"].Success && m.Groups["inDen"].Success)
        {
            double num = double.Parse(m.Groups["inNum"].Value, System.Globalization.CultureInfo.InvariantCulture);
            double den = double.Parse(m.Groups["inDen"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (den == 0) throw new FormatException($"Cannot parse '{s}' — fraction denominator is zero.");
            inFrac = num / den;
        }

        return ft * 12.0 + inWhole + inFrac;
    }

    public override string ToString() => Text ?? (Number?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "0");
}

public class LengthJsonConverter : JsonConverter<Length>
{
    public override Length Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Number => new Length(reader.GetDouble()),
            JsonTokenType.String => new Length(reader.GetString()!),
            JsonTokenType.Null   => new Length(0),
            _ => throw new JsonException($"Length must be a number or a string, got {reader.TokenType}"),
        };

    public override void Write(Utf8JsonWriter writer, Length value, JsonSerializerOptions options)
    {
        if (value.Text is not null) writer.WriteStringValue(value.Text);
        else writer.WriteNumberValue(value.Number ?? 0);
    }
}
