namespace RevitActionRecorder.Model;

/// <summary>
/// Версия JSON-схемы всех выходных файлов (manifest, snapshot, events, diff).
/// Любое несовместимое изменение формата — инкремент версии.
/// </summary>
public static class Schema
{
    public const int Version = 1;
}
