using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RevitActionRecorder.Model;

namespace RevitActionRecorder.Diff;

/// <summary>
/// Дифф двух файлов снапшота с ограниченной памятью: снапшот на 300 МБ не разбирается
/// в дерево целиком. Три прохода:
///   1) «до»    — ключ элемента → хеш его JSON (несколько МБ на 144k элементов);
///   2) «после» — added / unchanged / кандидаты-в-изменённые (в памяти оседают только изменённые);
///   3) «до»    — подтягиваем прежний JSON только для кандидатов и удалённых, считаем поля.
/// Небольшие разделы (project, categories, resources, warnings) сравниваются деревом.
/// </summary>
public static class StreamingDiff
{
    private static readonly string[] ElementSections =
    [
        "types", "instances", "views", "sheets", "schedules",
    ];

    private static readonly JsonSerializerOptions OutOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class SectionState
    {
        public Dictionary<string, byte[]> BeforeHashes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> SeenInAfter { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ChangedAfterJson { get; } = new(StringComparer.Ordinal);
        public JsonArray Added { get; } = new();
        public JsonArray Removed { get; } = new();
        public JsonArray Modified { get; } = new();
        public JsonObject ByCategory { get; } = new();
    }

    public static JsonObject DiffFiles(string beforePath, string afterPath, string outputPath)
    {
        var result = Diff(beforePath, afterPath);
        using var output = File.Create(outputPath);
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        result.WriteTo(writer);
        writer.Flush();
        return result;
    }

    public static JsonObject Diff(string beforePath, string afterPath)
    {
        var sections = ElementSections.ToDictionary(name => name, _ => new SectionState(), StringComparer.Ordinal);
        var sectionNames = new HashSet<string>(ElementSections, StringComparer.Ordinal);
        var beforeOther = new Dictionary<string, string>(StringComparer.Ordinal);
        var afterOther = new Dictionary<string, string>(StringComparer.Ordinal);

        // Проход 1: хеши элементов «до».
        SnapshotScanner.Scan(beforePath, sectionNames,
            (section, json) => sections[section].BeforeHashes[KeyOf(json)] = Hash(json),
            (name, json) => beforeOther[name] = json);

        // Проход 2: «после» — added / unchanged / кандидаты.
        SnapshotScanner.Scan(afterPath, sectionNames,
            (section, json) =>
            {
                var state = sections[section];
                var key = KeyOf(json);
                state.SeenInAfter.Add(key);
                if (!state.BeforeHashes.TryGetValue(key, out var beforeHash))
                {
                    var node = Parse(json);
                    state.Added.Add(Brief(node));
                    Bump(state.ByCategory, CategoryOf(node), "added");
                    return;
                }
                if (!beforeHash.AsSpan().SequenceEqual(Hash(json)))
                    state.ChangedAfterJson[key] = json;
            },
            (name, json) => afterOther[name] = json);

        // Проход 3: прежние версии изменённых + карточки удалённых.
        var removedKeys = sections.ToDictionary(
            kv => kv.Key,
            kv => new HashSet<string>(kv.Value.BeforeHashes.Keys.Where(k => !kv.Value.SeenInAfter.Contains(k)), StringComparer.Ordinal),
            StringComparer.Ordinal);

        SnapshotScanner.Scan(beforePath, sectionNames,
            (section, json) =>
            {
                var state = sections[section];
                var key = KeyOf(json);

                if (removedKeys[section].Contains(key))
                {
                    var removedNode = Parse(json);
                    state.Removed.Add(Brief(removedNode));
                    Bump(state.ByCategory, CategoryOf(removedNode), "removed");
                    return;
                }

                if (!state.ChangedAfterJson.TryGetValue(key, out var afterJson))
                    return;

                var before = Parse(json);
                var after = Parse(afterJson);
                var changes = new JsonArray();
                DiffEngine.DiffNode("", before, after, changes);
                if (changes.Count == 0)
                    return;

                var entry = Brief(after);
                entry["changes"] = changes;
                state.Modified.Add(entry);
                Bump(state.ByCategory, CategoryOf(after), "modified");
            },
            (_, _) => { });

        // Сборка результата.
        var beforeRoot = ParseRootScalars(beforePath);
        var afterRoot = ParseRootScalars(afterPath);

        var diff = new JsonObject
        {
            ["schema"] = Schema.Version,
            ["kind"] = "diff",
            ["beforeTakenAt"] = beforeRoot,
            ["afterTakenAt"] = afterRoot,
        };

        var summary = new JsonObject();
        var sectionsNode = new JsonObject();
        foreach (var name in ElementSections)
        {
            var state = sections[name];
            sectionsNode[name] = new JsonObject
            {
                ["added"] = state.Added,
                ["removed"] = state.Removed,
                ["modified"] = state.Modified,
            };
            var entry = new JsonObject
            {
                ["added"] = state.Added.Count,
                ["removed"] = state.Removed.Count,
                ["modified"] = state.Modified.Count,
            };
            if (name is "instances" or "types")
                entry["byCategory"] = state.ByCategory;
            summary[name] = entry;
        }
        diff["summary"] = summary;
        diff["sections"] = sectionsNode;

        diff["project"] = DiffOther(beforeOther, afterOther, "project");
        diff["categories"] = DiffOther(beforeOther, afterOther, "categories");
        diff["resources"] = DiffOther(beforeOther, afterOther, "resources");
        diff["warnings"] = DiffWarnings(beforeOther, afterOther);

        return diff;
    }

    // ---- вспомогательное ---------------------------------------------------

    private static JsonObject Parse(string json) =>
        JsonNode.Parse(json) as JsonObject ?? new JsonObject();

    private static byte[] Hash(string json) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(json));

    private static string KeyOf(string json)
    {
        // Ключ достаём потоково, не строя дерево: uid → id → number → name.
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        string? id = null, uid = null, number = null, name = null;
        int depth = 0;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    depth++;
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    depth--;
                    break;
                case JsonTokenType.PropertyName when depth == 1:
                {
                    var prop = reader.GetString();
                    if (prop is "uid" or "id" or "number" or "name")
                    {
                        reader.Read();
                        var value = reader.TokenType switch
                        {
                            JsonTokenType.String => reader.GetString(),
                            JsonTokenType.Number => reader.GetInt64().ToString(),
                            _ => null,
                        };
                        switch (prop)
                        {
                            case "uid": uid = value; break;
                            case "id": id = value; break;
                            case "number": number = value; break;
                            case "name": name = value; break;
                        }
                    }
                    else
                    {
                        reader.Read();
                        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                            reader.Skip();
                    }
                    break;
                }
            }
        }
        if (uid is not null) return "uid:" + uid;
        if (id is not null) return "id:" + id;
        if (number is not null) return "number:" + number;
        if (name is not null) return "name:" + name;
        return "hash:" + Convert.ToHexString(Hash(json));
    }

    private static JsonObject Brief(JsonObject item)
    {
        var brief = new JsonObject();
        foreach (var field in new[] { "id", "uid", "cat", "cls", "name", "number", "viewType" })
            if (item[field] is JsonNode value)
                brief[field] = value.DeepClone();
        return brief;
    }

    private static string CategoryOf(JsonObject item) =>
        item["cat"]?.GetValue<string>() ?? "(none)";

    private static void Bump(JsonObject byCategory, string category, string bucket)
    {
        if (byCategory[category] is not JsonObject entry)
            byCategory[category] = entry = new JsonObject();
        entry[bucket] = (entry[bucket]?.GetValue<int>() ?? 0) + 1;
    }

    private static JsonArray DiffOther(
        Dictionary<string, string> before, Dictionary<string, string> after, string name)
    {
        var changes = new JsonArray();
        before.TryGetValue(name, out var b);
        after.TryGetValue(name, out var a);
        if (b is null && a is null)
            return changes;
        DiffEngine.DiffNode(name, b is null ? null : JsonNode.Parse(b), a is null ? null : JsonNode.Parse(a), changes);
        return changes;
    }

    private static JsonObject DiffWarnings(
        Dictionary<string, string> before, Dictionary<string, string> after)
    {
        static Dictionary<string, JsonNode> Map(Dictionary<string, string> source)
        {
            var map = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            if (!source.TryGetValue("warnings", out var json) || JsonNode.Parse(json) is not JsonArray array)
                return map;
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
            if (!beforeMap.ContainsKey(key)) added.Add(item.DeepClone());
        foreach (var (key, item) in beforeMap)
            if (!afterMap.ContainsKey(key)) removed.Add(item.DeepClone());
        return new JsonObject { ["added"] = added, ["removed"] = removed };
    }

    /// <summary>Читает только скалярное takenAt из шапки файла, не разбирая тело.</summary>
    private static JsonNode? ParseRootScalars(string path)
    {
        string? takenAt = null;
        try
        {
            using var file = File.OpenRead(path);
            using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress)
                : file;
            var head = new byte[4096];
            int read = stream.Read(head, 0, head.Length);
            var text = Encoding.UTF8.GetString(head, 0, read);
            const string marker = "\"takenAt\":\"";
            var start = text.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                start += marker.Length;
                var end = text.IndexOf('"', start);
                if (end > start) takenAt = text[start..end];
            }
        }
        catch
        {
        }

        return takenAt is null ? null : JsonValue.Create(takenAt);
    }
}
