using Autodesk.Revit.DB;

namespace RevitActionRecorder.Snapshot;

/// <summary>
/// Компактная подпись положения элемента для теневого кэша: Move/Drag не меняют параметров,
/// и без неё перемещение видно только как факт транзакции, без величины.
/// </summary>
internal readonly record struct LocationSignature(string Kind, double[] Values)
{
    private const int Digits = 6;

    public static LocationSignature? Of(Element element)
    {
        try
        {
            switch (element.Location)
            {
                case LocationPoint lp:
                {
                    var p = lp.Point;
                    double rotation = 0;
                    try { rotation = lp.Rotation; } catch { }
                    return new LocationSignature("point",
                    [
                        Math.Round(p.X, Digits), Math.Round(p.Y, Digits), Math.Round(p.Z, Digits),
                        Math.Round(rotation, Digits),
                    ]);
                }
                case LocationCurve lc when lc.Curve is { } curve:
                {
                    var a = curve.GetEndPoint(0);
                    var b = curve.GetEndPoint(1);
                    return new LocationSignature("curve",
                    [
                        Math.Round(a.X, Digits), Math.Round(a.Y, Digits), Math.Round(a.Z, Digits),
                        Math.Round(b.X, Digits), Math.Round(b.Y, Digits), Math.Round(b.Z, Digits),
                    ]);
                }
            }
        }
        catch
        {
        }
        return null;
    }

    public bool SameAs(in LocationSignature other)
    {
        if (Kind != other.Kind || Values.Length != other.Values.Length)
            return false;
        for (int i = 0; i < Values.Length; i++)
            if (Values[i] != other.Values[i])
                return false;
        return true;
    }

    /// <summary>Смещение между одинаковыми по типу подписями; null, если сдвиг не однородный.</summary>
    public double[]? DisplacementFrom(in LocationSignature before)
    {
        if (Kind != before.Kind || Values.Length != before.Values.Length)
            return null;

        var dx = Math.Round(Values[0] - before.Values[0], Digits);
        var dy = Math.Round(Values[1] - before.Values[1], Digits);
        var dz = Math.Round(Values[2] - before.Values[2], Digits);

        if (Kind == "curve")
        {
            var dx2 = Math.Round(Values[3] - before.Values[3], Digits);
            var dy2 = Math.Round(Values[4] - before.Values[4], Digits);
            var dz2 = Math.Round(Values[5] - before.Values[5], Digits);
            // Оба конца сдвинулись одинаково — это перенос; иначе кривая изменила форму.
            if (dx != dx2 || dy != dy2 || dz != dz2)
                return null;
        }

        return dx == 0 && dy == 0 && dz == 0 ? null : [dx, dy, dz];
    }
}
