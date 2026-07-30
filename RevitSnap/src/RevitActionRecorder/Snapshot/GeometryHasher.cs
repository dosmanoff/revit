using System.Security.Cryptography;
using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Детерминированный SHA-256 по тесселированной геометрии (DetailLevel.Fine).
/// Сама геометрия не сохраняется — только хеш («геометрия изменилась / не изменилась»).
/// Координаты округляются до 1e-6 фт, чтобы шум регенерации не менял хеш.
/// </summary>
internal static class GeometryHasher
{
    public static string? Hash(Element element)
    {
        try
        {
            var options = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
            };

            var geometry = element.get_Geometry(options);
            if (geometry is null)
                return null;

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[8];
            bool any = false;

            Walk(geometry);
            if (!any)
                return null;

            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();

            void AddDouble(double value)
            {
                BitConverter.TryWriteBytes(buffer, Math.Round(value, 6));
                sha.AppendData(buffer);
                any = true;
            }

            void AddXyz(XYZ point)
            {
                AddDouble(point.X);
                AddDouble(point.Y);
                AddDouble(point.Z);
            }

            void Walk(GeometryElement geometryElement)
            {
                foreach (var obj in geometryElement)
                {
                    switch (obj)
                    {
                        case Solid solid:
                            foreach (Face face in solid.Faces)
                            {
                                try
                                {
                                    var mesh = face.Triangulate();
                                    if (mesh is not null)
                                        foreach (XYZ v in mesh.Vertices)
                                            AddXyz(v);
                                }
                                catch { }
                            }
                            break;
                        case Mesh mesh:
                            foreach (XYZ v in mesh.Vertices)
                                AddXyz(v);
                            break;
                        case Curve curve:
                            try
                            {
                                foreach (XYZ v in curve.Tessellate())
                                    AddXyz(v);
                            }
                            catch { }
                            break;
                        case PolyLine poly:
                            foreach (XYZ v in poly.GetCoordinates())
                                AddXyz(v);
                            break;
                        case Autodesk.Revit.DB.Point point:
                            AddXyz(point.Coord);
                            break;
                        case GeometryInstance instance:
                            try
                            {
                                var inner = instance.GetInstanceGeometry();
                                if (inner is not null)
                                    Walk(inner);
                            }
                            catch { }
                            break;
                    }
                }
            }
        }
        catch
        {
            return null;
        }
    }
}
