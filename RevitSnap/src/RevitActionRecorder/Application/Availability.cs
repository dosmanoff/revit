using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitActionRecorder.Application;

public sealed class StartRecordingAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        => applicationData.ActiveUIDocument is not null && !RecordingState.IsRecording;
}

public sealed class RecordingActiveAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        => applicationData.ActiveUIDocument is not null && RecordingState.IsRecording;
}

public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}
