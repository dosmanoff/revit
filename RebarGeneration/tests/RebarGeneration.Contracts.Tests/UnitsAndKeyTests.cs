using RebarGeneration.Contracts;
using Xunit;

namespace RebarGeneration.Contracts.Tests;

public class UnitsTests
{
    [Theory]
    [InlineData("mm", 304.8, 1.0)]
    [InlineData("cm", 30.48, 1.0)]
    [InlineData("m", 0.3048, 1.0)]
    [InlineData("in", 12.0, 1.0)]
    [InlineData("ft", 1.0, 1.0)]
    public void Перевод_в_футы(string units, double value, double expectedFt)
    {
        var c = new LengthUnits.Converter(units);
        Assert.Equal(expectedFt, c.Ft(value), 9);
    }

    [Fact]
    public void Регистр_и_пробелы_единиц_не_важны()
    {
        Assert.Equal(1.0, new LengthUnits.Converter(" MM ").Ft(304.8), 9);
    }

    [Fact]
    public void Неизвестные_единицы_падают_с_кодом()
    {
        JobException ex = Assert.Throws<JobException>(() => new LengthUnits.Converter("вершок"));
        Assert.Equal("BAD_UNITS", ex.Code);
    }

    [Fact]
    public void Точка_переводится_покоординатно()
    {
        Vec3 p = new LengthUnits.Converter("mm").Pt([304.8, 609.6, 0]);
        Assert.Equal(1.0, p.X, 9);
        Assert.Equal(2.0, p.Y, 9);
        Assert.Equal(0.0, p.Z, 9);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Точка_неверной_длины_падает_с_понятным_кодом(int n)
    {
        var c = new LengthUnits.Converter("mm");
        JobException ex = Assert.Throws<JobException>(() => c.Pt(new double[n]));
        Assert.Equal("BAD_POINT", ex.Code);
    }
}

public class KeyTokenTests
{
    [Fact]
    public void Токен_вклеивается_не_затирая_марку()
    {
        // В Comments у арматуры живут марки — затирать их нельзя.
        string merged = KeyToken.Merge("8#7 [1]", "FDN:F1:BOT:X");
        Assert.Equal("8#7 [1] [[ctx:FDN:F1:BOT:X]]", merged);
        Assert.Equal("FDN:F1:BOT:X", KeyToken.Parse(merged));
    }

    [Fact]
    public void Повторная_запись_заменяет_токен_а_не_добавляет_второй()
    {
        string once = KeyToken.Merge("марка", "A");
        string twice = KeyToken.Merge(once, "B");

        Assert.Equal("марка [[ctx:B]]", twice);
        Assert.Equal("B", KeyToken.Parse(twice));
    }

    [Fact]
    public void Пустой_комментарий_даёт_чистый_токен()
    {
        Assert.Equal("[[ctx:K]]", KeyToken.Merge(null, "K"));
        Assert.Equal("[[ctx:K]]", KeyToken.Merge("", "K"));
    }

    [Fact]
    public void Хвостовые_пробелы_не_копятся()
    {
        Assert.Equal("текст [[ctx:K]]", KeyToken.Merge("текст   ", "K"));
    }

    [Fact]
    public void Ключ_с_двоеточиями_читается_целиком()
    {
        Assert.Equal("COL:C1.3:TIES", KeyToken.Parse("[[ctx:COL:C1.3:TIES]]"));
    }

    [Fact]
    public void Текст_без_токена_даёт_null()
    {
        Assert.Null(KeyToken.Parse("просто комментарий"));
        Assert.Null(KeyToken.Parse(null));
    }
}
