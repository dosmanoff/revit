using FluentAssertions;
using RevitPlugin.Domain.Configs;
using RevitPlugin.Domain.Geometry;
using RevitPlugin.Domain.Placement;
using RevitPlugin.Domain.Rules;
using Xunit;

namespace RevitPlugin.Tests.Domain;

public class OpeningEdgeRuleTests
{
    private static WallContext WallWithDoor(double doorWidth = 1000, double doorHeight = 2100)
    {
        var opening = new OpeningGeometry(
            Id: "door-1",
            XStart: 1500,
            XEnd: 1500 + doorWidth,
            YStart: 0,
            YEnd: doorHeight,
            Kind: OpeningKind.Door);

        return new WallContext(
            Id: 1, Mark: "СТ-1", TypeName: "Wall 200",
            LocationLine: new Line3(Vec3.Zero, new Vec3(5000, 0, 0)),
            Height: 3000, Thickness: 200, BaseElevation: 0,
            ExteriorNormal: Vec3.UnitY,
            Openings: new[] { opening },
            Parameters: new Dictionary<string, string>());
    }

    private static RebarConfig WithOpening(OpeningEdgeConfig edge) => new()
    {
        Name = "test",
        Opening = new OpeningConfig { Enabled = true, EdgeRebar = edge }
    };

    [Fact]
    public void All_sides_both_faces_count_1_on_door_produces_8_bars()
    {
        // 4 стороны × 2 грани × 1 стержень = 8. Bottom для двери лежит на YStart=0 →
        // offsetIntoWall=30 → z=-30 → ниже основания → стержень пропускается.
        // Поэтому ожидаем 6.
        var rule = new OpeningEdgeRule();
        var wall = WallWithDoor();
        var config = WithOpening(new OpeningEdgeConfig { Sides = "All", Position = "Both", Count = 1 });

        var result = rule.Execute(wall, config);

        result.Placements.Should().HaveCount(6, "Bottom-стержень двери у уровня пола пропускается");
    }

    [Fact]
    public void Window_with_bottom_above_floor_produces_all_4_sides()
    {
        var rule = new OpeningEdgeRule();
        var window = new OpeningGeometry("win-1", 1000, 2200, 900, 2400, OpeningKind.Window);
        var wall = new WallContext(
            Id: 1, Mark: "СТ-2", TypeName: "Wall 200",
            LocationLine: new Line3(Vec3.Zero, new Vec3(5000, 0, 0)),
            Height: 3000, Thickness: 200, BaseElevation: 0,
            ExteriorNormal: Vec3.UnitY,
            Openings: new[] { window },
            Parameters: new Dictionary<string, string>());

        var config = WithOpening(new OpeningEdgeConfig { Sides = "All", Position = "External", Count = 1 });

        var result = rule.Execute(wall, config);
        result.Placements.Should().HaveCount(4);
    }

    [Fact]
    public void Min_max_width_filter_skips_narrow_openings()
    {
        var rule = new OpeningEdgeRule();
        var wall = WallWithDoor(doorWidth: 700);
        var config = WithOpening(new OpeningEdgeConfig { Sides = "Top", Position = "External", MinWidth = 1000 });

        var result = rule.Execute(wall, config);
        result.Placements.Should().BeEmpty();
    }

    [Fact]
    public void No_openings_means_rule_is_not_applicable()
    {
        var rule = new OpeningEdgeRule();
        var wall = WallContext.CreateStraight(5000, 3000, 200);
        var config = WithOpening(new OpeningEdgeConfig());

        rule.IsApplicable(wall, config).Should().BeFalse();
        rule.Execute(wall, config).Placements.Should().BeEmpty();
    }

    [Fact]
    public void Anchorage_extends_top_bar_beyond_opening_corners()
    {
        var rule = new OpeningEdgeRule();
        var wall = WallWithDoor(doorWidth: 1000);
        var config = WithOpening(new OpeningEdgeConfig
        {
            Sides = "Top", Position = "External", Count = 1, AnchorageLength = 400
        });

        var result = rule.Execute(wall, config);
        var topBar = result.Placements.Single();
        var segment = topBar.Segments.Single();
        var length = (segment.End - segment.Start).Length;

        // Door width 1000 + 2 × anchorage 400 = 1800
        length.Should().BeApproximately(1800, 1e-6);
    }
}
