using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace WallReinforcement.Config;

public enum UnitSystem
{
    /// <summary>Plain numbers in JSON are millimetres.</summary>
    Metric,
    /// <summary>Plain numbers in JSON are inches. String values may use feet-inches syntax.</summary>
    Imperial,
}

/// <summary>
/// A length value in a config file. Can be either a plain number (interpreted per the
/// top-level <see cref="ReinforcementConfig.Units"/>) or a string in feet-inches syntax
/// (always parsed unambiguously regardless of <c>units</c>).
///
/// Accepted string forms: <c>"1'-3""</c>, <c>"1'3""</c>, <c>"1'-3 1/2""</c>, <c>"3""</c>,
/// <c>"3 1/2""</c>, <c>"1'"</c>. Whitespace and the dash between feet and inches are optional.
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

    /// <summary>
    /// Parses a feet-inches string and returns total inches. Delegates to the Revit-free
    /// <see cref="WallReinforcement.Geometry.FeetInches"/> parser, which is unit-tested.
    /// </summary>
    public static double ParseFeetInches(string s) => WallReinforcement.Geometry.FeetInches.ParseToInches(s);

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
