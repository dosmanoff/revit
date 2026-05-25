namespace SlabRebar.Engine;

public class ClassificationConfig
{
    // Slab horizontal rebar — written to TargetParameterSlab
    public string LabelBottomX { get; set; } = "BOTTOM_X";
    public string LabelBottomY { get; set; } = "BOTTOM_Y";
    public string LabelTopX    { get; set; } = "TOP_X";
    public string LabelTopY    { get; set; } = "TOP_Y";

    // Dowel rebar (vertical bars / выпуски) — written to TargetParameterDowel
    public string LabelDowel { get; set; } = "DOWEL";

    public string TargetParameterSlab  { get; set; } = "T/B SLAB";
    public string TargetParameterDowel { get; set; } = "Dowel";
}
