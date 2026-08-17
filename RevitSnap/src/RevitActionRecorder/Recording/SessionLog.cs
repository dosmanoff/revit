using System.IO;

namespace RevitActionRecorder.Recording;

/// <summary>log.txt внутри папки сессии. Сбой логирования глушится — не имеет права влиять на Revit.</summary>
internal sealed class SessionLog
{
    private readonly object _sync = new();
    private readonly string _path;

    public SessionLog(string path) => _path = path;

    public void Info(string message) => Write($"INFO  {message}");

    public void Warn(string message) => Write($"WARN  {message}");

    public void Error(string context, Exception ex) => Write($"ERROR [{context}] {ex}");

    private void Write(string line)
    {
        try
        {
            lock (_sync)
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
