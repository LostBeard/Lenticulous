using System.Text;

namespace Lenticulous.Scan;

/// <summary>
/// Writes a pixel pattern in the format the 3DGM renderers read (the GMRender / ZPlay3D pixel pattern ini): the view number of every
/// sub-pixel of one repeating cell, row-major, with the cell's size. Read back from RendererPixelPatternBase in the 3DGM source:
/// <c>cols</c> counts sub-pixel COLUMNS (3 per pixel), <c>rows</c> counts rows, <c>data</c> must hold exactly <c>rows * cols</c> values,
/// and <c>format=rgb</c> means the three columns of a pixel are its red, green and blue sub-pixels in that order.
/// </summary>
public static class PixelPatternExporter
{
    /// <summary>The pattern as the text of a 3DGM pixel pattern ini file.</summary>
    /// <param name="tile">The repeating cell.</param>
    /// <param name="name">Display name shown in the renderer's menu.</param>
    /// <param name="reverse">Reverse the view order. By 3DGM convention view 0 is the farthest LEFT view; set this when the camera
    /// axis that numbered the views ran the other way.</param>
    public static string ToIni(PixelPatternTile tile, string name, bool reverse = false)
    {
        if (tile.Cols % 3 != 0) throw new ArgumentException($"cols must be a multiple of 3 (three sub-pixels per pixel), got {tile.Cols}", nameof(tile));
        var text = new StringBuilder();
        text.Append("name=").Append(name).Append("\r\n\r\n");
        text.Append("[pixelpattern]\r\n\r\n");
        text.Append("; data - the view number of each sub-pixel of one repeating cell, row-major, rows * cols values\r\n");
        text.Append("data=").Append(string.Join(",", tile.Views)).Append("\r\n\r\n");
        text.Append("; format - the order of the three sub-pixels of each pixel in the data\r\n");
        text.Append("format=rgb\r\n\r\n");
        text.Append("; rows and cols - the cell size in rows and in sub-pixel columns (not pixels)\r\n");
        text.Append("rows=").Append(tile.Rows).Append("\r\n");
        text.Append("cols=").Append(tile.Cols).Append("\r\n\r\n");
        text.Append("views=").Append(tile.ViewCount).Append("\r\n\r\n");
        text.Append("; reverse - reverse the view order (swap left and right views)\r\n");
        text.Append("reverse=").Append(reverse ? 1 : 0).Append("\r\n\r\n");
        text.Append("; debug - draw the layout texture instead of the views\r\n");
        text.Append("debug=0\r\n");
        return text.ToString();
    }
}
