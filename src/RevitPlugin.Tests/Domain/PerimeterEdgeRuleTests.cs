using FluentAssertions;
using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Rules;
using Xunit;

namespace RevitPlugin.Tests.Domain;

public class PerimeterEdgeRuleTests
{
    private static RebarConfig WithPerimeter(PerimeterEdgeConfig edge) => new()
    {
        Name = "test",
        Perimeter = new PerimeterConfig { Enabled = true, EdgeRebar = edge }
    };

    [Fact]
    public void All_edges_on_both_faces_count_1_produces_8_bars()
    {
        // 4 ребра × 2 грани × 1 стержень = 8
        var rule = new PerimeterEdgeRule();
        var wall = WallContext.CreateStraight(length: 5000, height: 3000, thickness: 200);
        var config = WithPerimeter(new PerimeterEdgeConfig { Position = "Both", Count = 1, Edges = "All" });

        var result = rule.Execute(wall, config);

        result.Warnings.Should().BeEmpty();
        result.Placements.Should().HaveCount(8);
        result.Placements.Should().AllSatisfy(p => p.Role.Should().Be(BarRole.Edge));
    }

    [Fact]
    public void Only_top_external_count_2_produces_2_bars()
    {
        var rule = new PerimeterEdgeRule();
        var wall = WallContext.CreateStraight(5000, 3000, 200);
        var config = WithPerimeter(new PerimeterEdgeConfig { Edges = "Top", Position = "External", Count = 2 });

        var result = rule.Execute(wall, config);

        result.Placements.Should().HaveCount(2);
    }

    [Fact]
    public void Disabled_perimeter_produces_nothing()
    {
        var rule = new PerimeterEdgeRule();
        var wall = WallContext.CreateStraight(5000, 3000, 200);
        var config = new RebarConfig { Name = "x", Perimeter = new PerimeterConfig { Enabled = false } };

        rule.IsApplicable(wall, config).Should().BeFalse();
        rule.Execute(wall, config).Placements.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_edge_value_emits_warning_but_continues()
    {
        var rule = new PerimeterEdgeRule();
        var wall = WallContext.CreateStraight(5000, 3000, 200);
        var config = WithPerimeter(new PerimeterEdgeConfig { Edges = "Top,Garbage,Bottom" });

        var result = rule.Execute(wall, config);

        result.Warnings.Should().ContainSingle(w => w.Contains("Garbage"));
        // 2 валидных ребра × 2 грани × 1 = 4
        result.Placements.Should().HaveCount(4);
    }

    [Fact]
    public void Top_edge_bar_is_horizontal_along_wall_length()
    {
        var rule = new PerimeterEdgeRule();
        var wall = WallContext.CreateStraight(5000, 3000, 200);
        var config = WithPerimeter(new PerimeterEdgeConfig { Edges = "Top", Position = "External", Count = 1, EndCover = 30 });

        var result = rule.Execute(wall, config);
        var bar = result.Placements.Single();
        var segment = bar.Segments.Single();

        var direction = (segment.End - segment.Start).Normalized();
        direction.X.Should().BeApproximately(1.0, 1e-6);
        direction.Z.Should().BeApproximately(0.0, 1e-6);

        var length = (segment.End - segment.Start).Length;
        length.Should().BeApproximately(5000 - 60, 1e-6); // 2 × end_cover
    }
}
