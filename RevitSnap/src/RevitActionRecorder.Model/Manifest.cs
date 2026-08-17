using System.Text.Json.Serialization;

namespace RevitActionRecorder.Model;

/// <summary>manifest.json — паспорт сессии записи.</summary>
public sealed class SessionManifest
{
    [JsonPropertyName("schema")]
    public int SchemaVersion { get; set; } = Schema.Version;

    public string? RevitVersion { get; set; }
    public string? RevitBuild { get; set; }
    public string? AddinVersion { get; set; }

    public string? ModelTitle { get; set; }
    public string? ModelPath { get; set; }
    public bool? Workshared { get; set; }

    public string? RevitUser { get; set; }
    public string? WindowsUser { get; set; }
    public string? Machine { get; set; }

    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }

    public IReadOnlyDictionary<string, long>? EventCounts { get; set; }

    /// <summary>Сколько doc_changed-событий помечено undone:true при финализации.</summary>
    public long? UndoneMarked { get; set; }
}
