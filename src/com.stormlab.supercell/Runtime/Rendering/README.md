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
on the storm (`WorldToVol`, `VolToWorld`). Atmospheric motion, including detail flow and integrated rotation, uses
playback/model seconds: 1x is physical time, pause freezes motion, and timelapse scales it consistently. The second
argument of `StepTo(t, clock)` remains a presentation wall clock and no longer drives atmospheric shaders.
See [cloud-motion.md](../../../../docs/meteorology/cloud-motion.md) for the source map, checks and limitations.

## What happens each frame

| Step | Where | Work |
| --- | --- | --- |
| Designer | `FeatureDesigner.Evaluate` (CPU) | parameter block at the playback time ([../Designer/README.md](../Designer/README.md)) |
| Paint | `StormSkinVolume.compute` `Build` | 1.0's BUILD: the designed storm into the **target** volume, 1/`buildSlabs` of it per frame (default 4) |
| Advect | `Advect` | the **live** volume is carried by the simulated wind for the playback dt (semi-Lagrangian; rain also falls at 7 m/s), then relaxed toward the target with time constant `relaxSeconds` (60 s) |
| Light | `Light` | 1.0's LIGHT on a 1/3-resolution grid (sun optical depth, sky, base shade, anvil ice shade), 1/`lightSlabs` per frame |
| March | `StormSkinMarch.compute` `March` | 1.0's RENDER at `renderScale` (0.5): sub-grid billows, striations in a frame turning at the simulated meso spin, the anvil's ice sheets, near-ground features at full detail, rain shafts, light shafts; stops at scene depth |
| Resolve | `Resolve` | temporal accumulation: reprojects the previous frame by the march's mean hit distance, clamped to the current 3 x 3 neighbourhood |
| (checkerboard) | `March`, `Resolve` | with `checkerboard` on, the march runs at half width on alternating pixels each frame; the resolve fills the rest from fresh neighbours and history (about half the cost) |
| Composite | `StormSkinComposite.shader` | `colour = storm.rgb + storm.a × scene` over the camera colour (`BeforeRenderingTransparents`) |

**Volume**: 400 x 400 x 120 at 150 m (60 x 60 x 18 km), RGBA8 (r cloud, g precipitation, b near-ground features for
shadows, a material from cauliflower water to smooth ice). Three copies (target and two live) = 231 MB, plus a 6 MB
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

**Detail flow**: fine turbulent billows within 8 km use two wind-driven phases, including vertical motion;
the near-field volume also samples flow-mapped detail. Coarse distant billows and some analytic base textures
remain tied to their feature frames. The 30 m near-field volume and analytic funnel are implemented; the original
Phase 2 exclusion list is obsolete. See the motion audit above for what is and is not physically transported.

## Look settings

**Sunlight on cloud** (`physicalLighting`, on by default). The light grids store the sun's optical depth (half floats),
and the march lights cloud from it with Wrenninge et al.'s (2013) multiple-scattering octaves: `scatterOctaves` terms,
each with half the extinction, energy and phase anisotropy of the one before. The phase function for water cloud is
Jendersie and d'Eon's (2023) fit to Mie theory for `dropletDiameter` (16 µm), which gives silver linings when back-lit,
and anvil ice uses Henyey-Greenstein with g = 0.75. `sunScale` sets their brightness against 1.0's calibration. Rain,
light shafts, the ground shadow and the tornado still use 1.0's two-lobe transmittance of the same depth (`sunT`).
`octaveExtinction` (0.7) is each octave's extinction relative to the one before; Wrenninge's 0.5 carried orange low-sun
light so deep into the core that it turned muddy brown.

**Sky and ground light** (physical lighting). `SkyVis` traces 19 cones from each point of a half-resolution sky grid
(1/`skySlabs` of it per frame) and stores how much of the zenith cap, the mid sky, the horizon ring and the horizon toward
the sun or moon each point sees. `SkyLighting.Bands` integrates the sky's radiance over the same four parts once a frame on
the CPU (constants, so they cost the march no registers), and the march multiplies the two: an underside sees only the
horizon, a shadowed side the open sky beside it. `GroundLight` maps how lit the ground is over the 4 km under each point,
and the march bounces it onto undersides (Plains albedo ~0.12, plus town light at night). `skyDiffuse` (0.2) is sky light
that diffused through the cloud to points that cannot see the sky; `skyScale` and `bounceScale` scale the rest.

**Night** (`SkyLighting.cs`, Night settings). The key light (light grids, phase, shadows) is the sun until it is 6
degrees down, then the moon (`moonPhase`; with TimeOfDay it is also placed in the sky). Cloud high enough to see over the
earth's curve keeps the sun after ground sunset, so anvils glow pink. The sky falls through twilight as measured (1/10 by
6 degrees down, starlight by 18), and moonlight is physical (a full moon is ~5e-6 of the evening sun). Below sunset levels
an adaptation gain (`nightAdaptation`) shows a moonlit night at `nightBrightness` (0.08 of day) and never below
`nightFloor`, with a blue shift (`purkinje`), a fill under the base (`nightFill`) and town light on the haze and cloud bases
(`lightPollution`). With `toneMap` off and a game exposure that adapts itself, turn `nightAdaptation` off.

Off, the cloud is lit as in 1.0 (the night model still applies). Skin test flags: `-skinAB` also renders every shot with
1.0's lighting as `*_legacy.png`; `-skinNight` adds dusk, moonlit and moonless views; `-skinSet field=value,...` sets any
`StormSkin` field for look tuning; `-skinLegacyLight` uses 1.0's lighting throughout.

`exposure` (1.7, as 1.0) and `toneMap` (on: 1.0's display curve, for standalone use; off: linear radiance, for a game
with its own HDR tone mapping). `drawBackdrop` draws 1.0's sky and section-road fields wherever the camera sees no
geometry; the storm's shadow on that ground includes the part beyond the volume, for a low sun. The sun comes from
the `sun` Light, or 1.0's evening sun (azimuth 272°, 15° up) if none is set.

**Orientation**: on D3D, Metal and Vulkan the composite flips vertically and the scene-depth lookup does not. This is
verified on D3D12 by the depth-test mast in `skin_depth_test.png`. If a platform shows the storm upside down, or cut
by the wrong objects, tick `invertCompositeFlip` or `invertDepthFlip`.

## Test and results

For repeatable base-feature reviews, run `DesignedSkinTests.RunFromCommandLine -designedOnly anatomy`
with the same Unity batch command below. This uses a fixed Plains seed and mature cone, with inflow, rear-slot,
aerial, side-lit, backlit and interior cameras, plus the inflow camera without the near volume. It writes
`Screenshots/anatomy_*.png` and `validation/reports/AnatomySkinReport.md`. Use `-designedOnly sheet` to check
all nine tornado shapes. The batch run fails on logged rendering errors as well as missing tornadoes.

The low cloud and clear-slot boundaries share rotating water-cloud texture. The horseshoe has a rounded lip,
the near-field layers use continuous angular variation and subdued underside shading, and the exposed interior
keeps billows rather than plate bands. Funnel lighting blends into the parent cloud over the upper flare;
debris retains a dense center with irregular outer streamers, filtered texture and local self-shading.

`tests/SupercellLabTest`: the scene `Assets/Scenes/SupercellSkinTest.unity` has a `SupercellRunner` on
`validation/soundings/ddc_20160524_hrrr.json` (copied to `Assets/StormData`), a `StormSkin`, a camera and the sun. Its
URP asset `Assets/Settings/SkinTest_URP.asset` carries the feature. Headless run (do not pass `-nographics`):

```
"C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\Jonny Glessner\Desktop\ClaudeCoding\StormLab-2.0\tests\SupercellLabTest" -executeMethod SkinTests.RunFromCommandLine -logFile skin.log
```

`-skinQuick` runs one time and two views; `-skinTimes 1500,2700` picks the times (model seconds). The test runs the
brain to 103 min, capturing the wind at every timeline frame. For each time it spins the volume up over the
90 seconds before, advecting every 2 s, then renders a warm-up frame followed by the chaser (24 km SSE), wide
(60 km S), close (12 km SSE) and aerial views. Results go to `Screenshots/skin_*.png` and `validation/reports/SkinReport.md`.
The menu equivalents are **Supercell Lab > Build Skin Test Scene** and **Run Skin Screenshots**.

GPU cost per frame on the RX 7900 XT (D3D12), average tier, ray-march at half of 2560 x 1440, from the final run
(`validation/reports/SkinReport.md`):

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
