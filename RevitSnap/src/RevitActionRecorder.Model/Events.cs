using System.Text.Json.Serialization;

namespace RevitActionRecorder.Model;

/// <summary>
/// Одна строка events.jsonl. Общий конверт + необязательные секции по видам событий;
/// null-поля не сериализуются. Порядок свойств = порядок ключей в JSON.
/// </summary>
public sealed class EventRecord
{
    [JsonPropertyName("schema")]
    public int SchemaVersion { get; set; } = Schema.Version;

    public long Seq { get; set; }

    /// <summary>Локальное время с офсетом: 2026-07-25T14:03:11.482+03:00.</summary>
    public string T { get; set; } = "";

    public string Kind { get; set; } = "";

    /// <summary>true — событие относится к чужому документу (переключение контекста), не к записываемому.</summary>
    public bool? Foreign { get; set; }

    /// <summary>UndoOperation для doc_changed.</summary>
    public string? Op { get; set; }

    /// <summary>Для doc_changed: помечается true при финализации, если транзакция была отменена.</summary>
    public bool? Undone { get; set; }

    /// <summary>GetTransactionNames() — человекочитаемые подписи стека Undo.</summary>
    public IReadOnlyList<string>? Txn { get; set; }

    /// <summary>Seq-номера событий, которые отменил этот undo-шаг.</summary>
    public IReadOnlyList<long>? UndoneSeqs { get; set; }

    /// <summary>Seq-номера событий, которые вернул этот redo-шаг.</summary>
    public IReadOnlyList<long>? RedoneSeqs { get; set; }

    public CommandRef? Cmd { get; set; }

    public ViewRef? View { get; set; }

    public ViewRef? PrevView { get; set; }

    public IReadOnlyList<ElementBrief>? Added { get; set; }

    public IReadOnlyList<ElementBrief>? Modified { get; set; }

    public IReadOnlyList<long>? Deleted { get; set; }

    public DialogInfo? Dialog { get; set; }

    public FailuresInfo? Failures { get; set; }

    public SelectionInfo? Selection { get; set; }

    public DocInfo? Doc { get; set; }

    /// <summary>Итоги по видам событий (session_stop).</summary>
    public IReadOnlyDictionary<string, long>? Counts { get; set; }

    /// <summary>Комментарий пользователя (mark) или служебная пометка.</summary>
    public string? Note { get; set; }
}

public sealed class ElementBrief
{
    public long Id { get; set; }
    public string? Uid { get; set; }
    public string? Cat { get; set; }
    public string? Cls { get; set; }
    public string? Family { get; set; }
    public string? Type { get; set; }
    public string? Level { get; set; }
    public string? Workset { get; set; }

    /// <summary>Дельты параметров изменённого элемента.</summary>
    public IReadOnlyList<ParamDelta>? Params { get; set; }

    /// <summary>
    /// Изменение положения элемента. Транзакции Move/Drag не меняют параметров,
    /// поэтому без этого поля перемещение видно только как факт, без величины.
    /// </summary>
    public LocDelta? Loc { get; set; }
}

/// <summary>Положение «до» и «после»: point — [x,y,z(,поворот)], curve — [x1,y1,z1,x2,y2,z2].</summary>
public sealed class LocDelta
{
    public string? Kind { get; set; }
    public IReadOnlyList<double>? From { get; set; }
    public IReadOnlyList<double>? To { get; set; }

    /// <summary>Смещение по осям, если тип положения не изменился.</summary>
    public IReadOnlyList<double>? By { get; set; }
}

public sealed class ParamDelta
{
    public string? Name { get; set; }
    public string? Bip { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

public sealed class CommandRef
{
    public string? Id { get; set; }
    public string? Title { get; set; }

    /// <summary>Сколько мс прошло от нажатия кнопки до этой транзакции (только в doc_changed).</summary>
    public long? AgeMs { get; set; }
}

public sealed class ViewRef
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
}

public sealed class DialogInfo
{
    public string? DialogId { get; set; }

    /// <summary>TaskDialog | MessageBox | имя типа args для прочих.</summary>
    public string? Type { get; set; }

    /// <summary>MessageBoxShowingEventArgs.DialogType (флаги WinAPI MessageBox).</summary>
    public int? DialogType { get; set; }

    public string? Message { get; set; }
}

public sealed class FailuresInfo
{
    public string? Severity { get; set; }
    public string? Txn { get; set; }
    public bool? Committing { get; set; }
    public string? Result { get; set; }
    public IReadOnlyList<FailureItem>? Items { get; set; }
}

public sealed class FailureItem
{
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public string? DefId { get; set; }
    public string? Resolution { get; set; }
    public string? Caption { get; set; }
    public IReadOnlyList<long>? ElementIds { get; set; }
    public IReadOnlyList<long>? AdditionalIds { get; set; }
}

public sealed class SelectionInfo
{
    public int Count { get; set; }
    public IReadOnlyList<long>? Ids { get; set; }
    public IReadOnlyList<string>? Cats { get; set; }
}

public sealed class DocInfo
{
    public string? Title { get; set; }
    public string? Path { get; set; }

    /// <summary>Документ с рабочими наборами (central-based).</summary>
    public bool? Workshared { get; set; }

    /// <summary>RevitAPIEventStatus для post-событий: Succeeded | Failed | Cancelled.</summary>
    public string? Status { get; set; }

    public string? OriginalPath { get; set; }
    public bool? SavingAsCentral { get; set; }
    public IReadOnlyList<ViewRef>? PrintedViews { get; set; }
    public IReadOnlyList<ViewRef>? FailedViews { get; set; }
}
