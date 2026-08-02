using Autodesk.Revit.DB;
using RebarGeneration.Contracts;

namespace RebarGeneration.Core;

/// <summary>
/// Поиск хоста по <c>hostId</c> или по стабильному <c>hostKey</c>.
/// <para>
/// Индекс по ключам хостов строится ЛЕНИВО и только если задание вообще
/// использует <c>hostKey</c>: обход конструктивных категорий стоит заметно
/// дороже, чем обход одной арматуры, и платить за него в типовом задании
/// (где хосты адресуются по id) незачем.
/// </para>
/// </summary>
public sealed class HostResolver(Document doc)
{
    private static readonly BuiltInCategory[] HostCategories =
    [
        BuiltInCategory.OST_StructuralFoundation,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Walls,
    ];

    private readonly Document _doc = doc;
    private KeyIndex? _hostKeys;

    public Element Resolve(GroupSpec g)
    {
        if (g.HostId is { } id)
        {
            Element? e = _doc.GetElement(new ElementId(id));
            return e ?? throw new JobException("NO_HOST", $"{g.Key}: элемент {id} не найден");
        }

        if (!string.IsNullOrWhiteSpace(g.HostKey))
        {
            _hostKeys ??= KeyIndex.Build(_doc, HostCategories);

            if (_hostKeys.IsAmbiguous(g.HostKey))
                throw new JobException("AMBIGUOUS_HOST_KEY",
                    $"{g.Key}: ключ хоста '{g.HostKey}' носят несколько элементов — уточни адресацию");

            if (!_hostKeys.TryGet(g.HostKey, out ElementId hid))
                throw new JobException("NO_HOST", $"{g.Key}: хост с ключом '{g.HostKey}' не найден");

            Element? e = _doc.GetElement(hid);
            return e ?? throw new JobException("NO_HOST", $"{g.Key}: хост '{g.HostKey}' исчез из модели");
        }

        throw new JobException("NO_HOST", $"{g.Key}: не задан ни hostId, ни hostKey");
    }

    /// <summary>Ключ, по которому группы объединяются в одну транзакцию.</summary>
    public static long TransactionKey(Element host) => host.Id.Value;
}
