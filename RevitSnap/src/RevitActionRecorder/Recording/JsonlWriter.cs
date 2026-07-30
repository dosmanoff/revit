using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RevitActionRecorder.Model;

namespace RevitActionRecorder.Recording;

/// <summary>
/// Потоковая запись events.jsonl: главный поток кладёт готовые POCO в Channel,
/// фоновый писатель сериализует и пишет с флашем после каждой строки.
/// Revit API в фоновом потоке не трогается — сюда попадают только POCO.
/// </summary>
internal sealed class JsonlWriter
{
    private readonly Channel<EventRecord> _channel = Channel.CreateUnbounded<EventRecord>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly StreamWriter _out;
    private readonly SessionLog _log;
    private readonly Task _pump;

    public JsonlWriter(string path, SessionLog log)
    {
        _log = log;
        // FileShare.ReadWrite: иначе внешний читатель (tail, агент, скрипт) не может открыть
        // файл, пока идёт запись — открытие на чтение конфликтует с нашим Write-доступом.
        _out = new StreamWriter(
            new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _pump = Task.Run(PumpAsync);
    }

    public bool TryWrite(EventRecord record) => _channel.Writer.TryWrite(record);

    private async Task PumpAsync()
    {
        await foreach (var record in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                var line = JsonSerializer.Serialize(record, RarJson.Line);
                await _out.WriteLineAsync(line).ConfigureAwait(false);
                await _out.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error("JsonlWriter.Pump", ex);
            }
        }
    }

    /// <summary>Дописать очередь и закрыть файл. Вызывается один раз при остановке сессии.</summary>
    public void Complete(TimeSpan drainTimeout)
    {
        _channel.Writer.TryComplete();
        try
        {
            if (!_pump.Wait(drainTimeout))
                _log.Warn($"writer drain timeout after {drainTimeout.TotalSeconds:0}s");
        }
        catch (AggregateException ex)
        {
            _log.Error("JsonlWriter.Complete", ex);
        }

        try
        {
            _out.Flush();
            _out.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("JsonlWriter.Dispose", ex);
        }
    }
}
