namespace SlabReinforcement.Geometry;

/// <summary>
/// Tension development length and lap-splice calculations per ACI 318-19. Pure functions, no
/// Revit dependency — unit-tested. Ported from <c>ColumnReinforcement.Engine.AciAnchorageCalculator</c>
/// (tension only; the slab plugin laps mat bars, which are tension splices).
///
/// <para>Simplified §25.4.2.3: ψs baked into the divisor (25 / 20 for adequate spacing &amp; cover;
/// 16.7 / 13.3 otherwise), confinement Ktr = 0. Class B tension lap = 1.3·ℓd (§25.5.2.1).</para>
/// </summary>
public static class AciAnchorageCalculator
{
    public sealed record Inputs
    {
        public required string BarSize { get; init; }   // "#3".."#11", "#14", "#18"
        public required double FcPsi { get; init; }      // f'c, psi
        public required double FyPsi { get; init; }      // fy, psi
        public bool IsTopBar { get; init; }              // ψt = 1.3 (>12" concrete cast below)
        public bool IsEpoxyCoated { get; init; }         // ψe = 1.5 (cap ψt·ψe ≤ 1.7)
        public bool IsLightweight { get; init; }         // λ = 0.75
        public bool AdequateSpacing { get; init; } = true;
    }

    /// <summary>ASTM A615 nominal bar diameters (inches).</summary>
    public static readonly IReadOnlyDictionary<string, double> BarDiameters = new Dictionary<string, double>
    {
        ["#3"] = 0.375, ["#4"] = 0.500, ["#5"] = 0.625, ["#6"] = 0.750, ["#7"] = 0.875,
        ["#8"] = 1.000, ["#9"] = 1.128, ["#10"] = 1.270, ["#11"] = 1.410, ["#14"] = 1.693, ["#18"] = 2.257,
    };

    public static double DiameterIn(string barSize) =>
        BarDiameters.TryGetValue(barSize, out double d)
            ? d
            : throw new ArgumentException(
                $"Unknown bar size '{barSize}'. Expected one of: {string.Join(", ", BarDiameters.Keys)}.");

    /// <summary>Tension development length ℓd, inches (§25.4.2.3 simplified). Min 12".</summary>
    public static double DevelopmentLengthTensionIn(Inputs i)
    {
        if (i.FcPsi <= 0) throw new ArgumentException("f'c must be positive.", nameof(i));
        if (i.FyPsi <= 0) throw new ArgumentException("fy must be positive.", nameof(i));

        double db = DiameterIn(i.BarSize);
        double psiT = i.IsTopBar ? 1.3 : 1.0;
        double psiE = i.IsEpoxyCoated ? 1.5 : 1.0;
        if (psiT * psiE > 1.7) psiE = 1.7 / psiT;                 // §25.4.2.5 cap
        double psiG = i.FyPsi <= 60000.0 ? 1.0 : (i.FyPsi <= 80000.0 ? 1.15 : 1.3);
        double lambda = i.IsLightweight ? 0.75 : 1.0;
        double sqrtFc = Math.Min(100.0, Math.Sqrt(i.FcPsi));       // §25.4.1.4

        bool small = i.BarSize is "#3" or "#4" or "#5" or "#6";
        double divisor = i.AdequateSpacing ? (small ? 25.0 : 20.0) : (small ? 16.7 : 13.3);

        double ldOverDb = (i.FyPsi * psiT * psiE * psiG) / (divisor * lambda * sqrtFc);
        return Math.Max(12.0, ldOverDb * db);
    }

    /// <summary>Class B tension lap splice length ℓst = max(12", 1.3·ℓd), inches (§25.5.2.1).</summary>
    public static double TensionLapSpliceClassBIn(Inputs i) =>
        Math.Max(12.0, 1.3 * DevelopmentLengthTensionIn(i));
}
