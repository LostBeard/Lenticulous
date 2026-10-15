namespace Lenticulous;

/// <summary>
/// BroadcastChannel shared by the control page and the stimulus window. Plain text, one message per line:
/// the stimulus posts "status:..." and listens for "cmd:..." (fill, info, px, clear).
/// </summary>
public static class StimulusChannel
{
    public const string Name = "lenticulous-stimulus";
    public const string StatusPrefix = "status:";
    public const string CommandPrefix = "cmd:";
    /// <summary>The text a display's QR code starts with; the stimulus window's id follows.</summary>
    public const string QrPayloadPrefix = "LNT1:";
}
