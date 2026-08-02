using RebarGeneration.Contracts;
using Xunit;

namespace RebarGeneration.Contracts.Tests;

/// <summary>
/// Нормаль плоскости — та самая грабля, из-за которой стержни в вертикальных
/// плоскостях «улетали». Поведение зафиксировано тестами, включая исторический
/// частный случай прямого горизонтального стержня.
/// </summary>
public class PlaneNormalTests
{
    private static void AssertParallel(Vec3 expected, Vec3 actual)
    {
        // Знак нормали не важен — важна плоскость.
        double dot = Math.Abs(expected.Normalized().Dot(actual.Normalized()));
        Assert.True(Math.Abs(dot - 1.0) < 1e-6, $"ожидалось ±{expected}, получено {actual}");
    }

    [Fact]
    public void Г_образный_стержень_в_плоскости_XZ_даёт_нормаль_по_Y()
    {
        Vec3 n = PlaneNormal.FromPolyline([
            new Vec3(0, 0, 0),
            new Vec3(5, 0, 0),
            new Vec3(5, 0, 3),
        ]);

        AssertParallel(Vec3.BasisY, n);
    }

    [Fact]
    public void Стержень_ныряющий_под_приямок_не_получает_BasisZ()
    {
        // Плоскость XZ: если бы нормаль захардкодили как BasisZ, Revit развернул
        // бы стержень — ровно этот случай и ломался раньше.
        Vec3 n = PlaneNormal.FromPolyline([
            new Vec3(0, 2, 10),
            new Vec3(4, 2, 10),
            new Vec3(6, 2, 8),
            new Vec3(12, 2, 8),
        ]);

        Assert.True(Math.Abs(n.Z) < 1e-6, $"нормаль не должна быть вертикальной, получено {n}");
        AssertParallel(Vec3.BasisY, n);
    }

    [Fact]
    public void Хомут_в_плане_даёт_вертикальную_нормаль()
    {
        Vec3 n = PlaneNormal.FromPolyline([
            new Vec3(0, 0, 5),
            new Vec3(2, 0, 5),
            new Vec3(2, 3, 5),
        ]);

        AssertParallel(Vec3.BasisZ, n);
    }

    [Fact]
    public void Прямой_горизонтальный_стержень_сохраняет_исторический_BasisZ()
    {
        // Для коллинеарного горизонтального стержня плоскость не определена
        // однозначно. Совместимость важнее «красоты»: смена нормали поменяла бы
        // плоскость загиба крюка у всех ранее построенных стержней.
        Vec3 n = PlaneNormal.FromPolyline([new Vec3(0, 0, 0), new Vec3(10, 0, 0)]);

        AssertParallel(Vec3.BasisZ, n);
    }

    [Fact]
    public void Прямой_вертикальный_стержень_даёт_горизонтальную_нормаль()
    {
        Vec3 n = PlaneNormal.FromPolyline([new Vec3(1, 1, 0), new Vec3(1, 1, 12)]);

        Assert.True(Math.Abs(n.Z) < 1e-6, $"нормаль вертикального стержня должна быть горизонтальной, получено {n}");
    }

    [Fact]
    public void Вырожденный_ввод_не_падает()
    {
        Assert.Equal(Vec3.BasisZ, PlaneNormal.FromPolyline([]));
        Assert.Equal(Vec3.BasisZ, PlaneNormal.FromPolyline([new Vec3(1, 1, 1)]));
        // Две совпадающие точки — сегмент нулевой длины, направления нет.
        Assert.Equal(Vec3.BasisZ, PlaneNormal.FromPolyline([new Vec3(1, 1, 1), new Vec3(1, 1, 1)]));
    }
}
