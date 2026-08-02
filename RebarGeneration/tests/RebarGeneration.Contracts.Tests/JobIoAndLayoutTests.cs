using RebarGeneration.Contracts;
using Xunit;

namespace RebarGeneration.Contracts.Tests;

public class JobIoTests
{
    private const string Sample = """
    {
      // задание пишет агент руками, поэтому комментарии и висящие запятые
      // не должны валить разбор
      "schema": "rebar-job-1.0",
      "units": "mm",
      "defaults": { "barType": "#5", "cover": 75 },
      "groups": [
        {
          "key": "FDN:F1:MAT",
          "generator": "footing",
          "hostId": 987654,
          "bottom": { "x": { "spacing": 300 }, "y": { "spacing": 300 } },
        },
      ],
    }
    """;

    [Fact]
    public void Задание_с_комментариями_и_висящими_запятыми_разбирается()
    {
        Job job = JobIo.Parse(Sample);

        Assert.Equal("mm", job.Units);
        Assert.Equal("#5", job.Defaults.BarType);
        Assert.Single(job.Groups);
        Assert.Equal("footing", job.Groups[0].Generator);
        Assert.Equal(300, job.Groups[0].Bottom!.X!.Spacing);
    }

    [Fact]
    public void Регистр_имён_полей_не_важен()
    {
        Job job = JobIo.Parse("""{"Schema":"rebar-job-1.0","UNITS":"ft","Groups":[]}""");
        Assert.Equal("ft", job.Units);
    }

    [Fact]
    public void Умолчания_политики_безопасны()
    {
        Job job = JobIo.Parse("""{"schema":"rebar-job-1.0","groups":[]}""");

        // По умолчанию повторный прогон не дублирует и не сносит ничего молча.
        Assert.Equal("skip", job.Policy.OnExistingKey);
        Assert.False(job.DryRun);
        Assert.True(job.Policy.SuppressWarnings);
        // Id по умолчанию НЕ возвращаются: карта уезжает в файл, а не в контекст.
        Assert.False(job.Policy.IncludeIds);
    }

    [Fact]
    public void Битый_json_даёт_код_а_не_голое_исключение()
    {
        JobException ex = Assert.Throws<JobException>(() => JobIo.Parse("{ не json"));
        Assert.Equal("BAD_JSON", ex.Code);
    }

    [Fact]
    public void Отчёт_сериализуется_с_читаемой_кириллицей()
    {
        string json = JobIo.ToJson(RunReport.Failure("NO_HOST", "хост не найден"));

        Assert.Contains("хост не найден", json);
        Assert.Contains("\"ok\": false", json);
    }
}

public class LayoutMathTests
{
    [Fact]
    public void Равномерная_раскладка_включает_оба_конца()
    {
        (int count, double spacing, double first) = LayoutMath.Uniform(0, 10, 3);

        Assert.True(count >= 2);
        Assert.Equal(0, first, 9);
        Assert.True(spacing <= 3 + 1e-9, "фактический шаг не должен превышать желаемый");
        Assert.Equal(10, first + spacing * (count - 1), 6);
    }

    [Fact]
    public void Раскладка_с_защитным_слоем_сжимает_пролёт()
    {
        (int count, _, double first) = LayoutMath.WithinCover(0, 10, 1, 2);

        Assert.Equal(1, first, 9);
        Assert.True(count >= 2);
    }

    [Fact]
    public void Длина_полилинии_суммирует_сегменты()
    {
        double l = LayoutMath.PolylineLength([
            new Vec3(0, 0, 0), new Vec3(3, 0, 0), new Vec3(3, 4, 0),
        ]);

        Assert.Equal(7, l, 9);
    }

    [Theory]
    [InlineData(1, 0, 0.0, "нулевая длина стержня")]
    [InlineData(0, 1, 5.0, "нулевое количество стержней")]
    [InlineData(5, 0, 5.0, "шаг набора не задан при count > 1")]
    public void Негодный_набор_отбраковывается_до_вызова_api(
        int count, double spacing, double length, string expected)
    {
        Assert.Equal(expected, LayoutMath.ValidateSet(count, spacing, length));
    }

    [Fact]
    public void Годный_набор_проходит()
    {
        Assert.Null(LayoutMath.ValidateSet(count: 12, spacingFt: 1.0, barLengthFt: 20));
        Assert.Null(LayoutMath.ValidateSet(count: 1, spacingFt: 0, barLengthFt: 20));
    }

    [Theory]
    [InlineData(0.5)]    // полдюйма
    [InlineData(0.9)]    // почти дюйм
    public void Стержень_короче_дюйма_отбраковывается(double inches)
    {
        // Revit считает это ОШИБКОЙ, а не предупреждением: проглотить её нельзя,
        // она поднимает модальный диалог и останавливает весь пакет. Поэтому
        // отсев обязан происходить до вызова API.
        string? why = LayoutMath.ValidateSet(1, 0, inches / 12.0);

        Assert.NotNull(why);
        Assert.Contains("минимума Revit", why);
    }

    [Fact]
    public void Ровно_дюйм_допустим()
    {
        Assert.Null(LayoutMath.ValidateSet(1, 0, LayoutMath.MinBarLengthFt));
    }

    [Fact]
    public void Минимум_равен_одному_дюйму()
    {
        Assert.Equal(1.0 / 12.0, LayoutMath.MinBarLengthFt, 12);
    }
}
