using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SlabRebar.Engine;

public class RebarClassifier
{
    // |dz| of the longest centerline segment above this threshold ⇒ Dowel.
    // 0.7 ≈ angle from horizontal > 44°.
    private const double DowelVerticalDotZ = 0.7;

    private readonly Document _doc;
    private readonly Dictionary<long, (XYZ X, XYZ Y)> _hostBasisCache = new();

    public RebarClassifier(Document doc) => _doc = doc;

    // ── Build / classify ─────────────────────────────────────────────────────

    public List<RebarItem> BuildItems(IList<ElementId> ids)
    {
        var items = new List<RebarItem>();

        foreach (ElementId id in ids)
        {
            if (_doc.GetElement(id) is not Rebar rebar) continue;

            var item = new RebarItem { Id = id };
            ResolveHost(rebar, item);
            item.TypeName = ReadTypeName(rebar);
            item.Kind = ClassifyKind(rebar);

            if (item.Kind == RebarKind.Slab)
            {
                ClassifyDirection(rebar, item);
                ClassifyFace(rebar, item);
            }

            items.Add(item);
        }

        return items;
    }

    public static void RefreshProposedLabels(IEnumerable<RebarItem> items, ClassificationConfig config)
    {
        foreach (RebarItem item in items)
        {
            if (!item.IsIncluded) { item.ProposedLabel = string.Empty; continue; }

            item.ProposedLabel = item.Kind == RebarKind.Dowel
                ? config.LabelDowel
                : (item.Zone, item.Direction) switch
                {
                    ("Bottom", "X") => config.LabelBottomX,
                    ("Bottom", "Y") => config.LabelBottomY,
                    ("Top",    "X") => config.LabelTopX,
                    ("Top",    "Y") => config.LabelTopY,
                    _               => string.Empty,
                };
        }
    }

    /// <summary>Reads the current value of the target parameter for each item.</summary>
    public void RefreshCurrentValues(IEnumerable<RebarItem> items, ClassificationConfig config)
    {
        foreach (RebarItem item in items)
        {
            Element? element = _doc.GetElement(item.Id);
            if (element is null) { item.CurrentValue = string.Empty; continue; }

            string paramName = item.Kind == RebarKind.Dowel
                ? config.TargetParameterDowel
                : config.TargetParameterSlab;

            Parameter? p = element.LookupParameter(paramName);
            item.CurrentValue = p?.AsString() ?? string.Empty;
        }
    }

    // ── Write to model ───────────────────────────────────────────────────────

    public (int succeeded, int failed, List<string> errors) Apply(
        IEnumerable<RebarItem> items, ClassificationConfig config)
    {
        int succeeded = 0, failed = 0;
        var errors = new List<string>();

        foreach (RebarItem item in items.Where(i => i.IsIncluded))
        {
            try
            {
                Element? element = _doc.GetElement(item.Id);
                if (element is null) { failed++; continue; }

                string paramName = item.Kind == RebarKind.Dowel
                    ? config.TargetParameterDowel
                    : config.TargetParameterSlab;

                if (!TryWrite(element, paramName, item.ProposedLabel))
                {
                    errors.Add($"Element {item.Id.Value}: '{paramName}' not writable.");
                    failed++;
                }
                else
                {
                    succeeded++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Element {item.Id.Value}: {ex.Message}");
                failed++;
            }
        }

        return (succeeded, failed, errors);
    }

    // ── Parameter helpers ────────────────────────────────────────────────────

    public static IList<string> GetWritableStringParams(Document doc, IList<ElementId> ids)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return names.ToList();

        Element? elem = doc.GetElement(ids[0]);
        if (elem is null) return names.ToList();

        foreach (Parameter p in elem.Parameters)
        {
            if (p.IsReadOnly || p.StorageType != StorageType.String) continue;
            string name = p.Definition?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }

        return names.ToList();
    }

    // ── Kind detection ───────────────────────────────────────────────────────

    private static RebarKind ClassifyKind(Rebar rebar)
    {
        Line? longest = LongestStraightSegment(rebar);
        if (longest is null) return RebarKind.Slab;

        return Math.Abs(longest.Direction.Z) > DowelVerticalDotZ
            ? RebarKind.Dowel
            : RebarKind.Slab;
    }

    // ── Direction (X/Y) in host's local basis ────────────────────────────────

    private void ClassifyDirection(Rebar rebar, RebarItem item)
    {
        XYZ? horizDir = GetPrimaryHorizontalDirection(rebar);
        if (horizDir is null) { item.Direction = "X"; return; }

        (XYZ localX, XYZ localY) = GetHostLocalBasis(rebar.GetHostId());
        double dx = Math.Abs(horizDir.DotProduct(localX));
        double dy = Math.Abs(horizDir.DotProduct(localY));
        item.Direction = dx >= dy ? "X" : "Y";
    }

    private (XYZ X, XYZ Y) GetHostLocalBasis(ElementId hostId)
    {
        if (hostId == ElementId.InvalidElementId)
            return (XYZ.BasisX, XYZ.BasisY);

        if (_hostBasisCache.TryGetValue(hostId.Value, out var cached))
            return cached;

        var basis = ComputeHostLocalBasis(hostId);
        _hostBasisCache[hostId.Value] = basis;
        return basis;
    }

    private (XYZ X, XYZ Y) ComputeHostLocalBasis(ElementId hostId)
    {
        Element? host = _doc.GetElement(hostId);

        // Floor: use the longest straight horizontal segment of its sketch profile.
        if (host is Floor floor)
        {
            XYZ? longest = LongestHorizontalSketchEdge(floor);
            if (longest is not null)
            {
                XYZ y = XYZ.BasisZ.CrossProduct(longest).Normalize();
                return (longest, y);
            }
        }

        return (XYZ.BasisX, XYZ.BasisY);
    }

    private XYZ? LongestHorizontalSketchEdge(Floor floor)
    {
        ElementId sketchId = floor.SketchId;
        if (sketchId == ElementId.InvalidElementId) return null;

        if (_doc.GetElement(sketchId) is not Sketch sketch) return null;

        double maxLen = 0;
        XYZ?   longest = null;

        foreach (CurveArray ca in sketch.Profile)
        {
            foreach (Curve c in ca)
            {
                if (c is not Line line) continue;
                XYZ d = line.Direction;
                var horiz = new XYZ(d.X, d.Y, 0);
                if (horiz.IsZeroLength()) continue;
                if (line.Length > maxLen)
                {
                    maxLen  = line.Length;
                    longest = horiz.Normalize();
                }
            }
        }

        return longest;
    }

    private static XYZ? GetPrimaryHorizontalDirection(Rebar rebar)
    {
        var curves = rebar.GetCenterlineCurves(
            false, false, false,
            MultiplanarOption.IncludeAllMultiplanarCurves, 0);

        double maxLen = 0;
        XYZ?   longest = null;

        foreach (Curve c in curves)
        {
            if (c is not Line line) continue;
            XYZ d = line.Direction;
            var horiz = new XYZ(d.X, d.Y, 0);
            if (horiz.IsZeroLength()) continue;
            if (line.Length > maxLen)
            {
                maxLen  = line.Length;
                longest = horiz.Normalize();
            }
        }

        if (longest is not null) return longest;

        // Fallback: longer side of bounding box
        BoundingBoxXYZ? bb = rebar.get_BoundingBox(null);
        if (bb is null) return null;

        double sx = Math.Abs(bb.Max.X - bb.Min.X);
        double sy = Math.Abs(bb.Max.Y - bb.Min.Y);
        return sx >= sy ? XYZ.BasisX : XYZ.BasisY;
    }

    private static Line? LongestStraightSegment(Rebar rebar)
    {
        var curves = rebar.GetCenterlineCurves(
            false, false, false,
            MultiplanarOption.IncludeAllMultiplanarCurves, 0);

        Line?  longest = null;
        double maxLen  = 0;

        foreach (Curve c in curves)
        {
            if (c is Line line && line.Length > maxLen)
            {
                longest = line;
                maxLen  = line.Length;
            }
        }

        return longest;
    }

    // ── Host / face ──────────────────────────────────────────────────────────

    private string ReadTypeName(Rebar rebar)
    {
        Element? type = _doc.GetElement(rebar.GetTypeId());
        return type?.Name ?? string.Empty;
    }

    private void ResolveHost(Rebar rebar, RebarItem item)
    {
        ElementId hostId = rebar.GetHostId();
        Element?  host   = hostId != ElementId.InvalidElementId ? _doc.GetElement(hostId) : null;
        item.HostName = host?.Name ?? string.Empty;
    }

    private void ClassifyFace(Rebar rebar, RebarItem item)
    {
        BoundingBoxXYZ? rebarBB = rebar.get_BoundingBox(null);
        if (rebarBB is null) { item.Zone = "Bottom"; return; }

        double rebarCenterZ = (rebarBB.Min.Z + rebarBB.Max.Z) / 2.0;

        ElementId hostId = rebar.GetHostId();
        Element?  host   = hostId != ElementId.InvalidElementId ? _doc.GetElement(hostId) : null;

        BoundingBoxXYZ? hostBB = host?.get_BoundingBox(null);
        if (hostBB is null) { item.Zone = "Bottom"; return; }

        double hostCenterZ = (hostBB.Min.Z + hostBB.Max.Z) / 2.0;
        item.Zone = rebarCenterZ < hostCenterZ ? "Bottom" : "Top";
    }

    private static bool TryWrite(Element element, string paramName, string value)
    {
        Parameter? p = element.LookupParameter(paramName);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        p.Set(value);
        return true;
    }
}
