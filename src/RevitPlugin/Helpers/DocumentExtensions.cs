using Autodesk.Revit.DB;

namespace RevitPlugin.Helpers;

/// <summary>
/// Extension-методы для <see cref="Document"/>.
/// Упрощают частые операции с Revit DB.
/// </summary>
public static class DocumentExtensions
{
    /// <summary>
    /// Возвращает все элементы указанного типа из документа.
    /// </summary>
    /// <typeparam name="T">Тип элемента Revit.</typeparam>
    /// <param name="doc">Документ Revit.</param>
    /// <returns>Список элементов (может быть пустым).</returns>
    public static IList<T> GetElements<T>(this Document doc) where T : Element
        => new FilteredElementCollector(doc)
            .OfClass(typeof(T))
            .Cast<T>()
            .ToList();

    /// <summary>
    /// Возвращает первый найденный уровень в документе.
    /// </summary>
    /// <param name="doc">Документ Revit.</param>
    /// <returns>Первый уровень или <c>null</c> если уровней нет.</returns>
    public static Level? GetFirstLevel(this Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .FirstOrDefault();

    /// <summary>
    /// Возвращает уровень по имени.
    /// </summary>
    /// <param name="doc">Документ Revit.</param>
    /// <param name="name">Имя уровня (регистрозависимо).</param>
    /// <returns>Уровень или <c>null</c> если не найден.</returns>
    public static Level? GetLevelByName(this Document doc, string name)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name == name);

    /// <summary>
    /// Проверяет, является ли элемент валидным объектом Revit.
    /// </summary>
    /// <param name="element">Элемент для проверки.</param>
    /// <returns><c>true</c> если элемент существует и не удалён.</returns>
    public static bool IsValid(this Element? element)
        => element?.IsValidObject == true;
}
