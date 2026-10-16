using Lenticulous.Scan;
using SpawnDev.UnitTesting;

namespace Lenticulous.Tests;

/// <summary>
/// The lens of the Philips 22-3D2N11/00 as scanned with the Reachy camera on 2026-10-15: the exact pitch from a long contiguous run,
/// the slant from consecutive rows, and the phase curve across the panel. The numbers below are REAL measurements (region sums of
/// one lit sub-pixel at a time, in hundreds of camera counts), not simulations.
/// </summary>
public class LensTests
{
    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    /// <summary>The Philips' lens: 14/3 sub-pixels per lens, shifting 1/2 sub-pixel per row.</summary>
    private static LensModel Philips(double center = 0) => new(14, 3, 1, 2, center);

    // x=200, y=440 (panel row 496), 30 pixels = 90 consecutive sub-pixels R,G,B,R,G,B...
    private static readonly double[] LongWindow = ("38 55 1 5 10 37 10 6 1 16 86 2 4 7 13 39 8 1 11 76 13 6 6 4 70 9 0 6 34 46 8 7 2 62 24 2 7 15 56 9 7 2 27 78 3 6 9 25 17 8 2 14 " +
        "90 5 6 6 9 61 9 0 8 57 32 6 6 5 74 13 2 7 24 61 6 7 3 49 49 1 5 13 54 8 8 3 20 90 3 4 8 16").Split(' ').Select(double.Parse).ToArray();

    // x=520, rows 440..447 (stimulus), 6 pixels = 18 sub-pixels.
    private static readonly double[][] DownRows =
    {
        Row("3 80 18 1 5 22 36 13 5 2 62 75 2 5 9 39 24 5"),
        Row("2 41 93 0 5 7 28 39 7 2 14 100 6 6 3 11 83 8"),
        Row("2 12 98 3 6 5 7 73 8 2 7 49 18 9 1 3 69 18"),
        Row("2 6 38 16 9 5 0 62 36 1 5 10 20 12 5 -1 27 93"),
        Row("2 5 9 16 14 5 1 18 94 3 4 3 11 48 7 -1 8 92"),
        Row("1 3 6 5 49 10 0 3 78 4 5 4 0 56 13 0 5 28"),
        Row("4 5 6 0 47 15 -2 4 20 5 7 5 -2 33 67 -1 2 8"),
        Row("3 6 7 0 17 75 -1 3 2 4 18 5 -1 7 89 0 2 4"),
    };

    // y=440, 6 pixels = 18 sub-pixels at x = 240, 280, ... 880.
    private static readonly (int X, double[] Values)[] Across =
    {
        (240, Row("7 11 37 14 7 3 16 97 4 7 8 14 59 10 1 9 71 27")), (280, Row("34 6 4 16 105 8 5 12 14 82 11 2 13 65 48 8 9 6")),
        (320, Row("19 106 14 6 14 12 88 14 3 17 51 66 6 8 7 85 29 4")), (360, Row("6 10 14 97 16 3 12 52 82 8 7 11 84 44 5 7 34 87")),
        (400, Row("104 22 5 5 50 100 10 7 10 83 77 7 5 34 94 17 10 5")), (440, Row("7 45 101 16 8 4 63 109 8 7 18 78 40 13 4 30 128 12")),
        (480, Row("21 11 6 33 126 12 6 12 51 81 15 5 11 112 25 9 8 20")), (520, Row("4 83 18 0 5 26 41 14 4 3 65 78 3 6 7 42 26 6")),
        (560, Row("9 9 8 85 30 5 7 32 104 12 9 5 64 96 7 7 16 96")), (600, Row("89 97 7 8 20 105 30 12 5 23 130 11 8 13 51 93 17 3")),
        (640, Row("8 15 59 103 17 3 18 125 21 10 12 13 127 27 3 11 69 93")), (680, Row("127 28 4 11 77 87 15 10 8 116 73 7 7 28 102 24 10 6")),
        (720, Row("14 33 98 23 12 7 64 119 7 9 17 86 80 15 2 20 128 15")), (760, Row("85 13 3 20 126 13 8 12 20 122 20 5 12 101 66 13 11 10")),
        (800, Row("13 102 64 13 12 10 130 40 7 11 35 105 20 10 6 92 110 9")), (840, Row("22 11 7 88 112 8 9 21 99 45 12 9 25 133 14 10 15 54")),
        (880, Row("24 136 11 7 14 57 121 14 6 15 127 35 9 11 15 141 26 5")),
    };

    private static double[] Row(string text) => text.Split(' ').Select(double.Parse).ToArray();

    [TestMethod]
    public void DetectPitch_FindsFourteenThirdsInTheRealLongWindow()
    {
        var pitch = LensLattice.DetectPitch(LongWindow, out var lobes, out var gaps);
        Check(pitch == (14, 3), $"pitch {(pitch is { } p ? $"{p.Numerator}/{p.Denominator}" : "none")}, lobes {string.Join(",", lobes)}");
        var expected = new[] { 4, 5, 5 };
        for (int i = 0; i < gaps.Count; i++) Check(gaps[i] == expected[i % 3], $"gap {i} is {gaps[i]}, expected {expected[i % 3]}: {string.Join(",", gaps)}");
        Check(gaps.Count >= 15, $"only {gaps.Count} gaps");
    }

    [TestMethod]
    public void DetectPitch_ReadsOtherFractionsAndRefusesShortOrIrregularScans()
    {
        // A synthetic lattice for each candidate: bright sub-pixels at round(k * pitch).
        foreach (var (num, den) in new[] { (14, 3), (19, 4), (9, 2), (24, 5), (23, 5) })
        {
            var profile = new double[100];
            for (int k = 0; k * num / den < 99; k++) profile[k * num / den] = 100; // integer lattice: floor(k * pitch)
            var pitch = LensLattice.DetectPitch(profile, out _, out _);
            Check(pitch == (num, den), $"{num}/{den} read as {(pitch is { } p ? $"{p.Numerator}/{p.Denominator}" : "none")}");
        }
        // 18 sub-pixels (the usual window) hold only 3-4 lobes: not enough gaps to see a repeat.
        Check(LensLattice.DetectPitch(DownRows[0], out _, out _) == null, "a short window must not claim a pitch");
        // Irregular gaps (noise) have no repeating run.
        var irregular = new double[60];
        foreach (var i in new[] { 2, 7, 11, 18, 22, 30, 33, 41, 47, 52 }) irregular[i] = 100;
        Check(LensLattice.DetectPitch(irregular, out _, out _) == null, "irregular lobes must not give a pitch");
    }

    [TestMethod]
    public void PhilipsTile_IsExactlyPeriodicAndSlantedByHalfASubPixelPerRow()
    {
        var lens = Philips();
        Check(lens.TileCols == 42 && lens.TileRows == 28, $"tile {lens.TileCols} x {lens.TileRows}, expected 42 x 28");
        Check(lens.Classes == 28, $"{lens.Classes} lens phase classes, expected 28");
        for (int r = 0; r < 60; r++)
            for (int s = 0; s < 120; s++)
            {
                int v = lens.ViewOf(s, r, 8);
                Check(lens.ViewOf(s + lens.TileCols, r, 8) == v, $"not periodic across columns at ({s},{r})");
                Check(lens.ViewOf(s, r + lens.TileRows, 8) == v, $"not periodic down rows at ({s},{r})");
                Check(lens.ViewOf(s + 1, r + 2, 8) == v, $"slant of 1/2 sub-pixel per row broken at ({s},{r})");
            }
        // No smaller rectangle repeats it (the tile is the smallest cell that tiles with whole pixels).
        for (int c = 3; c < lens.TileCols; c += 3)
            Check(lens.PhaseClass(c, 0) != lens.PhaseClass(0, 0) || lens.PhaseClass(c, 1) != lens.PhaseClass(0, 1), $"{c} columns already repeat");
        for (int rows = 1; rows < lens.TileRows; rows++)
            Check(lens.PhaseClass(0, rows) != lens.PhaseClass(0, 0), $"{rows} rows already repeat");
        // With one view per class, every view owns the same number of sub-pixels.
        var tile = lens.ToTile(28);
        var counts = new int[28];
        foreach (var v in tile.Views) counts[v]++;
        Check(counts.All(c => c == tile.Views.Length / 28), $"unbalanced views: {string.Join(",", counts)}");
        // 8 views over 28 classes: 3 or 4 classes per view, so occupancy within a factor of 4/3.
        var tile8 = lens.ToTile(8);
        var c8 = new int[8];
        foreach (var v in tile8.Views) c8[v]++;
        Check(c8.Max() * 3 <= c8.Min() * 4 + 1, $"8-view occupancy {string.Join(",", c8)}");
    }

    [TestMethod]
    public void BrightSubPixelsOfOnePhaseFollowTheMeasuredGaps()
    {
        // For one camera angle the bright sub-pixels are those within a narrow band of lens phase; along a row their gaps are the
        // measured 4, 5, 5 (or a rotation of it).
        var lens = Philips();
        var values = new double[90];
        for (int k = 0; k < 90; k++)
        {
            double dd = lens.Phase(600 + k, 496) - 0.30;
            dd -= Math.Round(dd);
            // The camera's response is wider than the 0.214 cycles one sub-pixel advances the phase (measured width about 0.10).
            values[k] = Math.Exp(-0.5 * (dd / 0.10) * (dd / 0.10)) * 100;
        }
        var pitch = LensLattice.DetectPitch(values, out _, out var gaps);
        Check(pitch == (14, 3), $"model lattice read as {(pitch is { } p ? $"{p.Numerator}/{p.Denominator}" : "none")}, gaps {string.Join(",", gaps)}");
    }

    [TestMethod]
    public void WindowPhase_HoldsAcrossRowsOnlyWithTheRightSlant()
    {
        // Eight consecutive rows of the same 6 pixels: with the measured slant the phase seen is the same on every row.
        double Spread(LensModel lens)
        {
            var phases = new List<double>();
            for (int i = 0; i < DownRows.Length; i++)
            {
                var (phase, corr) = LensLattice.WindowPhase(lens, DownRows[i], 3 * 520, 440 + 56 + i);
                phases.Add(phase);
            }
            // circular spread around the first phase
            double lo = 0, hi = 0;
            foreach (var p in phases) { double d = p - phases[0]; d -= Math.Round(d); lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
            return hi - lo;
        }
        double right = Spread(Philips());
        Check(right < 0.12, $"with the slant the phase spread over 8 rows is {right:0.000} cycles, expected under 0.12");
        double none = Spread(new LensModel(14, 3, 0, 1, 0));
        Check(none > 0.3, $"with no slant the phase spread is {none:0.000} cycles; it should drift by about 0.1 per row (0.7 over 8 rows)");
        double wrongSign = Spread(new LensModel(14, 3, -1, 2, 0));
        Check(wrongSign > 0.3, $"with the slant reversed the phase spread is {wrongSign:0.000} cycles; it should drift twice as fast");
    }

    [TestMethod]
    public void FitSlant_FindsOneHalfInTheRealRows()
    {
        var rows = DownRows.Select(r => (IReadOnlyList<double>)r).ToList();
        var (n, d, spread) = LensLattice.FitSlant(14, 3, rows, 3 * 520, 440 + 56);
        Check(n == 1 && d == 2, $"slant {n}/{d} (spread {spread:0.000}), expected 1/2");
        Check(spread < 0.12, $"spread {spread:0.000}");
        // The same rows with the first and last dropped still agree (the choice is not an accident of one noisy row).
        var (n2, d2, _) = LensLattice.FitSlant(14, 3, rows.Skip(1).Take(6).ToList(), 3 * 520, 440 + 56 + 1);
        Check(n2 == 1 && d2 == 2, $"six middle rows give {n2}/{d2}");
    }

    [TestMethod]
    public void EveryRealWindowIsExplainedByTheLens()
    {
        // 17 across windows + 8 rows + the long window: one free phase per window leaves a high correlation, and a wrong pitch of the
        // same size does worse on the long window (where the lattice is visible).
        var lens = Philips();
        double total = 0; int n = 0;
        foreach (var (x, values) in Across) { total += LensLattice.WindowPhase(lens, values, 3 * x, 440 + 56).Correlation; n++; }
        for (int i = 0; i < DownRows.Length; i++) { total += LensLattice.WindowPhase(lens, DownRows[i], 3 * 520, 440 + 56 + i).Correlation; n++; }
        double mean = total / n;
        Check(mean > 0.9, $"mean correlation over {n} windows is {mean:0.000}, expected over 0.9");
        double longRight = LensLattice.WindowPhase(lens, LongWindow, 3 * 200, 440 + 56).Correlation;
        double longWrong = LensLattice.WindowPhase(new LensModel(19, 4, 1, 2, 0), LongWindow, 3 * 200, 440 + 56).Correlation;
        Check(longRight > 0.85 && longRight > longWrong + 0.15, $"long window: 14/3 gives {longRight:0.000}, 19/4 gives {longWrong:0.000}");
    }

    [TestMethod]
    public void ViewCurve_FitsThePhaseSweepAcrossThePanel()
    {
        var lens = Philips();
        var xs = new List<double>();
        var phases = new List<double>();
        foreach (var (x, values) in Across)
        {
            xs.Add(x + 3); // window centre
            phases.Add(LensLattice.WindowPhase(lens, values, 3 * x, 440 + 56).Phase);
        }
        var unwrapped = ViewCurveFit.Unwrap(phases);
        for (int i = 1; i < unwrapped.Length; i++) Check(unwrapped[i] > unwrapped[i - 1], $"phase must rise with the column; step {i} is {unwrapped[i] - unwrapped[i - 1]:0.000}");
        Check(unwrapped[^1] - unwrapped[0] > 1.9 && unwrapped[^1] - unwrapped[0] < 2.4, $"total sweep {unwrapped[^1] - unwrapped[0]:0.00} cycles, measured 2.13");
        var fit = ViewCurveFit.Fit(xs, unwrapped, pixelPitchMm: 475.0 / 1680, distanceHintMm: 150, distanceToleranceMm: 30);
        Check(fit != null, "no fit");
        Check(fit!.ResidualCycles < 0.02, $"residual {fit.ResidualCycles:0.0000} cycles");
        Check(fit.CyclesPerRadian < 0, $"K {fit.CyclesPerRadian:0.00}: phase must fall as the camera moves right");
        Check(fit.FootX > 650 && fit.FootX < 900, $"foot at column {fit.FootX:0}, measured about 740");
        Check(fit.ZoneDegrees > 15 && fit.ZoneDegrees < 40, $"zone {fit.ZoneDegrees:0.0} degrees");
        double frac = fit.CenterPhase - Math.Floor(fit.CenterPhase);
        Check(frac > 0.1 && frac < 0.45, $"centre phase {frac:0.000}");
    }

    [TestMethod]
    public void ViewCurve_RecoversASyntheticCurveExactly()
    {
        // A curve generated from known geometry must come back out: distance 150, foot 740, K -2.1, centre 0.25 (+2 whole cycles).
        double pitch = 0.2827, d = 150, foot = 740, k = -2.1, center = 2.25;
        var xs = Enumerable.Range(0, 17).Select(i => 243.0 + 40 * i).ToList();
        var ph = xs.Select(x => center + k * Math.Atan((foot - x) * pitch / d)).ToList();
        var fit = ViewCurveFit.Fit(xs, ph, pitch, 150, 40)!;
        Check(Math.Abs(fit.DistanceMm - 150) <= 2 && Math.Abs(fit.FootX - 740) <= 5, $"distance {fit.DistanceMm}, foot {fit.FootX}");
        Check(Math.Abs(fit.CyclesPerRadian - k) < 0.1 && Math.Abs(fit.CenterPhase - center) < 0.03, $"K {fit.CyclesPerRadian:0.00}, centre {fit.CenterPhase:0.000}");
        Check(fit.ResidualCycles < 0.01, $"residual {fit.ResidualCycles}");
    }

    [TestMethod]
    public void ViewNumbering_RunsLeftToRightWithTheCameraAndTheLensInverting()
    {
        // A camera to the RIGHT sees a LOWER lens phase; view 0 is the farthest left, so the view grows as the phase falls.
        var lens = Philips(center: 0.25);
        // The sub-pixel of row 0 nearest each phase (a row holds only 14 of the 28 phases, so take the nearest).
        int Nearest(double phase)
        {
            int best = 0; double bestDistance = 1;
            for (int s = 0; s < 42; s++)
            {
                double dd = lens.Phase(s, 0) - phase;
                dd -= Math.Round(dd);
                if (Math.Abs(dd) < bestDistance) { bestDistance = Math.Abs(dd); best = s; }
            }
            return best;
        }
        int left = Nearest(0.25 + 0.4), right = Nearest(0.25 - 0.4), ahead = Nearest(0.25);
        Check(lens.ViewOf(left, 0, 10) <= 1, $"far-left phase gives view {lens.ViewOf(left, 0, 10)}, expected 0..1");
        Check(lens.ViewOf(right, 0, 10) >= 8, $"far-right phase gives view {lens.ViewOf(right, 0, 10)}, expected 8..9");
        // The phase straight ahead lands in the middle of the numbering.
        Check(lens.ViewOf(ahead, 0, 10) is >= 4 and <= 5, $"straight ahead gives view {lens.ViewOf(ahead, 0, 10)}, expected 4..5");
    }

    [TestMethod]
    public void ExportedPhilipsPatternParsesBackAsAFullCell()
    {
        var tile = Philips(center: 0.23).ToTile(8);
        var ini = PixelPatternExporter.ToIni(tile, "Philips 22-3D2N11/00");
        string Value(string key) => ini.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith(key + "=")).Substring(key.Length + 1);
        Check(Value("rows") == "28" && Value("cols") == "42" && Value("views") == "8", $"rows {Value("rows")} cols {Value("cols")} views {Value("views")}");
        var data = Value("data").Split(',').Select(int.Parse).ToArray();
        Check(data.Length == 28 * 42, $"{data.Length} values, expected {28 * 42}");
        Check(data.All(v => v is >= 0 and < 8), "view out of range");
        Check(Enumerable.Range(0, 8).All(v => data.Contains(v)), "not every view is used");
    }
}
