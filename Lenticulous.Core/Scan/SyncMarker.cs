namespace Lenticulous.Scan;

/// <summary>
/// A row of blocks drawn in the SAME display frame as the sub-pixel being lit. The camera cannot tell when the display changed (the
/// link has latency nobody controls), but a block pattern the camera can read says which frame it is looking at: when the pattern
/// shows sequence number N, the sub-pixel lit with it is on too, however faint. Timing stops mattering.
/// </summary>
/// <remarks>
/// Layout, left to right: block 0 is a black reference (never drawn), block 1 a white reference, blocks 2..9 the eight bits of the
/// sequence number, most significant first (white = 1). The references give the camera's own black and white for this moment, so
/// the threshold needs no tuning for exposure.
/// </remarks>
public static class SyncMarker
{
    public const int BitCount = 8;
    /// <summary>Highest sequence number (0 is not used, it is what a blank screen decodes to).</summary>
    public const int MaxSequence = (1 << BitCount) - 1;
    /// <summary>Blocks in the row: black reference, white reference, then the bits.</summary>
    public const int BlockCount = 2 + BitCount;
    /// <summary>Side of one block and distance between block starts, in stimulus pixels. Big enough for the camera to average a clean
    /// reading from (about 14 camera pixels at the working distance), apart enough that blur does not mix neighbours.</summary>
    public const int BlockSize = 14, BlockPitch = 20;

    /// <summary>The bits (true = white block) of a sequence number, most significant first.</summary>
    public static bool[] Bits(int sequence)
    {
        if (sequence < 1 || sequence > MaxSequence) throw new ArgumentOutOfRangeException(nameof(sequence), $"1..{MaxSequence}");
        var bits = new bool[BitCount];
        for (int i = 0; i < BitCount; i++) bits[i] = ((sequence >> (BitCount - 1 - i)) & 1) != 0;
        return bits;
    }

    /// <summary>
    /// The sequence number shown, from the brightness read at each block (index 0 = black reference, 1 = white reference, then the
    /// bits). Null when the references do not differ by at least <paramref name="minContrast"/> (no marker in view), when a bit
    /// block sits in the middle band between black and white (a frame caught half way, or glare), or when every bit reads black.
    /// </summary>
    public static int? Decode(IReadOnlyList<double> blockBrightness, double minContrast = 40, double middleBand = 0.3)
    {
        if (blockBrightness.Count != BlockCount) throw new ArgumentException($"{BlockCount} block readings expected");
        double black = blockBrightness[0], white = blockBrightness[1];
        if (white - black < minContrast) return null;
        int value = 0;
        for (int i = 0; i < BitCount; i++)
        {
            double t = (blockBrightness[2 + i] - black) / (white - black); // 0 = black, 1 = white
            if (Math.Abs(t - 0.5) < middleBand / 2) return null;
            value = (value << 1) | (t > 0.5 ? 1 : 0);
        }
        return value == 0 ? null : value;
    }
}
