using System.IO;

namespace RevitActionRecorder.Infrastructure;

/// <summary>
/// Служебный лог аддина вне сессий записи (ошибки старта, ошибки обработчиков).
/// Логирование никогда не должно бросать исключения наружу — аддин не имеет права ронять Revit.
/// </summary>
internal static class AddinLog
{
    private static readonly object Sync = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitActionRecorder", "addin.log");

    public static void Info(string message) => Write($"INFO  {message}");

    public static void Error(string context, Exception ex) => Write($"ERROR [{context}] {ex}");

    private static void Write(string line)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Намеренно глушим: сбой логирования не должен влиять на Revit.
        }
    }
}
