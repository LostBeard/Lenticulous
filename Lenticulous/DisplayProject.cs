namespace Lenticulous;

/// <summary>
/// What the user tells us about the display being scanned. Physical size comes from the user, not from EDID:
/// the Philips 22-3D2N11/00 reports 47 x 27 cm in EDID for a panel that is 47.5 x 29.5 cm.
/// </summary>
public class DisplayProject
{
    public string ProjectName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public double PhysicalWidthCm { get; set; }
    public double PhysicalHeightCm { get; set; }

    /// <summary>Horizontal pixel pitch in mm, or 0 when either value is missing.</summary>
    public double PixelPitchXmm => WidthPx > 0 && PhysicalWidthCm > 0 ? PhysicalWidthCm * 10d / WidthPx : 0d;

    /// <summary>Vertical pixel pitch in mm, or 0 when either value is missing.</summary>
    public double PixelPitchYmm => HeightPx > 0 && PhysicalHeightCm > 0 ? PhysicalHeightCm * 10d / HeightPx : 0d;

    /// <summary>A sub-pixel is a third of a pixel wide (RGB stripes).</summary>
    public double SubPixelWidthMm => PixelPitchXmm / 3d;

    public bool IsComplete => !string.IsNullOrWhiteSpace(ProjectName) && WidthPx > 0 && HeightPx > 0
        && PhysicalWidthCm > 0 && PhysicalHeightCm > 0;
}
