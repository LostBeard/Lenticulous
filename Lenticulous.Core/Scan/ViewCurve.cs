namespace Lenticulous.Scan;

/// <summary>
/// The lens phase a fixed camera sees, as a function of the panel column. A camera close to the panel looks at different columns
/// from different angles, so the phase sweeps through the view fan; for a flat panel it follows
/// <c>phase(x) = center - K * atan((footX - x) * pitch / distance)</c>, where <c>footX</c> is the column straight in front of the
/// camera, <c>center</c> the phase seen there (the lens centre) and K the cycles per radian. Fitting it ties the lens phase to the
/// geometry, which fixes where the zone straight ahead lies, i.e. which sub-pixels are the middle view.
/// </summary>
public sealed record ViewCurveFit(double DistanceMm, double FootX, double CyclesPerRadian, double CenterPhase, double ResidualCycles)
{
    /// <summary>Degrees of viewing angle for one full cycle of lens phase (one zone).</summary>
    public double ZoneDegrees => 180.0 / Math.PI / Math.Abs(CyclesPerRadian);

    /// <summary>
    /// Fit the curve to window phases. <paramref name="columns"/> are panel columns (pixels); <paramref name="phases"/> their phases
    /// in cycles, UNWRAPPED (made continuous: use <see cref="Unwrap"/>). The distance is not determined well by the curve alone
    /// (it trades against the foot position and the zone width), so a measured <paramref name="distanceHintMm"/> and its tolerance narrow
    /// the search; the centre phase moves about 0.075 per 10 mm of distance.
    /// </summary>
    public static ViewCurveFit? Fit(IReadOnlyList<double> columns, IReadOnlyList<double> phases, double pixelPitchMm,
        double distanceHintMm = 150, double distanceToleranceMm = 40)
    {
        int n = columns.Count;
        if (n < 5 || phases.Count != n) return null;
        ViewCurveFit? best = null;
        double dLow = Math.Max(30, distanceHintMm - distanceToleranceMm), dHigh = distanceHintMm + distanceToleranceMm;
        double lo = columns[0], hi = columns[n - 1];
        for (double d = dLow; d <= dHigh; d += 2)
            for (double foot = lo - 0.5 * (hi - lo); foot <= hi + 3.0 * (hi - lo); foot += 5)
            {
                var a = new double[n];
                double meanA = 0, meanP = 0;
                for (int i = 0; i < n; i++) { a[i] = Math.Atan((foot - columns[i]) * pixelPitchMm / d); meanA += a[i]; meanP += phases[i]; }
                meanA /= n; meanP /= n;
                double sxx = 0, sxy = 0;
                for (int i = 0; i < n; i++) { sxx += (a[i] - meanA) * (a[i] - meanA); sxy += (a[i] - meanA) * (phases[i] - meanP); }
                if (sxx < 1e-12) continue;
                double k = sxy / sxx;
                double center = meanP - k * meanA;
                double sse = 0;
                for (int i = 0; i < n; i++) { double r = phases[i] - (center + k * a[i]); sse += r * r; }
                double residual = Math.Sqrt(sse / n);
                if (best == null || residual < best.ResidualCycles) best = new ViewCurveFit(d, foot, k, center, residual);
            }
        return best;
    }

    /// <summary>Make a series of wrapped phases (0 to 1) continuous: each step is taken as the short way round.</summary>
    public static double[] Unwrap(IReadOnlyList<double> wrapped)
    {
        var result = new double[wrapped.Count];
        if (wrapped.Count == 0) return result;
        result[0] = wrapped[0];
        for (int i = 1; i < wrapped.Count; i++)
        {
            double step = wrapped[i] - wrapped[i - 1];
            step -= Math.Round(step);
            result[i] = result[i - 1] + step;
        }
        return result;
    }
}
