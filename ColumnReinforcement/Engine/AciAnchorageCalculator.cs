namespace ColumnReinforcement.Engine;

/// <summary>
/// Anchorage and lap-splice length calculations per ACI 318-19. Pure functions, no
/// Revit API dependencies — kept testable in isolation and reusable from
/// command-line tools or other dialogs.
///
/// <para>Covers four common queries:
/// <list type="bullet">
///   <item><c>ℓd</c> — tension development length per §25.4.2.3 (simplified table).</item>
///   <item><c>ℓst</c> — Class B tension lap splice per §25.5.2.1 (1.3·ℓd).</item>
///   <item><c>ℓdc</c> — compression development length per §25.4.9.2.</item>
///   <item><c>ℓsc</c> — compression lap splice per §25.5.5.</item>
/// </list>
/// </para>
///
/// <para>Simplifications:
/// <list type="bullet">
///   <item>Modification factor <c>ψs</c> baked into the divisor (25 / 20 for adequate
///         spacing; 16.7 / 13.3 for limited spacing).</item>
///   <item>Confinement index <c>Ktr</c> assumed zero — adequate-spacing rows of Table
///         25.4.2.3 already require stirrups ≥ ACI minimum, which is the typical column case.</item>
///   <item>Bar sizes restricted to ASTM A615 inch designations (#3..#11, #14, #18).</item>
/// </list>
/// </para>
/// </summary>
public static class AciAnchorageCalculator
{
    public record Inputs
    {
        public required string BarSize { get; init; }            // "#3".."#11", "#14", "#18"
        public required double FcPsi   { get; init; }            // f'c, psi
        public required double FyPsi   { get; init; }            // fy, psi

        /// <summary>ψt = 1.3 when more than 12" of fresh concrete is cast below the bar.</summary>
        public bool IsTopBar { get; init; }

        /// <summary>ψe = 1.5 for epoxy-coated bars (capped so ψt·ψe ≤ 1.7).</summary>
        public bool IsEpoxyCoated { get; init; }

        /// <summary>λ = 0.75 for lightweight concrete (§25.4.1.4).</summary>
        public bool IsLightweight { get; init; }

        /// <summary>
        /// When true, use the "adequate spacing and cover" rows of Table 25.4.2.3
        /// (divisor 25 for #6 and smaller, 20 for #7 and larger). When false, use
        /// the "other" rows (16.7 / 13.3).
        /// </summary>
        public bool AdequateSpacing { get; init; } = true;

        /// <summary>ψr = 0.75 for compression development with adequate confinement (§25.4.9.3).</summary>
        public bool ConfinedCompression { get; init; }
    }

    public record Outputs
    {
        /// <summary>Tension development length ℓd, inches. ACI 318-19 §25.4.2.3.</summary>
        public double DevelopmentLengthTensionIn { get; init; }

        /// <summary>Class B tension lap splice length ℓst, inches. ACI 318-19 §25.5.2.1.</summary>
        public double TensionLapSpliceClassBIn { get; init; }

        /// <summary>Compression development length ℓdc, inches. ACI 318-19 §25.4.9.2.</summary>
        public double DevelopmentLengthCompressionIn { get; init; }

        /// <summary>Compression lap splice length ℓsc, inches. ACI 318-19 §25.5.5.</summary>
        public double CompressionLapSpliceIn { get; init; }
    }

    /// <summary>ASTM A615 nominal bar diameters (inches).</summary>
    public static readonly IReadOnlyDictionary<string, double> BarDiameters = new Dictionary<string, double>
    {
        ["#3"]  = 0.375,
        ["#4"]  = 0.500,
        ["#5"]  = 0.625,
        ["#6"]  = 0.750,
        ["#7"]  = 0.875,
        ["#8"]  = 1.000,
        ["#9"]  = 1.128,
        ["#10"] = 1.270,
        ["#11"] = 1.410,
        ["#14"] = 1.693,
        ["#18"] = 2.257,
    };

    public static double DiameterIn(string barSize) =>
        BarDiameters.TryGetValue(barSize, out double d)
            ? d
            : throw new ArgumentException(
                $"Unknown bar size '{barSize}'. Expected one of: {string.Join(", ", BarDiameters.Keys)}.");

    public static Outputs Compute(Inputs i)
    {
        if (i.FcPsi <= 0) throw new ArgumentException("f'c must be positive.", nameof(i));
        if (i.FyPsi <= 0) throw new ArgumentException("fy must be positive.", nameof(i));

        double db = DiameterIn(i.BarSize);

        double psiT = i.IsTopBar       ? 1.3 : 1.0;
        double psiE = i.IsEpoxyCoated  ? 1.5 : 1.0;
        if (psiT * psiE > 1.7)         psiE = 1.7 / psiT;   // §25.4.2.5 cap
        double psiG = PsiG(i.FyPsi);
        double lambda = i.IsLightweight ? 0.75 : 1.0;

        // §25.4.1.4: √f'c ≤ 100 psi.
        double sqrtFc = Math.Min(100.0, Math.Sqrt(i.FcPsi));

        bool small = IsSmallBar(i.BarSize);
        double divisor = i.AdequateSpacing
            ? (small ? 25.0   : 20.0)
            : (small ? 16.7   : 13.3);

        // §25.4.2.3 simplified — ℓd / db.
        double ldOverDb = (i.FyPsi * psiT * psiE * psiG) / (divisor * lambda * sqrtFc);
        double ld = Math.Max(12.0, ldOverDb * db);

        // §25.5.2.1 — Class B tension lap splice.
        double lst = Math.Max(12.0, 1.3 * ld);

        // §25.4.9.2 — compression development length.
        double psiR = i.ConfinedCompression ? 0.75 : 1.0;
        double ldc1 = (i.FyPsi * psiR) / (50.0 * lambda * sqrtFc) * db;
        double ldc2 = 0.0003 * i.FyPsi * psiR * db;
        double ldc  = Math.Max(8.0, Math.Max(ldc1, ldc2));

        // §25.5.5 — compression lap splice.
        double lsc = i.FyPsi <= 60000.0
            ? 0.0005 * i.FyPsi * db
            : (0.0009 * i.FyPsi - 24.0) * db;
        if (i.FcPsi < 3000.0) lsc *= 4.0 / 3.0;             // §25.5.5.1.3
        lsc = Math.Max(12.0, lsc);

        return new Outputs
        {
            DevelopmentLengthTensionIn     = ld,
            TensionLapSpliceClassBIn       = lst,
            DevelopmentLengthCompressionIn = ldc,
            CompressionLapSpliceIn         = lsc,
        };
    }

    // #6 and smaller → smaller-bar simplified divisor.
    private static bool IsSmallBar(string size) =>
        size is "#3" or "#4" or "#5" or "#6";

    // Table 25.4.2.5 — ψg by grade.
    private static double PsiG(double fyPsi)
    {
        if (fyPsi <= 60000.0) return 1.0;
        if (fyPsi <= 80000.0) return 1.15;
        return 1.3;
    }
}
