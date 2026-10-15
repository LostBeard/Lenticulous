using SpawnDev.Reachy;
using SpawnDev.RTC;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace Lenticulous.Services;

/// <summary>
/// The Reachy Mini as scan camera and positioner, reached over the LAN from an http page: the daemon's REST API
/// for motion, the robot's own GStreamer signalling server (ws://host:8443) for the WebRTC video session.
/// </summary>
/// <remarks>
/// The camera picture never enters .NET: the browser's decoded track goes straight to a video element.
/// Park uses Data's confirmed recipe (SpawnDev.Reachy CLAUDE.md): go home FIRST, then sleep, wait, then motors off.
/// </remarks>
public sealed class RobotService : IAsyncDisposable
{
    readonly SpawnJSRuntime _js;
    ReachyMiniClient? _client;
    RoseAudioLink? _link;
    HTMLVideoElement? _video;

    public RobotService(SpawnJSRuntime js) => _js = js;

    public string Host { get; private set; } = "";
    public bool VideoConnected => _link != null;
    public string LinkState { get; private set; } = "not connected";
    public string TrackInfo { get; private set; } = "no video track yet";
    public event Action? Changed;
    public event Action<string>? Log;

    void Say(string message)
    {
        Log?.Invoke(message);
        Changed?.Invoke();
    }

    /// <summary>Connect the video session and show it in the element with the given id.</summary>
    public async Task ConnectVideoAsync(string host, string containerId)
    {
        await DisconnectVideoAsync();
        Host = host.Trim();
        using var document = _js.Get<Document>("document");
        using var container = document.GetElementById(containerId);
        if (container == null) throw new InvalidOperationException($"No element with id '{containerId}'.");

        _video = document.CreateElement<HTMLVideoElement>("video");
        _video.Muted = true;       // the robot's mic track rides the same stream; no sound is wanted here
        _video.AutoPlay = true;
        _video.PlaysInline = true;
        _video.Style.SetProperty("width", "100%");
        _video.Style.SetProperty("background", "#000");
        container.AppendChild(_video);

        var link = new RoseAudioLink(Host);
        link.Log += m => Say("link: " + m);
        link.OnConnectionStateChanged += s => { LinkState = s; Say("state " + s); };
        // Subscribe BEFORE ConnectAsync (SpawnDev.Reachy 0.1.0-local.15 contract). No OnMicAudio subscriber, so
        // no PCM capture starts; no OnVideoFrame subscriber, so no desktop decoder.
        link.OnVideoTrack += OnVideoTrack;
        _link = link;
        await link.ConnectAsync();
    }

    void OnVideoTrack(IRTCMediaStreamTrack track)
    {
        try
        {
            if (track is not SpawnDev.RTC.Browser.BrowserRTCMediaStreamTrack browserTrack || _video == null)
            {
                Say("video track arrived but is not a browser track");
                return;
            }
            // NativeTrack is owned by the SpawnDev.RTC track wrapper and shared: do not dispose it here.
            var native = browserTrack.NativeTrack;
            var stream = new MediaStream(new[] { native });
            _video.SrcObject = stream;
            _ = _video.Play();
            _track = browserTrack;
            TrackInfo = $"video track {track.Id}, ready {track.ReadyState}";
            Say(TrackInfo);
        }
        catch (Exception ex)
        {
            // A throw in a runtime callback exits the WASM runtime, so it must not escape.
            Say($"OnVideoTrack failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    SpawnDev.RTC.Browser.BrowserRTCMediaStreamTrack? _track;

    /// <summary>
    /// What the video element is really showing, plus the track's own settings, or null with no video.
    /// </summary>
    /// <remarks>
    /// A remote video track reports only deviceId and resizeMode until its first frame has decoded: width, height and
    /// frame rate appear a moment AFTER the track arrives (measured 2026-10-15: absent at arrival, 1280x720 at 29 fps
    /// three seconds later, SpawnJS and Chrome agreeing exactly). Read them on demand, never once at arrival.
    /// </remarks>
    public string? VideoElementInfo()
    {
        if (_video == null) return null;
        var s = _track?.GetSettings();
        // The frame rate can read Infinity for a moment while the track is settling: show it only when it is a real number.
        var fps = s?.FrameRate is { } f && double.IsFinite(f) ? $" @ {f:0.#} fps" : "";
        var settings = s?.Width is { } w && s.Height is { } h ? $"track {w}x{h}{fps}" : "track settings not reported yet";
        return $"element {_video.VideoWidth}x{_video.VideoHeight}, paused {_video.Paused}, ready {_video.ReadyState}; {settings}";
    }

    HTMLCanvasElement? _grab;
    HTMLCanvasElement? _probeCanvas;

    /// <summary>Width and height of the frames <see cref="CaptureAveragedAsync"/> returns.</summary>
    public (int Width, int Height) ProbeSize { get; private set; }

    /// <summary>
    /// EXPERIMENT ONLY - a measurement path, not the production one. Averages <paramref name="frames"/> full-resolution camera
    /// frames into one RGB float image (interleaved, <c>width * height * 3</c>) so a single lit sub-pixel can be told from sensor
    /// noise. Every frame crosses into .NET as a byte array (3.7 MB at 720p), which Rule 4 forbids for production: the real scan
    /// reduces on the GPU (a WebGPU texture straight from the video frame). This exists to MEASURE how much light one sub-pixel
    /// gives the camera, which decides how that GPU path must be built.
    /// </summary>
    public async Task<float[]?> CaptureAveragedAsync(int frames, int spacingMs)
    {
        if (_video == null || _video.VideoWidth == 0) return null;
        int w = _video.VideoWidth, h = _video.VideoHeight;
        if (_probeCanvas == null)
        {
            using var document = _js.Get<Document>("document");
            _probeCanvas = document.CreateElement<HTMLCanvasElement>("canvas");
        }
        if (_probeCanvas.Width != w) _probeCanvas.Width = w;
        if (_probeCanvas.Height != h) _probeCanvas.Height = h;
        ProbeSize = (w, h);
        var sum = new float[w * h * 3];
        for (int f = 0; f < frames; f++)
        {
            await Task.Delay(spacingMs); // let the next camera frame arrive (about 33 ms apart at 30 fps)
            using var ctx = _probeCanvas.Get2DContext(new CanvasRenderingContext2DSettings { WillReadFrequently = true });
            ctx.DrawImage(_video, 0, 0, w, h);
            using var image = ctx.GetImageData(0, 0, w, h);
            using var data = image.Data;
            var rgba = data.ToArray();
            for (int p = 0, i = 0, j = 0; p < w * h; p++, i += 4, j += 3)
            {
                sum[j] += rgba[i];
                sum[j + 1] += rgba[i + 1];
                sum[j + 2] += rgba[i + 2];
            }
        }
        for (int i = 0; i < sum.Length; i++) sum[i] /= frames;
        return sum;
    }

    /// <summary>
    /// Try to read a display ID code (QR) from the current camera picture.
    /// </summary>
    /// <remarks>
    /// KNOWN COPY, flagged for TJ: SpawnDev.ILGPU's QR decoder takes a managed RGBA byte[], so the frame (downscaled to
    /// <paramref name="maxWidth"/> by the browser first) crosses into .NET once per attempt. It runs only while searching
    /// for a display, never in the scan loop. The right fix is a library overload that decodes from a JS typed array or a
    /// GPU buffer (Rule 2), not a caller workaround.
    /// </remarks>
    public QrAttempt TryDecodeQr(int maxWidth = 1280)
    {
        if (_video == null || _video.VideoWidth == 0) return new QrAttempt(null, 0, 0, 0);
        var w = Math.Min(maxWidth, _video.VideoWidth);
        var h = _video.VideoHeight * w / _video.VideoWidth;
        if (_grab == null)
        {
            using var document = _js.Get<Document>("document");
            _grab = document.CreateElement<HTMLCanvasElement>("canvas");
        }
        if (_grab.Width != w) _grab.Width = w;
        if (_grab.Height != h) _grab.Height = h;
        using var ctx = _grab.Get2DContext(new CanvasRenderingContext2DSettings { WillReadFrequently = true });
        ctx.DrawImage(_video, 0, 0, w, h);
        using var image = ctx.GetImageData(0, 0, w, h);
        using var data = image.Data;
        var rgba = data.ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var text = SpawnDev.ILGPU.QR.QRCode.Decode(rgba, w, h);
        return new QrAttempt(text, w, h, sw.Elapsed.TotalMilliseconds);
    }

    public record QrAttempt(string? Text, int Width, int Height, double DecodeMs);

    HTMLCanvasElement? _meter;

    /// <summary>
    /// A 4x4 brightness grid (0-255, luma) of the current camera picture, row by row. The frame is downscaled by the
    /// browser so only 64 bytes cross into .NET. Null while there is no picture yet.
    /// </summary>
    public int[]? BrightnessGrid4x4()
    {
        if (_video == null || _video.VideoWidth == 0) return null;
        if (_meter == null)
        {
            using var document = _js.Get<Document>("document");
            _meter = document.CreateElement<HTMLCanvasElement>("canvas");
            _meter.Width = 4;
            _meter.Height = 4;
        }
        using var ctx = _meter.Get2DContext(new CanvasRenderingContext2DSettings { WillReadFrequently = true });
        ctx.ImageSmoothingQuality = "high";
        ctx.DrawImage(_video, 0, 0, 4, 4);
        using var image = ctx.GetImageData(0, 0, 4, 4);
        using var data = image.Data;
        var rgba = data.ToArray();
        var grid = new int[16];
        for (var i = 0; i < 16; i++)
            grid[i] = (int)Math.Round(0.299 * rgba[i * 4] + 0.587 * rgba[i * 4 + 1] + 0.114 * rgba[i * 4 + 2]);
        return grid;
    }

    public async Task DisconnectVideoAsync()
    {
        var link = _link;
        _link = null;
        if (link != null)
        {
            link.OnVideoTrack -= OnVideoTrack;
            try { await link.DisposeAsync(); }
            catch (Exception ex) { Say("link dispose: " + ex.Message); }
        }
        if (_video != null)
        {
            _video.SrcObject = null;
            _video.Remove();
            _video.Dispose();
            _video = null;
        }
        _meter?.Dispose();
        _meter = null;
        _grab?.Dispose();
        _grab = null;
        _probeCanvas?.Dispose();
        _probeCanvas = null;
        _track = null;
        TrackInfo = "no video track yet";
        LinkState = "not connected";
    }

    ReachyMiniClient Client(string host)
    {
        if (_client == null || _client.BaseAddress.Host != host.Trim())
        {
            _client?.Dispose();
            _client = new ReachyMiniClient(host.Trim());
        }
        return _client;
    }

    /// <summary>Motors on, then the daemon's own wake_up move. The head comes up and the camera sees the room.</summary>
    public async Task WakeAsync(string host)
    {
        var client = Client(host);
        await client.SetMotorModeAsync(MotorMode.Enabled);
        await client.WakeUpAsync();
        await Task.Delay(1500);
        Say("woke");
    }

    /// <summary>Current body and head pose as the daemon reports it (read-only).</summary>
    public async Task<string> PoseTextAsync(string host)
    {
        var client = Client(host);
        var body = await client.GetBodyYawAsync();
        var head = await client.GetHeadPoseAsync();
        const double Deg = 180d / Math.PI;
        return head == null
            ? $"body yaw {body * Deg:0.0} deg, head pose unavailable"
            : $"body yaw {body * Deg:0.0} deg, head yaw {head.Yaw * Deg:0.0}, pitch {head.Pitch * Deg:0.0}, roll {head.Roll * Deg:0.0} deg, z {head.Z * 1000:0.0} mm";
    }

    /// <summary>
    /// Move body and head yaw (degrees, positive turns left) and wait for the move. The daemon clamps silently, so the
    /// caller reads the pose back rather than trusting the command.
    /// </summary>
    public async Task TurnAsync(string host, double bodyYawDeg, double headYawDeg, double seconds = 1.5)
    {
        var client = Client(host);
        const double Rad = Math.PI / 180d;
        await client.GotoAsync(bodyYaw: bodyYawDeg * Rad, headPose: new XyzRpyPose(Yaw: headYawDeg * Rad), duration: seconds);
        await Task.Delay((int)(seconds * 1000) + 400);
    }

    /// <summary>Mean luma of the current picture (0-255) from the 4x4 grid, or null with no picture.</summary>
    public double? MeanBrightness()
    {
        var grid = BrightnessGrid4x4();
        return grid == null ? null : grid.Average();
    }

    /// <summary>Data's park recipe: home first, then sleep, wait, then motors off.</summary>
    public async Task ParkAsync(string host)
    {
        var client = Client(host);
        await client.GoHomeAsync(1.0);
        await Task.Delay(1100);
        await client.GotoSleepAsync();
        await Task.Delay(1500);
        await client.SetMotorModeAsync(MotorMode.Disabled);
        Say("parked, motors off");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectVideoAsync();
        _client?.Dispose();
        _client = null;
    }
}
