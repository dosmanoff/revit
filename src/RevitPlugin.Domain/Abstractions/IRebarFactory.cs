using RevitPlugin.Domain.Placement;

namespace RevitPlugin.Domain.Abstractions;

/// <summary>Создаёт/обновляет/удаляет стержни <c>Autodesk.Revit.DB.Structure.Rebar</c>.</summary>
public interface IRebarFactory
{
    /// <returns>Revit ElementId созданного стержня.</returns>
    long Create(long hostWallId, RebarPlacement placement);
    void Update(long rebarId, RebarPlacement placement);
    void Delete(long rebarId);
}
