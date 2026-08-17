using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace RevitActionRecorder.Recording;

/// <summary>
/// Финализация events.jsonl при остановке: отменённым doc_changed проставляется undone:true.
/// Файл переписывается построчно во временный и атомарно подменяется; события не удаляются.
/// </summary>
internal static class EventsFinalizer
{
    public static int MarkUndone(string eventsPath, IReadOnlyCollection<long> undoneSeqs, SessionLog log)
    {
        if (undoneSeqs.Count == 0 || !File.Exists(eventsPath))
            return 0;

        var seqs = undoneSeqs as ISet<long> ?? new HashSet<long>(undoneSeqs);
        var tmpPath = eventsPath + ".tmp";
        int patched = 0;

        using (var writer = new StreamWriter(
                   new FileStream(tmpPath, FileMode.Create, FileAccess.Write),
                   new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            foreach (var line in File.ReadLines(eventsPath))
            {
                var outLine = line;
                try
                {
                    var node = JsonNode.Parse(line);
                    if (node?["kind"]?.GetValue<string>() == "doc_changed"
                        && node["seq"] is JsonNode seqNode
                        && seqs.Contains(seqNode.GetValue<long>()))
                    {
                        node["undone"] = true;
                        outLine = node.ToJsonString(RarJson.Line);
                        patched++;
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"finalize: line left as-is ({ex.Message})");
                }
                writer.WriteLine(outLine);
            }
        }

        File.Move(tmpPath, eventsPath, overwrite: true);
        return patched;
    }
}
