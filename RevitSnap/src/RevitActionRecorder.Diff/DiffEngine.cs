using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitActionRecorder.Model;

namespace RevitActionRecorder.Diff;

/// <summary>
/// Движок сравнения снапшотов. Не зависит от Revit API — работает по JSON.
/// Элементные разделы сопоставляются по uid (запасной вариант — id), объекты
/// сравниваются рекурсивно, результат — списки added/removed/modified с путями {path, from, to}.
/// </summary>
public static class DiffEngine
{
    public static int SupportedSchemaVersion => Schema.Version;

    /// <summary>Разделы-массивы элементов, сопоставляемые по uid/id.</summary>
    private static readonly string[] ElementSections =
    [
        "types", "instances", "views", "sheets", "schedules",
    ];

    private static readonly JsonSerializerOptions OutOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Дифф пары файлов. Идёт потоком (см. <see cref="StreamingDiff"/>): снапшот реальной
    /// модели — это сотни МБ, и разбор обоих в дерево съедал бы гигабайты в процессе Revit.
    /// </summary>
    public static void DiffFiles(string beforePath, string afterPath, string outputPath) =>
        StreamingDiff.DiffFiles(beforePath, afterPath, outputPath);

    public static JsonNode? LoadJson(string path)
    {
        using Stream file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        return JsonNode.Parse(stream);
    }

    public static JsonObject Diff(JsonNode? beforeNode, JsonNode? afterNode)
    {
        var before = beforeNode as JsonObject ?? new JsonObject();
        var after = afterNode as JsonObject ?? new JsonObject();

        var result = new JsonObject
        {
            ["schema"] = Schema.Version,
            ["kind"] = "diff",
            ["beforeTakenAt"] = before["takenAt"]?.DeepClone(),
            ["afterTakenAt"] = after["takenAt"]?.DeepClone(),
        };

        var summary = new JsonObject();
        var sections = new JsonObject();

        foreach (var name in ElementSections)
        {
            var sectionDiff = DiffKeyedArray(before[name] as JsonArray, after[name] as JsonArray);
            sections[name] = sectionDiff.Node;
            summary[name] = new JsonObject
            {
                ["added"] = sectionDiff.Added,
                ["removed"] = sectionDiff.Removed,
                ["modified"] = sectionDiff.Modified,
            };
            if (name is "instances" or "types")
                ((JsonObject)summary[name]!)["byCategory"] = sectionDiff.ByCategory;
        }
        result["summary"] = summary;
        result["sections"] = sections;

        result["project"] = DiffToChanges("project", before["project"], after["project"]);
        result["categories"] = DiffToChanges("categories", before["categories"], after["categories"]);
        result["resources"] = DiffToChanges("resources", before["resources"], after["resources"]);
        result["warnings"] = DiffWarnings(before["warnings"] as JsonArray, after["warnings"] as JsonArray);

        return result;
    }

    // ---- элементные разделы ------------------------------------------------

    private sealed record SectionDiff(JsonObject Node, int Added, int Removed, int Modified, JsonObject ByCategory);

    private static SectionDiff DiffKeyedArray(JsonArray? before, JsonArray? after)
    {
        var beforeMap = BuildKeyMap(before);
        var afterMap = BuildKeyMap(after);

        var added = new JsonArray();
        var removed = new JsonArray();
        var modified = new JsonArray();
        var byCategory = new JsonObject();

        foreach (var (key, item) in afterMap)
        {
            if (!beforeMap.ContainsKey(key))
            {
                added.Add(Brief(item));
                Bump(byCategory, CategoryOf(item), "added");
            }
        }

        foreach (var (key, item) in beforeMap)
        {
            if (!afterMap.ContainsKey(key))
            {
                removed.Add(Brief(item));
                Bump(byCategory, CategoryOf(item), "removed");
            }
        }

        foreach (var (key, beforeItem) in beforeMap)
        {
            if (!afterMap.TryGetValue(key, out var afterItem))
                continue;
            var changes = new JsonArray();
            DiffNode("", beforeItem, afterItem, changes);
            if (changes.Count == 0)
                continue;
            var entry = Brief(afterItem);
            entry["changes"] = changes;
            modified.Add(entry);
            Bump(byCategory, CategoryOf(afterItem), "modified");
        }

        var node = new JsonObject
        {
            ["added"] = added,
            ["removed"] = removed,
            ["modified"] = modified,
        };
        return new SectionDiff(node, added.Count, removed.Count, modified.Count, byCategory);
    }

    private static Dictionary<string, JsonObject> BuildKeyMap(JsonArray? array)
    {
        var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        if (array is null)
            return map;
        int index = 0;
        foreach (var item in array)
        {
            if (item is JsonObject obj)
                map[KeyOf(obj, index)] = obj;
            index++;
        }
        return map;
    }

    private static string KeyOf(JsonObject obj, int index)
    {
        if (obj["uid"] is JsonValue uid) return "uid:" + uid;
        if (obj["id"] is JsonValue id) return "id:" + id;
        if (obj["pid"] is JsonValue pid) return "pid:" + pid;
        if (obj["spec"] is JsonValue spec) return "spec:" + spec;
        if (obj["name"] is JsonValue name) return "name:" + name;
        return "idx:" + index;
    }

    private static string CategoryOf(JsonObject obj) =>
        obj["cat"]?.GetValue<string>() ?? "(none)";

    private static void Bump(JsonObject byCategory, string category, string bucket)
    {
        if (byCategory[category] is not JsonObject entry)
            byCategory[category] = entry = new JsonObject();
        entry[bucket] = (entry[bucket]?.GetValue<int>() ?? 0) + 1;
    }

    private static JsonObject Brief(JsonObject item)
    {
        var brief = new JsonObject();
        foreach (var field in new[] { "id", "uid", "cat", "cls", "name", "number", "viewType" })
        {
            if (item[field] is JsonNode value)
                brief[field] = value.DeepClone();
        }
        return brief;
    }

    // ---- рекурсивное сравнение --------------------------------------------

    private static JsonArray DiffToChanges(string rootPath, JsonNode? before, JsonNode? after)
    {
        var changes = new JsonArray();
        DiffNode(rootPath, before, after, changes);
        return changes;
    }

    internal static void DiffNode(string path, JsonNode? before, JsonNode? after, JsonArray changes)
    {
        if (before is null && after is null)
            return;

        if (before is JsonObject beforeObj && after is JsonObject afterObj)
        {
            foreach (var key in beforeObj.Select(p => p.Key).Union(afterObj.Select(p => p.Key)).ToList())
                DiffNode(Combine(path, key), beforeObj[key], afterObj[key], changes);
            return;
        }

        if (before is JsonArray beforeArr && after is JsonArray afterArr)
        {
            if (IsKeyedObjectArray(beforeArr) || IsKeyedObjectArray(afterArr))
            {
                var beforeMap = BuildKeyMap(beforeArr);
                var afterMap = BuildKeyMap(afterArr);
                foreach (var key in beforeMap.Keys.Union(afterMap.Keys).ToList())
                {
                    beforeMap.TryGetValue(key, out var b);
                    afterMap.TryGetValue(key, out var a);
                    var itemPath = $"{path}[{key}]";
                    if (b is null || a is null)
                        AddChange(changes, itemPath, b, a);
                    else
                        DiffNode(itemPath, b, a, changes);
                }
                return;
            }

            if (!JsonEquals(beforeArr, afterArr))
                AddChange(changes, path, beforeArr, afterArr);
            return;
        }

        if (!JsonEquals(before, after))
            AddChange(changes, path, before, after);
    }

    private static bool IsKeyedObjectArray(JsonArray array) =>
        array.Count > 0
        && array[0] is JsonObject first
        && (first["uid"] is not null || first["id"] is not null || first["pid"] is not null
            || first["spec"] is not null || first["name"] is not null);

    private static string Combine(string path, string key) =>
        path.Length == 0 ? key : path + "." + key;

    private static bool JsonEquals(JsonNode? a, JsonNode? b) =>
        (a?.ToJsonString() ?? "null") == (b?.ToJsonString() ?? "null");

    private static void AddChange(JsonArray changes, string path, JsonNode? from, JsonNode? to) =>
        changes.Add(new JsonObject
        {
            ["path"] = path,
            ["from"] = from?.DeepClone(),
            ["to"] = to?.DeepClone(),
        });

    // ---- предупреждения ----------------------------------------------------

    private static JsonObject DiffWarnings(JsonArray? before, JsonArray? after)
    {
        static Dictionary<string, JsonObject> Map(JsonArray? array)
        {
            var map = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            if (array is null) return map;
            foreach (var item in array)
            {
                if (item is not JsonObject obj) continue;
                var key = (obj["description"]?.GetValue<string>() ?? "") + "|" + (obj["elements"]?.ToJsonString() ?? "");
                map[key] = obj;
            }
            return map;
        }

        var beforeMap = Map(before);
        var afterMap = Map(after);

        var added = new JsonArray();
        var removed = new JsonArray();
        foreach (var (key, item) in afterMap)
            if (!beforeMap.ContainsKey(key))
                added.Add(item.DeepClone());
        foreach (var (key, item) in beforeMap)
            if (!afterMap.ContainsKey(key))
                removed.Add(item.DeepClone());

        return new JsonObject
        {
            ["added"] = added,
            ["removed"] = removed,
        };
    }
}
