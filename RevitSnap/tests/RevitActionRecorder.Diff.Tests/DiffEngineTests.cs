using System.Text.Json.Nodes;
using RevitActionRecorder.Diff;
using Xunit;

namespace RevitActionRecorder.Diff.Tests;

public class DiffEngineTests
{
    private static JsonNode Snap(string instances, string warnings = "[]", string project = "{}") =>
        JsonNode.Parse($$"""
        {
          "schema": 1,
          "kind": "snapshot",
          "takenAt": "2026-07-25T22:00:00.000+03:00",
          "project": {{project}},
          "instances": {{instances}},
          "warnings": {{warnings}}
        }
        """)!;

    [Fact]
    public void Added_and_removed_elements_are_detected()
    {
        var before = Snap("""[{"id":1,"uid":"a","cat":"Стены","name":"W1","params":[]}]""");
        var after = Snap("""[{"id":2,"uid":"b","cat":"Стены","name":"W2","params":[]}]""");

        var diff = DiffEngine.Diff(before, after);

        var instances = diff["sections"]!["instances"]!;
        Assert.Single(instances["added"]!.AsArray());
        Assert.Single(instances["removed"]!.AsArray());
        Assert.Empty(instances["modified"]!.AsArray());
        Assert.Equal("b", instances["added"]![0]!["uid"]!.GetValue<string>());
        Assert.Equal(1, diff["summary"]!["instances"]!["added"]!.GetValue<int>());
        Assert.Equal(1, diff["summary"]!["instances"]!["byCategory"]!["Стены"]!["added"]!.GetValue<int>());
    }

    [Fact]
    public void Param_change_produces_path_from_to()
    {
        var before = Snap("""[{"id":1,"uid":"a","cat":"Стены","params":[{"pid":10,"name":"Смещение сверху","raw":"0"}]}]""");
        var after = Snap("""[{"id":1,"uid":"a","cat":"Стены","params":[{"pid":10,"name":"Смещение сверху","raw":"-150"}]}]""");

        var diff = DiffEngine.Diff(before, after);

        var modified = diff["sections"]!["instances"]!["modified"]!.AsArray();
        Assert.Single(modified);
        var changes = modified[0]!["changes"]!.AsArray();
        Assert.Single(changes);
        Assert.Equal("params[pid:10].raw", changes[0]!["path"]!.GetValue<string>());
        Assert.Equal("0", changes[0]!["from"]!.GetValue<string>());
        Assert.Equal("-150", changes[0]!["to"]!.GetValue<string>());
    }

    [Fact]
    public void Geometry_hash_change_is_reported()
    {
        var before = Snap("""[{"id":1,"uid":"a","geomHash":"aaa","params":[]}]""");
        var after = Snap("""[{"id":1,"uid":"a","geomHash":"bbb","params":[]}]""");

        var diff = DiffEngine.Diff(before, after);

        var changes = diff["sections"]!["instances"]!["modified"]![0]!["changes"]!.AsArray();
        Assert.Contains(changes, c => c!["path"]!.GetValue<string>() == "geomHash");
    }

    [Fact]
    public void Warnings_diff_by_description_and_elements()
    {
        var before = Snap("[]", warnings: """[{"description":"Перекрытие пересекается","elements":[5]}]""");
        var after = Snap("[]", warnings: """[{"description":"Стена не ограничена","elements":[7]}]""");

        var diff = DiffEngine.Diff(before, after);

        Assert.Single(diff["warnings"]!["added"]!.AsArray());
        Assert.Single(diff["warnings"]!["removed"]!.AsArray());
        Assert.Equal("Стена не ограничена", diff["warnings"]!["added"]![0]!["description"]!.GetValue<string>());
    }

    [Fact]
    public void Project_scalar_change_gets_full_path()
    {
        var before = Snap("[]", project: """{"units":[{"spec":"autodesk.spec.aec:length-2.0.0","name":"L","accuracy":0.01}]}""");
        var after = Snap("[]", project: """{"units":[{"spec":"autodesk.spec.aec:length-2.0.0","name":"L","accuracy":0.001}]}""");

        var diff = DiffEngine.Diff(before, after);

        var changes = diff["project"]!.AsArray();
        Assert.Single(changes);
        Assert.Equal("project.units[spec:autodesk.spec.aec:length-2.0.0].accuracy", changes[0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public void Identical_snapshots_produce_empty_diff()
    {
        var json = """[{"id":1,"uid":"a","cat":"Стены","params":[{"pid":1,"name":"x","raw":"1"}]}]""";
        var diff = DiffEngine.Diff(Snap(json), Snap(json));

        Assert.Empty(diff["sections"]!["instances"]!["added"]!.AsArray());
        Assert.Empty(diff["sections"]!["instances"]!["removed"]!.AsArray());
        Assert.Empty(diff["sections"]!["instances"]!["modified"]!.AsArray());
        Assert.Empty(diff["project"]!.AsArray());
    }
}
