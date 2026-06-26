using Autodesk.Revit.DB;
using WallReinforcement.Domain;
using WallReinforcement.Geometry;

namespace WallReinforcement.Engine;

/// <summary>
/// Reads a wall's exterior-face elevation outline into a Revit-free <see cref="ElevationProfile"/>
/// in (u,v) so the field-bar builder can clip bars to the real wall shape (e.g. a slanted end or
/// top). Returns <c>null</c> when the face can't be read — the caller then keeps the rectangular
/// assumption.
/// </summary>
public static class WallProfile
{
    public static ElevationProfile? For(WallAxes axes, Document doc)
    {
        IList<Reference> refs = HostObjectUtils.GetSideFaces(axes.Wall, ShellLayerType.Exterior);
        if (refs.Count == 0) return null;
        if (doc.GetElement(refs[0]).GetGeometryObjectFromReference(refs[0]) is not Face face) return null;

        List<UVPoint>? outer = null;
        double bestArea = -1;
        foreach (EdgeArray loop in face.EdgeLoops)
        {
            var pts = new List<UVPoint>();
            foreach (Edge e in loop)
            {
                IList<XYZ> tess = e.Tessellate();
                for (int i = 0; i < tess.Count - 1; i++)   // drop the shared endpoint between edges
                {
                    XYZ rel = tess[i] - axes.Origin;
                    pts.Add(new UVPoint(rel.DotProduct(axes.LengthDir), rel.DotProduct(axes.HeightDir)));
                }
            }
            if (pts.Count < 3) continue;
            double area = BBoxArea(pts);                   // outer outline = largest loop; rest = openings
            if (area > bestArea) { bestArea = area; outer = pts; }
        }
        return outer is null ? null : new ElevationProfile(outer);
    }

    private static double BBoxArea(List<UVPoint> pts)
    {
        double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
        foreach (UVPoint p in pts)
        {
            uMin = Math.Min(uMin, p.U); uMax = Math.Max(uMax, p.U);
            vMin = Math.Min(vMin, p.V); vMax = Math.Max(vMax, p.V);
        }
        return (uMax - uMin) * (vMax - vMin);
    }
}
