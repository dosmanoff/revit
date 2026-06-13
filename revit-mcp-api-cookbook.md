# Revit MCP — API Cookbook

*Verified, copy-paste-ready C# for `send_code_to_revit`. These are patterns proven in real sessions — paired with the pitfalls that cost hours. Last verified 2026-06-13.*

Companion docs: [`revit-mcp-runbook.md`](revit-mcp-runbook.md) · [`revit-mcp-agent-guide.md`](revit-mcp-agent-guide.md)
External reference (don't reproduce — link): [revitapidocs 2025](https://www.revitapidocs.com/2025/) · [Autodesk RVT 2025 help](https://help.autodesk.com/view/RVT/2025/ENU/)

---

## Ground rules for every snippet

- The body runs inside `Execute(Document, object[])`; the document is the variable **`document`**.
- **No `using` aliases or namespace-scoped statements** in the body — use fully-qualified type names.
- `transactionMode`: **`auto`** for any change (incl. setting view params); **`none`** for pure reads and `ExportImage`.
- Return a `string` (build with `StringBuilder`). Lengths/diameters are in **decimal feet** (#8 = 0.0833 ft = 1").
- `new ElementId(123456L)` to build an id.

---

## 1. Find elements

```csharp
// By category
var walls = new FilteredElementCollector(document)
    .OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToElements();

// By Mark
Func<Element,string> mark = e => {
    var p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
    return p != null ? (p.AsString() ?? "") : "";
};

// Rebar hosted by a specific element (note fully-qualified type)
var rebars = new FilteredElementCollector(document)
    .OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar)).Cast<Autodesk.Revit.DB.Structure.Rebar>()
    .Where(rb => { try { return rb.GetHostId().IntegerValue == 2841; } catch { return false; } });
```

## 2. Geometry probing (solids, faces, profiles)

Extract the elevation outline of a wall (used for the stepped-wall profile):

```csharp
var wall = document.GetElement(new ElementId(3158996)) as Wall;
var go = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
Solid sol = null;
foreach (var g in wall.get_Geometry(go)) { var s = g as Solid; if (s != null && s.Volume > 0.1) sol = s; }
// biggest face whose normal is ~ +Y, then dump its loop as (X,Z)
PlanarFace big = null; double best = 0;
foreach (Face f in sol.Faces) { var pf = f as PlanarFace;
    if (pf != null && Math.Abs(pf.FaceNormal.Y) > 0.9 && pf.Area > best) { best = pf.Area; big = pf; } }
foreach (EdgeArray loop in big.EdgeLoops) {
    foreach (Edge e in loop) { var p = e.AsCurve().GetEndPoint(0); /* p.X, p.Z */ } break; }
```

Bounding box / centroid: `var bb = el.get_BoundingBox(null); var c = (bb.Min + bb.Max).Multiply(0.5);`

## 3. Rebar creation

```csharp
// From a standard shape (host-element overload)
var shape   = document.GetElement(new ElementId(6610)) as Autodesk.Revit.DB.Structure.RebarShape;
var barType = document.GetElement(new ElementId(6500)) as Autodesk.Revit.DB.Structure.RebarBarType;
var rebar = Autodesk.Revit.DB.Structure.Rebar.CreateFromRebarShape(
    document, shape, barType, host, origin, xVec, yVec);
```

```csharp
// From explicit curves (bent bars / shapes 17/17A) — recognised automatically
var curves = new List<Curve> { Line.CreateBound(p0, p1), Line.CreateBound(p1, p2) };
var bar = Autodesk.Revit.DB.Structure.Rebar.CreateFromCurves(
    document, Autodesk.Revit.DB.Structure.RebarStyle.Standard, barType,
    null, null, host, normal, curves,
    Autodesk.Revit.DB.Structure.RebarHookOrientation.Right,
    Autodesk.Revit.DB.Structure.RebarHookOrientation.Right, true, true);
```

Strict bar-type lookup (project convention — never silently pick a default):
```csharp
var bt = new FilteredElementCollector(document)
    .OfClass(typeof(Autodesk.Revit.DB.Structure.RebarBarType)).Cast<Autodesk.Revit.DB.Structure.RebarBarType>()
    .FirstOrDefault(t => t.Name == "#7");   // throw/report if null
```

Set a segment dimension directly (avoids `ScaleToBox` non-determinism / schedule "varies"):
```csharp
rebar.LookupParameter("B").Set(9.252/12.0);   // feet
```

## 4. Per-view rebar display

```csharp
var view = document.GetElement(new ElementId(476382)) as View;
// presentation: All / Middle / FirstLast  (fully-qualified enum)
rebar.SetPresentationMode(view, Autodesk.Revit.DB.Structure.RebarPresentationMode.FirstLast);
// "View unobscured" — show bar through concrete. NOT copied when you copy rebar!
rebar.SetUnobscuredInView(view, true);
// hide individual positions
for (int i = 0; i < rebar.NumberOfBarPositions; i++) rebar.SetBarHiddenStatus(view, i, true);
```

## 5. Copy + rehost rebar across floors

```csharp
var ids = new List<ElementId> { new ElementId(34001), new ElementId(34002) };
var copied = ElementTransformUtils.CopyElements(document, ids, new XYZ(0, 0, 11.0)); // +dz
foreach (var nid in copied) {
    var rb = document.GetElement(nid) as Autodesk.Revit.DB.Structure.Rebar;
    rb.SetHostId(document, new ElementId(2841));        // rehost to target floor
    rb.SetUnobscuredInView(view, true);                // re-apply: not carried over
}
```

## 6. MultiReferenceAnnotation (MRA) — create + adjust

```csharp
var mt  = document.GetElement(new ElementId(470531)) as MultiReferenceAnnotationType;
var opt = new MultiReferenceAnnotationOptions(mt);
opt.DimensionStyleType   = DimensionStyleType.Linear;
opt.SetElementsToDimension(new List<ElementId> { targetBar.Id });
opt.DimensionLineOrigin    = new XYZ(ox, oy, oz);
opt.DimensionLineDirection = new XYZ(0, 1, 0);
opt.DimensionPlaneNormal   = new XYZ(0, 0, 1);
opt.TagHasLeader   = true;
opt.TagHeadPosition = new XYZ(hx, hy, hz);
var mra = MultiReferenceAnnotation.Create(document, viewId, opt);

// read its children later
var dim = document.GetElement(mra.DimensionId) as Dimension;   // the dimension line
var tag = document.GetElement(mra.TagId) as IndependentTag;    // the tag
```

Nudge the dimension line perpendicular to match a reference origin:
```csharp
var d = dim.Curve as Line; var n = new XYZ(-d.Direction.Y, d.Direction.X, 0).Normalize();
double off = (refOrigin - d.Origin).DotProduct(n);
if (Math.Abs(off) > 0.01) ElementTransformUtils.MoveElement(document, dim.Id, n.Multiply(off));
```

> A **Linear** MRA needs references perpendicular to the dim line; a **single-bar set has nothing to dimension internally** and will throw "references can't be used…". Match multi-position sets, or dimension against a grid.

## 7. Tags & dimensions

- Tag a rebar subelement via reference string `uid:<elemId>:<subelementIndex>:SUBELEMENT` (e.g. `...:2000000:SUBELEMENT`).
- Dimensions: build `ReferenceArray` from `PlanarFace`/silhouette references obtained with `Options { ComputeReferences = true }`, then `document.Create.NewDimension(view, line, refArray)`.
- Leader auto-placement is **unsolved** — arrows can miss the bar; place leaders by hand.

## 8. Views

```csharp
// SECTION along an axis (right=+Y, up=+Z, look toward -X)
var sVft = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
    .Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.Section);
var tf = Transform.Identity;
tf.Origin = new XYZ(111.38, 29.88, -5.5);
tf.BasisX = new XYZ(0,1,0); tf.BasisY = new XYZ(0,0,1); tf.BasisZ = new XYZ(1,0,0);
var box = new BoundingBoxXYZ { Transform = tf, Min = new XYZ(-7,-7,-3), Max = new XYZ(7,7,3) };
var sec = ViewSection.CreateSection(document, sVft.Id, box);

// 3D isometric, section-boxed + isolated
var v3t = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType))
    .Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
var v3 = View3D.CreateIsometric(document, v3t.Id);
v3.SetSectionBox(new BoundingBoxXYZ { Min = new XYZ(105,18,-12), Max = new XYZ(120,40,2) });
v3.IsolateElementsTemporary(new List<ElementId> { new ElementId(3158996) });
v3.SetOrientation(new ViewOrientation3D(eye, up, forward));   // forward = look dir

// hide a category in a view (e.g. rebar, to read concrete)
var cat = Category.GetCategory(document, BuiltInCategory.OST_Rebar);
if (sec.CanCategoryBeHidden(cat.Id)) sec.SetCategoryHidden(cat.Id, true);
```

> Setting `VIEW_SCALE` / `DetailLevel` needs a **transaction** (`auto`). `ExportImage` does not.

## 9. Export an image for review

```csharp
var opt = new ImageExportOptions {
    ZoomType = ZoomFitType.FitToPage,
    PixelSize = 1900,                       // KEEP <= 2000 so it's readable back
    FilePath = @"C:\dev\wc54_elev",         // view name gets appended to the file
    HLRandWFViewsFileType = ImageFileType.PNG,
    ImageResolution = ImageResolution.DPI_150,
    ExportRange = ExportRange.SetOfViews
};
opt.SetViewsAndSheets(new List<ElementId> { view.Id });
document.ExportImage(opt);   // transactionMode: none
```
Then read the PNG back (`C:\dev\<name> - <ViewType> - <ViewName>.png`) to verify the result.

---

## Pitfalls (each one bit us at least once)

| Pitfall | Fix |
| --- | --- |
| `'Rebar' is inaccessible due to its protection level` | Use fully-qualified `Autodesk.Revit.DB.Structure.Rebar` (same for `RebarShape`, `RebarBarType`, `RebarPresentationMode`, `RebarHookOrientation`). |
| `using X = ...;` alias fails to compile | The body is a method body — no alias/namespace directives. Fully-qualify instead. |
| Copied rebar invisible on other floors | `SetUnobscuredInView` is **not** copied — re-apply per view after `CopyElements`. |
| Hiding an MRA wrapper leaves dims/tags visible | Hiding the MRA doesn't hide its child `Dimension`/`IndependentTag` — hide/move them directly via `mra.DimensionId` / `mra.TagId`. |
| Schedule shows "varies" | Group is inhomogeneous — set segment params directly (`LookupParameter("B").Set(...)`) instead of `ScaleToBox` (non-deterministic). |
| `MoveElement` doesn't move a rebar | It's constrained (RebarConstraints) — adjust via constraints / `SetDistanceToTargetHostFace`, or recreate from curves. |
| MRA "references can't be used with Linear dimension…" | Single-bar set / non-perpendicular reference. Use a multi-position set or dimension to a grid. |
| End hooks fling bars away ("placed outside host") | Recreate the bar from explicit polyline curves with the bend segment (`CreateFromCurves`) so Revit recognises the shape. |
| Image won't load back | `PixelSize` > 2000 — re-export at ≤ 1900. |
| `say_hello` "fails" | Modal must be clicked within 15 s — use `get_current_view_info` for health checks. |
| Parameter `.Set()` throws | You're in `transactionMode: none` — switch to `auto`. |
