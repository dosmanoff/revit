using System.Text;
using System.Text.Json.Nodes;
using RevitActionRecorder.Diff;
using Xunit;

namespace RevitActionRecorder.Diff.Tests;

public class ParamNamesInDiffTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rar-pnames").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string instances, string? paramDefs)
    {
        var defs = paramDefs is null ? "" : ",\"paramDefs\":" + paramDefs;
        var json =
            "{\"schema\":2,\"kind\":\"snapshot\",\"takenAt\":\"2026-07-30T20:00:00.000+02:00\"," +
            "\"project\":{},\"types\":[],\"instances\":" + instances +
            ",\"views\":[],\"sheets\":[],\"schedules\":[],\"warnings\":[]" + defs + "}";
        var path = Path.Combine(_dir, name + ".json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void Change_paths_are_annotated_with_the_parameter_name()
    {
        var defs = "{\"-1006521\":{\"name\":\"Total Length\",\"bip\":\"DIM_TOTAL_LENGTH\",\"storage\":\"Double\"}}";
        var before = Write("b", "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Dimensions\",\"params\":[{\"pid\":-1006521,\"display\":\"38' - 5 1/2\\\"\"}]}]", defs);
        var after = Write("a", "[{\"id\":1,\"uid\":\"a\",\"cat\":\"Dimensions\",\"params\":[{\"pid\":-1006521,\"display\":\"28' - 3 13/16\\\"\"}]}]", defs);

        var diff = StreamingDiff.Diff(before, after);

        var change = diff["sections"]!["instances"]!["modified"]![0]!["changes"]![0]!;
        Assert.Equal("params[pid:-1006521].display", change["path"]!.GetValue<string>());
        Assert.Equal("Total Length", change["param"]!.GetValue<string>());
        Assert.Equal("DIM_TOTAL_LENGTH", change["bip"]!.GetValue<string>());
    }

    [Fact]
    public void Snapshots_without_the_dictionary_still_diff()
    {
        // Схема 1: метаданные лежали в каждом параметре, словаря нет — имя просто не подставляется.
        var before = Write("b", "[{\"id\":1,\"uid\":\"a\",\"params\":[{\"pid\":10,\"name\":\"x\",\"raw\":\"0\"}]}]", null);
        var after = Write("a", "[{\"id\":1,\"uid\":\"a\",\"params\":[{\"pid\":10,\"name\":\"x\",\"raw\":\"1\"}]}]", null);

        var diff = StreamingDiff.Diff(before, after);

        var change = diff["sections"]!["instances"]!["modified"]![0]!["changes"]![0]!;
        Assert.Equal("params[pid:10].raw", change["path"]!.GetValue<string>());
        Assert.Null(change["param"]);
    }
}
