using System.Text.Json;
using RevitPlugin.Domain.Configs;

namespace RevitPlugin.Domain.Infrastructure;

/// <summary>
/// Чтение/запись конфигов <c>.wrsconfig.json</c>. Без знания о Revit и без
/// файловой системы — потребитель передаёт поток или строку. Тонкая обёртка
/// над System.Text.Json с настройками плагина (camelCase отключён, индентация, etc.).
/// </summary>
public static class ConfigStorage
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return opts;
    }

    public static RebarConfig FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Config JSON is empty.", nameof(json));

        var config = JsonSerializer.Deserialize<RebarConfig>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize RebarConfig.");

        if (config.SchemaVersion != RebarConfig.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Config schema_version={config.SchemaVersion} is not supported. " +
                $"Current version is {RebarConfig.CurrentSchemaVersion}.");
        }

        return config;
    }

    public static string ToJson(RebarConfig config) => JsonSerializer.Serialize(config, Options);

    public static RebarConfig Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<RebarConfig>(stream, Options)
            ?? throw new InvalidOperationException($"Failed to load config from {path}.");
    }

    public static void Save(RebarConfig config, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);

        File.WriteAllText(path, ToJson(config));
    }
}
