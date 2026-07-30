using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitActionRecorder.Recording;

internal static class RarJson
{
    /// <summary>Однострочный JSON для events.jsonl: camelCase, без null, кириллица без эскейпа.</summary>
    public static readonly JsonSerializerOptions Line = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Indented = new(Line)
    {
        WriteIndented = true,
    };
}
