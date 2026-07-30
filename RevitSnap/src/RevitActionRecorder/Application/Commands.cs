using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitActionRecorder.Configuration;
using RevitActionRecorder.Infrastructure;
using RevitActionRecorder.Recording;

namespace RevitActionRecorder.Application;

[Transaction(TransactionMode.Manual)]
public sealed class StartRecordingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc?.Document is null)
                return Result.Cancelled;

            if (RecordingState.IsRecording)
            {
                TaskDialog.Show("RevitActionRecorder", "Запись уже идёт.");
                return Result.Cancelled;
            }

            Imaging.ImageExportQueue.EnsureCreated();
            var config = RecorderConfig.Load();
            var session = RecordingSession.Start(commandData.Application, uidoc.Document, config);
            if (session is null)
            {
                TaskDialog.Show("RevitActionRecorder", "Снапшот «до» отменён — запись не начата.");
                return Result.Cancelled;
            }
            RecordingState.Attach(session);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(StartRecordingCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class StopRecordingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var session = RecordingState.Session;
            if (session is null || !session.IsActive)
                return Result.Cancelled;

            var stats = session.Stop();

            var breakdown = string.Join(
                "\n",
                stats.Counts.OrderByDescending(kv => kv.Value).Select(kv => $"  {kv.Key}: {kv.Value}"));
            var diffNote = stats.DiffStarted
                ? "diff.json считается в фоне и появится в папке сессии."
                : "diff.json не считается (нет пары снапшотов).";
            var dialog = new TaskDialog("RevitActionRecorder")
            {
                MainInstruction = "Запись остановлена",
                MainContent =
                    $"Событий: {stats.TotalEvents}\n{breakdown}\n" +
                    $"Помечено undone: {stats.UndoneMarked}\n" +
                    $"Снапшот «после»: {(stats.AfterSnapshotTaken ? "снят" : "нет")}\n{diffNote}\n\n{stats.Folder}",
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(StopRecordingCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class SnapshotNowCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var session = RecordingState.Session;
            if (session is null || !session.IsActive)
                return Result.Cancelled;

            var path = session.TakeMidSnapshot();
            if (path is null)
            {
                TaskDialog.Show("RevitActionRecorder", "Промежуточный снапшот отменён.");
                return Result.Cancelled;
            }
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(SnapshotNowCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class MarkMomentCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var session = RecordingState.Session;
            if (session is null || !session.IsActive)
                return Result.Cancelled;

            Imaging.ImageExportQueue.EnsureCreated();
            var window = new MarkMomentWindow(commandData.Application.MainWindowHandle);
            if (window.ShowDialog() != true)
                return Result.Cancelled;

            session.Mark(window.CommentText ?? "");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(MarkMomentCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class OpenSessionFolderCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var folder = RecordingState.LastSessionFolder;
            if (folder is null || !Directory.Exists(folder))
            {
                TaskDialog.Show("RevitActionRecorder",
                    "Сессий записи ещё не было — открывать нечего.");
                return Result.Succeeded;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(OpenSessionFolderCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class SettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var config = RecorderConfig.Load();
            var window = new SettingsWindow(config, commandData.Application.MainWindowHandle);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error(nameof(SettingsCommand), ex);
            message = ex.Message;
            return Result.Failed;
        }
    }
}
