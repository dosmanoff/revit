using RebarGeneration.Contracts;
using Xunit;

namespace RebarGeneration.Contracts.Tests;

/// <summary>
/// Валидатор существует ради одного свойства: пакет применяется целиком, поэтому
/// опечатка должна всплыть ДО транзакции и сразу списком, а не по одной за прогон.
/// </summary>
public class JobValidatorTests
{
    private static Job Valid() => new()
    {
        Units = "mm",
        Defaults = new GroupDefaults { BarType = "#5" },
        Groups =
        [
            new GroupSpec
            {
                Key = "FDN:F1:BOT:X",
                Generator = "polyline",
                HostId = 123456,
                Count = 10,
                Spacing = 300,
                Curves = [[[0, 0, 0], [3000, 0, 0]]],
            },
        ],
    };

    [Fact]
    public void Корректное_задание_проходит()
    {
        Assert.Empty(JobValidator.Validate(Valid()));
    }

    [Fact]
    public void Дубль_ключа_ловится_до_транзакции()
    {
        Job job = Valid();
        job.Groups.Add(job.Groups[0]);

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "DUPLICATE_KEY");
    }

    [Fact]
    public void Пустой_ключ_ловится()
    {
        Job job = Valid();
        job.Groups[0].Key = "  ";

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "NO_KEY");
    }

    [Fact]
    public void Отсутствие_хоста_ловится()
    {
        Job job = Valid();
        job.Groups[0].HostId = null;

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "NO_HOST");
    }

    [Fact]
    public void Набор_без_шага_ловится()
    {
        Job job = Valid();
        job.Groups[0].Spacing = 0;

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "NO_SPACING");
    }

    [Fact]
    public void Одиночный_стержень_без_шага_допустим()
    {
        Job job = Valid();
        job.Groups[0].Count = 1;
        job.Groups[0].Spacing = 0;

        Assert.Empty(JobValidator.Validate(job));
    }

    [Fact]
    public void Кривая_из_одной_точки_ловится()
    {
        Job job = Valid();
        job.Groups[0].Curves = [[[0, 0, 0]]];

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "SHORT_CURVE");
    }

    [Fact]
    public void Точка_из_двух_координат_ловится()
    {
        Job job = Valid();
        job.Groups[0].Curves = [[[0, 0], [1, 1]]];

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "BAD_POINT");
    }

    [Fact]
    public void Неизвестный_генератор_ловится()
    {
        Job job = Valid();
        job.Groups[0].Generator = "магия";

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "UNKNOWN_GENERATOR");
    }

    [Fact]
    public void Неизвестные_единицы_ловятся()
    {
        Job job = Valid();
        job.Units = "локоть";

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "BAD_UNITS");
    }

    [Fact]
    public void Чужая_схема_ловится()
    {
        Job job = Valid();
        job.Schema = "rebar-job-9.9";

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "BAD_SCHEMA");
    }

    [Fact]
    public void Подошва_без_сеток_ловится()
    {
        Job job = Valid();
        job.Groups[0].Generator = "footing";
        job.Groups[0].Curves = null;

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "NO_MAT");
    }

    [Fact]
    public void Подошва_с_нулевым_шагом_ловится()
    {
        Job job = Valid();
        GroupSpec g = job.Groups[0];
        g.Generator = "footing";
        g.Curves = null;
        g.Bottom = new MatSpec { X = new DirSpec { BarType = "#5", Spacing = 0 } };

        Assert.Contains(JobValidator.Validate(job), e => e.Code == "NO_SPACING");
    }

    [Fact]
    public void Ошибки_возвращаются_списком_а_не_по_одной()
    {
        var job = new Job
        {
            Units = "локоть",
            Groups =
            [
                new GroupSpec { Key = "", Generator = "polyline" },
                new GroupSpec { Key = "", Generator = "магия" },
            ],
        };

        IReadOnlyList<ReportError> errors = JobValidator.Validate(job);

        Assert.True(errors.Count >= 5, $"ожидался полный список, получено {errors.Count}");
        Assert.Contains(errors, e => e.Code == "BAD_UNITS");
        Assert.Contains(errors, e => e.Code == "NO_KEY");
        Assert.Contains(errors, e => e.Code == "NO_HOST");
        Assert.Contains(errors, e => e.Code == "UNKNOWN_GENERATOR");
    }
}
