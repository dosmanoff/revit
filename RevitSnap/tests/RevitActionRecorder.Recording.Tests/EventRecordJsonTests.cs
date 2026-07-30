using System.Text.Json;
using RevitActionRecorder.Model;
using RevitActionRecorder.Recording;
using Xunit;

namespace RevitActionRecorder.Recording.Tests;

public class EventRecordJsonTests
{
    [Fact]
    public void Serializes_single_line_camel_case_without_nulls()
    {
        var record = new EventRecord
        {
            Seq = 312,
            T = "2026-07-25T14:03:11.482+03:00",
            Kind = "doc_changed",
            Op = "TransactionCommitted",
            Undone = false,
            Txn = ["Разместить стену"],
            Cmd = new CommandRef { Id = "ID_OBJECTS_WALL", Title = "Стена" },
            View = new ViewRef { Id = 1204, Name = "1 этаж", Type = "FloorPlan" },
            Added = [new ElementBrief { Id = 845201, Cat = "Стены", Type = "Внутренняя 200", Level = "Уровень 2" }],
            Deleted = [],
        };

        var line = JsonSerializer.Serialize(record, RarJson.Line);

        Assert.DoesNotContain('\n', line);
        Assert.StartsWith("{\"schema\":1,\"seq\":312,", line);
        Assert.Contains("\"kind\":\"doc_changed\"", line);
        Assert.Contains("\"undone\":false", line);
        Assert.Contains("Разместить стену", line);
        Assert.Contains("\"cmd\":{\"id\":\"ID_OBJECTS_WALL\",\"title\":\"Стена\"}", line);
        Assert.DoesNotContain("null", line);
        Assert.DoesNotContain("modified", line);
        Assert.Contains("\"deleted\":[]", line);
    }

    [Fact]
    public void Manifest_serializes_schema_first()
    {
        var manifest = new SessionManifest { RevitVersion = "2025" };

        var json = JsonSerializer.Serialize(manifest, RarJson.Line);

        Assert.StartsWith("{\"schema\":1,", json);
    }
}
