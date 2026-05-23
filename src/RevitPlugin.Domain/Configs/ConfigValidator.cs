namespace RevitPlugin.Domain.Configs;

/// <summary>
/// Severity сообщения валидатора. Error блокирует сохранение / запуск,
/// Warning — нет, но подсвечивается в Config Editor.
/// </summary>
public enum ValidationSeverity { Info, Warning, Error }

public sealed record ValidationMessage(
    ValidationSeverity Severity,
    string Field,
    string Message);

public sealed record ValidationReport(IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Messages.Any(m => m.Severity == ValidationSeverity.Warning);

    public static ValidationReport Empty => new(Array.Empty<ValidationMessage>());
}

/// <summary>
/// Статический валидатор конфигурации — основа для UI-секции Validate
/// (см. <c>docs/MODULES.md §M6.3</c>) и для pre-flight проверки в оркестраторах.
/// Проверяет только то, что не требует доступа к Revit-проекту (наличие конкретных
/// bar_type / view_template — отдельная проверка в адаптерах при Apply).
/// </summary>
public static class ConfigValidator
{
    /// <summary>Минимальный коэффициент: spacing ≥ 2 × предполагаемого диаметра.</summary>
    private const double MinSpacingDiameterRatio = 2.0;

    /// <summary>Эмпирическая оценка диаметра по имени bar type (для bar_type вида «Ø12 …»).
    /// Если распарсить не удалось, берём 12 мм по умолчанию.</summary>
    private const double DefaultBarDiameterMm = 12;

    public static ValidationReport Validate(RebarConfig config)
    {
        var messages = new List<ValidationMessage>();

        ValidateHeader(config, messages);
        ValidateApplicability(config.Applicability, messages);
        ValidateCommon(config.Common, messages);

        if (config.ExternalReinforcement is { } external)
            ValidateMesh(external, "external_reinforcement", messages);
        if (config.InternalReinforcement is { } internalMesh)
            ValidateMesh(internalMesh, "internal_reinforcement", messages);

        if (config.Perimeter is { } perim)
            ValidatePerimeter(perim, messages);
        if (config.Opening is { } opening)
            ValidateOpening(opening, messages);

        return new ValidationReport(messages);
    }

    private static void ValidateHeader(RebarConfig config, List<ValidationMessage> messages)
    {
        if (string.IsNullOrWhiteSpace(config.Name))
            messages.Add(new(ValidationSeverity.Error, "name", "Имя конфигурации не должно быть пустым."));

        if (config.SchemaVersion != RebarConfig.CurrentSchemaVersion)
            messages.Add(new(ValidationSeverity.Error, "schema_version",
                $"Неподдерживаемая schema_version={config.SchemaVersion} (ожидается {RebarConfig.CurrentSchemaVersion})."));

        if (!Guid.TryParse(config.Id, out _))
            messages.Add(new(ValidationSeverity.Warning, "id",
                $"id='{config.Id}' не похож на GUID; рекомендуется использовать UUID для глобальной уникальности."));
    }

    private static void ValidateApplicability(Applicability app, List<ValidationMessage> messages)
    {
        if (app.MinThickness is { } min && app.MaxThickness is { } max && min > max)
            messages.Add(new(ValidationSeverity.Error, "applicability.min_thickness",
                $"min_thickness ({min}) > max_thickness ({max})."));

        if (app.MinThickness is { } mn && mn < 50)
            messages.Add(new(ValidationSeverity.Warning, "applicability.min_thickness",
                $"min_thickness={mn}мм слишком мало для монолитной стены."));
    }

    private static void ValidateCommon(CommonConfig common, List<ValidationMessage> messages)
    {
        if (common.Cover < 15)
            messages.Add(new(ValidationSeverity.Warning, "common.cover",
                $"common.cover={common.Cover}мм меньше минимально-рекомендованных 15 мм."));
    }

    private static void ValidateMesh(MeshConfig mesh, string path, List<ValidationMessage> messages)
    {
        if (!mesh.Enabled) return;

        var dv = GuessDiameter(mesh.BarTypeVertical);
        var dh = GuessDiameter(mesh.BarTypeHorizontal);

        if (mesh.SpacingVertical < MinSpacingDiameterRatio * dv)
            messages.Add(new(ValidationSeverity.Warning, $"{path}.spacing_vertical",
                $"Шаг вертикальных стержней {mesh.SpacingVertical}мм меньше 2×диаметра ({2 * dv}мм)."));
        if (mesh.SpacingHorizontal < MinSpacingDiameterRatio * dh)
            messages.Add(new(ValidationSeverity.Warning, $"{path}.spacing_horizontal",
                $"Шаг горизонтальных стержней {mesh.SpacingHorizontal}мм меньше 2×диаметра ({2 * dh}мм)."));

        if (mesh.Cover < dv + 5)
            messages.Add(new(ValidationSeverity.Warning, $"{path}.cover",
                $"Защитный слой {mesh.Cover}мм меньше диаметра+5мм ({dv + 5}мм)."));

        if (mesh.Major is not ("Vertical" or "Horizontal"))
            messages.Add(new(ValidationSeverity.Error, $"{path}.major",
                $"major='{mesh.Major}' должно быть Vertical или Horizontal."));

        if (mesh.WallEndOffsetMode is not ("FromStart" or "FromEnd" or "FromStartEnd" or "Centered"))
            messages.Add(new(ValidationSeverity.Error, $"{path}.wall_end_offset_mode",
                $"wall_end_offset_mode='{mesh.WallEndOffsetMode}' должно быть FromStart/FromEnd/FromStartEnd/Centered."));

        if (mesh.VerticalOffsetTop < 0 || mesh.VerticalOffsetBottom < 0
            || mesh.HorizontalOffsetStart < 0 || mesh.HorizontalOffsetEnd < 0
            || mesh.WallEndOffsetDistance < 0)
        {
            messages.Add(new(ValidationSeverity.Error, $"{path}.offsets",
                "Отрицательные offset значения недопустимы."));
        }
    }

    private static void ValidatePerimeter(PerimeterConfig perim, List<ValidationMessage> messages)
    {
        if (!perim.Enabled) return;
        var e = perim.EdgeRebar;
        if (!e.Enabled) return;

        if (e.Count is < 1 or > 2)
            messages.Add(new(ValidationSeverity.Error, "perimeter.edge_rebar.count",
                $"count={e.Count} должно быть 1 или 2."));

        if (e.Position is not ("External" or "Internal" or "Both" or "Center"))
            messages.Add(new(ValidationSeverity.Error, "perimeter.edge_rebar.position",
                $"position='{e.Position}' должно быть External/Internal/Both/Center."));

        if (e.EdgeCover < 10)
            messages.Add(new(ValidationSeverity.Warning, "perimeter.edge_rebar.edge_cover",
                $"edge_cover={e.EdgeCover}мм слишком мал."));

        if (e.LLegLength < 0)
            messages.Add(new(ValidationSeverity.Error, "perimeter.edge_rebar.l_leg_length",
                $"l_leg_length={e.LLegLength} не может быть отрицательным."));
    }

    private static void ValidateOpening(OpeningConfig opening, List<ValidationMessage> messages)
    {
        if (!opening.Enabled) return;
        var e = opening.EdgeRebar;
        if (!e.Enabled) return;

        if (e.Count < 1)
            messages.Add(new(ValidationSeverity.Error, "opening.edge_rebar.count",
                $"count={e.Count} должно быть ≥ 1."));

        if (e.Position is not ("External" or "Internal" or "Both" or "Center"))
            messages.Add(new(ValidationSeverity.Error, "opening.edge_rebar.position",
                $"position='{e.Position}' должно быть External/Internal/Both/Center."));

        if (e.MaxWidth is { } mx && mx < e.MinWidth)
            messages.Add(new(ValidationSeverity.Error, "opening.edge_rebar.max_width",
                $"max_width ({mx}) меньше min_width ({e.MinWidth})."));

        if (e.AnchorageLength < 0)
            messages.Add(new(ValidationSeverity.Error, "opening.edge_rebar.anchorage_length",
                $"anchorage_length={e.AnchorageLength} не может быть отрицательным."));
    }

    /// <summary>
    /// Извлекает номинальный диаметр из имени bar type. Поддерживает форматы:
    /// «Ø12 A500C», «Ø 12 …», «d10 …», «12mm …». Если не получилось — возвращает дефолт.
    /// </summary>
    internal static double GuessDiameter(string barTypeName)
    {
        if (string.IsNullOrWhiteSpace(barTypeName)) return DefaultBarDiameterMm;

        // Ищем первое число в строке
        var digits = new System.Text.StringBuilder();
        var seenDigit = false;
        foreach (var ch in barTypeName)
        {
            if (char.IsDigit(ch))
            {
                digits.Append(ch);
                seenDigit = true;
            }
            else if (seenDigit)
            {
                break;
            }
        }

        return digits.Length > 0 && double.TryParse(digits.ToString(), out var d)
            ? d
            : DefaultBarDiameterMm;
    }
}
