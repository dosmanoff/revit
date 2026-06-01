using ColumnReinforcement.Domain;
using System.Globalization;
using System.IO;
using System.Text;

namespace ColumnReinforcement.Config;

/// <summary>
/// CSV loader/saver for the per-column assignments table — see
/// <c>per-column-assignments-spec.md</c> for the full format. Pure C# / .NET
/// without Revit API references, so unit-testable in isolation.
///
/// <para>Empty cells = "use POCO default". Lines starting with <c>#</c> are
/// comments and skipped. Header row is required and gives field names. Field
/// names are matched case-insensitively.</para>
/// </summary>
public static class AssignmentCsv
{
    public static AssignmentTable Load(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        return Parse(text, path);
    }

    /// <summary>Parse a CSV string. Exposed for tests / round-trip without the file system.</summary>
    public static AssignmentTable Parse(string text, string? sourcePath = null)
    {
        var issues  = new List<ParseIssue>();
        var byMark  = new Dictionary<string, ColumnReinforcementConfig>(StringComparer.OrdinalIgnoreCase);
        var expByMk = new Dictionary<string, ExpectedGeometry?>(StringComparer.OrdinalIgnoreCase);

        string[] lines = SplitLines(text);
        if (lines.Length == 0)
            throw new InvalidDataException("CSV is empty.");

        // Find the first non-comment, non-blank line and treat it as the header.
        int headerLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
            headerLine = i;
            break;
        }
        if (headerLine < 0)
            throw new InvalidDataException("CSV contains no header row.");

        IReadOnlyList<string> headerFields = ParseCsvLine(lines[headerLine]);
        var fieldIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerFields.Count; i++)
            fieldIndex[headerFields[i].Trim()] = i;

        if (!fieldIndex.ContainsKey("Mark"))
            throw new InvalidDataException("CSV must contain a 'Mark' column.");

        for (int lineIdx = headerLine + 1; lineIdx < lines.Length; lineIdx++)
        {
            string raw = lines[lineIdx];
            string trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            IReadOnlyList<string> fields = ParseCsvLine(raw);
            int humanLineNo = lineIdx + 1;

            string mark = GetField(fields, fieldIndex, "Mark");
            if (string.IsNullOrWhiteSpace(mark))
            {
                issues.Add(new ParseIssue(humanLineNo, "Mark", "Row has empty Mark; skipped."));
                continue;
            }
            mark = mark.Trim();

            if (byMark.ContainsKey(mark))
                issues.Add(new ParseIssue(humanLineNo, "Mark",
                    $"Duplicate Mark '{mark}'; later row overrides the earlier."));

            try
            {
                ColumnReinforcementConfig cfg = BuildConfig(mark, fields, fieldIndex, issues, humanLineNo);
                ExpectedGeometry?         exp = BuildExpected(fields, fieldIndex, issues, humanLineNo);
                byMark[mark]  = cfg;
                expByMk[mark] = exp;
            }
            catch (Exception ex)
            {
                issues.Add(new ParseIssue(humanLineNo, "", $"Row failed to parse: {ex.Message}"));
            }
        }

        return new AssignmentTable(byMark, expByMk, issues, sourcePath);
    }

    /// <summary>Round-trip: dump an <see cref="AssignmentTable"/> back to CSV text.</summary>
    public static string Format(AssignmentTable t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", AllFieldNames));
        foreach (var kvp in t.ByMark.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            string mark = kvp.Key;
            ColumnReinforcementConfig cfg = kvp.Value;
            ExpectedGeometry? exp = t.TryGetExpected(mark);

            var row = new List<string>();
            foreach (string name in AllFieldNames)
                row.Add(EmitField(name, mark, cfg, exp));

            sb.AppendLine(string.Join(",", row.Select(QuoteIfNeeded)));
        }
        return sb.ToString();
    }

    // ── Field schema ─────────────────────────────────────────────────────────

    public static readonly string[] AllFieldNames =
    [
        "Mark",
        "ExpectedSection", "ExpectedW", "ExpectedD", "ExpectedDia",
        "Units",
        "CoverSides", "CoverEnds",
        "LongBarType", "LongBarsW", "LongBarsD", "LongBarsAround", "LongCornersOnly", "LongHookTop", "LongHookBot",
        "LongTopDefault", "LongTopModes", "LongTopBentLeg", "LongTopBentOutward", "LongTopBentDirs",
        "LongCrankUpperInset", "LongCrankSlope", "LongCrankLowerBendOffset", "LongCrankPenetration",
        "LongCrankTargets", "LongCrankEqualizeEndHeight", "LongCrankedShape", "LongTopBentShape",
        "StirrupBarType", "StirrupSpacing", "StirrupHookType", "StirrupOffsetTop", "StirrupOffsetBot", "StirrupRotate45",
        "ConfBotEnabled", "ConfBotSpacing", "ConfBotZoneFraction", "ConfBotZoneLength",
        "ConfTopEnabled", "ConfTopSpacing", "ConfTopZoneFraction", "ConfTopZoneLength",
        "StirrupShape",
        "CrosstiesEnabled", "CrosstiesAuto", "CrosstiesBarType", "CrosstiesHookType", "CrosstiesMaxClear", "CrosstiesManual", "CrosstieShape",
        "DowelsEnabled", "DowelForm", "DowelHost", "DowelBarType", "DowelExt", "DowelEmbed", "DowelLeg", "DowelOnlyFoundation", "DowelHookTop", "DowelHookBot", "DowelPositions", "DowelShape",
        "SplicesEnabled", "SpliceForm", "SpliceBarType", "SpliceLap", "SpliceExt", "SpliceBentLeg",
        "SpliceUpperInset", "SpliceCrankedSlope", "SpliceLowerBendOffset", "SpliceIgnoreSlabAbove", "SpliceHookTop", "SpliceHookBot",
        "CleanExisting",
    ];

    // ── Per-row builders ─────────────────────────────────────────────────────

    private static ColumnReinforcementConfig BuildConfig(
        string mark, IReadOnlyList<string> fields, Dictionary<string, int> idx,
        List<ParseIssue> issues, int lineNo)
    {
        var cfg = new ColumnReinforcementConfig { Name = mark };

        SetEnum<UnitSystem>(fields, idx, "Units", v => cfg.Units = v, issues, lineNo);

        SetLength(fields, idx, "CoverSides", v => cfg.Cover.Sides = v, issues, lineNo);
        SetLength(fields, idx, "CoverEnds",  v => cfg.Cover.Ends  = v, issues, lineNo);

        SetString(fields, idx, "LongBarType", v => cfg.Longitudinal.BarType = v);
        SetInt   (fields, idx, "LongBarsW",       v => cfg.Longitudinal.BarsAlongWidth = v, issues, lineNo);
        SetInt   (fields, idx, "LongBarsD",       v => cfg.Longitudinal.BarsAlongDepth = v, issues, lineNo);
        SetInt   (fields, idx, "LongBarsAround",  v => cfg.Longitudinal.BarsAround     = v, issues, lineNo);
        SetBool  (fields, idx, "LongCornersOnly", v => cfg.Longitudinal.CornerOnly     = v, issues, lineNo);
        SetOptStr(fields, idx, "LongHookTop", v => cfg.Longitudinal.HookTopType    = v);
        SetOptStr(fields, idx, "LongHookBot", v => cfg.Longitudinal.HookBottomType = v);
        SetEnum<BarTopMode>(fields, idx, "LongTopDefault", v => cfg.Longitudinal.TopDefault = v, issues, lineNo);
        SetOptStr(fields, idx, "LongTopModes", v => cfg.Longitudinal.TopModes = v);
        SetLength(fields, idx, "LongTopBentLeg", v => cfg.Longitudinal.TopBentLeg = v, issues, lineNo);
        SetBool  (fields, idx, "LongTopBentOutward", v => cfg.Longitudinal.TopBentOutward = v, issues, lineNo);
        SetOptStr(fields, idx, "LongTopBentDirs",    v => cfg.Longitudinal.TopBentDirs    = v);
        SetLength(fields, idx, "LongCrankUpperInset",      v => cfg.Longitudinal.CrankUpperInset      = v, issues, lineNo);
        SetDouble(fields, idx, "LongCrankSlope",           v => cfg.Longitudinal.CrankSlope           = v, issues, lineNo);
        SetLength(fields, idx, "LongCrankLowerBendOffset", v => cfg.Longitudinal.CrankLowerBendOffset = v, issues, lineNo);
        SetLength(fields, idx, "LongCrankPenetration",     v => cfg.Longitudinal.CrankPenetration     = v, issues, lineNo);
        SetOptStr(fields, idx, "LongCrankTargets",            v => cfg.Longitudinal.CrankTargets = v);
        SetBool  (fields, idx, "LongCrankEqualizeEndHeight",  v => cfg.Longitudinal.CrankEqualizeEndHeight = v, issues, lineNo);
        SetOptStr(fields, idx, "LongCrankedShape",            v => cfg.Longitudinal.CrankedShape = v);
        SetOptStr(fields, idx, "LongTopBentShape",            v => cfg.Longitudinal.TopBentShape = v);

        SetBool  (fields, idx, "StirrupEnabled",   v => cfg.Stirrups.Enabled  = v, issues, lineNo);
        SetString(fields, idx, "StirrupBarType",   v => cfg.Stirrups.BarType  = v);
        SetLength(fields, idx, "StirrupSpacing",   v => cfg.Stirrups.Spacing  = v, issues, lineNo);
        SetOptStr(fields, idx, "StirrupHookType",  v => cfg.Stirrups.HookType = v);
        SetOptStr(fields, idx, "StirrupShape",     v => cfg.Stirrups.Shape    = v);
        SetOptLen(fields, idx, "StirrupOffsetTop", v => cfg.Stirrups.OffsetTop    = v, issues, lineNo);
        SetOptLen(fields, idx, "StirrupOffsetBot", v => cfg.Stirrups.OffsetBottom = v, issues, lineNo);
        SetBool  (fields, idx, "StirrupRotate45",  v => cfg.Stirrups.Rotate45 = v, issues, lineNo);

        SetBool  (fields, idx, "ConfBotEnabled",      v => cfg.Stirrups.Confinement.Bottom.Enabled = v, issues, lineNo);
        SetLength(fields, idx, "ConfBotSpacing",      v => cfg.Stirrups.Confinement.Bottom.Spacing = v, issues, lineNo);
        SetOptDbl(fields, idx, "ConfBotZoneFraction", v => cfg.Stirrups.Confinement.Bottom.ZoneFraction = v, issues, lineNo);
        SetOptLen(fields, idx, "ConfBotZoneLength",   v => cfg.Stirrups.Confinement.Bottom.ZoneLength = v, issues, lineNo);

        SetBool  (fields, idx, "ConfTopEnabled",      v => cfg.Stirrups.Confinement.Top.Enabled = v, issues, lineNo);
        SetLength(fields, idx, "ConfTopSpacing",      v => cfg.Stirrups.Confinement.Top.Spacing = v, issues, lineNo);
        SetOptDbl(fields, idx, "ConfTopZoneFraction", v => cfg.Stirrups.Confinement.Top.ZoneFraction = v, issues, lineNo);
        SetOptLen(fields, idx, "ConfTopZoneLength",   v => cfg.Stirrups.Confinement.Top.ZoneLength = v, issues, lineNo);

        SetBool  (fields, idx, "CrosstiesEnabled",  v => cfg.Stirrups.Crossties.Enabled = v, issues, lineNo);
        SetBool  (fields, idx, "CrosstiesAuto",     v => cfg.Stirrups.Crossties.Auto    = v, issues, lineNo);
        SetOptStr(fields, idx, "CrosstiesBarType",  v => cfg.Stirrups.Crossties.BarType = v);
        SetOptStr(fields, idx, "CrosstiesHookType", v => cfg.Stirrups.Crossties.HookType = v);
        SetLength(fields, idx, "CrosstiesMaxClear", v => cfg.Stirrups.Crossties.MaxClearSpacing = v, issues, lineNo);
        SetOptStr(fields, idx, "CrosstiesManual",   v => cfg.Stirrups.Crossties.Manual = v);
        SetOptStr(fields, idx, "CrosstieShape",     v => cfg.Stirrups.Crossties.Shape  = v);

        SetBool         (fields, idx, "DowelsEnabled",       v => cfg.Dowels.Enabled   = v, issues, lineNo);
        SetEnum<DowelForm>(fields, idx, "DowelForm",         v => cfg.Dowels.Form      = v, issues, lineNo);
        SetEnum<DowelHost>(fields, idx, "DowelHost",         v => cfg.Dowels.Host      = v, issues, lineNo);
        SetString       (fields, idx, "DowelBarType",        v => cfg.Dowels.BarType   = v);
        SetLength       (fields, idx, "DowelExt",            v => cfg.Dowels.Extension = v, issues, lineNo);
        SetLength       (fields, idx, "DowelEmbed",          v => cfg.Dowels.Embedment = v, issues, lineNo);
        SetLength       (fields, idx, "DowelLeg",            v => cfg.Dowels.LegLength = v, issues, lineNo);
        SetBool         (fields, idx, "DowelOnlyFoundation", v => cfg.Dowels.OnlyStructuralFoundation = v, issues, lineNo);
        SetOptStr       (fields, idx, "DowelHookTop", v => cfg.Dowels.HookTopType    = v);
        SetOptStr       (fields, idx, "DowelHookBot", v => cfg.Dowels.HookBottomType = v);
        SetOptStr       (fields, idx, "DowelPositions", v => cfg.Dowels.Positions    = v);
        SetOptStr       (fields, idx, "DowelShape",     v => cfg.Dowels.Shape        = v);

        SetBool                  (fields, idx, "SplicesEnabled",        v => cfg.UpperSplices.Enabled  = v, issues, lineNo);
        SetEnum<UpperSpliceForm> (fields, idx, "SpliceForm",            v => cfg.UpperSplices.Form     = v, issues, lineNo);
        SetString                (fields, idx, "SpliceBarType",         v => cfg.UpperSplices.BarType  = v);
        SetLength                (fields, idx, "SpliceLap",             v => cfg.UpperSplices.LapInsideColumn = v, issues, lineNo);
        SetLength                (fields, idx, "SpliceExt",             v => cfg.UpperSplices.Extension       = v, issues, lineNo);
        SetLength                (fields, idx, "SpliceBentLeg",         v => cfg.UpperSplices.BentLegLength   = v, issues, lineNo);
        SetLength                (fields, idx, "SpliceUpperInset",      v => cfg.UpperSplices.UpperCageInset  = v, issues, lineNo);
        SetDouble                (fields, idx, "SpliceCrankedSlope",    v => cfg.UpperSplices.CrankedSlopeRatio = v, issues, lineNo);
        SetLength                (fields, idx, "SpliceLowerBendOffset", v => cfg.UpperSplices.LowerBendOffsetFromTop = v, issues, lineNo);
        SetBool                  (fields, idx, "SpliceIgnoreSlabAbove", v => cfg.UpperSplices.IgnoreSlabAbove = v, issues, lineNo);
        SetOptStr                (fields, idx, "SpliceHookTop",         v => cfg.UpperSplices.HookTopType    = v);
        SetOptStr                (fields, idx, "SpliceHookBot",         v => cfg.UpperSplices.HookBottomType = v);

        SetBool(fields, idx, "CleanExisting", v => cfg.CleanExisting = v, issues, lineNo);

        return cfg;
    }

    private static ExpectedGeometry? BuildExpected(
        IReadOnlyList<string> fields, Dictionary<string, int> idx, List<ParseIssue> issues, int lineNo)
    {
        string? sectionStr = TryGet(fields, idx, "ExpectedSection");
        double? w   = TryGetDouble(fields, idx, "ExpectedW",   issues, lineNo);
        double? d   = TryGetDouble(fields, idx, "ExpectedD",   issues, lineNo);
        double? dia = TryGetDouble(fields, idx, "ExpectedDia", issues, lineNo);

        if (string.IsNullOrWhiteSpace(sectionStr) && w is null && d is null && dia is null)
            return null;

        ColumnSection section = ColumnSection.Rectangular;
        if (!string.IsNullOrWhiteSpace(sectionStr))
        {
            if (!Enum.TryParse<ColumnSection>(sectionStr, ignoreCase: true, out section))
            {
                issues.Add(new ParseIssue(lineNo, "ExpectedSection",
                    $"Unknown section '{sectionStr}'; expected Rectangular or Round."));
                return null;
            }
        }
        else
        {
            section = dia is not null ? ColumnSection.Round : ColumnSection.Rectangular;
        }

        return new ExpectedGeometry(section, w, d, dia);
    }

    // ── CSV line parsing ─────────────────────────────────────────────────────

    private static string[] SplitLines(string text)
    {
        // Strip leading BOM if present so the first header field is clean.
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        return text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
    }

    /// <summary>RFC 4180-ish line parser: supports quoted fields with embedded commas and doubled quotes.</summary>
    internal static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb     = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static string QuoteIfNeeded(string s)
    {
        if (s is null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    // ── Field accessors ──────────────────────────────────────────────────────

    private static string GetField(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name)
        => idx.TryGetValue(name, out int i) && i < fields.Count ? fields[i] : "";

    private static string? TryGet(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name)
    {
        string v = GetField(fields, idx, name).Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static double? TryGetDouble(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name,
        List<ParseIssue> issues, int lineNo)
    {
        string? raw = TryGet(fields, idx, name);
        if (raw is null) return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d;
        issues.Add(new ParseIssue(lineNo, name, $"'{raw}' is not a valid number; field ignored."));
        return null;
    }

    private static void SetString(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<string> setter)
    {
        string? v = TryGet(fields, idx, name);
        if (v is not null) setter(v);
    }

    private static void SetOptStr(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<string?> setter)
    {
        if (!idx.ContainsKey(name)) return;
        string raw = GetField(fields, idx, name).Trim();
        if (raw.Length == 0) return;                           // empty = keep default (no setter call)
        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) setter(null);
        else setter(raw);
    }

    private static void SetInt(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<int> setter,
        List<ParseIssue> issues, int lineNo)
    {
        string? raw = TryGet(fields, idx, name);
        if (raw is null) return;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            setter(n);
        else
            issues.Add(new ParseIssue(lineNo, name, $"'{raw}' is not a valid integer; field ignored."));
    }

    private static void SetDouble(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<double> setter,
        List<ParseIssue> issues, int lineNo)
    {
        double? v = TryGetDouble(fields, idx, name, issues, lineNo);
        if (v is not null) setter(v.Value);
    }

    private static void SetOptDbl(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<double?> setter,
        List<ParseIssue> issues, int lineNo)
    {
        if (!idx.ContainsKey(name)) return;
        string raw = GetField(fields, idx, name).Trim();
        if (raw.Length == 0) return;
        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) { setter(null); return; }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            setter(d);
        else
            issues.Add(new ParseIssue(lineNo, name, $"'{raw}' is not a valid number; field ignored."));
    }

    private static void SetBool(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<bool> setter,
        List<ParseIssue> issues, int lineNo)
    {
        string? raw = TryGet(fields, idx, name);
        if (raw is null) return;
        if (bool.TryParse(raw, out bool b)) { setter(b); return; }
        if (raw is "1" or "yes" or "Y" or "Yes" or "true" or "TRUE" or "True") { setter(true);  return; }
        if (raw is "0" or "no"  or "N" or "No"  or "false" or "FALSE" or "False") { setter(false); return; }
        issues.Add(new ParseIssue(lineNo, name, $"'{raw}' is not a valid boolean; field ignored."));
    }

    private static void SetLength(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<Length> setter,
        List<ParseIssue> issues, int lineNo)
    {
        string? raw = TryGet(fields, idx, name);
        if (raw is null) return;
        Length? parsed = TryParseLength(raw, name, issues, lineNo);
        if (parsed is not null) setter(parsed.Value);
    }

    private static void SetOptLen(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<Length?> setter,
        List<ParseIssue> issues, int lineNo)
    {
        if (!idx.ContainsKey(name)) return;
        string raw = GetField(fields, idx, name).Trim();
        if (raw.Length == 0) return;
        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase)) { setter(null); return; }
        Length? parsed = TryParseLength(raw, name, issues, lineNo);
        if (parsed is not null) setter(parsed);
    }

    private static Length? TryParseLength(string raw, string field, List<ParseIssue> issues, int lineNo)
    {
        // Feet-inches string syntax (contains a quote or apostrophe) goes via the Length text form;
        // otherwise interpret as a number using the row's Units setting.
        if (raw.Contains('\'') || raw.Contains('"') || raw.Contains('′') || raw.Contains('″'))
        {
            try { _ = Length.ParseFeetInches(raw); return new Length(raw); }
            catch (Exception ex)
            {
                issues.Add(new ParseIssue(lineNo, field, $"Cannot parse '{raw}' as feet-inches: {ex.Message}"));
                return null;
            }
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return new Length(d);
        issues.Add(new ParseIssue(lineNo, field, $"'{raw}' is not a valid number; field ignored."));
        return null;
    }

    private static void SetEnum<TEnum>(IReadOnlyList<string> fields, Dictionary<string, int> idx, string name, Action<TEnum> setter,
        List<ParseIssue> issues, int lineNo) where TEnum : struct, Enum
    {
        string? raw = TryGet(fields, idx, name);
        if (raw is null) return;
        if (Enum.TryParse<TEnum>(raw, ignoreCase: true, out TEnum value))
            setter(value);
        else
            issues.Add(new ParseIssue(lineNo, name,
                $"'{raw}' is not a valid {typeof(TEnum).Name}; expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}."));
    }

    // ── Format helpers (round-trip) ──────────────────────────────────────────

    private static string EmitField(string name, string mark, ColumnReinforcementConfig c, ExpectedGeometry? e)
    {
        // Most fields read straight off the POCO. A few need composition (Mark, Expected*).
        return name switch
        {
            "Mark"            => mark,
            "ExpectedSection" => e?.Section.ToString() ?? "",
            "ExpectedW"       => e?.WidthIn?.ToString("G", CultureInfo.InvariantCulture)   ?? "",
            "ExpectedD"       => e?.DepthIn?.ToString("G", CultureInfo.InvariantCulture)   ?? "",
            "ExpectedDia"     => e?.DiameterIn?.ToString("G", CultureInfo.InvariantCulture) ?? "",

            "Units"      => c.Units.ToString(),
            "CoverSides" => c.Cover.Sides.ToString(),
            "CoverEnds"  => c.Cover.Ends.ToString(),

            "LongBarType"        => c.Longitudinal.BarType,
            "LongBarsW"          => c.Longitudinal.BarsAlongWidth.ToString(CultureInfo.InvariantCulture),
            "LongBarsD"          => c.Longitudinal.BarsAlongDepth.ToString(CultureInfo.InvariantCulture),
            "LongBarsAround"     => c.Longitudinal.BarsAround.ToString(CultureInfo.InvariantCulture),
            "LongCornersOnly"    => c.Longitudinal.CornerOnly.ToString().ToLowerInvariant(),
            "LongHookTop"        => c.Longitudinal.HookTopType    ?? "",
            "LongHookBot"        => c.Longitudinal.HookBottomType ?? "",
            "LongTopDefault"     => c.Longitudinal.TopDefault.ToString(),
            "LongTopModes"       => c.Longitudinal.TopModes ?? "",
            "LongTopBentLeg"     => c.Longitudinal.TopBentLeg.ToString(),
            "LongTopBentOutward" => c.Longitudinal.TopBentOutward.ToString().ToLowerInvariant(),
            "LongTopBentDirs"    => c.Longitudinal.TopBentDirs ?? "",
            "LongCrankUpperInset"      => c.Longitudinal.CrankUpperInset.ToString(),
            "LongCrankSlope"           => c.Longitudinal.CrankSlope.ToString("G", CultureInfo.InvariantCulture),
            "LongCrankLowerBendOffset" => c.Longitudinal.CrankLowerBendOffset.ToString(),
            "LongCrankPenetration"     => c.Longitudinal.CrankPenetration.ToString(),
            "LongCrankTargets"            => c.Longitudinal.CrankTargets ?? "",
            "LongCrankEqualizeEndHeight"  => c.Longitudinal.CrankEqualizeEndHeight.ToString().ToLowerInvariant(),
            "LongCrankedShape"            => c.Longitudinal.CrankedShape ?? "",
            "LongTopBentShape"            => c.Longitudinal.TopBentShape ?? "",

            "StirrupBarType"   => c.Stirrups.BarType,
            "StirrupSpacing"   => c.Stirrups.Spacing.ToString(),
            "StirrupHookType"  => c.Stirrups.HookType ?? "",
            "StirrupShape"     => c.Stirrups.Shape    ?? "",
            "StirrupOffsetTop" => c.Stirrups.OffsetTop?.ToString() ?? "",
            "StirrupOffsetBot" => c.Stirrups.OffsetBottom?.ToString() ?? "",
            "StirrupRotate45"  => c.Stirrups.Rotate45.ToString().ToLowerInvariant(),

            "ConfBotEnabled"      => c.Stirrups.Confinement.Bottom.Enabled.ToString().ToLowerInvariant(),
            "ConfBotSpacing"      => c.Stirrups.Confinement.Bottom.Spacing.ToString(),
            "ConfBotZoneFraction" => c.Stirrups.Confinement.Bottom.ZoneFraction?.ToString("G", CultureInfo.InvariantCulture) ?? "",
            "ConfBotZoneLength"   => c.Stirrups.Confinement.Bottom.ZoneLength?.ToString() ?? "",

            "ConfTopEnabled"      => c.Stirrups.Confinement.Top.Enabled.ToString().ToLowerInvariant(),
            "ConfTopSpacing"      => c.Stirrups.Confinement.Top.Spacing.ToString(),
            "ConfTopZoneFraction" => c.Stirrups.Confinement.Top.ZoneFraction?.ToString("G", CultureInfo.InvariantCulture) ?? "",
            "ConfTopZoneLength"   => c.Stirrups.Confinement.Top.ZoneLength?.ToString() ?? "",

            "CrosstiesEnabled"  => c.Stirrups.Crossties.Enabled.ToString().ToLowerInvariant(),
            "CrosstiesAuto"     => c.Stirrups.Crossties.Auto.ToString().ToLowerInvariant(),
            "CrosstiesBarType"  => c.Stirrups.Crossties.BarType  ?? "",
            "CrosstiesHookType" => c.Stirrups.Crossties.HookType ?? "",
            "CrosstiesMaxClear" => c.Stirrups.Crossties.MaxClearSpacing.ToString(),
            "CrosstiesManual"   => c.Stirrups.Crossties.Manual ?? "",
            "CrosstieShape"     => c.Stirrups.Crossties.Shape  ?? "",

            "DowelsEnabled"       => c.Dowels.Enabled.ToString().ToLowerInvariant(),
            "DowelForm"           => c.Dowels.Form.ToString(),
            "DowelHost"           => c.Dowels.Host.ToString(),
            "DowelBarType"        => c.Dowels.BarType,
            "DowelExt"            => c.Dowels.Extension.ToString(),
            "DowelEmbed"          => c.Dowels.Embedment.ToString(),
            "DowelLeg"            => c.Dowels.LegLength.ToString(),
            "DowelOnlyFoundation" => c.Dowels.OnlyStructuralFoundation.ToString().ToLowerInvariant(),
            "DowelHookTop"        => c.Dowels.HookTopType    ?? "",
            "DowelHookBot"        => c.Dowels.HookBottomType ?? "",
            "DowelPositions"      => c.Dowels.Positions      ?? "",
            "DowelShape"          => c.Dowels.Shape          ?? "",

            "SplicesEnabled"        => c.UpperSplices.Enabled.ToString().ToLowerInvariant(),
            "SpliceForm"            => c.UpperSplices.Form.ToString(),
            "SpliceBarType"         => c.UpperSplices.BarType,
            "SpliceLap"             => c.UpperSplices.LapInsideColumn.ToString(),
            "SpliceExt"             => c.UpperSplices.Extension.ToString(),
            "SpliceBentLeg"         => c.UpperSplices.BentLegLength.ToString(),
            "SpliceUpperInset"      => c.UpperSplices.UpperCageInset.ToString(),
            "SpliceCrankedSlope"    => c.UpperSplices.CrankedSlopeRatio.ToString("G", CultureInfo.InvariantCulture),
            "SpliceLowerBendOffset" => c.UpperSplices.LowerBendOffsetFromTop.ToString(),
            "SpliceIgnoreSlabAbove" => c.UpperSplices.IgnoreSlabAbove.ToString().ToLowerInvariant(),
            "SpliceHookTop"         => c.UpperSplices.HookTopType    ?? "",
            "SpliceHookBot"         => c.UpperSplices.HookBottomType ?? "",

            "CleanExisting" => c.CleanExisting.ToString().ToLowerInvariant(),

            _ => "",
        };
    }
}
