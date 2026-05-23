using FluentAssertions;
using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Rules;
using Xunit;

namespace RevitPlugin.Tests.Domain;

public class ExternalMeshRuleTests
{
    private static RebarConfig DefaultConfig() => new()
    {
        Name = "test",
        ExternalReinforcement = new MeshConfig(),
        InternalReinforcement = new MeshConfig { Enabled = false }
    };

    /// <summary>
    /// ROADMAP M1 DoD: Стена 5×3 м, шаг 200 — 25 вертикальных + 14 горизонтальных стержней.
    /// </summary>
    [Fact]
    public void Wall_5x3_at_step_200_produces_25_verticals_and_14_horizontals()
    {
        var rule = new ExternalMeshRule();
        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);

        var result = rule.Execute(wall, DefaultConfig());

        result.Warnings.Should().BeEmpty();

        var verticals = result.Placements.Where(p => p.Role == BarRole.Vertical).ToList();
        var horizontals = result.Placements.Where(p => p.Role == BarRole.Horizontal).ToList();

        verticals.Should().HaveCount(25, "5000mm с offset 100/100 + шаг 200 даёт 25 стержней");
        horizontals.Should().HaveCount(14, "3000mm с offset 200/200 (vertical_offset_top/bottom) + шаг 200 даёт 14 стержней");
    }

    [Fact]
    public void Verticals_have_length_height_minus_top_and_bottom_offsets()
    {
        var rule = new ExternalMeshRule();
        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);

        var result = rule.Execute(wall, DefaultConfig());
        var firstVertical = result.Placements.First(p => p.Role == BarRole.Vertical);

        var segment = firstVertical.Segments.Single();
        var length = (segment.End - segment.Start).Length;
        length.Should().BeApproximately(3000 - 200 - 200, precision: 1e-6);
    }

    [Fact]
    public void Horizontals_have_length_wall_minus_start_and_end_offsets()
    {
        var rule = new ExternalMeshRule();
        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);

        var result = rule.Execute(wall, DefaultConfig());
        var firstHorizontal = result.Placements.First(p => p.Role == BarRole.Horizontal);

        var segment = firstHorizontal.Segments.Single();
        var length = (segment.End - segment.Start).Length;
        length.Should().BeApproximately(5000 - 100 - 100, precision: 1e-6);
    }

    [Fact]
    public void Short_wall_falls_back_to_single_central_bar()
    {
        var rule = new ExternalMeshRule();
        var wall = WallContext.CreateStraight(length: 150, height: 3000, thickness: 200);

        var result = rule.Execute(wall, DefaultConfig());
        var verticals = result.Placements.Where(p => p.Role == BarRole.Vertical).ToList();

        verticals.Should().HaveCount(1, "при offset 100/100 на 150mm зона < spacing → один центральный стержень");
    }

    [Fact]
    public void Disabled_mesh_produces_no_placements()
    {
        var rule = new ExternalMeshRule();
        var config = new RebarConfig
        {
            Name = "test",
            ExternalReinforcement = new MeshConfig { Enabled = false }
        };

        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);
        rule.IsApplicable(wall, config).Should().BeFalse();

        var result = rule.Execute(wall, config);
        result.Placements.Should().BeEmpty();
    }

    [Fact]
    public void ComputePositions_FromStartEnd_returns_arithmetic_series()
    {
        var warnings = new List<string>();
        var positions = MeshRuleBase.ComputePositions(
            extent: 5000, offsetStart: 100, offsetEnd: 100,
            spacing: 200, mode: "FromStartEnd", warnings: warnings, label: "test");

        positions.Should().HaveCount(25);
        positions[0].Should().Be(100);
        positions[^1].Should().Be(4900);
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Internal_mesh_places_bars_on_opposite_face()
    {
        var external = new ExternalMeshRule();
        var internalRule = new InternalMeshRule();
        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);
        var config = new RebarConfig
        {
            Name = "test",
            ExternalReinforcement = new MeshConfig(),
            InternalReinforcement = new MeshConfig()
        };

        var ext = external.Execute(wall, config);
        var intRes = internalRule.Execute(wall, config);

        var extVertical = ext.Placements.First(p => p.Role == BarRole.Vertical).Segments.Single().Start;
        var intVertical = intRes.Placements.First(p => p.Role == BarRole.Vertical).Segments.Single().Start;

        // С противоположных граней — Y-координата отличается по знаку (вдоль нормали)
        Math.Sign(extVertical.Y - 0).Should().NotBe(Math.Sign(intVertical.Y - 0));
    }
}
