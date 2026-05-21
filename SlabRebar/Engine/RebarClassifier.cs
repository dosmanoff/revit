using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SlabRebar.Engine;

public class RebarClassifier
{
    private readonly Document _doc;

    public RebarClassifier(Document doc) => _doc = doc;

    // ── Build / classify ─────────────────────────────────────────────────────

    public List<RebarItem> BuildItems(IList<ElementId> ids)
    {
        var items = new List<RebarItem>();

        foreach (ElementId id in ids)
        {
            if (_doc.GetElement(id) is not Rebar rebar) continue;

            var item = new RebarItem { Id = id };
            ClassifyDirection(rebar, item);
            ClassifyFace(rebar, item);

            Parameter? cp = rebar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            item.CurrentValue = cp?.AsString() ?? string.Empty;

            items.Add(item);
        }

        return items;
    }

    public static void RefreshProposedLabels(IEnumerable<RebarItem> items, ClassificationConfig config)
    {
        foreach (RebarItem item in items)
        {
            if (!item.IsIncluded) { item.ProposedLabel = string.Empty; continue; }

            item.ProposedLabel = (item.Zone, item.Direction) switch
            {
                ("Bottom", "X") => config.LabelBottomX,
                ("Bottom", "Y") => config.LabelBottomY,
                ("Top",    "X") => config.LabelTopX,
                ("Top",    "Y") => config.LabelTopY,
                _               => string.Empty,
            };
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

                if (!TryWrite(element, config, item.ProposedLabel))
                {
                    errors.Add($"Element {item.Id.Value}: '{config.TargetParameter}' not writable.");
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
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "Comments" };
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

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void ClassifyDirection(Rebar rebar, RebarItem item)
    {
        var curves = rebar.GetCenterlineCurves(
            false, false, false,
            MultiplanarOption.IncludeAllMultiplanarCurves, 0.001);

        if (curves.FirstOrDefault() is Line line)
        {
            XYZ dir = line.Direction;
            item.Direction = Math.Abs(dir.X) >= Math.Abs(dir.Y) ? "X" : "Y";
            return;
        }

        // Fallback: use the longer bounding-box axis
        BoundingBoxXYZ? bb = rebar.get_BoundingBox(null);
        if (bb is not null)
        {
            double sizeX = Math.Abs(bb.Max.X - bb.Min.X);
            double sizeY = Math.Abs(bb.Max.Y - bb.Min.Y);
            item.Direction = sizeX >= sizeY ? "X" : "Y";
        }
        else
        {
            item.Direction = "X";
        }
    }

    private void ClassifyFace(Rebar rebar, RebarItem item)
    {
        BoundingBoxXYZ? rebarBB = rebar.get_BoundingBox(null);
        if (rebarBB is null) { item.Zone = "Bottom"; return; }

        double rebarCenterZ = (rebarBB.Min.Z + rebarBB.Max.Z) / 2.0;

        ElementId hostId = rebar.GetHostId();
        Element?  host   = hostId != ElementId.InvalidElementId ? _doc.GetElement(hostId) : null;
        item.HostName = host?.Name ?? string.Empty;

        BoundingBoxXYZ? hostBB = host?.get_BoundingBox(null);
        if (hostBB is null) { item.Zone = "Bottom"; return; }

        double hostCenterZ = (hostBB.Min.Z + hostBB.Max.Z) / 2.0;
        item.Zone = rebarCenterZ < hostCenterZ ? "Bottom" : "Top";
    }

    private static bool TryWrite(Element element, ClassificationConfig config, string value)
    {
        Parameter? p = config.TargetParameter.Equals("Comments", StringComparison.OrdinalIgnoreCase)
            ? element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
            : element.LookupParameter(config.TargetParameter);

        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        p.Set(value);
        return true;
    }
}
