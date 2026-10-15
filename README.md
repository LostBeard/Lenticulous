# Lenticulous

Recovers the **pixel pattern** of a lenticular (glasses-free 3D) display by scanning it with a camera, entirely in the
browser. A .NET 10 Blazor WebAssembly app built on [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS).

> **Status: early.** The pieces below marked done work on real hardware; the scan itself is not built yet. See
> [PLAN.md](PLAN.md) for the full plan, measurements and open questions.

## What a pixel pattern is

A lenticular display puts a lens sheet over the panel, so each sub-pixel (the red, green or blue third of a pixel) is
only visible from one viewing angle. The pixel pattern says which view each sub-pixel belongs to. Render each view into
its sub-pixels and the display shows real 3D. Manufacturers do not publish it, and it differs per display.

## How the scan works

1. Light the dimmest sub-pixel found so far (all others in the region off).
2. Move the camera until that sub-pixel looks as bright as possible, so the camera sits in that sub-pixel's viewing zone.
3. Light every sub-pixel in a small region one at a time and record what the camera sees. That is one scan.
4. After each scan, check whether every sub-pixel is well represented in at least one scan. If not, go to 1.
5. Overlapping scans are obvious and discarded. The camera's horizontal position per scan gives the view width.
6. One sub-pixel belongs to exactly one view; the view count and each sub-pixel's view number are the pattern.

## What works today

- A stimulus window placed on the chosen display at native resolution (Window Management API) that reports its real
  size, scale and origin back to the control page.
- A Reachy Mini robot as the moving camera: status, wake, park, head and body motion, and live video over WebRTC, from
  an `http://localhost` page through the robot's own signalling server (no sign-in).
- Finding the display: the stimulus window shows a QR code carrying its own ID and the robot's camera reads it, so the
  app knows which display it is looking at.
- A brightness meter on the camera picture.

## Not built yet

Single sub-pixel stimulus and read-back, the scan loop, coverage check, view assignment, and export in the formats the
3DGM renderers read. See [PLAN.md](PLAN.md).

## Run it

```
dotnet publish Lenticulous/Lenticulous.csproj -c Release -o publish
```

Serve `publish/wwwroot` from any static server with a fallback to `index.html`, and open it on `http://localhost`
(the robot link needs a secure context that is allowed plain `ws://`). The app references a **local** build of
`SpawnDev.ILGPU` (`5.3.8-local.2`, QR decoder fixes) and `SpawnDev.Reachy` from a local package feed; neither is on
nuget.org yet, so a fresh checkout will not restore until they are released.

## License

MIT - see [LICENSE.txt](LICENSE.txt).
