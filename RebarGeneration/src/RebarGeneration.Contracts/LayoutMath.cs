using WallReinforcement.Geometry;

namespace RebarGeneration.Contracts;

/// <summary>
/// Математика раскладки набора. Тонкая надстройка над уже оттестированным
/// <see cref="BarLayout"/> из <c>WallReinforcement.Geometry</c> — намеренно
/// делегирует, а не копирует: два расходящихся счётчика шага дороже, чем
/// ссылка между проектами.
/// </summary>
public static class LayoutMath
{
    /// <summary>
    /// Равномерная раскладка на отрезке [<paramref name="from"/>, <paramref name="to"/>]
    /// с шагом не крупнее <paramref name="desiredStep"/>. Возвращает количество
    /// стержней (оба конца включены), фактический шаг, делящий пролёт нацело, и
    /// позицию первого. Пустой или вырожденный пролёт даёт count = 0.
    /// </summary>
    public static (int Count, double Spacing, double First) Uniform(double from, double to, double desiredStep)
        => BarLayout.UniformLayout(from, to, desiredStep);

    /// <summary>
    /// Раскладка внутри габарита с защитным слоем с двух сторон: удобная обёртка
    /// над <see cref="Uniform"/> для сеток подошв и плит.
    /// </summary>
    public static (int Count, double Spacing, double First) WithinCover(
        double min, double max, double cover, double desiredStep)
        => Uniform(min + cover, max - cover, desiredStep);

    /// <summary>
    /// Жёсткий предел Revit: стержень короче одного дюйма он не строит, и это
    /// <b>ошибка</b>, а не предупреждение — то есть её нельзя проглотить
    /// обработчиком, она поднимает модальный диалог и останавливает прогон.
    /// Поэтому такие стержни обязаны отсеиваться на нашей стороне.
    /// </summary>
    public const double MinBarLengthFt = 1.0 / 12.0;

    /// <summary>
    /// Проверка набора перед отправкой в Revit. Возвращает <c>null</c>, если всё в
    /// порядке, иначе — причину, которая уходит в отчёт.
    /// Дешевле поймать здесь, чем ловить исключение из API на 500-м наборе, а в
    /// случае слишком коротких стержней — единственный способ не получить
    /// модальный диалог посреди пакета.
    /// </summary>
    public static string? ValidateSet(int count, double spacingFt, double barLengthFt)
    {
        if (barLengthFt <= 1e-6) return "нулевая длина стержня";
        if (barLengthFt < MinBarLengthFt)
            return $"длина стержня {barLengthFt * 12:0.###} in меньше минимума Revit (1 in)";
        if (count <= 0) return "нулевое количество стержней";
        if (count > 1 && spacingFt <= 1e-9) return "шаг набора не задан при count > 1";
        return null;
    }

    /// <summary>Полная длина полилинии, футы.</summary>
    public static double PolylineLength(IReadOnlyList<Vec3> pts)
    {
        double sum = 0;
        for (int i = 0; i + 1 < pts.Count; i++) sum += pts[i].DistanceTo(pts[i + 1]);
        return sum;
    }
}
