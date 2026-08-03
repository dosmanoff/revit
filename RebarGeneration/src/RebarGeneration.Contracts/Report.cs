using System.Text.Json.Serialization;

namespace RebarGeneration.Contracts;

/// <summary>
/// Отчёт о прогоне — то, что возвращается агенту.
/// <para>
/// Устроен так, чтобы быть ДЕШЁВЫМ в токенах: агрегаты, по одной строке на
/// группу, и никаких списков ElementId, если их явно не попросили. Карта
/// <c>key→ElementId</c> нужна документации, а не контексту, поэтому уезжает в
/// файл, а в отчёте остаётся только путь к нему.
/// </para>
/// </summary>
public sealed class RunReport
{
    public const string CurrentSchema = "rebar-report-1.0";

    [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
    [JsonPropertyName("schema")] public string Schema { get; set; } = CurrentSchema;

    /// <summary><c>apply</c> | <c>dry-run</c>.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "apply";

    [JsonPropertyName("document")] public string? Document { get; set; }
    [JsonPropertyName("elapsedMs")] public long ElapsedMs { get; set; }
    [JsonPropertyName("transactions")] public int Transactions { get; set; }

    [JsonPropertyName("counts")] public RunCounts Counts { get; set; } = new();

    [JsonPropertyName("totalLengthFt")] public double TotalLengthFt { get; set; }

    [JsonPropertyName("groups")] public List<GroupReport> Groups { get; set; } = [];

    /// <summary>Куда записана карта <c>key→ElementId</c>.</summary>
    [JsonPropertyName("mapFile")] public string? MapFile { get; set; }

    [JsonPropertyName("errors")] public List<ReportError> Errors { get; set; } = [];

    /// <summary>Замечания, не сорвавшие прогон (проглоченные предупреждения, отвергнутые layout'ы).</summary>
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];

    public static RunReport Failure(string code, string message) => new()
    {
        Ok = false,
        Errors = [new ReportError { Code = code, Message = message }],
    };
}

public sealed class RunCounts
{
    [JsonPropertyName("groups")] public int Groups { get; set; }
    [JsonPropertyName("sets")] public int Sets { get; set; }
    /// <summary>Стержней с учётом раскладки наборов — то, что попадёт в спецификацию.</summary>
    [JsonPropertyName("bars")] public int Bars { get; set; }
    [JsonPropertyName("skipped")] public int Skipped { get; set; }
    [JsonPropertyName("replaced")] public int Replaced { get; set; }
    [JsonPropertyName("failed")] public int Failed { get; set; }
}

/// <summary><c>created</c> | <c>skipped</c> | <c>replaced</c> | <c>failed</c>.</summary>
public sealed class GroupReport
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("generator")] public string Generator { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "created";
    [JsonPropertyName("hostId")] public long? HostId { get; set; }
    [JsonPropertyName("sets")] public int Sets { get; set; }
    [JsonPropertyName("bars")] public int Bars { get; set; }
    [JsonPropertyName("lengthFt")] public double LengthFt { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }

    /// <summary>Заполняется только при <c>policy.includeIds = true</c>.</summary>
    [JsonPropertyName("elementIds")] public List<long>? ElementIds { get; set; }
}

public sealed class ReportError
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    [JsonPropertyName("key")] public string? Key { get; set; }
}
