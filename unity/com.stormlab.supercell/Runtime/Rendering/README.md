# Runtime/Rendering: the volumetric skin (Phase 2)

Draws the storm the brain simulates (DESIGN.md §2.5). It ports StormLab 1.0's BUILD / LIGHT / RENDER passes from
WGSL to HLSL as URP compute passes, with one big change: **the cloud moves with the simulated wind.**

## Use it

1. Add `StormSkinFeature` to your URP renderer asset (Renderer → Add Renderer Feature).
2. Put `StormSkin` on the GameObject that has the `SupercellRunner`, and set `runner`.
3. Set `groundOrigin`: the world x, z (metres) of the simulation grid's south-west corner at t = 0. The grid is
   80 x 80 km and moves at the frame motion. The default `(-40000, -40000)` centres the grid on the world origin.
4. Play. `playbackTime` advances at `timelapse` sim-seconds per second; it waits when it reaches the brain's horizon.
   Setting `playbackTime` backwards (a scrub) repaints the volume.

Without the component's `Update`, as the test does: `skin.Bind(run)`, `skin.Wind.Capture(run, run.Time)` at each
timeline frame, then `skin.StepTo(t, clock)` every frame before rendering.

Units: 1 Unity unit = 1 m, +x east, +z north, +y up. The skin works internally in km, in a **volume frame** centred
on the storm (`WorldToVol`, `VolToWorld`).

## What happens each frame

| Step | Where | Work |
| --- | --- | --- |
| Designer | `FeatureDesigner.Evaluate` (CPU) | parameter block at the playback time ([../Designer/README.md](../Designer/README.md)) |
| Paint | `StormSkinVolume.compute` `Build` | 1.0's BUILD: the designed storm into the **target** volume, 1/`buildSlabs` of it per frame (default 4) |
| Advect | `Advect` | the **live** volume is carried by the simulated wind for the playback dt (semi-Lagrangian; rain also falls at 7 m/s), then relaxed toward the target with time constant `relaxSeconds` (60 s) |
| Light | `Light` | 1.0's LIGHT on a 1/3-resolution grid (sun transmittance, sky, base shade, anvil ice shade), 1/`lightSlabs` per frame |
| March | `StormSkinMarch.compute` `March` | 1.0's RENDER at `renderScale` (0.5): sub-grid billows, striations in a frame turning at the simulated meso spin, the anvil's ice sheets, near-ground features at full detail, rain shafts, light shafts; stops at scene depth |
| Resolve | `Resolve` | temporal accumulation: reprojects the previous frame by the march's mean hit distance, clamped to the current 3 x 3 neighbourhood |
| Composite | `StormSkinComposite.shader` | `colour = storm.rgb + storm.a × scene` over the camera colour (`BeforeRenderingTransparents`) |

**Volume**: 400 x 400 x 120 at 150 m (60 x 60 x 18 km), RGBA8 (r cloud, g precipitation, b near-ground features for
shadows, a material from cauliflower water to smooth ice). Three copies (target and two live) = 231 MB, plus a 2 MB
light grid. `tier = High` is 512 x 512 x 160 (117 m); `Low` is 288 x 288 x 96. The volume follows the storm in
whole-voxel steps when the suggested centre drifts more than 4 km, so the advected cloud shifts exactly.

**Wind** (`WindHistory.cs`): the brain runs ahead of playback, so its live wind is from the future. Every
10 sim-seconds a compute kernel reduces the model's C-grid u, v, w to 2 km blocks x 1 km layers. The result is read
back and stored by model time in system RAM: byte-quantised like `Map2D` (-51..51 m/s, 0.4 m/s steps), 122 KB per
snapshot, 75 MB for 103 minutes. Playback uploads the two snapshots around its time into two small 3D textures, and
the GPU blends them. It reads `run.model.U/V/W` between steps: those buffers hold the start-of-step wind until
segment 4, so a mid-step capture is still a consistent wind. **Runtime/Dynamics is not modified.** Motion finer than
2 km (striations turning, the wall cloud spinning) comes from the simulated mesocyclone vorticity, applied as a
rotating frame for the detail noise, as in 1.0.

**Not in Phase 2**: the 30 m near-field volume (DESIGN.md §2.5 item 2), the analytic tornado funnel (Phase 3, slots
13-15 are zero), and flow-mapped detail noise (the macro volume moves with the wind, but the sub-grid billows are
fixed in the volume frame apart from the meso rotation).

## Look settings

`exposure` (1.7, as 1.0) and `toneMap` (on: 1.0's display curve, for standalone use; off: linear radiance, for a game
with its own HDR tone mapping). `drawBackdrop` draws 1.0's sky and section-road fields wherever the camera sees no
geometry; the storm's shadow on that ground includes the part beyond the volume, for a low sun. The sun comes from
the `sun` Light, or 1.0's evening sun (azimuth 272°, 15° up) if none is set.

**Orientation**: on D3D, Metal and Vulkan the composite flips vertically and the scene-depth lookup does not. This is
verified on D3D12 by the depth-test mast in `skin_depth_test.png`. If a platform shows the storm upside down, or cut
by the wrong objects, tick `invertCompositeFlip` or `invertDepthFlip`.

## Test and results

`unity/SupercellLabTest`: the scene `Assets/Scenes/SupercellSkinTest.unity` has a `SupercellRunner` on
`data/raw/soundings/ddc_20160524_hrrr.json` (copied to `Assets/StormData`), a `StormSkin`, a camera and the sun. Its
URP asset `Assets/Settings/SkinTest_URP.asset` carries the feature. Headless run (do not pass `-nographics`):

```
"C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\Jonny Glessner\Desktop\ClaudeCoding\supercell-lab-2\unity\SupercellLabTest" -executeMethod SkinTests.RunFromCommandLine -logFile skin.log
```

`-skinQuick` runs one time and two views; `-skinTimes 1500,2700` picks the times (model seconds). The test runs the
brain to 103 min, capturing the wind at every timeline frame. For each time it spins the volume up over the
90 seconds before, advecting every 2 s, then renders a warm-up frame followed by the chaser (24 km SSE), wide
(60 km S), close (12 km SSE) and aerial views. Results go to `Screenshots/skin_*.png` and `Validation/SkinReport.md`.
The menu equivalents are **Supercell Lab > Build Skin Test Scene** and **Run Skin Screenshots**.

GPU cost per frame on the RX 7900 XT (D3D12), average tier, ray-march at half of 2560 x 1440, from the final run
(`Validation/SkinReport.md`):

| Work | ms |
| --- | --- |
| volume work per frame: a quarter of the paint (full paint 2.57), advection 0.98, half the light (full 0.71) | 1.98 |
| ray-march and temporal resolve, 1440p at 0.5 scale, chaser view | 0.87 |
| **total per frame at 1440p** | **2.84** |
| (march and resolve at 1080p, 0.5 scale) | 0.61 |

With the calibration session sharing the GPU, the same test measured 3.7-4.0 ms. An RTX 3060 is roughly 2.5x slower
(an estimate from the cards' compute and bandwidth, not a measurement), which gives about 7 ms against the 8 ms
storm budget in DESIGN.md §3. The brain's own run-ahead budget (4 ms per frame) is separate.

## Files

| File | What |
| --- | --- |
| `StormSkin.cs` | the component: binding, playback, volume centre, per-camera march state and temporal history |
| `SkinVolume.cs` | GPU volumes and the per-frame paint / advect / light schedule |
| `WindHistory.cs` | the 2 km wind snapshots, stored by model time |
| `StormSkinFeature.cs` | URP renderer feature (RenderGraph unsafe pass: march, resolve, composite) |
| `CloudNoise.cs` | 1.0's 48³ tiling value noise (same seed, same billows) |
| `Shaders/StormSkinCommon.hlsl` | the parameter block (`S_*`), noise, and the near-ground features shared by paint and march |
| `Resources/StormSkinVolume.compute` | `Build`, `Advect`, `Light`, `Wind` |
| `Resources/StormSkinMarch.compute` | `March`, `Resolve` |
| `Resources/StormSkinComposite.shader` | full-screen composite |

## Known limits

- Distant cauliflower detail is grainy: the fine noise octaves alias at half resolution. The temporal resolve
  smooths it over a few frames, but the test renders only 6.
- The 1440p march cost depends on the view. A camera inside the storm takes more steps than one 24 km away.
- Only D3D12 has been run.
