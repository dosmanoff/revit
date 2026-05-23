using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPlugin.Domain.Rules;
using Serilog;

namespace RevitPlugin.Commands;

/// <summary>
/// Команда «Arm Walls» — разместить арматуру в выбранных стенах по привязанной конфигурации.
/// На текущем этапе (M1 bootstrap) команда показывает информационный диалог; полноценное
/// подключение <c>WallReinforcementOrchestrator</c> + <c>IRebarFactory</c> произойдёт в
/// рамках задач M1 follow-up (адаптеры Revit).
/// См. UX-сценарий: <c>docs/UI_FLOWS.md §2.2</c>.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class ArmWallCommand : IExternalCommand
{
    public Result Execute(
        ExternalCommandData commandData,
        ref string message,
        ElementSet elements)
    {
        Log.Debug("ArmWallCommand.Execute called");

        try
        {
            // Доменный движок собирается уже сейчас — это smoke-check, что Domain-сборка
            // подгружается в процесс Revit. Реальный запуск (с WallContext, собранным из
            // Autodesk.Revit.DB.Wall, и IRebarFactory) — следующая задача.
            var engine = RuleEngine.CreateDefault();

            TaskDialog.Show(
                "Arm Walls",
                "Команда Arm Walls подключена.\n\n"
                + "MVP-движок правил: " + engine.GetType().FullName + "\n"
                + "Полноценное армирование появится после реализации адаптеров Revit "
                + "(WallRepository, RebarFactory, ParameterStore) — см. ROADMAP M1.");

            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            Log.Information("User cancelled ArmWallCommand");
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ArmWallCommand failed");
            message = ex.Message;
            return Result.Failed;
        }
    }
}
