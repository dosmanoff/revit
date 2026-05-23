using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace RevitPlugin.Services;

/// <summary>
/// Сервис создания стен по линиям модели.
/// Интерфейс определён для тестируемости.
/// </summary>
public interface IWallCreationService
{
    /// <summary>
    /// Создаёт стены по выбранным линиям модели.
    /// </summary>
    /// <param name="selection">Текущий выбор в Revit UI.</param>
    /// <returns>
    /// <see cref="Autodesk.Revit.UI.Result.Succeeded"/> если стены созданы,
    /// <see cref="Autodesk.Revit.UI.Result.Cancelled"/> если пользователь отменил,
    /// <see cref="Autodesk.Revit.UI.Result.Failed"/> при ошибке.
    /// </returns>
    Autodesk.Revit.UI.Result Execute(Selection selection);
}
