namespace Lenticulous.Scan;

/// <summary>
/// A projective mapping between two planes: here, from a pixel of the stimulus window to the camera pixel where that screen pixel
/// shows up. Fitted from a handful of known correspondences (white patches whose stimulus position is known and whose camera position
/// is measured), so the scan can look in the right place of the camera picture for the sub-pixel it lit.
/// </summary>
public sealed class Homography
{
    private readonly double[] _h; // 3x3 row-major, _h[8] = 1

    private Homography(double[] h) { _h = h; }

    /// <summary>The nine matrix values, row-major.</summary>
    public IReadOnlyList<double> Matrix => _h;

    /// <summary>Map a point.</summary>
    public (double X, double Y) Map(double x, double y)
    {
        double w = _h[6] * x + _h[7] * y + _h[8];
        return ((_h[0] * x + _h[1] * y + _h[2]) / w, (_h[3] * x + _h[4] * y + _h[5]) / w);
    }

    /// <summary>The inverse mapping (camera pixel back to stimulus pixel).</summary>
    public Homography Inverse()
    {
        var m = _h;
        double det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
        if (Math.Abs(det) < 1e-300) throw new InvalidOperationException("homography is singular");
        var inv = new[]
        {
            (m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
            (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
            (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det,
        };
        double s = inv[8];
        for (int i = 0; i < 9; i++) inv[i] /= s;
        return new Homography(inv);
    }

    /// <summary>Root-mean-square distance between the mapped <paramref name="from"/> points and <paramref name="to"/>.</summary>
    public double RmsError(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to)
    {
        double sum = 0;
        for (int i = 0; i < from.Count; i++)
        {
            var (x, y) = Map(from[i].X, from[i].Y);
            sum += (x - to[i].X) * (x - to[i].X) + (y - to[i].Y) * (y - to[i].Y);
        }
        return Math.Sqrt(sum / from.Count);
    }

    /// <summary>
    /// Least-squares fit of the homography taking <paramref name="from"/> to <paramref name="to"/> (four or more correspondences).
    /// Coordinates are first centred and scaled (Hartley normalisation) so pixel values in the thousands do not wreck the conditioning.
    /// </summary>
    public static Homography Fit(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to)
    {
        if (from.Count != to.Count) throw new ArgumentException("point lists differ in length");
        if (from.Count < 4) throw new ArgumentException("a homography needs at least 4 correspondences");

        var (tFrom, nFrom) = Normalise(from);
        var (tTo, nTo) = Normalise(to);

        // Normal equations of  [x y 1 0 0 0 -xu -yu] h = u,  [0 0 0 x y 1 -xv -yv] h = v  for the 8 unknowns (h33 = 1).
        var ata = new double[8, 8];
        var atb = new double[8];
        for (int i = 0; i < nFrom.Count; i++)
        {
            double x = nFrom[i].X, y = nFrom[i].Y, u = nTo[i].X, v = nTo[i].Y;
            var r1 = new[] { x, y, 1, 0, 0, 0, -x * u, -y * u };
            var r2 = new[] { 0, 0, 0, x, y, 1, -x * v, -y * v };
            for (int a = 0; a < 8; a++)
            {
                atb[a] += r1[a] * u + r2[a] * v;
                for (int b = 0; b < 8; b++) ata[a, b] += r1[a] * r1[b] + r2[a] * r2[b];
            }
        }
        var h = Solve(ata, atb);
        var normalised = new[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 };

        // Undo the normalisation: H = T_to^-1 * H_n * T_from.
        var result = Multiply(Multiply(InverseSimilarity(tTo), normalised), tFrom);
        double s = result[8];
        for (int i = 0; i < 9; i++) result[i] /= s;
        return new Homography(result);
    }

    // The similarity transform that moves the centroid to the origin and scales the mean distance to sqrt(2).
    private static (double[] T, List<(double X, double Y)> Points) Normalise(IReadOnlyList<(double X, double Y)> pts)
    {
        double cx = pts.Average(p => p.X), cy = pts.Average(p => p.Y);
        double mean = pts.Average(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)));
        double s = mean < 1e-12 ? 1 : Math.Sqrt(2) / mean;
        var t = new[] { s, 0, -s * cx, 0, s, -s * cy, 0, 0, 1 };
        return (t, pts.Select(p => ((p.X - cx) * s, (p.Y - cy) * s)).ToList());
    }

    private static double[] InverseSimilarity(double[] t)
    {
        double s = t[0];
        return new[] { 1 / s, 0, -t[2] / s, 0, 1 / s, -t[5] / s, 0, 0, 1 };
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        var r = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
        return r;
    }

    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            if (Math.Abs(a[p, c]) < 1e-12) throw new InvalidOperationException("the points do not determine a homography (collinear?)");
            if (p != c)
            {
                for (int k = 0; k < n; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]);
                (b[c], b[p]) = (b[p], b[c]);
            }
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = a[r, c] / a[c, c];
                for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                b[r] -= f * b[c];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = b[i] / a[i, i];
        return x;
    }
}
