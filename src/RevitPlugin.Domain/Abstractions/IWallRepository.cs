using RevitPlugin.Domain.Geometry;

namespace RevitPlugin.Domain.Abstractions;

/// <summary>Поставщик <see cref="WallContext"/> из Revit. Реализуется в адаптере.</summary>
public interface IWallRepository
{
    IReadOnlyList<WallContext> GetWalls(IEnumerable<long> ids);
    WallContext GetWall(long id);
}
