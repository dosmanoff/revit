using RevitActionRecorder.Model;

namespace RevitActionRecorder.Recording;

/// <summary>
/// «Последняя нажатая кнопка ленты» для привязки к последующим DocumentChanged.
///
/// Замер на живой сессии: клик по «Detail Line» → первая транзакция через 2.1 с,
/// затем ещё три за следующие 2 с (инструмент остаётся активным и рисует несколько
/// элементов). Поэтому команда НЕ потребляется первой же корреляцией, а остаётся
/// «липкой» до следующей команды, и в событие пишется возраст привязки (ageMs) —
/// агент сам решает, насколько доверять связи.
/// </summary>
internal sealed class CommandTracker
{
    private CommandRef? _last;
    private DateTime _lastAtUtc;

    public void Record(CommandRef command)
    {
        _last = command;
        _lastAtUtc = DateTime.UtcNow;
    }

    public CommandRef? TryCorrelate(int thresholdMs)
    {
        if (_last is null) return null;
        var ageMs = (long)(DateTime.UtcNow - _lastAtUtc).TotalMilliseconds;
        if (ageMs > thresholdMs) return null;
        return new CommandRef { Id = _last.Id, Title = _last.Title, AgeMs = ageMs };
    }
}
