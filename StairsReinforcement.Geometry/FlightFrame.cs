namespace StairsReinforcement.Geometry;

/// <summary>
/// Local orthonormal frame of an inclined flight waist (all in feet, world-aligned):
/// <c>U</c> = up-slope axis lying in the waist plane, <c>W</c> = horizontal axis across the
/// width, <c>N</c> = waist normal (points up-and-back, Z &gt; 0). Right-handed: <c>N = U × W</c>.
///
/// A flight longitudinal bar runs along <c>U</c>, is offset toward a face along <c>N</c>, and is
/// copied across the width along <c>W</c>. Crucially, a bar that folds at its ends bends in the
/// vertical plane spanned by the run and Z, whose normal is exactly <c>W</c> — so <c>W</c> is the
/// correct <c>Rebar.CreateFromCurves</c> normal for a sloped flight bar (never world Z).
/// </summary>
public sealed class FlightFrame
{
    public Pt3 Origin { get; }
    public Pt3 U { get; }
    public Pt3 W { get; }
    public Pt3 N { get; }
    public double SlopeRad { get; }

    private FlightFrame(Pt3 origin, Pt3 u, Pt3 w, Pt3 n, double slopeRad)
    {
        Origin = origin; U = u; W = w; N = n; SlopeRad = slopeRad;
    }

    /// <param name="origin">Reference point (typically lower edge, mid-width, on the soffit).</param>
    /// <param name="runDirHoriz">Horizontal direction the flight climbs toward (in plan).</param>
    /// <param name="slopeRad">Pitch above horizontal, radians (0 = flat, →π/2 = vertical).</param>
    public static FlightFrame Create(Pt3 origin, Pt2 runDirHoriz, double slopeRad)
    {
        Pt2 rd = runDirHoriz.Normalized();
        if (rd.Length < 1e-9) rd = new Pt2(1, 0);

        double c = Math.Cos(slopeRad), s = Math.Sin(slopeRad);
        Pt3 u = new Pt3(rd.X * c, rd.Y * c, s).Normalized();
        Pt3 w = new Pt3(-rd.Y, rd.X, 0).Normalized();   // horizontal, ⟂ run
        Pt3 n = u.Cross(w).Normalized();                // = (-s·rd.X, -s·rd.Y, c), Z = cos(slope) > 0
        if (n.Z < 0) n = n * -1;                        // guard against a negative pitch input
        return new FlightFrame(origin, u, w, n, slopeRad);
    }

    public static FlightFrame FromRiseRun(Pt3 origin, Pt2 runDirHoriz, double horizRun, double rise)
        => Create(origin, runDirHoriz, Math.Atan2(rise, Math.Max(1e-9, horizRun)));

    /// <summary>
    /// Build the frame from the run's real soffit face normal (the outward normal of the inclined
    /// bottom face, pointing down-and-out). This is the ground truth for a monolithic run: deriving
    /// the slope from the stairs path + <c>run.Height</c> is a few degrees off the actual waist, which
    /// makes longitudinal bars visibly non-parallel to the soffit. <paramref name="climbDirHoriz"/>
    /// only disambiguates which way U points (toward the top of the flight, in plan).
    /// </summary>
    public static FlightFrame FromSoffit(Pt3 origin, Pt3 soffitNormalOut, Pt2 climbDirHoriz)
    {
        Pt3 n = (soffitNormalOut * -1).Normalized();    // waist normal = -outward soffit normal
        if (n.Z < 0) n = n * -1;
        Pt2 rd = climbDirHoriz.Normalized();
        if (rd.Length < 1e-9) rd = new Pt2(1, 0);
        Pt3 w = new Pt3(-rd.Y, rd.X, 0).Normalized();   // horizontal, ⟂ the climb direction
        Pt3 u = w.Cross(n).Normalized();                // up-slope, in the soffit plane
        if (u.X * rd.X + u.Y * rd.Y < 0) { u = u * -1; w = w * -1; }   // make U climb toward rd
        double slope = Math.Atan2(u.Z, Math.Sqrt(u.X * u.X + u.Y * u.Y));
        return new FlightFrame(origin, u, w, n, slope);
    }

    /// <summary>World point at local coordinates (u along slope, w across width, n along normal).</summary>
    public Pt3 At(double u, double w, double n) => Origin + U * u + W * w + N * n;
}
