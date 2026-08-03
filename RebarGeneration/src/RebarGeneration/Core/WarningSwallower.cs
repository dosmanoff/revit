using Autodesk.Revit.DB;

namespace RebarGeneration.Core;

/// <summary>
/// Глушитель предупреждений на время массового прогона.
/// <para>
/// Без него тысяча созданий арматуры выливается в тысячу диалогов вида «стержень
/// выходит за пределы основы» — прогон встаёт и ждёт человека. Ошибки (Error и
/// выше) НЕ трогаются: они означают, что транзакция нежизнеспособна, и должны
/// откатить свой хост, а не быть замолчанными.
/// </para>
/// <para>
/// Тексты проглоченных предупреждений копятся и уходят в отчёт: «тихо» не должно
/// означать «незаметно».
/// </para>
/// </summary>
public sealed class WarningSwallower : IFailuresPreprocessor
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>Проглоченные предупреждения: текст → сколько раз.</summary>
    public IReadOnlyDictionary<string, int> Swallowed => _seen;

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        bool deleted = false;

        foreach (FailureMessageAccessor f in accessor.GetFailureMessages())
        {
            if (f.GetSeverity() != FailureSeverity.Warning) continue;

            string text = SafeDescription(f);
            _seen[text] = _seen.TryGetValue(text, out int n) ? n + 1 : 1;
            accessor.DeleteWarning(f);
            deleted = true;
        }

        return deleted ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
    }

    private static string SafeDescription(FailureMessageAccessor f)
    {
        try
        {
            string d = f.GetDescriptionText();
            return string.IsNullOrWhiteSpace(d) ? f.GetFailureDefinitionId().Guid.ToString() : d;
        }
        catch
        {
            return "unknown warning";
        }
    }

    /// <summary>Навесить глушитель на транзакцию и отключить модальные диалоги.</summary>
    public void AttachTo(Transaction tx)
    {
        FailureHandlingOptions opts = tx.GetFailureHandlingOptions();
        opts.SetFailuresPreprocessor(this);
        opts.SetClearAfterRollback(true);
        opts.SetDelayedMiniWarnings(true);
        opts.SetForcedModalHandling(false);
        tx.SetFailureHandlingOptions(opts);
    }

    /// <summary>Строки для отчёта, самые частые сверху.</summary>
    public List<string> ToReportLines() =>
        _seen.OrderByDescending(kv => kv.Value)
             .Select(kv => kv.Value > 1 ? $"{kv.Key} (×{kv.Value})" : kv.Key)
             .ToList();
}
