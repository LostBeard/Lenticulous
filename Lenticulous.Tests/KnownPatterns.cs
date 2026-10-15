namespace Lenticulous.Tests;

/// <summary>
/// A real pixel pattern: TJ's example from the 3DGM GMRender package (GMRender Pixel Patterns, PixelPatternCSV.ini): 12 rows by 24
/// sub-pixel columns, views 0 to 7. Used as ground truth for the scan logic.
/// </summary>
internal static class KnownPatterns
{
    public const int Cols = 24;
    public const int Rows = 12;
    public const int ViewCount = 8;

    /// <summary>Row-major, Rows x Cols.</summary>
    public static readonly int[] GMRenderExample = new[] { 2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6,3,2,1,6,5,4,1,0,7,4,3,2,7,6,5,2,1,0,5,4,3,0,7,6 };
}
