# Runtime/Designer: the feature designer (Phase 2)

The translator between the brain and the skin (DESIGN.md §2.3). It reads the timeline ([../Dynamics/TIMELINE.md](../Dynamics/TIMELINE.md))
and the environment, and writes the skin's **parameter block**: 40 `float4` slots that say where each visible
feature is, how big it is and how strong. It is pure C#: no GPU and no Unity objects, about 0.1 ms per call.

```csharp
var designer = new FeatureDesigner(run.timeline, run.env);
var P = new SkinParams();
designer.Evaluate(playbackSeconds, wallClockSeconds, volumeCentreGridMetres, P);
// P[Slot.Wall] = (x km, y km, radius km, amount); P.stage, P.caption; P.fromSim / P.fromFallback
```

`StormSkin` (../Rendering) calls this every frame. Nothing else needs to.

## How it works

1. **Measure each frame.** `Measure(frame)` turns one `TimelineFrame` into `FrameFeatures`: updraft, mesocyclones,
   anvil (ice extent from the 1 km `q3D` window), inflow (warm, rain-free surface wind ahead of the storm),
   forward-flank rain (weighted area and principal axes of the 1 km reflectivity map), the hail core (55 dBZ composite),
   RFD and hook rain, rain wrap round the meso, cold pool, flanking line along the gust front, and vault depth
   (the height of the echo overhang above the low-level updraft, a 1 km BWER). Frames never change, so results are
   cached by frame index.
2. **Blend in time.** Each quantity is a Gaussian-weighted mean over the frames around the playback time
   (σ = 25 s; σ = 150 s for storm motion and the volume centre). A feature present in only some frames gets a
   fractional amount, so it fades instead of popping. The result depends only on the time, so scrubbing backwards
   works and the designer keeps no state.
3. **Write the block.** Slots 0-20 keep StormLab 1.0's meaning (`design.ts`), so the ported shaders read them
   unchanged. Slots 21-39 are new (see `SkinParams.cs`): both mesocyclones, meso depth and occlusion, the overshooting
   top, anvil wind and age, the mammatus band, inflow, the tail bands, the vault, RFD dryness, the FFD shape, hook
   rain, the cold pool, the flanking line, storm motion, the environment, the time and stage, and source flags.
4. **Fall back per feature.** A feature with no signal in the sim over the blend window uses 1.0's scripted rule
   (`StormDesign.At` at the same storm age), placed at the simulated updraft. `P.fromSim` and `P.fromFallback`
   (also in slot 38) record which source drew what. On a capped day the sim stays quiet and 1.0 draws the bubbling
   cumulus.

| Feature | Driven by (timeline) | Fallback (1.0) used when |
| --- | --- | --- |
| Updraft tower: top, radius, tilt | `cloudTop`, `updraftArea5km`, low-level vs 5 km updraft position | never once there is an updraft |
| Overshooting top | `cloudTop` above the anvil top | no anvil yet |
| Anvil: top, base, length, backshear, direction | `q3D` ice: area per layer, downwind and upwind extent; `anvilWindSR`. Length is extrapolated at the SR wind once the ice reaches the window edge | no ice spreading yet |
| Mammatus | ice at the anvil base 6-20 km downwind, after 15-30 min of anvil | no anvil-base ice |
| Striation, mid-level spin | 4 km meso vorticity (spin = ζ/2, the real angular velocity) × 1.0's LP/classic/HP striation factor | no 4 km meso after 20 min |
| Wall cloud, tail cloud, scud | 1 km meso position, radius and ζ; `w1UnderMeso`; FFD strength; occlusion (`updraftToGustFront`) | no 1 km meso after 30 min |
| Inflow tail | storm-relative surface inflow: direction, speed (length 6-16 km, 1-3 bands), boundary-layer RH from the LCL | no inflow region found |
| Vault | BWER depth over the low-level updraft | no overhang in `q3D` |
| FFD, hail core | 1 km and composite reflectivity maps | no 30 dBZ forward-flank rain after 14 min, no 55 dBZ core after 18 min |
| RFD, clear slot, horseshoe | `rfdCentroid`, `rfdReach`, `rfdMeanTheta` (warm RFD = deeper slot), rain wrap (HP) | no RFD after 30 min |
| Flanking line | gust-front outline in the rear-right sector, cold-pool area | no cold pool after 18 min |
| Anti-mesocyclone | not in the timeline | always 1.0 (the anticyclonic tornado is zeroed for Phase 3) |
| Tornado (slots 13-15) | Phase 3 | zero |

## Choices worth knowing

- **Occlusion only partly removes the wall cloud.** Once the gust front is 6 km past the low-level updraft, the wall
  cloud shrinks to 30 % but does not vanish while the 1 km meso lives. The brain's cold pools run 1-3 K too cold
  (Dynamics README), so its occlusion signal comes early and stays on: in the Dodge City run it is on from about
  45 min onward. A full cut-off would hide the wall cloud for the rest of the storm.
- **The volume centre** (`P.suggestedCentre`) is the low-level updraft, moved 9 km downwind along the anvil wind,
  so the anvil fits in the 60 km volume. `StormSkin` follows it in whole voxels.
- **The storm motion** behind slot 0 is blended over σ = 150 s, because the brain's per-frame estimate scatters.

## Verified

`SupercellLabTest/Assets/Editor/SkinTests.cs` logs each shot's sources. In the Dodge City 2016-05-24 HRRR run, at
25, 45, 70 and 100 min every feature group came from the simulation and none from the fallback (see
`SupercellLabTest/Validation/SkinReport.md`).
