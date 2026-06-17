using System.Globalization;
using StairsReinforcement.Domain;

namespace StairsReinforcement.Config;

/// <summary>
/// Parses the agent's sparse <c>stairs-assignments.csv</c> into one
/// <see cref="StairsReinforcementConfig"/> per Mark. Absent/empty cells keep the config default;
/// bad cells log a <see cref="ParseIssue"/> and keep the default. See stairs-assignments-csv-guide.md.
/// </summary>
public static class AssignmentCsv
{
    private static readonly (string Prefix, Func<StairsReinforcementConfig, BarSetSpec> Get)[] Sets =
    {
        ("FlightBotMain", c => c.Flight.BottomMain),
        ("FlightBotDist", c => c.Flight.BottomDist),
        ("FlightTopMain", c => c.Flight.TopMain),
        ("FlightTopDist", c => c.Flight.TopDist),
        ("LandBotX",      c => c.Landing.BottomX),
        ("LandBotY",      c => c.Landing.BottomY),
        ("LandTopX",      c => c.Landing.TopX),
        ("LandTopY",      c => c.Landing.TopY),
    };

    public static AssignmentTable Parse(string text, string? sourcePath = null)
    {
        var issues = new List<ParseIssue>();
        var byMark = new Dictionary<string, StairsReinforcementConfig>();
        var expected = new Dictionary<string, ExpectedGeom>();

        List<Row> rows = ReadRows(text, issues);
        foreach (Row r in rows)
        {
            string? mark = r.Get("Mark");
            if (string.IsNullOrWhiteSpace(mark))
            {
                issues.Add(new ParseIssue(r.Line, "Mark", "row has no Mark — skipped"));
                continue;
            }

            var cfg = new StairsReinforcementConfig { Name = mark };
            ApplyConfig(cfg, r, issues);
            ExpectedGeom exp = ReadExpected(r, issues);

            string key = AssignmentTable.Key(mark);
            if (byMark.ContainsKey(key))
                issues.Add(new ParseIssue(r.Line, "Mark", $"duplicate Mark '{mark}' — later row wins"));
            byMark[key] = cfg;
            if (exp.Any) expected[key] = exp;
        }

        return new AssignmentTable { ByMark = byMark, ExpectedByMark = expected, Issues = issues, SourcePath = sourcePath };
    }

    // ── row → config ──────────────────────────────────────────────────────────

    private static void ApplyConfig(StairsReinforcementConfig cfg, Row r, List<ParseIssue> issues)
    {
        if (EnumCell<UnitSystem>(r, "Units", issues) is { } u) cfg.Units = u;
        if (BoolCell(r, "CleanExisting", issues) is { } ce) cfg.CleanExisting = ce;

        if (LengthCell(r, "CoverTop", issues) is { } ct) cfg.Cover.Top = ct;
        if (LengthCell(r, "CoverBottom", issues) is { } cb) cfg.Cover.Bottom = cb;
        if (LengthCell(r, "CoverSide", issues) is { } cs) cfg.Cover.Side = cs;

        foreach ((string prefix, var get) in Sets)
            ApplyBarSet(prefix, get(cfg), r, issues);

        // Flight top
        if (EnumCell<TopMode>(r, "FlightTopMode", issues) is { } ftm) cfg.Flight.TopMode = ftm;
        if (LengthCell(r, "FlightTopSupportExtent", issues) is { } fte) cfg.Flight.TopSupportExtent = fte;

        // Steps
        if (EnumCell<StepMode>(r, "StepsMode", issues) is { } sm) cfg.Flight.Steps.Mode = sm;
        if (r.Get("StepsBarType") is { } sbt) cfg.Flight.Steps.BarType = sbt;
        if (LengthCell(r, "StepsLeg", issues) is { } sl) cfg.Flight.Steps.Leg = sl;

        // Landing
        if (EnumCell<FieldMode>(r, "LandingMode", issues) is { } lm) cfg.Landing.Mode = lm;
        if (EnumCell<TopMode>(r, "LandingTopMode", issues) is { } ltm) cfg.Landing.TopMode = ltm;
        if (LengthCell(r, "LandingTopSupportExtent", issues) is { } lte) cfg.Landing.TopSupportExtent = lte;

        // Knee
        if (BoolCell(r, "KneeEnabled", issues) is { } ke) cfg.Connections.Knee.Enabled = ke;
        if (EnumCell<KneeMode>(r, "KneeMode", issues) is { } km) cfg.Connections.Knee.Mode = km;
        if (r.Get("KneeBarType") is { } kbt) cfg.Connections.Knee.BarType = kbt;
        if (LengthCell(r, "KneeSpacing", issues) is { } ksp) { cfg.Connections.Knee.Spacing = ksp; cfg.Connections.Knee.SpacingMode = SpacingMode.Spacing; }
        if (IntCell(r, "KneeCount", issues) is { } kc) { cfg.Connections.Knee.Count = kc; cfg.Connections.Knee.SpacingMode = SpacingMode.Count; }
        if (LengthCell(r, "KneeLeg", issues) is { } kl) cfg.Connections.Knee.Leg = kl;

        // Starters
        if (BoolCell(r, "StartersEnabled", issues) is { } se) cfg.Connections.Starters.Enabled = se;
        if (EnumCell<StarterHost>(r, "StarterHost", issues) is { } sh) cfg.Connections.Starters.Host = sh;
        if (EnumCell<StarterForm>(r, "StarterForm", issues) is { } sf) cfg.Connections.Starters.Form = sf;
        if (r.Get("StarterBarType") is { } stbt) cfg.Connections.Starters.BarType = stbt;
        if (LengthCell(r, "StarterSpacing", issues) is { } ssp) { cfg.Connections.Starters.Spacing = ssp; cfg.Connections.Starters.SpacingMode = SpacingMode.Spacing; }
        if (IntCell(r, "StarterCount", issues) is { } stc) { cfg.Connections.Starters.Count = stc; cfg.Connections.Starters.SpacingMode = SpacingMode.Count; }
        if (LengthCell(r, "StarterEmbed", issues) is { } sem) cfg.Connections.Starters.Embed = sem;
        if (LengthCell(r, "StarterProjection", issues) is { } spr) cfg.Connections.Starters.Projection = spr;

        // Lengths / lap
        if (LengthCell(r, "MaxBarLength", issues) is { } mbl) cfg.Lengths.MaxBarLength = mbl;
        if (EnumCell<LapMode>(r, "LapMode", issues) is { } lap) cfg.Lengths.LapMode = lap;
        if (LengthCell(r, "LapLength", issues) is { } ll) cfg.Lengths.LapLength = ll;
        if (DoubleCell(r, "LapFactor", issues) is { } lf) cfg.Lengths.LapFactor = lf;
        if (BoolCell(r, "LapStagger", issues) is { } ls) cfg.Lengths.LapStagger = ls;
    }

    private static void ApplyBarSet(string prefix, BarSetSpec spec, Row r, List<ParseIssue> issues)
    {
        if (r.Get(prefix + "BarType") is { } bt) spec.BarType = bt;
        if (BoolCell(r, prefix + "Enabled", issues) is { } en) spec.Enabled = en;
        if (LengthCell(r, prefix + "Spacing", issues) is { } sp) { spec.Spacing = sp; spec.SpacingMode = SpacingMode.Spacing; }
        if (IntCell(r, prefix + "Count", issues) is { } cnt) { spec.Count = cnt; spec.SpacingMode = SpacingMode.Count; }
        if (LengthCell(r, prefix + "Cover", issues) is { } cov) spec.Cover = cov;
        if (EnumCell<AnchorMode>(r, prefix + "StartAnchor", issues) is { } sa) spec.StartAnchor = sa;
        if (EnumCell<AnchorMode>(r, prefix + "EndAnchor", issues) is { } ea) spec.EndAnchor = ea;
        if (LengthCell(r, prefix + "StartAnchorLen", issues) is { } sal) spec.StartAnchorLen = sal;
        if (LengthCell(r, prefix + "EndAnchorLen", issues) is { } eal) spec.EndAnchorLen = eal;
        if (r.Get(prefix + "StartHook") is { } shk) spec.StartHook = shk;
        if (r.Get(prefix + "EndHook") is { } ehk) spec.EndHook = ehk;
    }

    private static ExpectedGeom ReadExpected(Row r, List<ParseIssue> issues) => new()
    {
        Waist = LengthCell(r, "ExpectedWaist", issues),
        Width = LengthCell(r, "ExpectedWidth", issues),
        Rise = LengthCell(r, "ExpectedRise", issues),
    };

    // ── low-level CSV reading ─────────────────────────────────────────────────

    private sealed class Row
    {
        private readonly string[] _cells;
        private readonly Dictionary<string, int> _header;
        public int Line { get; }

        public Row(string[] cells, Dictionary<string, int> header, int line) { _cells = cells; _header = header; Line = line; }

        public string? Get(string name)
        {
            if (!_header.TryGetValue(name, out int i) || i >= _cells.Length) return null;
            string v = _cells[i].Trim();
            return v.Length == 0 ? null : v;
        }
    }

    private static List<Row> ReadRows(string text, List<ParseIssue> issues)
    {
        var rows = new List<Row>();
        string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        Dictionary<string, int>? header = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;

            string[] cells = line.Split(',');
            if (header is null)
            {
                header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < cells.Length; c++)
                {
                    string name = cells[c].Trim();
                    if (name.Length > 0 && !header.ContainsKey(name)) header[name] = c;
                }
                continue;
            }
            rows.Add(new Row(cells, header, i + 1));
        }

        if (header is null) issues.Add(new ParseIssue(0, "header", "no header row found"));
        return rows;
    }

    // ── typed cells (null = absent/invalid; default survives) ─────────────────

    private static Length? LengthCell(Row r, string name, List<ParseIssue> issues)
    {
        string? s = r.Get(name);
        if (s is null) return null;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double n)
            ? new Length(n)
            : new Length(s);   // feet-inches string kept literal (e.g. 2'-0")
    }

    private static bool? BoolCell(Row r, string name, List<ParseIssue> issues)
    {
        string? s = r.Get(name);
        if (s is null) return null;
        switch (s.ToLowerInvariant())
        {
            case "true" or "yes" or "y" or "1": return true;
            case "false" or "no" or "n" or "0": return false;
            default: issues.Add(new ParseIssue(r.Line, name, $"'{s}' is not a bool")); return null;
        }
    }

    private static int? IntCell(Row r, string name, List<ParseIssue> issues)
    {
        string? s = r.Get(name);
        if (s is null) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
        issues.Add(new ParseIssue(r.Line, name, $"'{s}' is not an int")); return null;
    }

    private static double? DoubleCell(Row r, string name, List<ParseIssue> issues)
    {
        string? s = r.Get(name);
        if (s is null) return null;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
        issues.Add(new ParseIssue(r.Line, name, $"'{s}' is not a number")); return null;
    }

    private static T? EnumCell<T>(Row r, string name, List<ParseIssue> issues) where T : struct, Enum
    {
        string? s = r.Get(name);
        if (s is null) return null;
        if (Enum.TryParse(s, ignoreCase: true, out T v) && Enum.IsDefined(v)) return v;
        issues.Add(new ParseIssue(r.Line, name, $"'{s}' is not a valid {typeof(T).Name}")); return null;
    }
}
