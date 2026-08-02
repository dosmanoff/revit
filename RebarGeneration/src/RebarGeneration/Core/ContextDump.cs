using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Контекст модели для написания задания.
/// <para>
/// Главное свойство этого дампа — он <b>маленький</b>. Соблазн отдать агенту всю
/// геометрию велик, но именно это и жжёт токены: чтобы написать задание, нужны
/// система координат, доступные типы стержней, габарит и уровень хоста — а не
/// его солиды и меши. Точечная детализация запрашивается отдельно и осознанно.
/// </para>
/// </summary>
public static class ContextDump
{
    public static object Build(Document doc, IReadOnlyList<long> hostIds)
    {
        var types = new TypeCache(doc);

        return new
        {
            ok = true,
            document = Safe(() => doc.Title, "doc"),
            path = Safe(() => doc.PathName, string.Empty),
            units = new
            {
                model = "ft",
                note = "внутренние единицы Revit — футы; в задании указывай units (обычно mm)",
            },
            levels = Levels(doc),
            barTypes = types.BarTypeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
            hookTypes = types.HookTypeNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
            rebarCount = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType().GetElementCount(),
            keyedRebar = KeyIndex.Build(doc, BuiltInCategory.OST_Rebar).Count,
            hosts = hostIds.Count == 0 ? null : hostIds.Select(id => Host(doc, id)).ToArray(),
            generators = GeneratorRegistry.Names.OrderBy(n => n).ToArray(),
        };
    }

    private static object[] Levels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => (object)new { id = l.Id.Value, name = l.Name, elevationFt = Math.Round(l.Elevation, 4) })
            .ToArray();

    private static object Host(Document doc, long id)
    {
        Element? e = doc.GetElement(new ElementId(id));
        if (e is null) return new { id, found = false };

        BoundingBoxXYZ? bb = Safe(() => e.get_BoundingBox(null), null);

        return new
        {
            id,
            found = true,
            name = Safe(() => e.Name, string.Empty),
            category = Safe(() => e.Category?.Name, null),
            type = Safe(() => doc.GetElement(e.GetTypeId())?.Name, null),
            level = Safe(() => doc.GetElement(e.LevelId)?.Name, null),
            key = KeyIndex.ReadKey(e),
            bboxFt = bb is null
                ? null
                : new
                {
                    min = new[] { Round(bb.Min.X), Round(bb.Min.Y), Round(bb.Min.Z) },
                    max = new[] { Round(bb.Max.X), Round(bb.Max.Y), Round(bb.Max.Z) },
                    sizeFt = new[]
                    {
                        Round(bb.Max.X - bb.Min.X), Round(bb.Max.Y - bb.Min.Y), Round(bb.Max.Z - bb.Min.Z),
                    },
                },
            // Сколько арматуры уже висит на этом хосте — сразу видно, был ли он
            // уже армирован, без отдельного запроса.
            existingRebar = Safe(() => new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rebar).WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Structure.Rebar>()
                .Count(r => r.GetHostId() == e.Id), 0),
        };
    }

    private static double Round(double v) => Math.Round(v, 4);

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f() ?? fallback; }
        catch { return fallback; }
    }
}
