namespace StairsReinforcement.Domain;

/// <summary>
/// Logical reinforcement layers of a stair. Used in the idempotency tag
/// (<c>STR:{config}:{stairId}:{layer}</c>) so a re-run replaces exactly the bars it owns,
/// and so the (future) Stair Views command can isolate one layer per view.
/// </summary>
public enum StairLayer
{
    FlightBottomMain,
    FlightBottomDist,
    FlightTopMain,
    FlightTopDist,
    Steps,
    LandingBottomX,
    LandingBottomY,
    LandingTopX,
    LandingTopY,
    Knee,
    Starter,
    Dowel,
    Pashka,
}
