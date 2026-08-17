using Autodesk.Revit.UI;
using RevitActionRecorder.Infrastructure;

namespace RevitActionRecorder.Application;

public sealed class RecorderApplication : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            RibbonBuilder.Build(application);
            AddinLog.Info($"Startup OK. Revit {application.ControlledApplication.VersionBuild}, addin {typeof(RecorderApplication).Assembly.GetName().Version}.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error("OnStartup", ex);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            var session = RecordingState.Session;
            if (session is not null)
            {
                if (session.IsActive)
                    session.Stop(takeAfterSnapshot: false);
                session.DiffTask?.Wait(TimeSpan.FromSeconds(30));
            }
            AddinLog.Info("Shutdown OK.");
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            AddinLog.Error("OnShutdown", ex);
            return Result.Failed;
        }
    }
}
