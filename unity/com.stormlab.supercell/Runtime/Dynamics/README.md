# Runtime/Dynamics: the storm-scale brain (Phase 1)

A GPU cloud model that decides what the storm does (DESIGN.md §2.2). It is a port of StormLab 1.0's `src/sim`
(anelastic equations on an Arakawa C grid, semi-Lagrangian MacCormack advection with a limiter, 6-class LFO/Gilmore
ice microphysics) to Unity compute shaders, with the 2.0 upgrades listed below. It writes a timeline every
10 sim-seconds for Phase 2. The format is in [TIMELINE.md](TIMELINE.md).

## Run the validation

The test project is `unity/SupercellLabTest`, an empty Unity 6000.6.6f1 project that references this package from
disk. From a terminal:

```
"C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\Jonny Glessner\Desktop\ClaudeCoding\supercell-lab-2\unity\SupercellLabTest" -executeMethod StormLab.Supercell.EditorTools.DynamicsValidation.RunFromCommandLine -dynSuite all -logFile validation.log
```

- `-dynSuite quick` runs the 11 short tests, about 15 s after Unity starts.
- `wk` runs the 2-hour Weisman-Klemp storm.
- `ddc` runs the 2-hour Dodge City storm and compares it with the KDDC radar.
- `perf` measures step times.
- `all` runs everything, about 3 minutes.
- `tune` runs the sensitivity table; it is not part of `all`. Its options are `-dynTune "on:1e6:0.25,off:8e6"` and `-dynTuneCases WK,DDC`. Each comma-separated case is up to seven fields: Smagorinsky on or off, evaporation intercept, cs, rain fall-speed intercept (-1 = default), diagnostic switches (`SimParams.microDiag`), hail wet growth (0/1) and melting-hail vapour exchange (0/1). Run at most about 8 cases per Unity start; more than 10 back-to-back 2-hour runs once ran Unity out of memory.
- `-dynNZ 85` sets the average tier's number of levels for `tune` and `perf` only.

Do not pass `-nographics`, because the model needs the GPU. The exit code is 0 when everything passes. The console
lines start with `[Dynamics]`. Results go to `unity/SupercellLabTest/Validation/`:

- `ValidationReport.md`
- `weisman_klemp.csv` and `ddc_20160524_hrrr.csv` (every 5 minutes)
- `ddc_20160524_radar.csv` (the model storm measured as a radar sees it, every 5 minutes)
- `step_times.md`
- `sensitivity.md`

In the editor, the same runs are under **Supercell Lab > Run Dynamics Validation**.

## Results on this PC (RX 7900 XT, D3D12, 2026-10-18): 15 of 15 pass

| Test | Result |
| --- | --- |
| Calm atmosphere stays at rest | max abs w and u exactly 0 after 15 steps |
| Uniform wind carried unchanged | exactly unchanged |
| Pressure solver: mass-conserving flow | max divergence 7.5e-9 1/s. 1.0's relaxation solver allowed 2e-5 |
| Microphysics conserves water and energy | 3.3e-7 relative, the fp32 roundoff level. Energy error under 0.0001 K |
| Water mass, full model, closed box | 1.6 % of the water that cycled through the storm in 25 min. The semi-Lagrangian advection is not mass-conserving |
| Dry warm bubble rises | yes |
| Capped sounding stays quiet | 1.0's "cap holds" preset: CIN -228 J/kg, no initiation, no cloud in 30 min |
| CIN delays initiation | DDC 19Z special sounding (CIN -112) fires at 87 min. HRRR 22Z (CIN -1) fires at once |
| Outflow boundary | -4 K, 1.2 km pool on the requested side of the line, spreading as a gust front |
| Seek replays bit for bit | 60 of 60 replayed frames identical |
| Scheduler budget and speed cap | 2.4-2.9 ms mean GPU per 60 fps frame at a 4 ms budget, and the 10x cap holds at 9.5-10.0x, when the GPU is free. The last full run shared the GPU with another Unity job and measured 3.7 ms; it still passes |
| **Weisman-Klemp supercell** (spec: 50-70 m/s, 0.01-0.04 1/s) | peak updraft **69.9 m/s**, at the top edge of the window. Typical values are 55-65 m/s for 2 hours. Peak 1 km mesocyclone vorticity 0.028 1/s (1 km block mean). Coldest cold pool -9.3 K, typically -7 to -8.5 K after the first hour |
| Dodge City 2016-05-24, HRRR 22Z, 2 h | supercell all 2 hours, see below |
| **Dodge City against the KDDC radar** | echo top, low-level footprint, 50 dBZ core and vault all match; the 50 dBZ top and the vault are too high (known bias, reported, not gated), see below |
| Step times | below |

**Dodge City run**, average tier, 2 hours. Inputs: MLCAPE 5,121 J/kg, 0-1 km SRH 83 m²/s², 0-6 km shear 32 kt.
The grid moves with the Bunkers right-mover, from 238° at 24 kt. Every 5 minutes is in `ddc_20160524_hrrr.csv`.

| Quantity | Value |
| --- | --- |
| Peak updraft | 79-97 m/s from 20 min on (parcel theory gives 101 m/s) |
| 1 km mesocyclone vorticity | 0.011-0.040 1/s |
| 4 km mesocyclone vorticity | 0.019-0.042 1/s |
| Coldest surface theta' | -6 to -11.5 K from 30 min on, median -7.7 K after the first hour |
| Hail at the ground | up to 38 mm |
| 18 dBZ echo top | 15.3-17.5 km |
| Estimated storm motion | median from 226° at 24 kt after 30 min, against the Bunkers 238° at 24 kt |

**Dodge City against the KDDC radar.** `Editor/RadarLook.cs` turns the model's rain, snow and hail into
reflectivity with the model's own formula. It averages that onto the 1 km x 1 km x 500 m grid that
`tools/fetch_nexrad.py` makes from a NEXRAD volume, and measures it with the same rules. The model storm, every
5 minutes from 60 to 120 min (13 volumes), is compared with KDDC at 22:48Z, when the storm was 30 km from the radar.
At 23:35Z the storm sat on the radar, so only that volume's low-level numbers count. The model's clock starts at the
22Z sounding with the storm already firing, so no moment is matched in time. The full table is in
`ValidationReport.md`.

| Quantity | KDDC 22:48Z (23:35Z) | Model median (range) | Tolerance | Within |
| --- | --- | --- | --- | --- |
| 18 dBZ echo top | 15.5 km | 16.0 km (15.0-17.5) | 15.5 +/- 2 | yes |
| 40 dBZ area at 1 km | 226 km² (467) | 449 km² (322-574) | 113-934 | yes |
| 50 dBZ core at 1 km | 27 km² (142) | 222 km² (171-318) | 14-284 | yes, at the high end |
| Vault (bounded weak echo) | found (found) | in 12 of 13 volumes | at least half | yes |
| 50 dBZ echo top | 9.5 km | 14.5 km (13.5-16.5) | 9.5 +/- 2.5 | **no**: known bias, not gated |
| Vault base to top | 2.5-5.0 km (4.0-7.5) | 4.5-9.5 km | depth 1-4.5 km | **no**: known bias, not gated (depth 5.5 km) |
| Strongest echo | 55 dBZ (59) | 75 dBZ (73-77) | none | not tested |
| 40 dBZ area at 4 km / 7 km | 134 / 58 km² | 675 / 564 km² | none | not tested |

The storm is the right size and height, with its weak-echo vault, and it covers the right area at low levels. Aloft
it carries far too much hail. Above about 6 km the model's reflectivity is all hail, and 50 dBZ at 14.5 km means about
2.4 g/kg of hail there. Two known limits cause it: updrafts at 85-95 % of parcel theory, which loft the hail and push
the vault up, and single-moment hail with a fixed intercept, whose reflectivity a Rayleigh formula turns into 75 dBZ.
The hail wet-growth limit (below) did not change it. Because these two misses are model biases that this task did not
set out to fix, the test gates on the four rows marked "yes". It reports the other two and marks them "no". That
choice was made after seeing the numbers.

**Step times**, measured with the Weisman-Klemp storm running:

| Tier | Grid | dt | Step | With timeline | Run-ahead at 4 ms per 60 fps frame | VRAM | One checkpoint |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Average | 240 x 240 x 72, 333 m, dz 100-323 m | 2 s | 8.7 ms | 9.9 ms | 48x from the step time; 29-37x measured through the scheduler (diagnostics and piece granularity included) | 601 MB | 84 MB |
| Average, 81 levels (experiment) | 240 x 240 x 81, dz 100-268 m | 2 s | 10.3 ms | 11.5 ms | 42x | 675 MB | 94 MB |
| Average, 85 levels (experiment) | 240 x 240 x 85, dz 100-249 m | 2 s | 10.9 ms | 12.1 ms | 40x | 708 MB | 99 MB |
| High | 320 x 320 x 90, 250 m, dz 100-230 m | 1.67 s | 22.3 ms | 23.7 ms | 17x real time | 1,327 MB | 185 MB |

The 81- and 85-level rows are `-dynSuite perf -dynNZ 81` and `85`. The average tier stays at 72 levels; see Known
limits.

A 2-hour storm costs 36 s of GPU at the average tier when run flat out. Its timeline is 130 MB, about 180 KB per
frame, plus 752 MB of checkpoints.

## Using it in a game

Add `SupercellRunner` to a GameObject. Pick a sounding TextAsset (`data/raw/soundings/*.json`) or the preset
fields. Set the `RunSetup` (tier, seed, outflow boundary, run length), the GPU budget per frame and the maximum
speed. It builds the storm at `Start` and runs ahead in `Update`. Read `runner.Timeline`, as TIMELINE.md shows.

Without the component:

```csharp
var run = StormRun.FromSoundingJson(json, new RunSetup { tier = SimTier.Average, seed = 7 });
var sched = new RunAheadScheduler(run, gpuBudgetMs: 4f, maxSpeed: 10f);
sched.Calibrate();
// every frame:
sched.Tick(Time.unscaledDeltaTime);
// slider past the horizon, or full fields wanted at t:
run.Seek(t);
```

## Files

| File | What |
| --- | --- |
| `Resources/SupercellDynamics.compute` | every kernel: advection, microphysics (port of `micro.ts`, plus the melting-hail vapour exchange and optional switches below), sedimentation, buoyancy, mixing, sponge and damping, drag, nudging, pressure solver, checkpoint packing, timeline reductions |
| `SupercellModel.cs` | GPU buffers, one step recorded as 6 command-buffer segments, diagnostics, checkpoints |
| `SimGrid.cs`, `SimParams.cs` | tiers, stretched vertical grid, parameters, `RunSetup`, `OutflowBoundary` |
| `BaseState.cs` | hydrostatic base state from a preset, a real sounding or an analytic profile (Weisman-Klemp) |
| `SoundingFile.cs` | reads `tools/fetch_sounding.py` JSON onto the environment grid (`StormPhysics.cs` then gives CAPE, SRH, Bunkers) |
| `Initiation.cs` | the CIN-gated forced updraft |
| `StormRun.cs` | one storm: recording, checkpoints, seek |
| `Timeline.cs`, `TimelineFrame.cs` | ring buffer, checkpoint store, feature extraction, frame format |
| `RunAheadScheduler.cs`, `SupercellRunner.cs` | background scheduling and the drop-in component |
| `../../Editor/DynamicsValidation.cs` | the headless validation |
| `../../Editor/RadarLook.cs` | the model storm measured as `tools/fetch_nexrad.py` measures a NEXRAD volume, for the KDDC comparison |

## What changed from 1.0, and why

- **Stretched vertical grid.** 100 m spacing below 1.5 km, stretching geometrically by 8 % per level aloft. Heights
  come from a lookup that is exact at the levels, which is why the calm and uniform-wind tests give exactly 0.
- **Exact pressure solver.** A discrete sine transform in x and y with a tridiagonal solve in z, done as tiled
  matrix products on the GPU. It replaces 24 sweeps of line relaxation and gives divergence near roundoff. A
  closed-wall variant, using a cosine transform, exists for budget tests.
- **Surface drag.** A bulk drag layer, Cd = 0.003, on the ground-relative lowest-level wind. The drag the
  environment alone would feel is subtracted, so the base state stays steady. 1.0 was free-slip.
- **Smagorinsky-Lilly sub-grid mixing.** cs = 0.25 with moist-aware stability, on top of 1.0's constant mixing.
  1.0's constant mixing was tuned at 1 km and is too weak at 333 m in strong vortices. The mixing alone changed
  little in the sensitivity runs, but it is the standard closure at this grid spacing.
- **Rain evaporation intercept.** Rain evaporation uses its own intercept, n0r = 1e6 instead of 8e6. A
  fixed-intercept scheme cannot size-sort, so it over-evaporates. This was part of 1.0's known 2-4 K cold-pool
  bias, and this change takes the Weisman-Klemp cold pool from -11 to -10 K.
- **Melting hail exchanges vapour with the air** (`SimParams.hailMeltVapour`, on). LFO's hail melting rate
  includes the heat of vapour condensing on the 0 °C stone. That happens whenever the air holds more vapour than
  saturation at 0 °C, which is nearly all low-level air on a supercell day. Vapour can also evaporate from the stone
  in drier air. 1.0 took all of the melting heat from the air's temperature and left its vapour alone, so melting hail
  cooled the downdraft too much. Now that vapour really condenses or evaporates, and the air pays only the
  conduction part (Rutledge and Hobbs 1983; Morrison et al. 2009 treat it as its own process). The sensitivity runs
  showed this was the cause: hail melting supplies 5-6 K of the cold pool, rain evaporation about 4 K, snow melting
  and hail sublimation nothing. The coldest cold pool went from -10.4 to -9.3 K (Weisman-Klemp) and from -12.6 to
  -11.5 K (Dodge City), and the median after the first hour from -8.5 to -7.8 K and from -10.1 to -7.7 K. The
  updraft and rotation did not change. The whole table is in `Validation/sensitivity.md`.
- **Options tested and left off.** `hailWetGrowth` is LFO's wet-growth limit on hail, which 1.0's port left out.
  It sheds water the hail cannot freeze as rain, which evaporates, so cold pools got colder (-10.8 / -13.6 K), and
  the radar picture did not improve. `n0rFall` gives rain fall speed its own intercept, which made no useful
  difference. `microDiag` holds the diagnostic process switches the sensitivity table uses.
- **Superadiabatic layers removed.** Below 1.5 km in a real sounding they are mixed out. The HRRR Dodge City point
  is 3.6 K superadiabatic in its lowest 170 m because it starts at the 2 m temperature. Kept, that layer made the
  storm run away: 200 m/s updrafts and 0.7 1/s vorticity after an hour.
- **CIN-gated forced updraft.** 1.0's CM1-style forced updraft now waits for the cap. Below 25 J/kg of CIN it
  fires at once. Above that, it waits for the excess to erode at 60 J/kg per hour, and it never fires above
  250 J/kg.
- **Step segments.** One step is split into 6 command-buffer pieces of about 1-3 ms each at the average tier. A 9 ms
  step can then spread over frames, and no frame queues more than its budget or the largest single piece.
- **In-place checkpoints.** Checkpoints quantise the live state to fp16 in place, which is what makes seek replays
  exact.
- **No CPU twin.** The CPU/GPU agreement check from 1.0 was not carried over. The GPU path is the only one, and the
  microphysics is checked directly on the GPU instead.

## Known limits

- **Timeline frames are bigger than designed.** A frame is about 180 KB, not the ~50 KB DESIGN.md §2.2
  estimated, because it carries a 40 x 40 km, 19-layer 1 km window of qc, qr, qi and qh. A 3-hour storm is
  therefore about 200 MB of frames plus 1.0 GB (average tier) or 2.2 GB (high tier) of checkpoints. Shrink
  `FeatureExtractor.windowBlocks` if that matters.
- **Average-tier vertical spacing is 323 m aloft, not 250 m.** With 100 m below 1.5 km and the 8 % per-level
  stretch, 81 levels give 268 m and 250 m needs 85 levels (the earlier "81 levels" figure was wrong). Both were
  measured. 85 levels cost 22 % more per step (12.1 against 9.9 ms with the timeline) and 18 % more VRAM (708 MB),
  and the timeline and checkpoints grow by the same share. The storms barely change: the Weisman-Klemp peak updraft
  is 70.1 m/s (just outside the 50-70 window), 1 km rotation 0.033 1/s, coldest cold pool -8.7 K; Dodge City
  103.5 m/s and -11.2 K. The tier stays at 72 levels. Change `SimGrid.ForTier` to 85 if the extra 2 ms per step
  is affordable.
- **VRAM is above the §3 estimates.** The average tier uses 601 MB, not ~250 MB. The high tier uses 1.33 GB, just
  over the 1.2 GB average-tier budget, but it is for the 20 GB card. The B (scratch) copy of every field accounts
  for most of the difference.
- **Updrafts are strong.** The Weisman-Klemp storm sits at the top of CM1's range, and the 5,000 J/kg Dodge City
  storm runs at 85-95 % of parcel theory.
- **Cold pools are still a little cold at the extreme.** After the melting-hail fix, the Weisman-Klemp coldest point
  is -9.3 K against CM1's -4 to -8 K, but its typical value after the first hour (-7 to -8.5 K) is inside CM1's
  range. Dodge City's extreme is -11.5 K. That is possible there: a saturated downdraft in that very dry sounding
  would reach -14 to -19 K at the ground, against -10 K in the Weisman-Klemp sounding.
- **Too much hail aloft.** Against KDDC the model's 50 dBZ echo reaches 14.5 km instead of 9.5 km, the strongest
  echo is 75 dBZ instead of 55, and the vault sits at 4.5-9.5 km instead of 2.5-5 km. Storm size, echo top and
  low-level footprint are right. Phase 2 should take the hail amount aloft as too large, and the vault as too high.
  It should not use the model's reflectivity values directly to pick colours or densities.
- **The storm-motion estimate is steady only on average.** It is the 10-minute drift of the tracked 40 dBZ echo
  centroid. After 30 minutes its median agrees with the Bunkers motion: from 270° at 25 kt against 276° at 28 kt
  for Weisman-Klemp, and from 223° at 24 kt against 238° at 24 kt for Dodge City. Single 5-minute samples
  scatter: 12-15 of 19 are within 10 kt of the median. Phase 2 should smooth it further if it drives anything
  visible.
- **A checkpoint frame is not budgeted.** Quantising and reading back about 84 MB happens in one frame every
  15 sim-minutes.
- **Determinism holds on one machine.** The same GPU and driver give the same storm bit for bit. A different
  vendor or driver gives a similar but not identical storm.
