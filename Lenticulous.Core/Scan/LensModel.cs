namespace Lenticulous.Scan;

/// <summary>
/// The lens of a lenticular display as the scan found it: a pitch (sub-pixels per lens), a slant (sub-pixels the lens shifts per
/// row) and the lens phase that looks straight ahead. Pitch and slant are exact fractions because a real panel is built so its
/// pixel pattern repeats: the Philips 22-3D2N11/00 has 14/3 sub-pixels per lens and a slant of 1/2 sub-pixel per row.
/// </summary>
/// <remarks>
/// A sub-pixel's lens phase is where it sits under its lens, 0 to 1: <c>u = ((s - slant * r) / pitch) mod 1</c>, with
/// <c>s = 3 * pixelX + channel</c> and <c>r</c> the PANEL row. The lens inverts the picture, so a camera moving right sees
/// sub-pixels of a LOWER phase. View 0 is the farthest left view of the zone straight ahead.
/// </remarks>
public sealed class LensModel
{
    /// <param name="pitchNumerator">With <paramref name="pitchDenominator"/>: sub-pixels per lens, as a fraction (14 / 3).</param>
    /// <param name="slantNumerator">With <paramref name="slantDenominator"/>: sub-pixels the bright set moves right per row (1 / 2).</param>
    /// <param name="centerPhase">The lens phase a camera straight in front of the panel sees (0 to 1).</param>
    public LensModel(int pitchNumerator, int pitchDenominator, int slantNumerator, int slantDenominator, double centerPhase)
    {
        if (pitchNumerator <= 0 || pitchDenominator <= 0 || slantDenominator <= 0) throw new ArgumentException("pitch and slant must be positive fractions");
        PitchNumerator = pitchNumerator; PitchDenominator = pitchDenominator;
        SlantNumerator = slantNumerator; SlantDenominator = slantDenominator;
        CenterPhase = centerPhase - Math.Floor(centerPhase);
        // u = (pd * (sd * s - sn * r)) mod (pn * sd) / (pn * sd), in whole numbers so the pattern is exactly periodic.
        Classes = pitchNumerator * slantDenominator;
    }

    public int PitchNumerator { get; }
    public int PitchDenominator { get; }
    public int SlantNumerator { get; }
    public int SlantDenominator { get; }
    public double CenterPhase { get; }
    public double Pitch => (double)PitchNumerator / PitchDenominator;
    public double Slant => (double)SlantNumerator / SlantDenominator;

    /// <summary>How many distinct lens phases the lattice has: the phase is always a whole number of 1 / Classes.</summary>
    public int Classes { get; }

    /// <summary>The lens phase of a sub-pixel as a whole number of 1 / <see cref="Classes"/> (exact, no rounding).</summary>
    public int PhaseClass(int subX, int row)
    {
        long n = (long)PitchDenominator * ((long)SlantDenominator * subX - (long)SlantNumerator * row);
        return (int)(((n % Classes) + Classes) % Classes);
    }

    /// <summary>The lens phase of a sub-pixel, 0 to 1.</summary>
    public double Phase(int subX, int row) => (double)PhaseClass(subX, row) / Classes;

    /// <summary>Distance, in cycles and wrapped to -0.5..0.5, from a sub-pixel's phase to <paramref name="phase"/>.</summary>
    public double PhaseOffset(int subX, int row, double phase)
    {
        double d = Phase(subX, row) - phase;
        return d - Math.Round(d);
    }

    /// <summary>The view a sub-pixel belongs to when the zone is cut into <paramref name="viewCount"/> views (0 = farthest left).</summary>
    public int ViewOf(int subX, int row, int viewCount)
    {
        // A camera to the LEFT sees a HIGHER phase, so the left edge of the zone is CenterPhase + 0.5 and the view grows as the phase falls.
        double p = CenterPhase + 0.5 - Phase(subX, row);
        p -= Math.Floor(p);
        // The phase is a whole number of 1/Classes, so it can sit exactly on a view boundary; the tiny offset keeps floating-point
        // rounding (13/28 * 28 = 12.999999999999998) from tipping such a sub-pixel into the wrong view.
        int v = (int)Math.Floor(p * viewCount + 1e-9);
        return Math.Min(Math.Max(v, 0), viewCount - 1);
    }

    /// <summary>Columns after which the pattern repeats exactly, a multiple of 3 so a cell holds whole pixels.</summary>
    public int TileCols
    {
        get
        {
            // u repeats in s when pd * sd * cols is a multiple of pn * sd, i.e. cols is a multiple of pn / gcd(pn, pd).
            int period = PitchNumerator / Gcd(PitchNumerator, PitchDenominator);
            return Lcm(period, 3);
        }
    }

    /// <summary>Rows after which the pattern repeats exactly.</summary>
    public int TileRows
    {
        get
        {
            // u repeats in r when pd * sn * rows is a multiple of pn * sd.
            int num = PitchNumerator * SlantDenominator;
            int step = PitchDenominator * SlantNumerator;
            return step == 0 ? 1 : num / Gcd(num, step);
        }
    }

    /// <summary>The repeating cell as a pixel pattern with <paramref name="viewCount"/> views.</summary>
    public PixelPatternTile ToTile(int viewCount)
    {
        int cols = TileCols, rows = TileRows;
        var views = new int[cols * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                views[y * cols + x] = ViewOf(x, y, viewCount);
        return new PixelPatternTile(cols, rows, viewCount, views, agreement: 1.0);
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
    private static int Lcm(int a, int b) => a / Gcd(a, b) * b;
}
