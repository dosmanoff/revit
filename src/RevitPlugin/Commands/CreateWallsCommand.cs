using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPlugin.Services;
using Serilog;

namespace RevitPlugin.Commands;

/// <summary>
/// Команда создания стен по выбранным линиям модели.
/// Точка входа из Revit Ribbon.
/// </summary>
/// <remarks>
/// ⚠️ Команда — тонкий слой. Вся логика вынесена в <see cref="IWallCreationService"/>.
/// </remarks>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CreateWallsCommand : IExternalCommand
{
    /// <inheritdoc />
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        Log.Debug("CreateWallsCommand.Execute called");

        try
        {
            var service = new WallCreationService(doc);
            return service.Execute(uiDoc.Selection);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            Log.Information("User cancelled CreateWallsCommand");
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CreateWallsCommand failed");
            message = ex.Message;
            return Result.Failed;
        }
    }
}
