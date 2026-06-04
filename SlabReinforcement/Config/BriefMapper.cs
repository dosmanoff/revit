namespace SlabReinforcement.Config;

/// <summary>
/// Maps a <see cref="BriefSlab"/> to the <see cref="SlabReinforcementConfig"/> the field /
/// opening builders consume. Per-segment edges and detailed groups are taken from the brief
/// directly by their own builders, not flattened here.
/// </summary>
public static class BriefMapper
{
    public static SlabReinforcementConfig ToConfig(SlabBrief brief, BriefSlab s)
    {
        var cfg = new SlabReinforcementConfig
        {
            Name = !string.IsNullOrWhiteSpace(s.Mark) ? s.Mark! : $"brief-{s.ElementId}",
            Units = brief.Units,
            FieldMode = s.Field.Mode,
            CleanExisting = s.CleanExisting,
            Cover = s.Cover,
        };

        cfg.Field.BottomX = s.Field.Bottom.X;
        cfg.Field.BottomY = s.Field.Bottom.Y;
        cfg.Field.TopMode = s.Field.Top.Coverage;
        cfg.Field.TopX = s.Field.Top.X;
        cfg.Field.TopY = s.Field.Top.Y;

        cfg.Lengths.MaxBarLength = s.Lengths.MaxBarLength;
        cfg.Lengths.LapMode = s.Lengths.Lap.Mode;
        cfg.Lengths.LapFactor = s.Lengths.Lap.Factor;
        cfg.Lengths.LapLength = s.Lengths.Lap.Length;
        cfg.Lengths.LapStagger = s.Lengths.Lap.Stagger;

        cfg.Openings.TrimEnabled = !string.Equals(s.Openings.Trim, "none", StringComparison.OrdinalIgnoreCase);
        cfg.Openings.Selector = s.Openings.Trim;       // "auto" | "all" | "none" | indices
        cfg.Openings.BarType = s.Openings.BarType;
        cfg.Openings.ExtraEachSide = s.Openings.ExtraEachSide;
        cfg.Openings.UBars = s.Openings.UBars;
        cfg.Openings.Diagonals = s.Openings.Diagonals;
        cfg.Openings.DiagBarType = s.Openings.BarType;

        return cfg;
    }
}
