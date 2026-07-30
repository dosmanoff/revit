using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using RevitActionRecorder.Diff;
using Xunit;

namespace RevitActionRecorder.Diff.Tests;

public class StreamingDiffTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rar-sdiff").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteSnapshot(
        string name, string instances, string warnings = "[]", string project = "{}", bool gzip = false)
    {
        var json =
            "{\"schema\":1,\"kind\":\"snapshot\",\"takenAt\":\"2026-07-30T18:00:00.000+02:00\"," +
            "\"model\":{\"title\":\"T\"},\"project\":" + project + ",\"categories\":[],\"resources\":{}," +
            "\"types\":[],\"instances\":" + instances + ",\"views\":[],\"sheets\":[],\"schedules\":[]," +
            "\"legends\":[],\"warnings\":" + warnings + "}";

        var path = Path.Combine(_dir, name + (gzip ? ".json.gz" : ".json"));
        if (gzip)
        {
            using var file = File.Create(path);
            using var gz = new GZipStream(file, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(json));
        }
        else
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        return path;
    }

    [Fact]
    public void Detects_added_removed_and_param_change()
    {
        var before = WriteSnapshot("b",
            "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[{\"pid\":10,\"name\":\"Смещение\",\"raw\":\"0\"}]}," +
            "{\"id\":2,\"uid\":\"gone\",\"cat\":\"Оси\",\"params\":[]}]");
        var after = WriteSnapshot("a",
            "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[{\"pid\":10,\"name\":\"Смещение\",\"raw\":\"-150\"}]}," +
            "{\"id\":3,\"uid\":\"new\",\"cat\":\"Стены\",\"params\":[]}]");

        var diff = StreamingDiff.Diff(before, after);

        var instances = diff["sections"]!["instances"]!;
        Assert.Equal("new", instances["added"]![0]!["uid"]!.GetValue<string>());
        Assert.Equal("gone", instances["removed"]![0]!["uid"]!.GetValue<string>());
        var changes = instances["modified"]![0]!["changes"]!.AsArray();
        Assert.Equal("params[pid:10].raw", changes[0]!["path"]!.GetValue<string>());
        Assert.Equal("0", changes[0]!["from"]!.GetValue<string>());
        Assert.Equal("-150", changes[0]!["to"]!.GetValue<string>());
        Assert.Equal(1, diff["summary"]!["instances"]!["byCategory"]!["Стены"]!["added"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["instances"]!["byCategory"]!["Оси"]!["removed"]!.GetValue<int>());
    }

    [Fact]
    public void Identical_snapshots_produce_no_changes()
    {
        var json = "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[{\"pid\":1,\"name\":\"x\",\"raw\":\"1\"}]}]";
        var before = WriteSnapshot("b", json);
        var after = WriteSnapshot("a", json);

        var diff = StreamingDiff.Diff(before, after);

        Assert.Empty(diff["sections"]!["instances"]!["added"]!.AsArray());
        Assert.Empty(diff["sections"]!["instances"]!["removed"]!.AsArray());
        Assert.Empty(diff["sections"]!["instances"]!["modified"]!.AsArray());
        Assert.Empty(diff["project"]!.AsArray());
        Assert.Equal("2026-07-30T18:00:00.000+02:00", diff["beforeTakenAt"]!.GetValue<string>());
    }

    [Fact]
    public void Reads_gzipped_snapshots()
    {
        var before = WriteSnapshot("b", "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[]}]", gzip: true);
        var after = WriteSnapshot("a",
            "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[]},{\"id\":2,\"uid\":\"b\",\"cat\":\"Стены\",\"params\":[]}]",
            gzip: true);

        var diff = StreamingDiff.Diff(before, after);

        Assert.Single(diff["sections"]!["instances"]!["added"]!.AsArray());
    }

    [Fact]
    public void Project_and_warnings_sections_are_compared()
    {
        var before = WriteSnapshot("b", "[]",
            warnings: "[{\"description\":\"Старое предупреждение\",\"elements\":[5]}]",
            project: "{\"info\":{\"number\":\"001\"}}");
        var after = WriteSnapshot("a", "[]",
            warnings: "[{\"description\":\"Новое предупреждение\",\"elements\":[7]}]",
            project: "{\"info\":{\"number\":\"002\"}}");

        var diff = StreamingDiff.Diff(before, after);

        Assert.Single(diff["warnings"]!["added"]!.AsArray());
        Assert.Single(diff["warnings"]!["removed"]!.AsArray());
        var change = diff["project"]!.AsArray()[0]!;
        Assert.Equal("project.info.number", change["path"]!.GetValue<string>());
        Assert.Equal("002", change["to"]!.GetValue<string>());
    }

    [Fact]
    public void Matches_in_memory_engine_on_the_same_input()
    {
        var before = WriteSnapshot("b",
            "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"geomHash\":\"aaa\"," +
            "\"params\":[{\"pid\":10,\"name\":\"x\",\"raw\":\"0\"}]}]");
        var after = WriteSnapshot("a",
            "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"geomHash\":\"bbb\"," +
            "\"params\":[{\"pid\":10,\"name\":\"x\",\"raw\":\"1\"}]},{\"id\":2,\"uid\":\"b\",\"cat\":\"Оси\",\"params\":[]}]");

        var streamed = StreamingDiff.Diff(before, after);
        var inMemory = DiffEngine.Diff(DiffEngine.LoadJson(before), DiffEngine.LoadJson(after));

        Assert.Equal(
            inMemory["sections"]!["instances"]!.ToJsonString(),
            streamed["sections"]!["instances"]!.ToJsonString());
    }

    [Fact]
    public void Element_larger_than_initial_buffer_is_handled()
    {
        // Элемент заведомо больше стартового буфера сканера (128 КБ) — проверяем дорост буфера.
        var padding = new string('x', 40);
        var bigParams = string.Join(",", Enumerable.Range(0, 4000)
            .Select(i => "{\"pid\":" + i + ",\"name\":\"Параметр " + i + "\",\"raw\":\"" + padding + "\"}"));
        var big = "{\"id\":1,\"uid\":\"big\",\"cat\":\"Стены\",\"params\":[" + bigParams + "]}";

        var before = WriteSnapshot("b", "[" + big + "]");
        var after = WriteSnapshot("a", "[" + big + ",{\"id\":2,\"uid\":\"s\",\"cat\":\"Оси\",\"params\":[]}]");

        var diff = StreamingDiff.Diff(before, after);

        Assert.Empty(diff["sections"]!["instances"]!["modified"]!.AsArray());
        Assert.Single(diff["sections"]!["instances"]!["added"]!.AsArray());
    }

    [Fact]
    public void DiffFiles_writes_output_json()
    {
        var before = WriteSnapshot("b", "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Стены\",\"params\":[]}]");
        var after = WriteSnapshot("a", "[]");
        var output = Path.Combine(_dir, "diff.json");

        DiffEngine.DiffFiles(before, after, output);

        var written = JsonNode.Parse(File.ReadAllText(output))!;
        Assert.Equal(1, written["summary"]!["instances"]!["removed"]!.GetValue<int>());
    }
}
