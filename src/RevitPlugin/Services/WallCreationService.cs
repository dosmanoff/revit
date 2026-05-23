using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitPlugin.Helpers;
using Serilog;

namespace RevitPlugin.Services;

/// <summary>
/// Реализация <see cref="IWallCreationService"/>.
/// Создаёт стены Revit по выбранным линиям модели.
/// </summary>
public class WallCreationService : IWallCreationService
{
    private readonly Document _doc;

    /// <summary>
    /// Инициализирует сервис с указанным документом Revit.
    /// </summary>
    /// <param name="doc">Активный документ Revit. Не должен быть <c>null</c>.</param>
    public WallCreationService(Document doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    /// <inheritdoc />
    public Result Execute(Selection selection)
    {
        // Получить выбранные линии
        var curves = GetSelectedCurves(selection);
        if (curves.Count == 0)
        {
            TaskDialog.Show("Info", "Select model lines before running the command.");
            return Result.Cancelled;
        }

        // Получить уровень
        var level = _doc.GetFirstLevel();
        if (level == null)
        {
            TaskDialog.Show("Error", "No levels found in the document.");
            return Result.Failed;
        }

        // Получить тип стены по умолчанию
        var wallType = GetDefaultWallType();
        if (wallType == null)
        {
            TaskDialog.Show("Error", "No wall types found in the document.");
            return Result.Failed;
        }

        // Создать стены в транзакции
        using var t = new Transaction(_doc, "Create Walls from Lines");
        try
        {
            t.Start();

            var created = new List<Wall>();
            foreach (var curve in curves)
            {
                var wall = Wall.Create(_doc, curve, wallType.Id, level.Id,
                    height: 3000 / 304.8, // 3 метра в футах
                    offset: 0,
                    flip: false,
                    structural: false);
                created.Add(wall);
                Log.Debug("Created wall {Id} along {Curve}", wall.Id, curve);
            }

            t.Commit();
            Log.Information("Created {Count} walls", created.Count);
            TaskDialog.Show("Success", $"Created {created.Count} wall(s).");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            if (t.GetStatus() == TransactionStatus.Started)
                t.RollbackIfOpen();

            Log.Error(ex, "Failed to create walls");
            throw;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────

    private List<Curve> GetSelectedCurves(Selection selection)
    {
        var curves = new List<Curve>();

        foreach (var id in selection.GetElementIds())
        {
            if (_doc.GetElement(id) is ModelLine line)
                curves.Add(line.GeometryCurve);
        }

        return curves;
    }

    private WallType? GetDefaultWallType()
        => new FilteredElementCollector(_doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault(wt => wt.Kind == WallKind.Basic);
}
