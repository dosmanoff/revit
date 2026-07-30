using RevitActionRecorder.Recording;

namespace RevitActionRecorder.Application;

/// <summary>
/// Глобальное состояние записи. Единственный источник истины для availability-классов ленты.
/// Вся работа с ним идёт в главном потоке Revit.
/// </summary>
internal static class RecordingState
{
    public static RecordingSession? Session { get; private set; }

    /// <summary>Папка текущей или последней завершённой сессии; null, пока сессий не было.</summary>
    public static string? LastSessionFolder { get; private set; }

    public static bool IsRecording => Session?.IsActive == true;

    public static void Attach(RecordingSession session)
    {
        Session = session;
        LastSessionFolder = session.Folder;
    }
}
