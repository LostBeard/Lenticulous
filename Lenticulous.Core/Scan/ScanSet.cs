namespace Lenticulous.Scan;

/// <summary>A sub-pixel to light next: column in sub-pixels (physical left to right) and row.</summary>
public readonly record struct SubPixelTarget(int SubX, int Y)
{
    /// <summary>The pixel column the sub-pixel belongs to, and which of its three it is (0 = leftmost).</summary>
    public int PixelX => SubX / 3;
    public int Channel => SubX % 3;
}

/// <summary>
/// The scans taken so far of one region, and the two decisions the scan loop makes from them: which sub-pixel to aim the camera
/// at next, and whether enough has been seen.
/// </summary>
public sealed class ScanSet
{
    private readonly List<ScanRecord> _scans = new();
    private readonly float[] _best;

    public ScanSet(int subWidth, int height)
    {
        SubWidth = subWidth;
        Height = height;
        _best = new float[subWidth * height];
    }

    public int SubWidth { get; }
    public int Height { get; }
    public IReadOnlyList<ScanRecord> Scans => _scans;
    public int Count => _scans.Count;

    /// <summary>The highest normalised brightness any scan has seen each sub-pixel at (0 for a sub-pixel never seen bright).</summary>
    public IReadOnlyList<float> BestSeen => _best;

    public void Add(ScanRecord scan)
    {
        if (scan.SubWidth != SubWidth || scan.Height != Height)
            throw new ArgumentException($"scan is {scan.SubWidth}x{scan.Height}, the set is {SubWidth}x{Height}", nameof(scan));
        _scans.Add(scan);
        for (int i = 0; i < _best.Length; i++)
            if (scan.Normalized[i] > _best[i]) _best[i] = scan.Normalized[i];
    }

    /// <summary>
    /// The dimmest sub-pixel across all scans, the one least represented so far: light it alone, move the camera until it looks
    /// as bright as possible, and the next scan will cover its view. Null once every sub-pixel has been seen at or above
    /// <paramref name="satisfied"/>. The very first call (no scans) starts at the top-left sub-pixel.
    /// </summary>
    public SubPixelTarget? NextTarget(float satisfied = 0.6f)
    {
        int dimmest = -1;
        float dimmestValue = float.MaxValue;
        for (int i = 0; i < _best.Length; i++)
        {
            if (_best[i] >= satisfied) continue;
            if (_best[i] < dimmestValue)
            {
                dimmestValue = _best[i];
                dimmest = i;
            }
        }
        return dimmest < 0 ? null : new SubPixelTarget(dimmest % SubWidth, dimmest / SubWidth);
    }

    /// <summary>Fraction of sub-pixels some scan has seen at or above <paramref name="threshold"/>.</summary>
    public double Coverage(float threshold = 0.6f)
    {
        int covered = 0;
        for (int i = 0; i < _best.Length; i++)
            if (_best[i] >= threshold) covered++;
        return (double)covered / _best.Length;
    }
}
