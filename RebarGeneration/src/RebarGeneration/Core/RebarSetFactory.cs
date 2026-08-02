using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Единственное место, где создаётся арматура.
/// <para>
/// Ключевое решение — <b>набор вместо N стержней</b>. Один
/// <see cref="Rebar"/> с раскладкой
/// <see cref="RebarShapeDrivenAccessor.SetLayoutAsNumberWithSpacing"/> представляет
/// все стержни полосы: спецификация считает их правильно, теги и MRA работают,
/// а модель весит на два порядка меньше. Тысяча стержней плиты — это 20–40
/// элементов, а не тысяча.
/// </para>
/// </summary>
public sealed class RebarSetFactory(Document doc, TypeCache types, bool reuseStandardShapes)
{
    private readonly Document _doc = doc;
    private readonly TypeCache _types = types;

    /// <summary>Наборы, у которых Revit отверг раскладку — остались одиночным стержнем.</summary>
    public List<string> LayoutRejections { get; } = [];

    /// <summary>
    /// Создать набор. Вызывать внутри транзакции. Бросает
    /// <see cref="JobException"/>, если геометрия непригодна или ключ не записался.
    /// </summary>
    public Rebar Create(PlannedSet set, Element host, string key)
    {
        string? bad = LayoutMath.ValidateSet(set.Count, set.SpacingFt, set.BarLengthFt);
        if (bad is not null)
            throw new JobException("BAD_SET", $"{key}: {bad}");

        RebarBarType barType = _types.BarType(set.BarType);
        IList<Curve> curves = BuildCurves(set.Polyline, key);

        // Нормаль плоскости, а не BasisZ: иначе стержни в вертикальных плоскостях
        // Revit разворачивает или отвергает (см. PlaneNormal).
        Vec3 planeNormal = PlaneNormal.FromPolyline(set.Polyline);

        // Направление раскладки набора: явное из задания, иначе нормаль плоскости.
        Vec3 dist = set.Distribution is { } d && d.Length > 1e-9 ? d.Normalized() : planeNormal;

        Rebar rebar = Rebar.CreateFromCurves(
            _doc,
            RebarStyle.Standard,
            barType,
            _types.Hook(set.HookStart),
            _types.Hook(set.HookEnd),
            host,
            new XYZ(dist.X, dist.Y, dist.Z),
            curves,
            RebarHookOrientation.Right,
            RebarHookOrientation.Right,
            useExistingShapeIfPossible: reuseStandardShapes,
            createNewShape: true)
            ?? throw new JobException("CREATE_FAILED", $"{key}: Revit не создал стержень по этим кривым");

        if (set.Count > 1 && set.SpacingFt > 1e-9)
        {
            try
            {
                rebar.GetShapeDrivenAccessor()
                     .SetLayoutAsNumberWithSpacing(set.Count, set.SpacingFt, true, true, true);
            }
            catch (Exception ex)
            {
                // Не роняем прогон: остаётся представительный стержень, но об этом
                // обязательно сообщаем — иначе в модели молча недосчитается арматуры.
                LayoutRejections.Add($"{key}: раскладка отвергнута ({set.Count}×{set.SpacingFt:0.###} ft) — {ex.Message}");
            }
        }

        if (KeyIndex.WriteKey(rebar, key) is null)
            throw new JobException("KEY_WRITE_FAILED",
                $"{key}: не удалось записать ключ — элемент выпал бы из идемпотентности");

        return rebar;
    }

    /// <summary>Проставить параметры из задания. Отсутствующий параметр — не повод
    /// валить прогон, но и молчать о нём нельзя.</summary>
    public List<string> ApplyParams(Element element, IReadOnlyDictionary<string, object>? parameters, string key)
    {
        var missed = new List<string>();
        if (parameters is null) return missed;

        foreach ((string name, object raw) in parameters)
        {
            if (string.Equals(name, "barType", StringComparison.OrdinalIgnoreCase)) continue;

            Parameter? p = element.LookupParameter(name);
            if (p is null || p.IsReadOnly)
            {
                missed.Add($"{key}: параметр '{name}' отсутствует или только для чтения");
                continue;
            }

            try
            {
                SetParam(p, raw);
            }
            catch (Exception ex)
            {
                missed.Add($"{key}: параметр '{name}' не записан — {ex.Message}");
            }
        }

        return missed;
    }

    private static void SetParam(Parameter p, object raw)
    {
        string s = raw?.ToString() ?? string.Empty;
        switch (p.StorageType)
        {
            case StorageType.String:
                p.Set(s);
                break;
            case StorageType.Integer:
                p.Set(int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case StorageType.Double:
                p.Set(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case StorageType.ElementId:
                p.Set(new ElementId(long.Parse(s, System.Globalization.CultureInfo.InvariantCulture)));
                break;
            default:
                throw new JobException("BAD_STORAGE", $"тип хранения {p.StorageType} не поддержан");
        }
    }

    private static IList<Curve> BuildCurves(IReadOnlyList<Vec3> pts, string key)
    {
        var curves = new List<Curve>(Math.Max(0, pts.Count - 1));
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vec3 a = pts[i], b = pts[i + 1];
            // Нулевой сегмент Revit не примет; для рукописного задания это частая
            // опечатка (дважды одна точка), и внятное сообщение тут дешевле разбора
            // невнятного исключения из API.
            if (a.DistanceTo(b) < 1e-7)
                throw new JobException("ZERO_SEGMENT",
                    $"{key}: сегмент {i} нулевой длины — точки {a} и {b} совпадают");
            curves.Add(Line.CreateBound(new XYZ(a.X, a.Y, a.Z), new XYZ(b.X, b.Y, b.Z)));
        }

        if (curves.Count == 0)
            throw new JobException("NO_CURVES", $"{key}: в полилинии меньше двух точек");

        return curves;
    }
}
