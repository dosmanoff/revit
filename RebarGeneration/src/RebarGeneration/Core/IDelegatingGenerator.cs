using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Генератор, который НЕ считает геометрию сам, а передаёт группу готовому
/// движку (`SlabReinforcer`, `WallReinforcer`, `ColumnReinforcer`).
/// <para>
/// Почему это отдельный интерфейс, а не <see cref="IRebarGenerator"/>: обычный
/// генератор возвращает наборы, а элементы создаёт раннер — единообразно, с
/// ключами и идемпотентностью. Делегирующий создаёт элементы САМ, внутри своего
/// движка, со своей разметкой и своей идемпотентностью. Разница принципиальная,
/// и прятать её за общим интерфейсом значило бы врать вызывающему.
/// </para>
/// <para><b>Транзакции.</b> Движки открывают собственные транзакции (Wall и
/// Column — по одной на элемент, ожидая внешний <c>TransactionGroup</c>; Slab —
/// собственную группу). Поэтому раннер вызывает делегирующие генераторы ВНЕ
/// своих транзакций по хосту, но внутри общей группы прогона: старт транзакции
/// внутри уже открытой транзакции Revit не допускает.
/// </para>
/// <para><b>Ключи.</b> Модель ключей плагина (`CTX_Key` / <c>[[ctx:KEY]]</c>) к
/// делегированным элементам НЕ применяется — движки размечают арматуру своими
/// тегами (например <c>SR:{config}:{slabId}:{layer}</c>) и имеют собственную
/// защиту от повтора (`CleanExisting`). Поэтому такие группы не попадают в карту
/// <c>key→ElementId</c> и не участвуют в политике <c>onExistingKey</c>.
/// </para>
/// </summary>
public interface IDelegatingGenerator
{
    /// <summary>Имя в поле <c>generator</c> задания.</summary>
    string Name { get; }

    /// <summary>Сборка и тип движка — для внятного сообщения, когда плагин не установлен.</summary>
    string RequiredAssembly { get; }

    /// <summary>Передать группу движку. Бросить <see cref="JobException"/> при отказе.</summary>
    DelegatedResult Build(GroupSpec group, DelegatedContext ctx);
}

/// <summary>Итог делегированной группы в терминах отчёта плагина.</summary>
public sealed record DelegatedResult(
    int Created,
    int Skipped,
    int Failed,
    IReadOnlyList<string> Notes);

/// <summary>Что нужно делегирующему генератору. Специально без <c>TypeCache</c>
/// и <c>KeyIndex</c>: движок ими не пользуется, а передавать их значило бы
/// намекать, что пользуется.</summary>
public sealed class DelegatedContext
{
    public required Document Doc { get; init; }
    public required IReadOnlyList<ElementId> Hosts { get; init; }
    public required bool DryRun { get; init; }
}
