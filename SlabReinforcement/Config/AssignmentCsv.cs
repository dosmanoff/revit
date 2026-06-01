using System.Globalization;
using SlabReinforcement.Domain;

namespace SlabReinforcement.Config;

/// <summary>
/// Parses <c>slab-assignments.csv</c> (one row per slab Mark) and <c>slab-zones.csv</c>
/// (one row per strengthening zone). Sparse: an empty cell leaves the config default.
/// Cells must not contain commas (selectors use spaces); feet-inches values like
/// <c>40'-0"</c> are kept literal. Lines starting with <c>#</c> and blank lines are skipped.
/// </summary>
public static class AssignmentCsv
{
    // ── Assignments ─────────────────────────────────────────────────────────────

    public static AssignmentTable Parse(string text, string? sourcePath = null)
    {
        var issues = new List<ParseIssue>();
        var byMark = new Dictionary<string, SlabReinforcementConfig>();
        var expected = new Dictionary<string, double>();

        List<(int lineNo, string[] cells)> rows = ReadRows(text, out Dictionary<string, int>? header);
        if (header is null)
            return new AssignmentTable { Issues = [new ParseIssue(0, "header", "No header row found.")], SourcePath = sourcePath };

        foreach ((int lineNo, string[] cells) in rows)
        {
            var row = new Row(header, cells, lineNo);

            string? mark = row.Get("Mark");
            if (mark is null)
            {
                issues.Add(new ParseIssue(lineNo, "Mark", "Row has no Mark — skipped."));
                continue;
            }

            var cfg = new SlabReinforcementConfig { Name = mark };
            ApplyConfig(row, cfg, issues);

            string key = AssignmentTable.Key(mark);
            if (byMark.ContainsKey(key))
                issues.Add(new ParseIssue(lineNo, "Mark", $"Duplicate Mark '{mark}' — later row wins."));
            byMark[key] = cfg;

            if (TryExpectedThicknessIn(row, cfg.Units) is { } th)
                expected[key] = th;
        }

        return new AssignmentTable
        {
            ByMark = byMark,
            ExpectedThicknessInByMark = expected,
            Issues = issues,
            SourcePath = sourcePath,
        };
    }

    private static void ApplyConfig(Row row, SlabReinforcementConfig cfg, List<ParseIssue> issues)
    {
        if (EnumCell<UnitSystem>(row, "Units", issues) is { } units) cfg.Units = units;
        if (BoolCell(row, "CleanExisting", issues) is { } clean) cfg.CleanExisting = clean;
        if (EnumCell<FieldMode>(row, "FieldMode", issues) is { } fm) cfg.FieldMode = fm;

        if (LengthCell(row, "CoverTop") is { } ct) cfg.Cover.Top = ct;
        if (LengthCell(row, "CoverBottom") is { } cb) cfg.Cover.Bottom = cb;
        if (LengthCell(row, "CoverSide") is { } cs) cfg.Cover.Side = cs;

        if (row.Get("BottomXBarType") is { } bx) cfg.Field.BottomX.BarType = bx;
        if (LengthCell(row, "BottomXSpacing") is { } bxs) cfg.Field.BottomX.Spacing = bxs;
        if (row.Get("BottomYBarType") is { } by) cfg.Field.BottomY.BarType = by;
        if (LengthCell(row, "BottomYSpacing") is { } bys) cfg.Field.BottomY.Spacing = bys;

        if (EnumCell<TopMode>(row, "TopMode", issues) is { } tm) cfg.Field.TopMode = tm;
        if (row.Get("TopXBarType") is { } tx) cfg.Field.TopX.BarType = tx;
        if (LengthCell(row, "TopXSpacing") is { } txs) cfg.Field.TopX.Spacing = txs;
        if (row.Get("TopYBarType") is { } ty) cfg.Field.TopY.BarType = ty;
        if (LengthCell(row, "TopYSpacing") is { } tys) cfg.Field.TopY.Spacing = tys;

        if (LengthCell(row, "MaxBarLength") is { } mbl) cfg.Lengths.MaxBarLength = mbl;
        if (EnumCell<LapMode>(row, "LapMode", issues) is { } lm) cfg.Lengths.LapMode = lm;
        if (LengthCell(row, "LapLength") is { } ll) cfg.Lengths.LapLength = ll;
        if (DoubleCell(row, "LapFactor", issues) is { } lf) cfg.Lengths.LapFactor = lf;
        if (BoolCell(row, "LapStagger", issues) is { } ls) cfg.Lengths.LapStagger = ls;

        if (EnumCell<EdgeAnchorMode>(row, "EdgeAnchorMode", issues) is { } eam) cfg.Anchors.EdgeAnchorMode = eam;
        if (LengthCell(row, "EdgeAnchorLen") is { } eal) cfg.Anchors.EdgeAnchorLen = eal;

        if (BoolCell(row, "EdgeUBarsEnabled", issues) is { } ue) cfg.Edges.UBarsEnabled = ue;
        if (row.Get("EdgeUBarType") is { } ubt) cfg.Edges.BarType = ubt;
        if (LengthCell(row, "EdgeUBarSpacing") is { } ubsp) cfg.Edges.Spacing = ubsp;
        if (LengthCell(row, "EdgeUBarLeg") is { } ubl) cfg.Edges.Leg = ubl;
        if (row.Get("EdgeUBarSelector") is { } ubsel) cfg.Edges.Selector = ubsel;

        if (BoolCell(row, "OpeningTrimEnabled", issues) is { } ote) cfg.Openings.TrimEnabled = ote;
        if (row.Get("OpeningTrimBarType") is { } otbt) cfg.Openings.BarType = otbt;
        if (IntCell(row, "OpeningExtraEachSide", issues) is { } oes) cfg.Openings.ExtraEachSide = oes;
        if (BoolCell(row, "OpeningUBars", issues) is { } oub) cfg.Openings.UBars = oub;
        if (BoolCell(row, "OpeningDiagonals", issues) is { } od) cfg.Openings.Diagonals = od;
        if (row.Get("OpeningDiagBarType") is { } odbt) cfg.Openings.DiagBarType = odbt;
        if (row.Get("OpeningSelector") is { } osel) cfg.Openings.Selector = osel;
    }

    private static double? TryExpectedThicknessIn(Row row, UnitSystem units)
    {
        string? s = row.Get("ExpectedThickness");
        if (s is null) return null;
        if (s.Contains('\'') || s.Contains('"'))
            return Length.ParseFeetInches(s);                                    // feet-inches → inches
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double n))
            return units == UnitSystem.Metric ? n / 25.4 : n;                    // mm → in, else inches
        return null;
    }

    // ── Zones ───────────────────────────────────────────────────────────────────

    public static ZoneParseResult ParseZones(string text, string? sourcePath = null)
    {
        var issues = new List<ParseIssue>();
        var zones = new List<ZoneSpec>();

        List<(int lineNo, string[] cells)> rows = ReadRows(text, out Dictionary<string, int>? header);
        if (header is null)
            return new ZoneParseResult { Issues = [new ParseIssue(0, "header", "No header row found.")] };

        foreach ((int lineNo, string[] cells) in rows)
        {
            var row = new Row(header, cells, lineNo);

            string? slabMark = row.Get("SlabMark");
            string? barType = row.Get("BarType");
            string? shape = row.Get("Shape");
            if (slabMark is null || barType is null || shape is null)
            {
                issues.Add(new ParseIssue(lineNo, "SlabMark/BarType/Shape", "Zone row needs SlabMark, BarType and Shape — skipped."));
                continue;
            }

            ZoneSpec? zone = ParseZone(row, slabMark, barType, shape, issues);
            if (zone is not null) zones.Add(zone);
        }

        return new ZoneParseResult { Zones = zones, Issues = issues };
    }

    private static ZoneSpec? ParseZone(Row row, string slabMark, string barType, string shape, List<ParseIssue> issues)
    {
        string[] kindAndRest = shape.Split(':', 2);
        if (!Enum.TryParse(kindAndRest[0], ignoreCase: true, out ZoneShapeKind kind))
        {
            issues.Add(new ParseIssue(row.LineNo, "Shape", $"Unknown shape kind '{kindAndRest[0]}'."));
            return null;
        }
        string rest = kindAndRest.Length > 1 ? kindAndRest[1] : "";

        string? supportMark = null;
        Length stripWidth = new(0);
        double[] coords = [];

        switch (kind)
        {
            case ZoneShapeKind.SupportMark:
            {
                string[] parts = rest.Split(':', 2);
                if (parts.Length < 2)
                {
                    issues.Add(new ParseIssue(row.LineNo, "Shape", "SupportMark needs '<mark>:<stripWidth>'."));
                    return null;
                }
                supportMark = parts[0].Trim();
                stripWidth = ParseLengthString(parts[1].Trim());
                break;
            }
            case ZoneShapeKind.BBox:
            case ZoneShapeKind.Polygon:
            {
                coords = ParseCoords(rest);
                if (kind == ZoneShapeKind.BBox && coords.Length != 4)
                {
                    issues.Add(new ParseIssue(row.LineNo, "Shape", "BBox needs 4 numbers 'x1 y1 x2 y2'."));
                    return null;
                }
                if (kind == ZoneShapeKind.Polygon && (coords.Length < 6 || coords.Length % 2 != 0))
                {
                    issues.Add(new ParseIssue(row.LineNo, "Shape", "Polygon needs an even count of ≥ 6 numbers."));
                    return null;
                }
                break;
            }
        }

        return new ZoneSpec
        {
            SlabMark = slabMark,
            ZoneName = row.Get("ZoneName") ?? "",
            Face = EnumCell<ZoneFace>(row, "Face", issues) ?? ZoneFace.Top,
            Axis = EnumCell<ZoneAxis>(row, "Direction", issues) ?? ZoneAxis.X,
            BarType = barType,
            Spacing = LengthCell(row, "Spacing") ?? new Length(12),
            ShapeKind = kind,
            SupportMark = supportMark,
            StripWidth = stripWidth,
            Coords = coords,
            Extent = LengthCell(row, "Extent") ?? new Length(0),
        };
    }

    private static double[] ParseCoords(string s) =>
        s.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : double.NaN)
            .Where(d => !double.IsNaN(d))
            .ToArray();

    // ── Cell parsing ────────────────────────────────────────────────────────────

    private static Length? LengthCell(Row row, string field)
    {
        string? s = row.Get(field);
        return s is null ? null : ParseLengthString(s);
    }

    private static Length ParseLengthString(string s) =>
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double n)
            ? new Length(n)
            : new Length(s);

    private static bool? BoolCell(Row row, string field, List<ParseIssue> issues)
    {
        string? s = row.Get(field);
        if (s is null) return null;
        switch (s.ToLowerInvariant())
        {
            case "true": case "yes": case "y": case "1": return true;
            case "false": case "no": case "n": case "0": return false;
            default:
                issues.Add(new ParseIssue(row.LineNo, field, $"'{s}' is not a boolean."));
                return null;
        }
    }

    private static int? IntCell(Row row, string field, List<ParseIssue> issues)
    {
        string? s = row.Get(field);
        if (s is null) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
        issues.Add(new ParseIssue(row.LineNo, field, $"'{s}' is not an integer."));
        return null;
    }

    private static double? DoubleCell(Row row, string field, List<ParseIssue> issues)
    {
        string? s = row.Get(field);
        if (s is null) return null;
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
        issues.Add(new ParseIssue(row.LineNo, field, $"'{s}' is not a number."));
        return null;
    }

    private static T? EnumCell<T>(Row row, string field, List<ParseIssue> issues) where T : struct, Enum
    {
        string? s = row.Get(field);
        if (s is null) return null;
        if (Enum.TryParse<T>(s, ignoreCase: true, out T v) && Enum.IsDefined(v)) return v;
        issues.Add(new ParseIssue(row.LineNo, field, $"'{s}' is not a valid {typeof(T).Name}."));
        return null;
    }

    // ── Low-level CSV reading ────────────────────────────────────────────────────

    private static List<(int lineNo, string[] cells)> ReadRows(string text, out Dictionary<string, int>? header)
    {
        header = null;
        var rows = new List<(int, string[])>();

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

            string[] cells = line.Split(',').Select(c => c.Trim()).ToArray();

            if (header is null)
            {
                header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < cells.Length; c++)
                    if (!string.IsNullOrWhiteSpace(cells[c]))
                        header[cells[c]] = c;
                continue;
            }

            rows.Add((i + 1, cells));
        }

        return rows;
    }

    private sealed class Row
    {
        private readonly Dictionary<string, int> _header;
        private readonly string[] _cells;
        public int LineNo { get; }

        public Row(Dictionary<string, int> header, string[] cells, int lineNo)
        {
            _header = header;
            _cells = cells;
            LineNo = lineNo;
        }

        /// <summary>Trimmed cell for a header, or null if the column is absent or the cell is empty.</summary>
        public string? Get(string header)
        {
            if (!_header.TryGetValue(header, out int i) || i >= _cells.Length) return null;
            string c = _cells[i].Trim();
            return c.Length == 0 ? null : c;
        }
    }
}
