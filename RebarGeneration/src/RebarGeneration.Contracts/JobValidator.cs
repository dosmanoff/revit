namespace RebarGeneration.Contracts;

/// <summary>
/// Проверка задания ДО открытия транзакции.
/// <para>
/// Смысл: массовый прогон применяется целиком, поэтому опечатка в задании стоит
/// дорого. Всё, что можно поймать без Revit (схема, единицы, дубли ключей,
/// нулевые шаги, кривые из одной точки), ловится здесь и возвращается одним
/// списком — агент правит задание за один ход, а не выясняет ошибки по одной.
/// </para>
/// </summary>
public static class JobValidator
{
    public static IReadOnlyList<ReportError> Validate(Job? job)
    {
        var errors = new List<ReportError>();

        if (job is null)
        {
            errors.Add(new ReportError { Code = "NO_JOB", Message = "задание пустое или не разобралось" });
            return errors;
        }

        if (!string.Equals(job.Schema, Job.CurrentSchema, StringComparison.OrdinalIgnoreCase))
            errors.Add(new ReportError
            {
                Code = "BAD_SCHEMA",
                Message = $"схема '{job.Schema}', поддерживается '{Job.CurrentSchema}'",
            });

        if (!LengthUnits.IsKnown(job.Units))
            errors.Add(new ReportError
            {
                Code = "BAD_UNITS",
                Message = $"неизвестные единицы '{job.Units}'; допустимы mm, cm, m, in, ft",
            });

        string onExisting = (job.Policy.OnExistingKey ?? "skip").ToLowerInvariant();
        if (onExisting is not ("skip" or "replace" or "error"))
            errors.Add(new ReportError
            {
                Code = "BAD_POLICY",
                Message = $"onExistingKey='{job.Policy.OnExistingKey}'; допустимы skip, replace, error",
            });

        if (job.Groups.Count == 0)
            errors.Add(new ReportError { Code = "NO_GROUPS", Message = "в задании нет ни одной группы" });

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < job.Groups.Count; i++)
            ValidateGroup(job, job.Groups[i], i, seen, errors);

        return errors;
    }

    private static void ValidateGroup(
        Job job, GroupSpec g, int index, HashSet<string> seen, List<ReportError> errors)
    {
        string where = string.IsNullOrWhiteSpace(g.Key) ? $"groups[{index}]" : g.Key;

        if (string.IsNullOrWhiteSpace(g.Key))
            errors.Add(new ReportError
            {
                Code = "NO_KEY", Key = where,
                Message = $"groups[{index}]: пустой key — без него нет идемпотентности",
            });
        else if (!seen.Add(g.Key))
            errors.Add(new ReportError
            {
                Code = "DUPLICATE_KEY", Key = g.Key,
                Message = $"ключ '{g.Key}' встречается в задании дважды; ключи должны быть уникальны",
            });

        string gen = (g.Generator ?? string.Empty).ToLowerInvariant();
        bool delegating = gen is "slab" or "wall" or "column";

        if (g.HostId is null && string.IsNullOrWhiteSpace(g.HostKey)
            && (g.HostIds is null || g.HostIds.Count == 0))
            errors.Add(new ReportError
            {
                Code = "NO_HOST", Key = where,
                Message = $"{where}: не задан ни hostId, ни hostIds, ни hostKey",
            });

        // Делегирующие генераторы размеры стержней берут из СВОЕГО конфига —
        // требовать barType у них бессмысленно.
        if (string.IsNullOrWhiteSpace(g.BarType) && string.IsNullOrWhiteSpace(job.Defaults.BarType)
            && gen is not ("footing" or "slab" or "wall" or "column"))
            errors.Add(new ReportError
            {
                Code = "NO_BAR_TYPE", Key = where,
                Message = $"{where}: не задан barType и нет defaults.barType",
            });

        if (delegating && string.IsNullOrWhiteSpace(g.ConfigPath))
            errors.Add(new ReportError
            {
                Code = "NO_CONFIG_PATH", Key = where,
                Message = $"{where}: generator={gen} требует configPath — путь к конфигу движка",
            });

        switch (gen)
        {
            case "polyline":
                ValidatePolyline(g, where, errors);
                break;
            case "footing":
                ValidateFooting(g, where, errors);
                break;
            case "slab":
            case "wall":
            case "column":
                break;      // остальное проверяет сам движок по своему конфигу
            case "":
                errors.Add(new ReportError
                {
                    Code = "NO_GENERATOR", Key = where, Message = $"{where}: не указан generator",
                });
                break;
            default:
                errors.Add(new ReportError
                {
                    Code = "UNKNOWN_GENERATOR", Key = where,
                    Message = $"{where}: неизвестный generator '{g.Generator}'",
                });
                break;
        }
    }

    private static void ValidatePolyline(GroupSpec g, string where, List<ReportError> errors)
    {
        if (g.Curves is null || g.Curves.Count == 0)
        {
            errors.Add(new ReportError
            {
                Code = "NO_CURVES", Key = where,
                Message = $"{where}: generator=polyline требует curves",
            });
            return;
        }

        for (int c = 0; c < g.Curves.Count; c++)
        {
            List<double[]> pts = g.Curves[c];
            if (pts.Count < 2)
            {
                errors.Add(new ReportError
                {
                    Code = "SHORT_CURVE", Key = where,
                    Message = $"{where}: curves[{c}] содержит {pts.Count} точек, нужно минимум 2",
                });
                continue;
            }
            for (int p = 0; p < pts.Count; p++)
                if (pts[p] is null || pts[p].Length != 3)
                    errors.Add(new ReportError
                    {
                        Code = "BAD_POINT", Key = where,
                        Message = $"{where}: curves[{c}][{p}] должна быть [x, y, z]",
                    });
        }

        if (g.Count > 1 && g.Spacing <= 0)
            errors.Add(new ReportError
            {
                Code = "NO_SPACING", Key = where,
                Message = $"{where}: count={g.Count} при spacing={g.Spacing} — задай шаг набора",
            });

        if (g.Distribution is not null && g.Distribution.Length != 3)
            errors.Add(new ReportError
            {
                Code = "BAD_DISTRIBUTION", Key = where,
                Message = $"{where}: distribution должна быть [x, y, z]",
            });
    }

    private static void ValidateFooting(GroupSpec g, string where, List<ReportError> errors)
    {
        if (g.Bottom is null && g.Top is null)
        {
            errors.Add(new ReportError
            {
                Code = "NO_MAT", Key = where,
                Message = $"{where}: generator=footing требует bottom и/или top",
            });
            return;
        }

        foreach ((string name, MatSpec? mat) in new[] { ("bottom", g.Bottom), ("top", g.Top) })
        {
            if (mat is null) continue;
            if (mat.X is null && mat.Y is null)
                errors.Add(new ReportError
                {
                    Code = "EMPTY_MAT", Key = where,
                    Message = $"{where}: {name} задан, но в нём нет ни x, ни y",
                });
            foreach ((string axis, DirSpec? d) in new[] { ("x", mat.X), ("y", mat.Y) })
                if (d is not null && d.Spacing <= 0)
                    errors.Add(new ReportError
                    {
                        Code = "NO_SPACING", Key = where,
                        Message = $"{where}: {name}.{axis}.spacing должен быть больше нуля",
                    });
        }

        if (g.Edge is not null &&
            g.Edge.ToLowerInvariant() is not ("none" or "hook"))
            errors.Add(new ReportError
            {
                Code = "BAD_EDGE", Key = where,
                Message = $"{where}: edge='{g.Edge}'; допустимы none, hook",
            });
    }
}
