namespace Lenticulous.Scan;

/// <summary>
/// One finished scan: how bright every sub-pixel of the scan region looked to the camera from ONE camera position.
/// </summary>
/// <remarks>
/// The region is <c>SubWidth</c> sub-pixel columns by <c>Height</c> rows, row-major. A pixel is three consecutive columns, in COLOUR
/// order (0 = red, 1 = green, 2 = blue): the stimulus lights one colour channel of one pixel at a time, so that is how a sub-pixel is
/// addressed, and it is the order the 3DGM pattern files use (<c>format=rgb</c>), whatever the panel's physical stripe order. Raw
/// camera values are normalised per scan, because a scan sees only the sub-pixels of the view the
/// camera sits in as bright: the floor is the 5th percentile (the many dark sub-pixels) and the peak the 99th (the bright ones),
/// so exposure, distance and room light cancel out and scans from different positions compare.
/// </remarks>
public sealed class ScanRecord
{
    /// <param name="subWidth">Sub-pixel columns in the region (3 x its width in pixels).</param>
    /// <param name="height">Rows in the region.</param>
    /// <param name="cameraPosition">Horizontal camera position for this scan, in any consistent unit (rig units, millimetres,
    /// marker offset). Only the ORDER and the spacing matter.</param>
    /// <param name="intensity">Raw brightness per sub-pixel, row-major, <c>subWidth * height</c> values.</param>
    public ScanRecord(int subWidth, int height, double cameraPosition, float[] intensity)
    {
        if (intensity.Length != subWidth * height)
            throw new ArgumentException($"expected {subWidth * height} values for a {subWidth}x{height} region, got {intensity.Length}", nameof(intensity));
        SubWidth = subWidth;
        Height = height;
        CameraPosition = cameraPosition;
        Normalized = Normalize(intensity);
    }

    public int SubWidth { get; }
    public int Height { get; }
    public double CameraPosition { get; }

    /// <summary>Brightness per sub-pixel, 0 (dark) to 1 (as bright as this scan gets), row-major.</summary>
    public float[] Normalized { get; }

    public int Count => Normalized.Length;

    private static float[] Normalize(float[] raw)
    {
        var sorted = (float[])raw.Clone();
        Array.Sort(sorted);
        float floor = Percentile(sorted, 0.05);
        float peak = Percentile(sorted, 0.99);
        var result = new float[raw.Length];
        float range = peak - floor;
        if (range < 1e-6f) return result; // a scan with no contrast says nothing
        for (int i = 0; i < raw.Length; i++)
            result[i] = Math.Clamp((raw[i] - floor) / range, 0f, 1f);
        return result;
    }

    private static float Percentile(float[] sorted, double p)
        => sorted[Math.Clamp((int)Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
}
