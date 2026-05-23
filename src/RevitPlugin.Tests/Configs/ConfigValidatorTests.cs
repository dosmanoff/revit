using FluentAssertions;
using RevitPlugin.Domain.Configs;
using Xunit;

namespace RevitPlugin.Tests.Configs;

public class ConfigValidatorTests
{
    [Fact]
    public void Default_minimal_config_has_no_errors()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Минимальный",
            ExternalReinforcement = new MeshConfig()
        };

        var report = ConfigValidator.Validate(config);

        report.HasErrors.Should().BeFalse(string.Join("; ",
            report.Messages.Where(m => m.Severity == ValidationSeverity.Error)
                .Select(m => $"{m.Field}: {m.Message}")));
    }

    [Fact]
    public void Empty_name_is_error()
    {
        var report = ConfigValidator.Validate(new RebarConfig { Name = "" });
        report.HasErrors.Should().BeTrue();
        report.Messages.Should().Contain(m => m.Field == "name" && m.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Spacing_below_2x_diameter_emits_warning()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            ExternalReinforcement = new MeshConfig
            {
                BarTypeVertical = "Ø20 A500C",
                SpacingVertical = 30   // < 2 × 20
            }
        };

        var report = ConfigValidator.Validate(config);
        report.Messages.Should().Contain(m =>
            m.Field == "external_reinforcement.spacing_vertical"
            && m.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Invalid_major_is_error()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            ExternalReinforcement = new MeshConfig { Major = "Diagonal" }
        };

        var report = ConfigValidator.Validate(config);
        report.HasErrors.Should().BeTrue();
        report.Messages.Should().Contain(m => m.Field == "external_reinforcement.major");
    }

    [Fact]
    public void Negative_offset_is_error()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            ExternalReinforcement = new MeshConfig { WallEndOffsetDistance = -10 }
        };

        var report = ConfigValidator.Validate(config);
        report.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Min_thickness_greater_than_max_thickness_is_error()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            Applicability = new Applicability { MinThickness = 500, MaxThickness = 200 }
        };

        var report = ConfigValidator.Validate(config);
        report.HasErrors.Should().BeTrue();
        report.Messages.Should().Contain(m => m.Field == "applicability.min_thickness");
    }

    [Fact]
    public void Perimeter_count_3_is_error()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            Perimeter = new PerimeterConfig
            {
                EdgeRebar = new PerimeterEdgeConfig { Count = 3 }
            }
        };

        var report = ConfigValidator.Validate(config);
        report.HasErrors.Should().BeTrue();
        report.Messages.Should().Contain(m => m.Field == "perimeter.edge_rebar.count");
    }

    [Fact]
    public void Opening_max_width_less_than_min_width_is_error()
    {
        var config = new RebarConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "test",
            Opening = new OpeningConfig
            {
                EdgeRebar = new OpeningEdgeConfig { MinWidth = 1000, MaxWidth = 500 }
            }
        };

        var report = ConfigValidator.Validate(config);
        report.HasErrors.Should().BeTrue();
    }

    [Theory]
    [InlineData("Ø12 A500C", 12)]
    [InlineData("Ø 16 A500C", 16)]
    [InlineData("d10", 10)]
    [InlineData("25mm Bar", 25)]
    [InlineData("", 12)]              // дефолт
    [InlineData("No digits here", 12)] // дефолт
    public void GuessDiameter_parses_typical_bar_type_names(string barType, double expected)
    {
        ConfigValidator.GuessDiameter(barType).Should().Be(expected);
    }
}
