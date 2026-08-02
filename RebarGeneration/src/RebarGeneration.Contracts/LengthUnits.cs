namespace RebarGeneration.Contracts;

/// <summary>
/// Перевод длин в внутренние единицы Revit (футы). Единственное место, где это
/// делается: контракт задания — в миллиметрах (или в чём указано), а вся
/// геометрия ниже — уже в футах. Самая частая причина «собралось, но размеры
/// не те» — конверсия в двух местах, поэтому её здесь ровно одна.
/// </summary>
public static class LengthUnits
{
    private const double MmPerFoot = 304.8;

    /// <summary>Множитель «единица задания → фут».</summary>
    public static double FactorToFeet(string? units) => Normalize(units) switch
    {
        "mm" => 1.0 / MmPerFoot,
        "cm" => 10.0 / MmPerFoot,
        "m" => 1000.0 / MmPerFoot,
        "in" => 1.0 / 12.0,
        "ft" => 1.0,
        _ => throw new JobException("BAD_UNITS",
            $"неизвестные единицы '{units}'; допустимы mm, cm, m, in, ft"),
    };

    public static bool IsKnown(string? units)
    {
        try { FactorToFeet(units); return true; }
        catch (JobException) { return false; }
    }

    private static string Normalize(string? u) => (u ?? "mm").Trim().ToLowerInvariant();

    /// <summary>Готовый конвертер для одного задания — чтобы не разбирать строку на каждой точке.</summary>
    public sealed class Converter
    {
        private readonly double _k;

        public Converter(string? units) => _k = FactorToFeet(units);

        /// <summary>Скаляр → футы.</summary>
        public double Ft(double value) => value * _k;

        /// <summary>Точка [x, y, z] → футы. Проверяет длину массива, потому что
        /// это самая вероятная опечатка в рукописном задании.</summary>
        public Vec3 Pt(double[]? p)
        {
            if (p is null || p.Length != 3)
                throw new JobException("BAD_POINT",
                    $"точка должна быть [x, y, z], получено {(p is null ? "null" : p.Length + " координат")}");
            return new Vec3(p[0] * _k, p[1] * _k, p[2] * _k);
        }
    }
}

/// <summary>Ошибка, у которой есть машиночитаемый код — он уходит в отчёт как есть.</summary>
public sealed class JobException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
