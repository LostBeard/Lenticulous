using Lenticulous.Scan;

namespace Lenticulous.Tests;

/// <summary>
/// Plays the physical part of a scan so the logic can be tested on desktop: a lenticular display (a known pattern tiled over the panel)
/// seen by a camera that sits at some horizontal position. A sub-pixel looks bright when the camera is in ITS view's viewing zone, and
/// falls off smoothly into the neighbouring views, with a different exposure every scan and sensor noise on top. View <c>k</c> is
/// brightest at camera position <c>k + 0.5</c>, so the camera axis is measured in view widths.
/// </summary>
internal sealed class ScanSimulator
{
    public ScanSimulator(int[] tile, int cols, int rows, int pixelWidth, int height, int originPixelX, int originY,
        double sigma = 0.40, double noise = 0.04, int seed = 1)
    {
        Tile = tile; Cols = cols; Rows = rows;
        PixelWidth = pixelWidth; Height = height; OriginPixelX = originPixelX; OriginY = originY;
        Sigma = sigma; Noise = noise;
        Rng = new Random(seed);
    }

    public int[] Tile { get; }
    public int Cols { get; }
    public int Rows { get; }
    public int PixelWidth { get; }
    public int Height { get; }
    public int OriginPixelX { get; }
    public int OriginY { get; }
    public double Sigma { get; }
    public double Noise { get; }
    public Random Rng { get; }

    public int SubWidth => 3 * PixelWidth;

    /// <summary>The true view of a sub-pixel of the scan region, with the tile aligned to the PANEL (the region starts at an offset).</summary>
    public int TruthView(int subX, int y)
        => Tile[((OriginY + y) % Rows) * Cols + ((3 * OriginPixelX + subX) % Cols)];

    /// <summary>
    /// One scan with the camera at <paramref name="position"/> (in view widths). <paramref name="mirroredAxis"/> models a rig whose
    /// position axis runs the other way: the camera sees exactly the same thing, but the position it REPORTS is negated.
    /// </summary>
    public ScanRecord Scan(double position, bool mirroredAxis = false)
    {
        double gain = 0.7 + 0.6 * Rng.NextDouble(); // exposure differs scan to scan
        var raw = new float[SubWidth * Height];
        for (int y = 0; y < Height; y++)
            for (int sx = 0; sx < SubWidth; sx++)
            {
                double d = position - (TruthView(sx, y) + 0.5);
                double response = Math.Exp(-d * d / (2 * Sigma * Sigma));
                double sensorNoise = Noise * (Rng.NextDouble() + Rng.NextDouble() + Rng.NextDouble() - 1.5) * 1.55;
                raw[y * SubWidth + sx] = (float)(gain * (30 + 200 * (response + sensorNoise)));
            }
        return new ScanRecord(SubWidth, Height, mirroredAxis ? -position : position, raw);
    }

    /// <summary>
    /// The scan loop as the technique describes it: find the dimmest sub-pixel, aim the camera where it is brightest (the camera lands
    /// near, not exactly on, the best position), scan, and repeat until every sub-pixel has been seen bright.
    /// </summary>
    public ScanSet RunScanLoop(int maxScans = 60, double aimJitter = 0.08, bool reverseAxis = false)
    {
        var set = new ScanSet(SubWidth, Height);
        while (set.Count < maxScans && set.NextTarget() is { } target)
        {
            double position = TruthView(target.SubX, target.Y) + 0.5 + aimJitter * (Rng.NextDouble() * 2 - 1);
            set.Add(Scan(position, reverseAxis));
        }
        return set;
    }
}
