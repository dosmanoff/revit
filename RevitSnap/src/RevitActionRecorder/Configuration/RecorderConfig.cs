using System.IO;
using System.Text.Json;
using RevitActionRecorder.Infrastructure;

namespace RevitActionRecorder.Configuration;

/// <summary>
/// config.json рядом со сборкой. Отсутствие файла или ошибка чтения — молча дефолты
/// (аддин не имеет права мешать работе из-за конфига). Окно Settings — этап 8.
/// </summary>
public sealed class RecorderConfig
{
    public string OutputRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RevitActionRecorder");

    /// <summary>
    /// Порог корреляции «нажатая кнопка ленты → DocumentChanged», мс.
    /// ТЗ предлагало 2000, но на живом замере первая транзакция после клика по инструменту
    /// пришла через 2.1 с (и дальше инструмент рисовал ещё несколько секунд), так что при
    /// 2000 мс связь терялась целиком. Возраст привязки пишется в cmd.ageMs.
    /// </summary>
    public int CommandCorrelationMs { get; set; } = 30000;

    /// <summary>Точные имена и/или regex-шаблоны видов для PNG-снимков (этапы 3, 7).</summary>
    public List<string> SnapshotViews { get; set; } = new();

    /// <summary>Размер большей стороны PNG в пикселях (этап 7).</summary>
    public int ImagePixelSize { get; set; } = 1500;

    public bool Gzip { get; set; } = true;

    public bool GeometryHash { get; set; } = true;

    /// <summary>
    /// Собирать GetElementOverrides по всем элементам каждого вида.
    /// Стоимость — O(виды × элементы вида): на модели 558 видов × 144k элементов снапшот
    /// уходил в часы, поэтому по умолчанию выключено. Категорийные и фильтровые
    /// переопределения собираются всегда — они дёшевы.
    /// </summary>
    public bool CollectElementOverrides { get; set; } = false;

    public string LogLevel { get; set; } = "Info";

    public static string ConfigPath => Path.Combine(
        Path.GetDirectoryName(typeof(RecorderConfig).Assembly.Location) ?? ".", "config.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static RecorderConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<RecorderConfig>(File.ReadAllText(ConfigPath), ReadOptions)
                       ?? new RecorderConfig();
        }
        catch (Exception ex)
        {
            AddinLog.Error("RecorderConfig.Load", ex);
        }
        return new RecorderConfig();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        File.WriteAllText(ConfigPath, json);
    }
}
