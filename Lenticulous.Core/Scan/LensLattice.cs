namespace Lenticulous.Scan;

/// <summary>
/// Reads the lens out of scans of lit sub-pixels: the exact pitch from a long contiguous run, and, for any window, the lens phase
/// the camera sees there.
/// </summary>
public static class LensLattice
{
    /// <summary>
    /// The pitch from a long contiguous scan: the bright sub-pixels sit on an integer lattice, e.g. gaps 4, 5, 5, 4, 5, 5 ... for a
    /// pitch of 14/3. The shortest repeating run of gaps gives the fraction: its sum over its length. Short windows cannot do this
    /// (4.6 and 4.75 look alike over 18 sub-pixels); a run of several lens periods can.
    /// </summary>
    /// <param name="profile">The brightness of consecutive sub-pixels (one scan).</param>
    /// <param name="lobePositions">The sub-pixel index of each bright lobe, as found.</param>
    /// <param name="gaps">The distances between consecutive lobes.</param>
    /// <returns>The pitch as a reduced fraction, or null when the gaps do not repeat (not enough lobes, or the scan is noisy).</returns>
    public static (int Numerator, int Denominator)? DetectPitch(IReadOnlyList<double> profile, out List<int> lobePositions, out List<int> gaps,
        int minRepeats = 3, int maxCycle = 8)
    {
        lobePositions = Lobes(profile);
        gaps = new List<int>();
        for (int i = 1; i < lobePositions.Count; i++) gaps.Add(lobePositions[i] - lobePositions[i - 1]);
        int n = gaps.Count;
        for (int cycle = 1; cycle <= maxCycle; cycle++)
        {
            if (n < cycle * minRepeats) break;
            // The longest run of gaps that repeat the gap one cycle earlier. The ends of a scan are ragged (the first and last lobe
            // can be clipped), so the run only has to cover all but a few gaps; the middle must be exact.
            int run = 0, bestRun = 0, bestEnd = -1;
            for (int i = cycle; i < n; i++)
            {
                run = gaps[i] == gaps[i - cycle] ? run + 1 : 0;
                if (run > bestRun) { bestRun = run; bestEnd = i; }
            }
            if (bestRun < cycle * (minRepeats - 1) || (n - cycle) - bestRun > 3) continue;
            int sum = 0;
            for (int i = bestEnd - cycle + 1; i <= bestEnd; i++) sum += gaps[i];
            int g = Gcd(sum, cycle);
            return (sum / g, cycle / g);
        }
        return null;
    }

    /// <summary>The bright lobes of a profile: local maxima over a fifth of the brightest, adjacent maxima merged into the stronger.</summary>
    public static List<int> Lobes(IReadOnlyList<double> profile)
    {
        double max = 0;
        foreach (var v in profile) if (!double.IsNaN(v) && v > max) max = v;
        var lobes = new List<int>();
        if (max <= 0) return lobes;
        for (int i = 0; i < profile.Count; i++)
        {
            double v = profile[i];
            if (double.IsNaN(v) || v < 0.2 * max) continue;
            if (i > 0 && !double.IsNaN(profile[i - 1]) && profile[i - 1] > v) continue;
            if (i < profile.Count - 1 && !double.IsNaN(profile[i + 1]) && profile[i + 1] > v) continue;
            if (lobes.Count > 0 && i - lobes[^1] <= 1)
            {
                if (v > profile[lobes[^1]]) lobes[^1] = i;
            }
            else lobes.Add(i);
        }
        return lobes;
    }

    /// <summary>
    /// The lens phase the camera sees in a window: the phase that best explains which sub-pixels are bright, by correlating the
    /// window with a bump of width <paramref name="width"/> (in cycles) around each candidate phase.
    /// </summary>
    /// <param name="values">Brightness of consecutive sub-pixels, the first being sub-pixel <paramref name="firstSubX"/>.</param>
    /// <returns>The phase (0 to 1) and the correlation (how well the model explains the window, up to 1).</returns>
    public static (double Phase, double Correlation) WindowPhase(LensModel lens, IReadOnlyList<double> values, int firstSubX, int row, double width = 0.10, int steps = 400)
    {
        int n = values.Count;
        double max = 0;
        for (int i = 0; i < n; i++) if (!double.IsNaN(values[i]) && values[i] > max) max = values[i];
        if (max <= 0) return (0, 0);
        var d = new double[n];
        for (int i = 0; i < n; i++) d[i] = double.IsNaN(values[i]) ? 0 : Math.Max(0, values[i] / max);
        double meanD = 0;
        foreach (var v in d) meanD += v;
        meanD /= n;
        var phases = new double[n];
        for (int i = 0; i < n; i++) phases[i] = lens.Phase(firstSubX + i, row);

        double best = -2, bestPhase = 0;
        var g = new double[n];
        for (int s = 0; s < steps; s++)
        {
            double psi = (double)s / steps;
            double meanG = 0;
            for (int i = 0; i < n; i++)
            {
                double dd = phases[i] - psi;
                dd -= Math.Round(dd);
                g[i] = Math.Exp(-0.5 * (dd / width) * (dd / width));
                meanG += g[i];
            }
            meanG /= n;
            double num = 0, vd = 0, vg = 0;
            for (int i = 0; i < n; i++)
            {
                num += (d[i] - meanD) * (g[i] - meanG);
                vd += (d[i] - meanD) * (d[i] - meanD);
                vg += (g[i] - meanG) * (g[i] - meanG);
            }
            double c = vd > 0 && vg > 0 ? num / Math.Sqrt(vd * vg) : 0;
            if (c > best) { best = c; bestPhase = psi; }
        }
        return (bestPhase, best);
    }

    /// <summary>
    /// The slant from the SAME window of sub-pixels scanned on consecutive rows: with the right slant the phase the camera sees is the
    /// same on every row, with a wrong one it drifts. Every simple fraction of sub-pixels per row is tried and the one with the
    /// smallest drift wins (ties go to the simpler fraction).
    /// </summary>
    /// <param name="rows">The scans, one per row, in order of row; row numbers are <paramref name="firstRow"/>, +1, +2 ...</param>
    /// <returns>The slant as a fraction, and the spread (in cycles) of the phase over the rows with it.</returns>
    public static (int Numerator, int Denominator, double Spread) FitSlant(int pitchNumerator, int pitchDenominator, IReadOnlyList<IReadOnlyList<double>> rows,
        int firstSubX, int firstRow, int maxDenominator = 8, double maxSlant = 2.0)
    {
        (int n, int d, double spread)? best = null;
        for (int d = 1; d <= maxDenominator; d++)
            for (int n = (int)Math.Floor(-maxSlant * d); n <= (int)Math.Ceiling(maxSlant * d); n++)
            {
                if (Gcd(Math.Abs(n), d) != 1 && n != 0) continue; // reduced fractions only (0 only as 0/1)
                if (n == 0 && d != 1) continue;
                var lens = new LensModel(pitchNumerator, pitchDenominator, n, d, 0);
                var phases = new double[rows.Count];
                for (int i = 0; i < rows.Count; i++) phases[i] = WindowPhase(lens, rows[i], firstSubX, firstRow + i, steps: 200).Phase;
                double lo = 0, hi = 0;
                for (int i = 1; i < phases.Length; i++) { double dd = phases[i] - phases[0]; dd -= Math.Round(dd); lo = Math.Min(lo, dd); hi = Math.Max(hi, dd); }
                double spread = hi - lo;
                if (best == null || spread < best.Value.spread - 1e-9 || (Math.Abs(spread - best.Value.spread) <= 1e-9 && d < best.Value.d))
                    best = (n, d, spread);
            }
        return (best!.Value.n, best.Value.d, best.Value.spread);
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
}
