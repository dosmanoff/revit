using System.Text.Json.Serialization;

namespace RebarGeneration.Contracts;

/// <summary>
/// Задание на генерацию арматуры — то, что пишет агент и читает плагин.
/// <para>
/// Один вызов = одно задание = один <c>TransactionGroup</c>. Геометрия задаётся
/// либо явно (генератор <c>polyline</c>), либо параметрически (генератор под тип
/// хоста, например <c>footing</c>). Длины — в <see cref="Units"/> задания;
/// перевод во внутренние футы Revit происходит ровно один раз, на границе.
/// </para>
/// </summary>
public sealed class Job
{
    public const string CurrentSchema = "rebar-job-1.0";

    [JsonPropertyName("schema")] public string Schema { get; set; } = CurrentSchema;

    /// <summary>Единицы длины ВСЕГО задания: <c>mm</c> | <c>cm</c> | <c>m</c> | <c>in</c> | <c>ft</c>.</summary>
    [JsonPropertyName("units")] public string Units { get; set; } = "mm";

    /// <summary>Документ, для которого задание составлено. Пусто = активный/закреплённый.
    /// Заполненное поле сверяется с реальным — защита от прогона по чужой модели.</summary>
    [JsonPropertyName("document")] public string? Document { get; set; }

    /// <summary>Прогнать и откатить: модель не меняется, отчёт настоящий.</summary>
    [JsonPropertyName("dryRun")] public bool DryRun { get; set; }

    [JsonPropertyName("policy")] public JobPolicy Policy { get; set; } = new();

    [JsonPropertyName("defaults")] public GroupDefaults Defaults { get; set; } = new();

    [JsonPropertyName("groups")] public List<GroupSpec> Groups { get; set; } = [];
}

public sealed class JobPolicy
{
    /// <summary>Что делать, если элемент с таким <c>key</c> уже есть:
    /// <c>skip</c> (по умолчанию, идемпотентно) | <c>replace</c> | <c>error</c>.</summary>
    [JsonPropertyName("onExistingKey")] public string OnExistingKey { get; set; } = "skip";

    /// <summary>Подбирать стандартную <c>RebarShape</c>, если подходит. Лучше для спецификаций,
    /// чуть дороже на создании.</summary>
    [JsonPropertyName("reuseStandardShapes")] public bool ReuseStandardShapes { get; set; } = true;

    /// <summary>Глушить предупреждения Revit на время прогона (ошибки не глушатся).</summary>
    [JsonPropertyName("suppressWarnings")] public bool SuppressWarnings { get; set; } = true;

    /// <summary>Вторичный предохранитель: дробить транзакцию, если в одном хосте больше N наборов.
    /// 0 = выключено. Первичный критерий дробления — хост, а не количество.</summary>
    [JsonPropertyName("maxSetsPerTransaction")] public int MaxSetsPerTransaction { get; set; }

    /// <summary>Вкладывать ли список ElementId в ответ. По умолчанию НЕТ: карта
    /// <c>key→id</c> пишется в файл, чтобы не раздувать контекст агента.</summary>
    [JsonPropertyName("includeIds")] public bool IncludeIds { get; set; }

    /// <summary>Куда положить карту <c>key→ElementId</c>. Пусто — рядом с заданием.</summary>
    [JsonPropertyName("mapFile")] public string? MapFile { get; set; }
}

public sealed class GroupDefaults
{
    [JsonPropertyName("barType")] public string? BarType { get; set; }
    [JsonPropertyName("cover")] public double? Cover { get; set; }
    [JsonPropertyName("hooks")] public HookSpec? Hooks { get; set; }
}

public sealed class HookSpec
{
    /// <summary>Имя (или фрагмент имени) <c>RebarHookType</c>, например "135". Пусто = без крюка.</summary>
    [JsonPropertyName("start")] public string? Start { get; set; }
    [JsonPropertyName("end")] public string? End { get; set; }
}

/// <summary>
/// Одна группа задания. Группа адресуется стабильным <c>key</c> и превращается
/// в один или несколько rebar-НАБОРОВ — не в N отдельных стержней.
/// </summary>
public sealed class GroupSpec
{
    /// <summary>Стабильный ключ группы, например "FDN:F1:BOT:X". Адресует НАБОР.</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    /// <summary>Имя генератора: <c>polyline</c> | <c>footing</c> | … (см. GeneratorRegistry).</summary>
    [JsonPropertyName("generator")] public string Generator { get; set; } = "polyline";

    /// <summary>ElementId хоста. Либо это, либо <see cref="HostKey"/>.</summary>
    [JsonPropertyName("hostId")] public long? HostId { get; set; }

    /// <summary>Стабильный ключ хоста — если хост сам создан по ключу.</summary>
    [JsonPropertyName("hostKey")] public string? HostKey { get; set; }

    [JsonPropertyName("barType")] public string? BarType { get; set; }
    [JsonPropertyName("cover")] public double? Cover { get; set; }
    [JsonPropertyName("hooks")] public HookSpec? Hooks { get; set; }

    // ---- generator = polyline -------------------------------------------------
    /// <summary>Полилинии в координатах модели. Каждая полилиния — форма ОДНОГО стержня;
    /// повторяется <see cref="Count"/> раз с шагом <see cref="Spacing"/> вдоль
    /// <see cref="Distribution"/>. Точка = [x, y, z] в единицах задания.</summary>
    [JsonPropertyName("curves")] public List<List<double[]>>? Curves { get; set; }

    /// <summary>Сколько стержней в наборе. 1 (или 0) = одиночный стержень.</summary>
    [JsonPropertyName("count")] public int Count { get; set; } = 1;

    /// <summary>Шаг набора в единицах задания.</summary>
    [JsonPropertyName("spacing")] public double Spacing { get; set; }

    /// <summary>Направление раскладки набора [x, y, z]. Пусто — нормаль плоскости стержня.</summary>
    [JsonPropertyName("distribution")] public double[]? Distribution { get; set; }

    // ---- generator = footing --------------------------------------------------
    /// <summary>Нижняя сетка подошвы.</summary>
    [JsonPropertyName("bottom")] public MatSpec? Bottom { get; set; }

    /// <summary>Верхняя сетка подошвы (если есть).</summary>
    [JsonPropertyName("top")] public MatSpec? Top { get; set; }

    /// <summary>Обрамление: <c>none</c> (по умолчанию) | <c>hook</c> — загнуть концы крюками.</summary>
    [JsonPropertyName("edge")] public string Edge { get; set; } = "none";

    /// <summary>Угол направления стержней «x» в плане, градусы против часовой от
    /// оси X модели. Для повёрнутой подошвы задаётся её угол; «y» идёт
    /// перпендикулярно. По умолчанию 0 — оси модели.</summary>
    [JsonPropertyName("angle")] public double Angle { get; set; }

    /// <summary>Считать раскладку по реальному контуру хоста (с отверстиями), а не
    /// по габаритному боксу. По умолчанию да; <c>false</c> возвращает прежнее
    /// поведение, если контур снялся неудачно.</summary>
    [JsonPropertyName("useFootprint")] public bool UseFootprint { get; set; } = true;

    /// <summary>
    /// Минимальная длина стержня: полосы короче отбрасываются, их число уходит в
    /// отчёт. Ноль = только жёсткий предел Revit (1 дюйм).
    /// <para>
    /// Смысл поднимать выше предела: у изрезанного контура сканлайн даёт обрезки
    /// в углах и вокруг отверстий. Формально они законны, конструктивно
    /// бесполезны, а стоят по набору каждый.
    /// </para>
    /// </summary>
    [JsonPropertyName("minBarLength")] public double MinBarLength { get; set; }

    // ---- generator = slab | wall | column (делегирование готовым движкам) ------
    /// <summary>Путь к конфигу того движка, которому делегируется группа
    /// (`SlabReinforcementConfig`, `ReinforcementConfig`, `ColumnReinforcementConfig`).
    /// Формат конфига — свой у каждого движка, плагин его не разбирает.</summary>
    [JsonPropertyName("configPath")] public string? ConfigPath { get; set; }

    /// <summary>Несколько хостов одной группой — движки умеют брать список.
    /// Дополняет <see cref="HostId"/>, не заменяет его.</summary>
    [JsonPropertyName("hostIds")] public List<long>? HostIds { get; set; }

    // ---- общее ----------------------------------------------------------------
    /// <summary>Параметры, которые выставить на созданных элементах (Comments, Mark, …).</summary>
    [JsonPropertyName("params")] public Dictionary<string, object>? Params { get; set; }

    /// <summary>Свободные заметки — в модель не идут, нужны для читаемости задания.</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>Сетка в одном направлении (нижняя или верхняя зона подошвы).</summary>
public sealed class MatSpec
{
    [JsonPropertyName("x")] public DirSpec? X { get; set; }
    [JsonPropertyName("y")] public DirSpec? Y { get; set; }
}

public sealed class DirSpec
{
    [JsonPropertyName("barType")] public string? BarType { get; set; }
    /// <summary>Желаемый шаг; фактический пересчитывается, чтобы пролёт делился нацело.</summary>
    [JsonPropertyName("spacing")] public double Spacing { get; set; }
}
