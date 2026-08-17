using System.IO;
using System.Text.Json.Nodes;
using RevitActionRecorder.Recording;
using Xunit;

namespace RevitActionRecorder.Recording.Tests;

public class EventsFinalizerTests
{
    [Fact]
    public void Marks_only_listed_doc_changed_lines()
    {
        var dir = Directory.CreateTempSubdirectory("rar-tests").FullName;
        var path = Path.Combine(dir, "events.jsonl");
        File.WriteAllLines(path,
        [
            """{"schema":1,"seq":1,"kind":"doc_changed","undone":false,"txn":["Стена"]}""",
            """{"schema":1,"seq":2,"kind":"command","cmd":{"id":"ID_X"}}""",
            """{"schema":1,"seq":3,"kind":"doc_changed","undone":false,"txn":["Перекрытие"]}""",
        ]);
        var log = new SessionLog(Path.Combine(dir, "log.txt"));

        var patched = EventsFinalizer.MarkUndone(path, new HashSet<long> { 3 }, log);

        Assert.Equal(1, patched);
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.False(JsonNode.Parse(lines[0])!["undone"]!.GetValue<bool>());
        Assert.Null(JsonNode.Parse(lines[1])!["undone"]);
        Assert.True(JsonNode.Parse(lines[2])!["undone"]!.GetValue<bool>());
        Assert.Equal(["Перекрытие"], JsonNode.Parse(lines[2])!["txn"]!.AsArray().Select(n => n!.GetValue<string>()));

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void No_undone_seqs_leaves_file_untouched()
    {
        var dir = Directory.CreateTempSubdirectory("rar-tests").FullName;
        var path = Path.Combine(dir, "events.jsonl");
        File.WriteAllText(path, """{"schema":1,"seq":1,"kind":"doc_changed","undone":false}""" + Environment.NewLine);
        var before = File.ReadAllText(path);
        var log = new SessionLog(Path.Combine(dir, "log.txt"));

        var patched = EventsFinalizer.MarkUndone(path, Array.Empty<long>(), log);

        Assert.Equal(0, patched);
        Assert.Equal(before, File.ReadAllText(path));

        Directory.Delete(dir, recursive: true);
    }
}
