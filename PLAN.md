# Lenticulous - PLAN

New .NET 10 Blazor WASM app that recovers the **pixel pattern** of a lenticular (autostereoscopic) display by scanning
it with a webcam from many camera positions. Replaces the old app (kept for reference in `..\_reference`).

Owner: Riker (claimed on the board 2026-10-15). First test display: Philips 22-3D2N11/00 (Windows `DISPLAY3`, 1680x1050).

## Output

A pixel pattern: a tile of `rows x cols` sub-pixels (cols = 3 x pixels), each holding a view number. One sub-pixel
belongs to exactly one view. Also the view count. Export in the formats the 3DGM renderers already read
(`GMRender Examples\Share\GMRender Pixel Patterns`): ini with `data` = CSV or PNG/BMP, `format=bgr|rgb`, `rows`, `cols`,
`reverse`, `debug`. Consumers: `GMCore\...\RendererPixelPatternBase.cs`, ZPlay3D `RenderPixelPattern.cs` +
`pixelpattern.fragment.fx`.

## Technique (TJ, 2026-10-15)

Small scan region (rest of the screen is free for UI and progress; mind light bleed).

1. Find the sub-pixel with the lowest brightness across all scans so far (first round: any).
2. Light only that sub-pixel, neighbours off. Move the camera until it looks as bright as possible (targeted).
3. Scan: light every sub-pixel in the region one at a time, record camera brightness. Save as one scan.
4. After every scan, check coverage: is each sub-pixel (mostly) equally represented at sufficient brightness in some scan?
   Not yet -> go to 1.
5. When done there are more scans than viewing positions. Overlapping scans are obvious; discard them.
6. View count and each sub-pixel's view number come from the surviving scans (sub-pixel -> the scan where it is brightest).
   The camera's horizontal position per scan gives the view width (average camera travel between non-overlapping
   scans) and helps decide which scans to discard.

## What the old code gives us (read 2026-10-15; paths relative to `D:\users\tj\Projects`)

Reuse / port ideas:
- `SpawnDev\SpawnDev\Lenticulous\Models\ScanRegion.cs` - per-sub-pixel BGR scan buffer, Targets list, anchor pixels (4 quadrants).
- `...\Models\PatternScanProject.cs` - `GetNextTarget` / `GetBestTargetsForNextScan` = step 1.
- `...\Shared\Index.razor.cs` `FullScan` / `FindBounds` / `TaskCenter` - the closed loop: sweep until the target
  sub-pixel peaks (>=80% of range) and drops (<20%), pick the peak nearest centre, centre the camera on it, scan.
  Camera position is measured FROM THE IMAGE (`CenterOffsetX` of the screen markers in the frame), not from an encoder.
- `...\Services\SerialCameraSliderService.cs` + `wwwroot\serial.js` - WebSerial slider and gamepad control.
- `...\Services\MarkerPolyPts.cs`, `OpenCVService.cs`, `ScanCameraAlignment.cs`, `OpenCV\CameraCalibrationInfo.cs` - region
  finding, homography, camera drift/centering. (Not yet read in full.)
- `Blazor\PixelPatternTool\...\Pages\Webcam.razor.cs` - `GetLenticularAngle`, `PatternSizeDetector`, `GroupScans`
  (the 2021 build that produced a usable Exceptional pattern).

Known gaps in the old code (steps 4-6 are NOT finished in the files read):
- `PatternScanProject.GetComposite` computes `bestScanIndex` and never uses it; the satisfied check is stubbed (`passes = true`).
- `GroupScans` hard-codes `desiredViewCount = 6` and returns early.
- `FindBounds` exists twice (red-only and generic) - duplicate.
- Index.razor.cs is 81 KB of UI + control loop in one class.

## Camera = Reachy Mini over WebRTC (TJ, 2026-10-15)

Reachy Mini is the scanner camera and the positioner. What already exists (read, not yet run from this app):
- `SpawnDev.Reachy.Browser` (0.1.0-preview.5, `D:\users\tj\Projects\SpawnDev.Reachy`, owner Data): `ReachyMiniJs` wraps the
  Pollen JS SDK (loaded as an ES module from jsDelivr). `OnVideoTrack` hands over a `MediaStream` (camera + 4-mic array);
  `SetHeadRpyDeg`, `SetBodyYawDeg`, `GotoTarget`, `HeadMatrix` readback; `WakeUpAsync` (asleep = flat dark frame).
- Sign-in is Hugging Face OAuth and **only works inside an HF Space** (`hf_oauth: true` injects the client id). The
  SpawnDev.AI demo already ships that way (`SpawnDev.AI\tools\deploy-space.cs`, Space `LostBeard/spawndev-ai`). Wireless
  Reachy only, robot signed in under the SAME HF account. Consequence: the Reachy path cannot be tested from
  `localhost`; it needs a Space for Lenticulous.
- Camera measured by Data on the desktop path: 1280x720 H.264, about 4.6 frames/s. Browser path rate not measured.

**Two ways to reach the robot (TJ 2026-10-15, confirmed in code):**
1. **Localhost / LAN (dev, no HF login):** the robot runs its own GStreamer `webrtcsink` signalling server, plain
   `ws://reachy-mini.local:8443`. `SpawnDev.Reachy.GstSignallingClient` speaks it through an `ISignalingSocket`;
   `ClientWebSocketSignalingSocket` is a browser-capable implementation (ClientWebSocket wraps the JS WebSocket under WASM).
   An https page is blocked as mixed content (Data verified: page fails, extension service worker works), but an
   `http://localhost` page is not. Video then arrives through `SpawnDev.RTC` (native browser `RTCPeerConnection`).
   Not yet run from this app.
2. **Hosted (HF Space):** the Pollen SDK against `pollen-robotics-reachy-mini-central.hf.space` (HTTPS + SSE, Bearer
   token from HF OAuth). The SDK also accepts a token directly (`connect(token)`, or `hf_token` pre-seeded in the URL
   hash / sessionStorage), so a pasted HF token is a possible localhost fallback for this path too.
The scan code sees one `ICameraSource` + `IRobotPositioner`; the link is a driver choice.

Project form (TJ 2026-10-15, from his earlier scanner apps): the user fills out display details, screen size among
them. EDID is not trustworthy (the Philips reports 27 cm tall for a 1680x1050 panel), so size comes from the form,
prefilled from EDID width / diagonal and editable. The old `PatternScanProject` already has `ProjectName`,
`DisplayName`, `PhysicalWidth`, `PhysicalHeight`.

Finding the display: Reachy wakes and looks around (body yaw + head yaw/pitch) for a code each stimulus window shows
(QR or simpler). Decoding it says WHICH display it is looking at, and the code's corners give the screen's position and
pose in the frame. Browser `BarcodeDetector` wrapper exists in SpawnJS; whether Chrome on Windows supports QR is unverified.

Camera specs (daemon `GET /api/camera/specs`, read 2026-10-15, read-only): modes 1280x720@30 and @60, 1920x1080@30,
3072x1728@10, 3264x2448@10, 3840x2160@10, 3840x2592@10 (full sensor). Intrinsics K at the 3840x2592 mode: fx 2001.8,
fy 2003.1, cx 1905.9, cy 1328.3, plus a 12-term distortion vector D (fisheye-like, k1 about -1.47). Horizontal FOV from
fx = 2 atan(1920 / 2001.8) = 87.6 degrees, which confirms the 87 degree figure. Data says the WebRTC session carries
H.264 1280x720; whether the session can be switched to a higher mode is NOT known yet. If it can, the full-sensor mode
changes the optics below completely (about 0.05 mm per camera pixel at 10 cm, 0.10 mm at 20 cm, 0.15 mm at 30 cm,
against a 0.094 mm sub-pixel, so sub-pixels become resolvable near 20 cm). Lens distortion must be undone with K and D
before any geometry is measured.

Optics, Philips (size from TJ 2026-10-15: 47.5 cm x 29.5 cm; EDID said 47 x 27, so EDID is not trusted): pixel pitch
0.283 mm horizontal (475 / 1680), 0.281 mm vertical (295 / 1050), so a sub-pixel is about 0.094 mm wide x 0.28 mm tall. If the 87 degree horizontal FOV from the daemon holds at 1280 px, one camera pixel covers about
0.15 mm at 10 cm, 0.30 mm at 20 cm, 0.44 mm at 30 cm. So at any workable distance one camera pixel blends the lit
sub-pixel with dark neighbours: the scan reads brightness, it does not image sub-pixels. Needs exposure control and
frame averaging. Reachy's focus distance is not known yet. All of this must be measured.

The robot is shared: Data's Vertex companion runs on it (on the LAN). Scanning moves the head and body, so coordinate
with Data before any session on it.

## Camera positioning - one interface, three drivers

`ICameraPositioner`: move toward a lateral target, report done, report limits. The scan algorithm never knows which one.
1. **Manual** - user moves the camera; UI shows a live brightness meter and a "good" state. Needed first: it proves the
   scan without any rig.
2. **WebSerial slider** - port of the existing rig.
3. **Reachy Mini** (`SpawnDev.Reachy`, REST + WebRTC camera).

Reachy caveat (from the old loop's mechanics): the viewing angle depends on the camera POSITION, not its orientation.
A pure pan (head yaw) only shifts the screen in the frame. Lateral travel has to come from body yaw (head swings on an
arc around the base) plus head yaw to re-aim. Measure on the real robot: lateral travel available vs the display's view
fan at the working distance (angle = atan(lateral / distance)) before committing to it. Head lift is only 2.2 cm.
Camera lateral position must be derived from the image (marker pose, known display size), since joint angles alone do not
give it once re-aiming is in play.

## Architecture (proposed, open to change)

- .NET 10 Blazor WASM, SpawnJS (the current stack). Output display is a second browser window placed on the target
  monitor; UI window on another screen or in the free part of the scan screen.
- Bulk pixels stay in JS/GPU (AGENTS.md rule 4): camera frames to GPU, sample the target sub-pixel region there; .NET
  only orchestrates and holds small per-scan summaries. Decide OpenCvSharp-wasm (old) vs GPU path after measuring.
- Scans persisted in OPFS (survive reload), exportable. Real scan sets recorded from the Philips become test fixtures.
- Pure algorithm layer (target selection, coverage check, overlap pruning, view assignment, export) separate from camera
  and display I/O so it is testable on recorded real data.

## Phases (each ends with a passing test on real data/hardware)

- [x] P0 Repo skeleton, builds, publishes; second window on the Philips shows full-screen stimulus. DONE 2026-10-15,
      verified on hardware: stimulus window reports 1680x1050 device pixels, scale 1, fullscreen, native 1:1 YES
      (control page reads it over a BroadcastChannel). Chrome numbers the Philips at (-1680, 26), unlike Windows.
      No PlaywrightMultiTest project yet (add before the algorithm layer lands).
- [~] P1 robot link DONE 2026-10-15 (hardware-verified): from `http://localhost` the app reaches the Reachy Mini daemon
      (REST status, wake, park by Data's recipe) AND its video over the robot's own signaller (ws :8443), no HF login,
      via `RoseAudioLink.OnVideoTrack` (SpawnDev.Reachy 0.1.0-local.15). Video element decodes 1280x720. A 4x4 downscale
      brightness meter reads 2-3 of 255 parked (camera inside the shell) and 39-144 awake (real picture). Track
      NOT a SpawnJS bug (suspected, then disproved 2026-10-15): a remote video track reports only
      deviceId and resizeMode when it arrives; width, height, frame rate and aspect ratio appear after the first
      frame decodes (3 s later: 1280x720 @ 28.96 fps, SpawnJS typed value equal to Chrome's raw value). Read the
      settings on demand, never once at arrival. The app now does.
      Still open for P1: single lit sub-pixel on the stimulus, reading it from the robot picture, exposure control, and
      the stimulus window origin report (code written, not yet verified on the Philips).
- [~] P1 find the display DONE 2026-10-15 (hardware-verified): robot beside the Philips (display about 90 degrees to its
      left, 30-60 cm). MEASURED: the commanded HEAD yaw is a WORLD yaw, not body-relative (TJ watched the body turn
      under a head held at 0). With the body held at 55 deg, head world yaw swept 0..90 in 15 deg steps against a white
      stimulus: mean brightness 88 (head 0) rising to a peak of 119 at head 75, 108 at 90; the 4x4 grid at the peak
      shows a broad bright centre (143-157) over darker edges, i.e. the white stimulus window. Head read-back lags the
      command by 0-3 deg. Robot left awake pointing at the display (head 75). Next: look for an ID code (QR) instead of
      brightness, to know WHICH display, and locate the scan region in the frame.
- [~] P1 find the display by ID code DONE 2026-10-15 (hardware-verified): each stimulus window shows a QR carrying its own
      random id (`LNT1:<id>`, level H, drawn as one SVG path, no pixel copies); the control page reads the robot picture
      (1280x720, no movement needed) and matches the decoded id to the stimulus windows reporting over the channel.
      Real camera, first attempt: `LNT1:c31d3c46` decoded in 34 ms, display matched. This needed three fixes in
      SpawnDev.ILGPU's QR decoder (see "QR decoder" below); with the released 5.3.7 the same view decoded as garbage.
      Known copy (flagged, Rule 4): the decoder takes a managed RGBA `byte[]`, so a downscaled frame crosses into .NET
      per attempt, only while searching. Needs a library overload taking a JS typed array / GPU buffer.
- [ ] P1 Stimulus + capture: light one sub-pixel at native resolution (no scaling, 100% DPI), read it from the webcam,
      manual positioning with brightness meter. Verify bleed behaviour and the raw (no Dimenco header) display mode.
- [ ] P2 Region find + single full scan, saved to OPFS.
- [ ] P3 Scan loop: dimmest-target selection, repeated scans, coverage check.
- [ ] P4 Overlap pruning, view count, per-sub-pixel view assignment, view width from camera position.
- [ ] P5 Export (3DGM ini + PNG/CSV) and verification: render with the recovered pattern, photograph from several
      positions, each position shows its view. Cross-check against the Dimenco 2D+Z header render of the same scene.
- [ ] P6 Rig drivers: WebSerial slider, then Reachy.
- [ ] P7 Replace the old app: deploy, document, retire.

## Stimulus window geometry (found 2026-10-15)

While a Claude browser session is attached, Chrome shows an infobar that shrinks the stimulus viewport (observed:
1680 x 994 instead of 1680 x 1050, fullscreen). Any bar, taskbar or scaling can do the same. The pattern tile's phase
depends on where the stimulus pixel (0,0) sits on the panel, so the stimulus window must REPORT its true origin instead
of assuming (0,0): on a click, `ScreenX - ClientX` and `ScreenY - ClientY`, minus the screen's own left/top. Scale must be
exactly 1. A viewport smaller than the panel is acceptable if the origin and scale are known. Real scans run with no
Claude session attached; attached sessions are a development artifact.

## Shared robot

Data's Vertex also pulls Reachy video over WebRTC. Check that no Vertex session is on the robot before a scan session,
and that the robot's signalling server accepts a second consumer if one is.

## QR decoder (SpawnDev.ILGPU `QR/QRDecoder.cs`) - found and fixed while building this, 2026-10-15

Measured on released 5.3.7 with a harness that encodes, flips K random data modules, and decodes: ONE flipped module
decoded wrongly 31% of the time, ten flipped modules 87% wrong, and it almost never reported failure; a clean version 5
code never decoded. Fixed in the library (tests in `BackendTestBase.QRTests.cs`):
1. Reed-Solomon correction was not Forney (error value taken from the first syndromes "for simplicity"), syndromes were
   computed on the reversed polynomial, and a block that could not be corrected was passed on "best effort". Now
   Berlekamp-Massey + Chien + Forney, verified by re-computing the syndromes; an uncorrectable block fails the decode.
2. Binarisation took a local mean from 9 sample points, so inside a dark region the threshold fell below zero and the
   dark core of a finder pattern went light: large codes had no finders. Now block-based (8x8 black points, 5x5 average),
   CPU and GPU paths the same.
3. Finder look-alikes in the data were merged into the real finder and shifted its centre; the version estimate was
   truncated, not rounded. Now merge only same-size candidates, drop single-row hits, score triples by square legs and
   equal sizes, and try the neighbouring versions.
Clean decode: 2400/2400 (lengths 1-150, all levels, 2 module sizes, 2 alphabets); before: 36-59 of 150 per level.
Pushed to SpawnDev.ILGPU master (7eae1c1), local package `SpawnDev.ILGPU 5.3.8-local.1` in the local feed (never nuget.org);
this app uses it. Five new tests, red on all 7 backends against the old decoder (35 of 51), 51 of 51 green with the fix.
STILL OPEN (named, not fixed): the decoder fails on ANY rotation (3 degrees: top-left finder missed; 10 degrees: none) and
has no perspective correction (affine sampling from 3 finder centres, no alignment pattern); robust to blur, 3 px modules,
dark frames and brightness gradients. The robot's camera sees the code with roll under 1 degree, which is why it reads, but
an oblique view will not. Plan: ZXing-style finder cross-check (vertical, horizontal, diagonal) and homography sampling
using the alignment pattern. Also: no overload that takes a JS typed array or GPU buffer (Rule 4).

## Open questions

- Does the Philips keep its lens effect with no Dimenco header (raw mode), and does the header ever have to be off for a scan?
- Working distance: Reachy's focus range and how close it can get; lateral travel from body yaw vs the Philips view fan.
  Distance needs user help (TJ 2026-10-15): the app guides the user to place the robot, then measures.
- Where do the repo and the HF Space live (neither created; need TJ's go-ahead).
- Per-scan camera position: image-derived pose vs rig-reported - which is accurate enough for view width?
- Does Chrome on Windows decode QR with `BarcodeDetector`, or do we decode ourselves?
- Dev loop: the Reachy path only runs on a Space. Algorithm work uses a plain camera source meanwhile.
- The JavaScript-era builds TJ remembers: not found anywhere searched (Projects and other drives, 2026-10-15). Not needed.
