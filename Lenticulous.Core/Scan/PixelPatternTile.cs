namespace Lenticulous.Scan;

/// <summary>
/// The repeating cell of a display's pixel pattern: <c>Rows</c> by <c>Cols</c> sub-pixels, each the number of the view it belongs to.
/// The whole display is this cell tiled from the panel's top-left corner.
/// </summary>
public sealed class PixelPatternTile
{
    public PixelPatternTile(int cols, int rows, int viewCount, int[] views, double agreement)
    {
        Cols = cols;
        Rows = rows;
        ViewCount = viewCount;
        Views = views;
        Agreement = agreement;
    }

    /// <summary>Sub-pixel columns in the cell (a multiple of 3: a pixel is three).</summary>
    public int Cols { get; }
    public int Rows { get; }
    public int ViewCount { get; }

    /// <summary>The view of each cell, row-major, <c>Rows * Cols</c> values.</summary>
    public int[] Views { get; }

    /// <summary>Fraction of the scanned sub-pixels that agree with the cell they fall in.</summary>
    public double Agreement { get; }

    public int this[int subX, int y] => Views[y * Cols + subX];

    /// <summary>
    /// Find the smallest cell that repeats across a scanned region, and build it by majority vote.
    /// </summary>
    /// <param name="viewOf">View per sub-pixel of the region, row-major (-1 = unknown).</param>
    /// <param name="subWidth">Region width in sub-pixels.</param>
    /// <param name="height">Region height in rows.</param>
    /// <param name="viewCount">Views found.</param>
    /// <param name="originSubX">Where the region's left edge sits on the panel, in sub-pixels. The cell is aligned to the PANEL,
    /// because that is how a renderer tiles it, so the region's offset rolls the cell.</param>
    /// <param name="originY">Where the region's top edge sits on the panel, in rows.</param>
    /// <param name="minAgreement">The fraction of known sub-pixels that must agree with the cell.</param>
    /// <returns>The cell, or null if no cell small enough to repeat at least twice across the region fits.</returns>
    public static PixelPatternTile? Extract(int[] viewOf, int subWidth, int height, int viewCount, int originSubX = 0, int originY = 0,
        double minAgreement = 0.97)
    {
        int maxCols = subWidth / 2, maxRows = height / 2;
        var candidates = new List<(int cols, int rows)>();
        for (int rows = 1; rows <= maxRows; rows++)
            for (int cols = 3; cols <= maxCols; cols += 3)
                candidates.Add((cols, rows));
        // The smallest cell first: a cell that tiles the region at 4x4 also does at 8x8, and the smaller one is the real period.
        foreach (var (cols, rows) in candidates.OrderBy(c => c.cols * c.rows).ThenBy(c => c.rows))
        {
            var tile = TryCell(viewOf, subWidth, height, viewCount, originSubX, originY, cols, rows, minAgreement);
            if (tile != null) return tile;
        }
        return null;
    }

    private static PixelPatternTile? TryCell(int[] viewOf, int subWidth, int height, int viewCount, int originSubX, int originY,
        int cols, int rows, double minAgreement)
    {
        var votes = new int[cols * rows * viewCount];
        int known = 0;
        for (int y = 0; y < height; y++)
        {
            int cellRow = ((originY + y) % rows + rows) % rows;
            for (int sx = 0; sx < subWidth; sx++)
            {
                int v = viewOf[y * subWidth + sx];
                if (v < 0) continue;
                int cellCol = ((originSubX + sx) % cols + cols) % cols;
                votes[(cellRow * cols + cellCol) * viewCount + v]++;
                known++;
            }
        }
        if (known == 0) return null;

        var views = new int[cols * rows];
        int agreeing = 0;
        for (int cell = 0; cell < views.Length; cell++)
        {
            int best = -1, bestVotes = 0, total = 0;
            for (int v = 0; v < viewCount; v++)
            {
                int count = votes[cell * viewCount + v];
                total += count;
                if (count > bestVotes) { bestVotes = count; best = v; }
            }
            // Every cell must be seen, and more than once: a cell seen once is not evidence that anything repeats.
            if (total < 2) return null;
            views[cell] = best;
            agreeing += bestVotes;
        }
        double agreement = (double)agreeing / known;
        return agreement >= minAgreement ? new PixelPatternTile(cols, rows, viewCount, views, agreement) : null;
    }
}
