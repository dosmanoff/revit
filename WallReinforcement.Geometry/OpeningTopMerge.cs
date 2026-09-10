namespace WallReinforcement.Geometry;

/// <summary>
/// Decides when the strip of wall above an opening is so short that the opening-top U-bar (legs up)
/// and the wall-top U-bar (legs down) would touch — in which case those two U-bars plus the short
/// vertical between them are replaced by a single closed stirrup ("хомут") spanning the strip.
/// Pure (Revit-free) so it stays unit-testable.
/// </summary>
public static class OpeningTopMerge
{
    /// <summary>
    /// True when the clear strip above the opening (<paramref name="stripClearFt"/> =
    /// wall-top-cover minus opening top) is positive but no taller than the two U-bars' combined
    /// leg reach, so their legs overlap.
    /// </summary>
    public static bool Fires(double stripClearFt, double legUpFt, double legDownFt)
        => stripClearFt > 1e-6 && stripClearFt <= legUpFt + legDownFt;
}
