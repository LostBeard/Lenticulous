namespace Lenticulous.Scan;

/// <summary>The outcome of <see cref="ViewAssigner.Assign"/>: how many views the display has and which one each sub-pixel is in.</summary>
public sealed class ViewAssignment
{
    /// <summary>Number of views found.</summary>
    public int ViewCount { get; init; }

    /// <summary>The view of every sub-pixel (row-major, as the scans), 0 .. ViewCount-1 in ascending camera position, or -1 when the
    /// sub-pixel was never seen bright enough to say.</summary>
    public int[] ViewOf { get; init; } = Array.Empty<int>();

    /// <summary>The camera position at which each view looks brightest, ascending.</summary>
    public double[] ViewPosition { get; init; } = Array.Empty<double>();

    /// <summary>The camera travel from one view to the next: the median spacing of adjacent <see cref="ViewPosition"/>s, or NaN with fewer than two views.</summary>
    public double ViewWidth { get; init; } = double.NaN;

    /// <summary>Sub-pixels per view.</summary>
    public int[] Sizes { get; init; } = Array.Empty<int>();

    /// <summary>Smallest view over largest: 1 when every view owns the same number of sub-pixels, as a lenticular layout should.</summary>
    public double Balance { get; init; }

    /// <summary>Fraction of sub-pixels assigned to a view with a profile that matches it closely.</summary>
    public double Confidence { get; init; }

    /// <summary>Scans that duplicate another scan's view (the camera was in the same viewing zone twice). They can be discarded.</summary>
    public int[] RedundantScans { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Turns a set of scans into view numbers. The key fact: a sub-pixel's brightness across the scans is its fingerprint. Every
/// sub-pixel of one view is bright in the same scans (the ones whose camera sat in that view) and dim in the others, so sub-pixels
/// of one view have the same profile and different views have different ones. Cluster the profiles: one cluster per view. The
/// camera positions then put the views in order, and their spacing is the view width.
/// </summary>
public static class ViewAssigner
{
    /// <summary>
    /// Assign every sub-pixel to a view.
    /// </summary>
    /// <param name="set">The scans.</param>
    /// <param name="joinSimilarity">Cosine similarity at or above which two profiles are the same view.</param>
    /// <param name="minViewFraction">A cluster smaller than this fraction of the sub-pixels is noise, not a view.</param>
    public static ViewAssignment Assign(ScanSet set, double joinSimilarity = 0.85, double minViewFraction = 0.02)
    {
        int scanCount = set.Count;
        int n = set.SubWidth * set.Height;
        if (scanCount == 0) throw new InvalidOperationException("no scans to assign views from");

        // Profile of each sub-pixel: its normalised brightness in every scan, as a unit vector. A sub-pixel no scan saw bright has
        // no profile (its norm is too small to mean anything) and stays unassigned.
        var profiles = new float[n][];
        var strength = new float[n];
        for (int i = 0; i < n; i++)
        {
            var p = new float[scanCount];
            double sumSq = 0;
            for (int s = 0; s < scanCount; s++)
            {
                float v = set.Scans[s].Normalized[i];
                p[s] = v;
                sumSq += (double)v * v;
            }
            double norm = Math.Sqrt(sumSq);
            strength[i] = (float)norm;
            if (norm < 0.3) continue; // never seen bright
            for (int s = 0; s < scanCount; s++) p[s] = (float)(p[s] / norm);
            profiles[i] = p;
        }

        // Leader clustering, strongest profiles first so the cluster centres are clean.
        var order = Enumerable.Range(0, n).Where(i => profiles[i] != null).OrderByDescending(i => strength[i]).ToArray();
        var centroids = new List<float[]>();
        foreach (int i in order)
        {
            int best = -1;
            double bestSim = joinSimilarity;
            for (int c = 0; c < centroids.Count; c++)
            {
                double sim = Cosine(profiles[i]!, centroids[c]);
                if (sim >= bestSim) { bestSim = sim; best = c; }
            }
            if (best < 0) centroids.Add((float[])profiles[i]!.Clone());
        }

        // Refine: assign each profile to its nearest centre, move the centres to the mean of their members, merge centres that have
        // converged on the same view, drop clusters too small to be a view.
        var owner = new int[n];
        for (int pass = 0; pass < 6; pass++)
        {
            Array.Fill(owner, -1);
            foreach (int i in order) owner[i] = Nearest(profiles[i]!, centroids);
            var sums = centroids.Select(_ => new double[scanCount]).ToList();
            var counts = new int[centroids.Count];
            foreach (int i in order)
            {
                counts[owner[i]]++;
                for (int s = 0; s < scanCount; s++) sums[owner[i]][s] += profiles[i]![s];
            }
            var next = new List<float[]>();
            for (int c = 0; c < centroids.Count; c++)
            {
                if (counts[c] < Math.Max(1, (int)(minViewFraction * order.Length))) continue;
                next.Add(Normalise(sums[c]));
            }
            // Merge near-identical centres (the leader pass can split one view in two on noise).
            var merged = new List<float[]>();
            foreach (var centre in next)
            {
                bool absorbed = false;
                foreach (var kept in merged)
                    if (Cosine(centre, kept) >= 0.95) { absorbed = true; break; }
                if (!absorbed) merged.Add(centre);
            }
            if (merged.Count == 0) break;
            bool stable = merged.Count == centroids.Count;
            centroids = merged;
            if (stable && pass > 0) break;
        }

        if (centroids.Count == 0)
            return new ViewAssignment { ViewCount = 0, ViewOf = Enumerable.Repeat(-1, n).ToArray() };

        // Put the views in order of the camera position where each is brightest: the centroid is the view's profile over the scans,
        // so its square-weighted mean of the scan positions is where the camera saw that view best.
        var positions = centroids.Select(c =>
        {
            double w = 0, wp = 0;
            for (int s = 0; s < scanCount; s++)
            {
                double weight = (double)c[s] * c[s];
                w += weight;
                wp += weight * set.Scans[s].CameraPosition;
            }
            return w > 0 ? wp / w : 0;
        }).ToArray();
        var rank = Enumerable.Range(0, centroids.Count).OrderBy(c => positions[c]).ToArray();
        var viewOfCentroid = new int[centroids.Count];
        for (int v = 0; v < rank.Length; v++) viewOfCentroid[rank[v]] = v;

        var viewOf = Enumerable.Repeat(-1, n).ToArray();
        int confident = 0;
        foreach (int i in order)
        {
            int c = Nearest(profiles[i]!, centroids);
            viewOf[i] = viewOfCentroid[c];
            if (Cosine(profiles[i]!, centroids[c]) >= 0.8) confident++;
        }

        var sizes = new int[centroids.Count];
        foreach (int v in viewOf) if (v >= 0) sizes[v]++;
        double balance = sizes.Max() == 0 ? 0 : (double)sizes.Min() / sizes.Max();

        var viewPositions = rank.Select(c => positions[c]).ToArray();
        double width = double.NaN;
        if (viewPositions.Length >= 2)
        {
            var gaps = new List<double>();
            for (int v = 1; v < viewPositions.Length; v++) gaps.Add(viewPositions[v] - viewPositions[v - 1]);
            gaps.Sort();
            width = gaps[gaps.Count / 2];
        }

        return new ViewAssignment
        {
            ViewCount = centroids.Count,
            ViewOf = viewOf,
            ViewPosition = viewPositions,
            ViewWidth = width,
            Sizes = sizes,
            Balance = balance,
            Confidence = order.Length == 0 ? 0 : (double)confident / n,
            RedundantScans = FindRedundantScans(set, centroids, rank),
        };
    }

    /// <summary>
    /// A scan is redundant when its camera sat in a view that another scan covers at least as well: it adds nothing the other does not.
    /// Per view the strongest scan is kept; any other scan that is mostly that same view is reported.
    /// </summary>
    private static int[] FindRedundantScans(ScanSet set, List<float[]> centroids, int[] rank)
    {
        int scanCount = set.Count;
        var redundant = new List<int>();
        var strongest = new int[centroids.Count];
        for (int c = 0; c < centroids.Count; c++)
        {
            int best = 0;
            for (int s = 1; s < scanCount; s++) if (centroids[c][s] > centroids[c][best]) best = s;
            strongest[c] = best;
        }
        for (int s = 0; s < scanCount; s++)
        {
            int dominant = 0;
            for (int c = 1; c < centroids.Count; c++) if (centroids[c][s] > centroids[dominant][s]) dominant = c;
            if (strongest[dominant] != s && centroids[dominant][s] >= 0.7f * centroids[dominant][strongest[dominant]])
                redundant.Add(s);
        }
        return redundant.ToArray();
    }

    private static int Nearest(float[] profile, List<float[]> centroids)
    {
        int best = 0;
        double bestSim = double.MinValue;
        for (int c = 0; c < centroids.Count; c++)
        {
            double sim = Cosine(profile, centroids[c]);
            if (sim > bestSim) { bestSim = sim; best = c; }
        }
        return best;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

    private static float[] Normalise(double[] v)
    {
        double norm = Math.Sqrt(v.Sum(x => x * x));
        var result = new float[v.Length];
        if (norm > 0) for (int i = 0; i < v.Length; i++) result[i] = (float)(v[i] / norm);
        return result;
    }
}
