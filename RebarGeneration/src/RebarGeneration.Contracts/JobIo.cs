using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RebarGeneration.Contracts;

/// <summary>
/// Чтение задания и сериализация отчёта. Отдельный класс, потому что настройки
/// JSON должны быть ОДНИ и те же на входе и выходе: агент пишет задание руками,
/// поэтому регистр имён не должен иметь значения, а комментарии и висящие
/// запятые не должны валить разбор.
/// </summary>
public static class JobIo
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Кириллица в reason/message должна остаться читаемой, а не \uXXXX.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Job Parse(string json)
    {
        Job? job;
        try
        {
            job = JsonSerializer.Deserialize<Job>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new JobException("BAD_JSON", $"задание не разобралось: {ex.Message}");
        }
        return job ?? throw new JobException("NO_JOB", "задание пустое");
    }

    public static Job Load(string path)
    {
        if (!File.Exists(path))
            throw new JobException("NO_FILE", $"файл задания не найден: {path}");
        return Parse(File.ReadAllText(path));
    }

    public static string ToJson(RunReport report) => JsonSerializer.Serialize(report, WriteOptions);

    public static string ToJson(object value) => JsonSerializer.Serialize(value, WriteOptions);

    /// <summary>
    /// Записать карту <c>key→ElementId</c> рядом с заданием. Отдельным файлом, а
    /// не в ответе: карта нужна следующим стадиям (документация, теги), но в
    /// контексте агента она только жжёт токены.
    /// </summary>
    public static string WriteMap(string? explicitPath, string? jobPath, IReadOnlyDictionary<string, long> map)
    {
        string path = explicitPath ?? DefaultMapPath(jobPath);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson(map));
        return path;
    }

    private static string DefaultMapPath(string? jobPath)
    {
        if (!string.IsNullOrWhiteSpace(jobPath))
        {
            string dir = Path.GetDirectoryName(jobPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(jobPath);
            return Path.Combine(dir, name + "-map.json");
        }
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitPlugin", "RebarGeneration");
        return Path.Combine(fallback, "rebar-map.json");
    }
}
