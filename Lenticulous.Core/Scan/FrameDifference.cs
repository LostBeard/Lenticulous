namespace Lenticulous.Scan;

/// <summary>One local maximum in a frame difference: where, in which colour channel, and by how much.</summary>
public readonly record struct DifferencePeak(int X, int Y, int Channel, float Value);

/// <summary>What changed between a dark frame and a frame with something lit.</summary>
/// <param name="NoiseStd">Per-sample standard deviation of the difference away from the signal (robust: from the median absolute
/// deviation, so a bright spike does not inflate it).</param>
/// <param name="Peaks">The strongest local maxima, strongest first.</param>
/// <param name="SignalToNoise">The strongest peak over <paramref name="NoiseStd"/>.</param>
public sealed record DifferenceReport(float NoiseStd, IReadOnlyList<DifferencePeak> Peaks, float SignalToNoise)
{
    public DifferencePeak? Strongest => Peaks.Count == 0 ? null : Peaks[0];
}

/// <summary>
/// Finds the light that appeared between two (averaged) camera frames. Answers the question the whole technique rests on: does a
/// single lit sub-pixel stand out of the camera's noise, and where in the picture is it?
/// </summary>
public static class FrameDifference
{
    /// <param name="dark">The frame with nothing lit, interleaved channels, <c>width * height * channels</c> values.</param>
    /// <param name="lit">The frame with the target lit, same layout.</param>
    /// <param name="channels">Interleaved channels per pixel (3 for RGB).</param>
    /// <param name="maxPeaks">How many peaks to report.</param>
    /// <param name="exclusion">Pixels around a reported peak that cannot hold another one.</param>
    public static DifferenceReport Analyze(float[] dark, float[] lit, int width, int height, int channels = 3, int maxPeaks = 5, int exclusion = 6)
    {
        int n = width * height * channels;
        if (dark.Length != n || lit.Length != n)
            throw new ArgumentException($"frames must be {width}x{height}x{channels} = {n} values, got {dark.Length} and {lit.Length}");

        var diff = new float[n];
        for (int i = 0; i < n; i++) diff[i] = lit[i] - dark[i];

        // The camera's auto-exposure can shift the whole picture a little between the two frames. That is not signal: measure
        // everything relative to the difference's own median (almost every sample is unlit, so the median is the global shift).
        float median = Median(diff);
        for (int i = 0; i < n; i++) diff[i] -= median;

        // Noise from the median absolute deviation: 1.4826 x MAD is the standard deviation of a Gaussian, and unlike the plain
        // standard deviation a handful of bright samples cannot move it.
        var deviations = new float[n];
        for (int i = 0; i < n; i++) deviations[i] = Math.Abs(diff[i]);
        float noise = 1.4826f * Median(deviations);

        // Peaks: take the strongest sample, report it, blank its neighbourhood, repeat. A working copy keeps `diff` intact.
        var work = (float[])diff.Clone();
        var peaks = new List<DifferencePeak>();
        for (int p = 0; p < maxPeaks; p++)
        {
            int best = 0;
            for (int i = 1; i < n; i++) if (work[i] > work[best]) best = i;
            if (work[best] <= 0) break;
            int channel = best % channels;
            int pixel = best / channels;
            int x = pixel % width, y = pixel / width;
            peaks.Add(new DifferencePeak(x, y, channel, diff[best]));
            for (int dy = -exclusion; dy <= exclusion; dy++)
                for (int dx = -exclusion; dx <= exclusion; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || px >= width || py < 0 || py >= height) continue;
                    for (int c = 0; c < channels; c++) work[(py * width + px) * channels + c] = float.MinValue;
                }
        }

        float snr = peaks.Count == 0 || noise <= 0 ? 0 : peaks[0].Value / noise;
        return new DifferenceReport(noise, peaks, snr);
    }

    /// <summary>The result of looking for light in ONE place of a frame difference, and how that compares with everywhere else.</summary>
    /// <param name="RoiPeak">The brightest sample inside the region (where, which colour channel, how much).</param>
    /// <param name="ChannelSum">Total change per colour channel inside the region (the light the target added).</param>
    /// <param name="NoiseStd">Robust per-sample noise of the whole difference.</param>
    /// <param name="WindowsAtLeastAsBright">How many other windows of the same size, anywhere in the frame, have a sample at least as
    /// bright as the region's peak. 0 means the region is the brightest place in the picture.</param>
    /// <param name="WindowsTotal">How many other windows were compared.</param>
    public sealed record RegionResult(DifferencePeak RoiPeak, float[] ChannelSum, float NoiseStd, int WindowsAtLeastAsBright, int WindowsTotal);

    /// <summary>
    /// Is there light where a calibration says the target should be? The peak inside a (2r+1)-pixel square around the predicted camera
    /// position, ranked against every other non-overlapping square in the frame. The camera's noise has structure (compression
    /// artefacts, flicker), so the honest question is not "how many sigma" but "how many other places look this bright": the empirical
    /// false-alarm count.
    /// </summary>
    public static RegionResult AnalyzeRegion(float[] dark, float[] lit, int width, int height, int channels, double centreX, double centreY, int radius)
    {
        int n = width * height * channels;
        if (dark.Length != n || lit.Length != n) throw new ArgumentException($"frames must be {width}x{height}x{channels}");
        var diff = new float[n];
        for (int i = 0; i < n; i++) diff[i] = lit[i] - dark[i];
        float median = Median(diff);
        for (int i = 0; i < n; i++) diff[i] -= median;
        var abs = new float[n];
        for (int i = 0; i < n; i++) abs[i] = Math.Abs(diff[i]);
        float noise = 1.4826f * Median(abs);

        int cx = (int)Math.Floor(centreX), cy = (int)Math.Floor(centreY);
        float roiBest = float.MinValue;
        int bx = cx, by = cy, bc = 0;
        var sums = new float[channels];
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(height - 1, cy + radius); y++)
            for (int x = Math.Max(0, cx - radius); x <= Math.Min(width - 1, cx + radius); x++)
                for (int c = 0; c < channels; c++)
                {
                    float v = diff[(y * width + x) * channels + c];
                    sums[c] += v;
                    if (v > roiBest) { roiBest = v; bx = x; by = y; bc = c; }
                }

        int size = 2 * radius + 1, total = 0, atLeast = 0;
        for (int wy = 0; wy + size <= height; wy += size)
            for (int wx = 0; wx + size <= width; wx += size)
            {
                // Skip the window the region itself falls in (and its neighbours): it would count the signal against itself.
                if (Math.Abs(wx + radius - cx) <= size && Math.Abs(wy + radius - cy) <= size) continue;
                float best = float.MinValue;
                for (int y = wy; y < wy + size; y++)
                    for (int x = wx; x < wx + size; x++)
                        for (int c = 0; c < channels; c++)
                            if (diff[(y * width + x) * channels + c] > best) best = diff[(y * width + x) * channels + c];
                total++;
                if (best >= roiBest) atLeast++;
            }
        return new RegionResult(new DifferencePeak(bx, by, bc, roiBest), sums, noise, atLeast, total);
    }

    /// <summary>A bright patch in a frame difference: its centre of light, its strength and its extent in camera pixels.</summary>
    public readonly record struct Blob(double X, double Y, double Peak, int Pixels, int Width, int Height);

    /// <summary>
    /// The one bright patch that appeared between two frames: the region of the all-channel difference above half its maximum,
    /// grown from the brightest pixel, and its centre of light. A white patch of known size at a known stimulus position gives one
    /// stimulus-to-camera correspondence this way.
    /// </summary>
    /// <returns>The patch, or null when nothing rises clearly above the noise (the maximum is under <paramref name="minPeak"/>).</returns>
    public static Blob? BlobCentroid(float[] dark, float[] lit, int width, int height, int channels = 3, float minPeak = 30f)
    {
        int count = width * height;
        if (dark.Length != count * channels || lit.Length != count * channels)
            throw new ArgumentException($"frames must be {width}x{height}x{channels}");
        // Brightness change per pixel: all channels summed, less the global shift (the median).
        var diff = new float[count];
        for (int p = 0; p < count; p++)
        {
            float d = 0;
            for (int c = 0; c < channels; c++) d += lit[p * channels + c] - dark[p * channels + c];
            diff[p] = d;
        }
        float shift = Median(diff);
        int best = 0;
        for (int p = 0; p < count; p++)
        {
            diff[p] -= shift;
            if (diff[p] > diff[best]) best = p;
        }
        float peak = diff[best];
        if (peak < minPeak) return null;

        // Grow the region of pixels above half the peak, 8-connected, from the brightest one.
        float threshold = peak * 0.5f;
        var seen = new bool[count];
        var stack = new Stack<int>();
        stack.Push(best);
        seen[best] = true;
        double sumW = 0, sumX = 0, sumY = 0;
        int pixels = 0, minX = width, maxX = -1, minY = height, maxY = -1;
        while (stack.Count > 0)
        {
            int p = stack.Pop();
            int x = p % width, y = p / width;
            float w = diff[p] - threshold + 1; // weight: how far above the cut-off
            sumW += w; sumX += w * (x + 0.5); sumY += w * (y + 0.5);
            pixels++;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                    int q = ny * width + nx;
                    if (seen[q] || diff[q] < threshold) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
        }
        return new Blob(sumX / sumW, sumY / sumW, peak, pixels, maxX - minX + 1, maxY - minY + 1);
    }

    private static float Median(float[] values)
    {
        var copy = (float[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }
}
