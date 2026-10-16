using Lenticulous.Scan;
using SpawnDev.UnitTesting;

namespace Lenticulous.Tests;

/// <summary>
/// The scan logic against a real pattern. Ground truth is TJ's 12x24, 8-view example from the 3DGM GMRender package; the camera is
/// simulated (see <see cref="ScanSimulator"/>), the logic under test is the production code: dimmest-target selection, coverage,
/// view clustering, ordering by camera position, redundant-scan detection and tile extraction.
/// </summary>
public class ScanLogicTests
{
    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static ScanSimulator Simulator(int seed = 1, double noise = 0.04, double sigma = 0.40)
        // 20 pixels wide (60 sub-pixels, 2.5 periods) by 30 rows (2.5 periods), offset on the panel so the tile has to be rolled back.
        => new(KnownPatterns.GMRenderExample, KnownPatterns.Cols, KnownPatterns.Rows, pixelWidth: 20, height: 30,
            originPixelX: 7, originY: 5, sigma: sigma, noise: noise, seed: seed);

    private static int WrongViews(ScanSimulator sim, ViewAssignment assignment, bool reversed = false)
    {
        int wrong = 0;
        for (int y = 0; y < sim.Height; y++)
            for (int sx = 0; sx < sim.SubWidth; sx++)
            {
                int truth = sim.TruthView(sx, y);
                int expected = reversed ? assignment.ViewCount - 1 - truth : truth;
                if (assignment.ViewOf[y * sim.SubWidth + sx] != expected) wrong++;
            }
        return wrong;
    }

    [TestMethod]
    public void KnownPattern_HasEightViewsAndIsPeriodic()
    {
        // The ground truth itself: a 12x24 cell, 8 views, and every view owns the same number of sub-pixels (36 of 288).
        Check(KnownPatterns.GMRenderExample.Length == KnownPatterns.Rows * KnownPatterns.Cols, "pattern size");
        var counts = new int[KnownPatterns.ViewCount];
        foreach (int v in KnownPatterns.GMRenderExample) counts[v]++;
        Check(counts.All(c => c == 36), $"expected 36 sub-pixels per view, got [{string.Join(",", counts)}]");
    }

    [TestMethod]
    public void ScanLoop_RecoversTheViewOfEverySubPixel()
    {
        var sim = Simulator();
        var set = sim.RunScanLoop();
        Check(set.NextTarget() == null, $"the loop stopped with sub-pixels still unseen (coverage {set.Coverage():P1})");
        Check(set.Count >= KnownPatterns.ViewCount, $"{set.Count} scans cannot cover {KnownPatterns.ViewCount} views");
        Check(set.Count <= 24, $"{set.Count} scans: the dimmest-first loop should cover 8 views in a handful of scans");
        var assignment = ViewAssigner.Assign(set);
        Check(assignment.ViewCount == KnownPatterns.ViewCount, $"found {assignment.ViewCount} views, the display has {KnownPatterns.ViewCount}");
        int wrong = WrongViews(sim, assignment);
        Check(wrong == 0, $"{wrong} of {sim.SubWidth * sim.Height} sub-pixels got the wrong view");
        Check(assignment.Balance >= 0.85, $"views should own equal numbers of sub-pixels, balance {assignment.Balance:0.00}");
        Console.WriteLine($"[Scan] {set.Count} scans, {assignment.ViewCount} views, all {sim.SubWidth * sim.Height} sub-pixels right, balance {assignment.Balance:0.00}");
    }

    [TestMethod]
    public void ScanLoop_ViewWidthComesFromCameraPositions()
    {
        // The camera travels one view width between neighbouring views: measured from the scans' positions, not told.
        var sim = Simulator();
        var assignment = ViewAssigner.Assign(sim.RunScanLoop());
        Check(Math.Abs(assignment.ViewWidth - 1.0) < 0.1, $"view width {assignment.ViewWidth:0.000}, expected 1.0 view width");
        for (int v = 1; v < assignment.ViewPosition.Length; v++)
            Check(assignment.ViewPosition[v] > assignment.ViewPosition[v - 1], "views must be ordered by camera position");
    }

    [TestMethod]
    public void ScanLoop_ReversedCameraAxisReversesTheNumbers()
    {
        // Views are numbered in ascending camera position. A camera whose axis runs the other way gets the mirrored numbering,
        // which the pattern's reverse flag exists for.
        var sim = Simulator(seed: 3);
        var assignment = ViewAssigner.Assign(sim.RunScanLoop(reverseAxis: true));
        Check(assignment.ViewCount == KnownPatterns.ViewCount, $"found {assignment.ViewCount} views");
        int wrong = WrongViews(sim, assignment, reversed: true);
        Check(wrong == 0, $"{wrong} sub-pixels not mirrored");
    }

    [TestMethod]
    public void ScanLoop_SurvivesHeavySensorNoise()
    {
        foreach (int seed in new[] { 11, 12, 13 })
        {
            var sim = Simulator(seed: seed, noise: 0.12);
            var set = sim.RunScanLoop();
            var assignment = ViewAssigner.Assign(set);
            Check(assignment.ViewCount == KnownPatterns.ViewCount, $"seed {seed}: found {assignment.ViewCount} views in noise");
            int wrong = WrongViews(sim, assignment);
            Check(wrong <= sim.SubWidth * sim.Height / 100, $"seed {seed}: {wrong} wrong sub-pixels in noise (more than 1%)");
        }
    }

    [TestMethod]
    public void RedundantScansAreReportedAndHarmless()
    {
        // The camera lands in the same viewing zone twice. The extra scans add nothing, are reported so they can be discarded, and
        // do not change the answer.
        var sim = Simulator(seed: 5);
        var baseline = ViewAssigner.Assign(sim.RunScanLoop());
        var set = sim.RunScanLoop();
        int before = set.Count;
        for (int v = 0; v < KnownPatterns.ViewCount; v++)
            set.Add(sim.Scan(v + 0.5 + 0.05));
        var withExtras = ViewAssigner.Assign(set);
        Check(withExtras.ViewCount == KnownPatterns.ViewCount, $"extra scans changed the view count to {withExtras.ViewCount}");
        Check(WrongViews(sim, withExtras) == 0, "extra scans changed the assignment");
        Check(withExtras.RedundantScans.Length >= KnownPatterns.ViewCount,
            $"{set.Count - before} duplicate scans added but only {withExtras.RedundantScans.Length} reported redundant");
        Check(baseline.RedundantScans.Length < withExtras.RedundantScans.Length, "adding duplicates must raise the redundant count");
    }

    [TestMethod]
    public void NextTarget_IsTheDimmestSubPixelAndStopsWhenCovered()
    {
        var sim = Simulator(seed: 7);
        var set = new ScanSet(sim.SubWidth, sim.Height);
        var first = set.NextTarget();
        Check(first == new SubPixelTarget(0, 0), "with no scans the loop starts at the top-left sub-pixel");
        int firstView = sim.TruthView(0, 0);
        set.Add(sim.Scan(firstView + 0.5));
        var next = set.NextTarget();
        Check(next != null, "after one scan most sub-pixels are still unseen");
        Check(sim.TruthView(next!.Value.SubX, next.Value.Y) != firstView, "the next target must be a sub-pixel the first scan did NOT see bright");
        double covered = set.Coverage();
        Check(covered > 0.05 && covered < 0.30, $"one scan of an 8-view display should cover about 1/8, covered {covered:P1}");
        while (set.NextTarget() is { } t) set.Add(sim.Scan(sim.TruthView(t.SubX, t.Y) + 0.5));
        Check(set.Coverage() >= 0.999, $"loop ended at coverage {set.Coverage():P2}");
    }

    [TestMethod]
    public void TileExtraction_RebuildsThePatternOnThePanelGrid()
    {
        var sim = Simulator(seed: 9);
        var assignment = ViewAssigner.Assign(sim.RunScanLoop());
        var tile = PixelPatternTile.Extract(assignment.ViewOf, sim.SubWidth, sim.Height, assignment.ViewCount,
            originSubX: 3 * sim.OriginPixelX, originY: sim.OriginY);
        Check(tile != null, "no repeating cell found in a region of 2.5 periods");
        Check(tile!.Cols == KnownPatterns.Cols && tile.Rows == KnownPatterns.Rows, $"cell {tile.Cols}x{tile.Rows}, expected {KnownPatterns.Cols}x{KnownPatterns.Rows}");
        for (int i = 0; i < tile.Views.Length; i++)
            Check(tile.Views[i] == KnownPatterns.GMRenderExample[i], $"cell {i} is view {tile.Views[i]}, expected {KnownPatterns.GMRenderExample[i]}");
        Check(tile.Agreement >= 0.99, $"agreement {tile.Agreement:0.000}");
    }

    /// <summary>Reads a pixel pattern ini the way RendererPixelPatternBase does (CSV data, rows, cols, format, reverse, views).</summary>
    private static (byte[] Data, int Rows, int Cols, string Format, bool Reverse, int Views, int ViewCountFromData, bool Available) ParseLikeTheRenderer(string ini)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('[')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0) values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        var data = values["data"].Trim(',').Split(',').Select(t => byte.Parse(t.Trim())).ToArray();
        int rows = int.Parse(values["rows"]), cols = int.Parse(values["cols"]);
        int views = values.TryGetValue("views", out var v) ? int.Parse(v) : 0;
        int viewCount = Math.Max(views, data.Max() + 1);
        bool reverse = values.TryGetValue("reverse", out var r) && (r == "1" || r.Equals("true", StringComparison.OrdinalIgnoreCase));
        return (data, rows, cols, values.GetValueOrDefault("format", "bgr"), reverse, views, viewCount, data.Length == cols * rows && viewCount > 0);
    }

    [TestMethod]
    public void Export_WritesWhatTheRendererReads()
    {
        var sim = Simulator(seed: 4);
        var assignment = ViewAssigner.Assign(sim.RunScanLoop());
        var tile = PixelPatternTile.Extract(assignment.ViewOf, sim.SubWidth, sim.Height, assignment.ViewCount,
            3 * sim.OriginPixelX, sim.OriginY)!;
        var parsed = ParseLikeTheRenderer(PixelPatternExporter.ToIni(tile, "Philips 22-3D2N11"));
        Check(parsed.Available, "the renderer would reject the file: data.Length != rows * cols or no views");
        Check(parsed.Rows == KnownPatterns.Rows && parsed.Cols == KnownPatterns.Cols, $"cell {parsed.Cols}x{parsed.Rows} in the file");
        Check(parsed.Format == "rgb", $"format {parsed.Format}: the scan grid is in colour order");
        Check(parsed.ViewCountFromData == KnownPatterns.ViewCount, $"renderer would see {parsed.ViewCountFromData} views");
        Check(!parsed.Reverse, "reverse must default to 0");
        for (int i = 0; i < parsed.Data.Length; i++)
            Check(parsed.Data[i] == KnownPatterns.GMRenderExample[i], $"data[{i}] = {parsed.Data[i]}, the display's pattern has {KnownPatterns.GMRenderExample[i]}");
        Check(ParseLikeTheRenderer(PixelPatternExporter.ToIni(tile, "x", reverse: true)).Reverse, "reverse=1 was not written");
        Check(PixelPatternExporter.ToIni(tile, "Philips 22-3D2N11").StartsWith("name=Philips 22-3D2N11"), "name line");
    }

    [TestMethod]
    public void FrameDifference_FindsASingleLitSubPixelInNoise()
    {
        // 1280x720 RGB, sensor noise sigma 2 on both frames (their difference reaches about +-12 over 2.7M samples), and ONE red
        // sample that lights up by 30 (about 10 sigma of the difference): where is it, and how clear is it?
        const int w = 1280, h = 720;
        var rng = new Random(21);
        float Gauss() => (float)((rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 2.0);
        var dark = new float[w * h * 3];
        var lit = new float[w * h * 3];
        for (int i = 0; i < dark.Length; i++) { dark[i] = 20 + 2 * Gauss(); lit[i] = 20 + 2 * Gauss(); }
        int sx = 777, sy = 333;
        lit[(sy * w + sx) * 3 + 0] += 30; // channel 0 = the first sample of the pixel
        var report = FrameDifference.Analyze(dark, lit, w, h);
        var top = report.Strongest!.Value;
        Check(top.X == sx && top.Y == sy && top.Channel == 0, $"found ({top.X},{top.Y}) channel {top.Channel}, the light is at ({sx},{sy}) channel 0");
        Check(report.NoiseStd > 1.5f && report.NoiseStd < 4.0f, $"noise estimate {report.NoiseStd:0.00}, the noise is about 2.8 (two frames of sigma 2)");
        Check(report.SignalToNoise > 7f && report.SignalToNoise < 15f, $"signal to noise {report.SignalToNoise:0.0}, expected about 30 / 2.8 = 10.6");
        // Auto-exposure: the whole lit frame is 7 levels brighter. That is not a light source; the spike must still be the peak and its
        // height must be measured above the shift (30, not 37).
        var shifted = lit.Select(v => v + 7f).ToArray();
        var shiftedReport = FrameDifference.Analyze(dark, shifted, w, h);
        var shiftedTop = shiftedReport.Strongest!.Value;
        Check(shiftedTop.X == sx && shiftedTop.Y == sy, $"a global brightness shift moved the peak to ({shiftedTop.X},{shiftedTop.Y})");
        Check(Math.Abs(shiftedTop.Value - top.Value) < 1.5f, $"peak {shiftedTop.Value:0.0} with a global shift, {top.Value:0.0} without: the shift leaked into the signal");
        // Nothing lit: the strongest 'peak' must be noise-sized, not a signal.
        var none = FrameDifference.Analyze(dark, dark.Select(v => v).ToArray(), w, h);
        Check(none.Peaks.Count == 0 && none.SignalToNoise == 0, "identical frames must report no peak");
    }

    /// <summary>A real projective map (a slightly rotated, tilted view of a screen) used as ground truth.</summary>
    private static (double X, double Y) TrueStimulusToCamera(double x, double y)
    {
        double w = 1 + 0.00004 * x + 0.00009 * y;
        return ((0.82 * x - 0.06 * y + 410) / w, (0.05 * x + 0.78 * y + 95) / w);
    }

    [TestMethod]
    public void Homography_RecoversAProjectiveMapAndItsInverse()
    {
        // 3x3 grid of stimulus positions across a 1664x872 window, mapped by a known projective transform.
        var from = new List<(double X, double Y)>();
        foreach (var y in new[] { 150.0, 436.0, 722.0 })
            foreach (var x in new[] { 200.0, 832.0, 1460.0 })
                from.Add((x, y));
        var to = from.Select(p => TrueStimulusToCamera(p.X, p.Y)).ToList();
        var fit = Homography.Fit(from, to);
        Check(fit.RmsError(from, to) < 1e-6, $"noise-free fit residual {fit.RmsError(from, to):0.000000} px");
        // And it must hold between the grid points, not just at them.
        foreach (var (x, y) in new[] { (50.0, 40.0), (1000.0, 600.0), (1600.0, 840.0), (333.3, 777.7) })
        {
            var expected = TrueStimulusToCamera(x, y);
            var got = fit.Map(x, y);
            Check(Math.Abs(got.X - expected.X) < 1e-4 && Math.Abs(got.Y - expected.Y) < 1e-4,
                $"({x},{y}) maps to ({got.X:0.000},{got.Y:0.000}), the truth is ({expected.X:0.000},{expected.Y:0.000})");
        }
        var back = fit.Inverse().Map(fit.Map(1000, 600).X, fit.Map(1000, 600).Y);
        Check(Math.Abs(back.X - 1000) < 1e-4 && Math.Abs(back.Y - 600) < 1e-4, $"inverse gives ({back.X:0.0000},{back.Y:0.0000})");
    }

    [TestMethod]
    public void Homography_FitIsStableWithMeasurementNoise()
    {
        // Blob centroids are measured to about 0.3 px. The fitted map must predict a held-out position to well under a pixel.
        var rng = new Random(5);
        var from = new List<(double X, double Y)>();
        foreach (var y in new[] { 150.0, 436.0, 722.0 })
            foreach (var x in new[] { 200.0, 832.0, 1460.0 })
                from.Add((x, y));
        var to = from.Select(p => { var t = TrueStimulusToCamera(p.X, p.Y); return (t.X + 0.3 * (rng.NextDouble() * 2 - 1), t.Y + 0.3 * (rng.NextDouble() * 2 - 1)); }).ToList();
        var fit = Homography.Fit(from, to);
        double worst = 0;
        foreach (var (x, y) in new[] { (400.0, 300.0), (900.0, 500.0), (1300.0, 650.0), (700.0, 200.0) })
        {
            var expected = TrueStimulusToCamera(x, y);
            var got = fit.Map(x, y);
            worst = Math.Max(worst, Math.Sqrt((got.X - expected.X) * (got.X - expected.X) + (got.Y - expected.Y) * (got.Y - expected.Y)));
        }
        Check(worst < 0.6, $"held-out prediction off by {worst:0.00} px with 0.3 px measurement noise");
        Check(fit.RmsError(from, to) < 0.5, $"fit residual {fit.RmsError(from, to):0.00} px");
    }

    [TestMethod]
    public void AnalyzeRegion_RanksTheTargetAgainstTheRestOfTheFrame()
    {
        // Noise with STRUCTURE: a few places in the frame flicker (codec artefacts), +20 in both runs. A light of +14 at the predicted
        // spot is below those flickers, so a sigma threshold would be fooled; the rank says how many places beat it.
        const int w = 640, h = 360;
        var rng = new Random(31);
        var dark = new float[w * h * 3];
        var lit = new float[w * h * 3];
        for (int i = 0; i < dark.Length; i++) { dark[i] = 50 + (float)(rng.NextDouble() * 2 - 1) * 2f; lit[i] = 50 + (float)(rng.NextDouble() * 2 - 1) * 2f; }
        foreach (var (fx, fy) in new[] { (100, 60), (300, 200), (500, 80), (150, 300) }) lit[(fy * w + fx) * 3 + 2] += 30; // flickers
        int tx = 320, ty = 180;
        // The target: a 3x3 block of red at +14, clearly weaker than the flickers (a lone pixel's light would drown in the region's
        // summed noise, which is why the sums are checked on a block).
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++) lit[((ty + dy) * w + tx + dx) * 3 + 0] += 14;
        var onTarget = FrameDifference.AnalyzeRegion(dark, lit, w, h, 3, tx + 0.5, ty + 0.5, radius: 5);
        Check(Math.Abs(onTarget.RoiPeak.X - tx) <= 1 && Math.Abs(onTarget.RoiPeak.Y - ty) <= 1 && onTarget.RoiPeak.Channel == 0, $"region peak at ({onTarget.RoiPeak.X},{onTarget.RoiPeak.Y}) channel {onTarget.RoiPeak.Channel}");
        Check(onTarget.WindowsAtLeastAsBright == 4, $"{onTarget.WindowsAtLeastAsBright} windows beat the target, the 4 flickers should");
        Check(onTarget.WindowsTotal > 1000, $"only {onTarget.WindowsTotal} comparison windows");
        // Looking in the WRONG place (no light there) the region peak is noise and many windows match it: the rank must say 'not found'.
        var wrong = FrameDifference.AnalyzeRegion(dark, lit, w, h, 3, 100.5, 250.5, radius: 5);
        Check(wrong.WindowsAtLeastAsBright > 50, $"an empty region was beaten by only {wrong.WindowsAtLeastAsBright} windows: the rank would call noise a detection");
        // The light the target added shows up in its own channel only.
        Check(onTarget.ChannelSum[0] > 90 && Math.Abs(onTarget.ChannelSum[1]) < 50 && Math.Abs(onTarget.ChannelSum[2]) < 50, $"channel sums {string.Join(",", onTarget.ChannelSum.Select(v => v.ToString("0.0")))}");
    }

    [TestMethod]
    public void RegionChannelSums_MeasureTheAddedLightAndIgnoreTheGlobalShift()
    {
        const int w = 200, h = 100;
        var rng = new Random(5);
        var dark = new float[w * h * 3];
        var lit = new float[w * h * 3];
        for (int i = 0; i < dark.Length; i++) { dark[i] = 40 + (float)rng.NextDouble(); lit[i] = dark[i] + 3f; } // auto-exposure: +3 everywhere
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++) lit[((50 + dy) * w + 120 + dx) * 3 + 1] += 25; // green light, 9 px x 25
        var sums = FrameDifference.RegionChannelSums(dark, lit, w, h, 3, 120.5, 50.5, 4);
        Check(Math.Abs(sums[1] - 225) < 5, $"green sum {sums[1]:0.0}, expected 225");
        Check(Math.Abs(sums[0]) < 5 && Math.Abs(sums[2]) < 5, $"other channels {sums[0]:0.0}/{sums[2]:0.0}, expected 0 (the +3 shift removed)");
    }

    // The brightness a camera would read at each marker block for a sequence number, with its own black/white level and noise.
    private static double[] MarkerReading(int sequence, double black, double white, double noise, Random rng)
    {
        var bits = SyncMarker.Bits(sequence);
        var reading = new double[SyncMarker.BlockCount];
        reading[0] = black + (rng.NextDouble() * 2 - 1) * noise;
        reading[1] = white + (rng.NextDouble() * 2 - 1) * noise;
        for (int i = 0; i < SyncMarker.BitCount; i++) reading[2 + i] = (bits[i] ? white : black) + (rng.NextDouble() * 2 - 1) * noise;
        return reading;
    }

    [TestMethod]
    public void SyncMarker_EverySequenceNumberRoundTripsAtAnyExposure()
    {
        var rng = new Random(9);
        foreach (var (black, white) in new[] { (5.0, 250.0), (30.0, 120.0), (60.0, 130.0) })
            for (int n = 1; n <= SyncMarker.MaxSequence; n++)
            {
                var got = SyncMarker.Decode(MarkerReading(n, black, white, 8, rng));
                Check(got == n, $"sequence {n} at black {black} white {white} decoded as {got?.ToString() ?? "null"}");
            }
    }

    [TestMethod]
    public void SyncMarker_RefusesWhatIsNotAMarker()
    {
        var rng = new Random(4);
        // A blank screen: both references black. Nothing in view: no contrast. A frame caught half way: a bit mid-grey.
        Check(SyncMarker.Decode(new double[SyncMarker.BlockCount]) == null, "a blank picture decoded as a number");
        Check(SyncMarker.Decode(Enumerable.Repeat(100.0, SyncMarker.BlockCount).ToArray()) == null, "a flat picture decoded as a number");
        var half = MarkerReading(77, 10, 240, 2, rng);
        half[5] = 125; // a bit caught half way between black and white
        Check(SyncMarker.Decode(half) == null, "a half-way bit decoded as a number");
        var allBlack = MarkerReading(1, 10, 240, 2, rng);
        for (int i = 2; i < allBlack.Length; i++) allBlack[i] = 10;
        Check(SyncMarker.Decode(allBlack) == null, "all-black bits decoded as a number (that is sequence 0, which is not used)");
        // And a different number must never be read as this one: flip one bit and the decode must differ.
        var a = SyncMarker.Decode(MarkerReading(100, 10, 240, 2, rng));
        var b = SyncMarker.Decode(MarkerReading(101, 10, 240, 2, rng));
        Check(a == 100 && b == 101, $"neighbouring numbers decoded as {a} and {b}");
    }

    [TestMethod]
    public void BlobCentroid_FindsAPatchAndItsCentre()
    {
        // A soft 17x17-camera-pixel patch centred at (429.3, 182.6) on top of sensor noise; the other frame is a second dark capture.
        const int w = 1280, h = 720;
        var rng = new Random(8);
        var dark = new float[w * h * 3];
        var lit = new float[w * h * 3];
        for (int i = 0; i < dark.Length; i++) { dark[i] = 60 + (float)(rng.NextDouble() * 2 - 1) * 2f; lit[i] = 60 + (float)(rng.NextDouble() * 2 - 1) * 2f; }
        double cx = 429.3, cy = 182.6;
        for (int y = 160; y < 205; y++)
            for (int x = 405; x < 455; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double inside = Math.Exp(-(dx * dx + dy * dy) / (2 * 6.0 * 6.0)); // soft edge
                for (int c = 0; c < 3; c++) lit[(y * w + x) * 3 + c] += (float)(190 * inside);
            }
        var blob = FrameDifference.BlobCentroid(dark, lit, w, h);
        Check(blob != null, "no patch found");
        Check(Math.Abs(blob!.Value.X - cx) < 0.4 && Math.Abs(blob.Value.Y - cy) < 0.4, $"centre ({blob.Value.X:0.00},{blob.Value.Y:0.00}), truth ({cx},{cy})");
        Check(blob.Value.Peak > 400, $"peak {blob.Value.Peak:0}");
        Check(FrameDifference.BlobCentroid(dark, dark, w, h) == null, "identical frames must find no patch");
    }

    [TestMethod]
    public void TileExtraction_RejectsARegionWithNoRepeat()
    {
        // Random views do not repeat, so no small cell may be reported.
        var rng = new Random(2);
        int subWidth = 60, height = 30;
        var views = Enumerable.Range(0, subWidth * height).Select(_ => rng.Next(8)).ToArray();
        Check(PixelPatternTile.Extract(views, subWidth, height, 8) == null, "a random region must not produce a tile");
    }
}
