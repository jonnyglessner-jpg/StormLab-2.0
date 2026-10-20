# Timeline format (brain to designer and renderer)

The storm-scale model (the "brain", this folder) runs ahead of playback and writes a **timeline**. Phase 2 (the
feature designer and the volumetric renderer) and Phase 3 (the tornado model) read only the timeline. They never
touch the model's GPU buffers, except through `Seek` when they need full-resolution fields at one moment.

Format version 1, 2026-10-18. Code: `TimelineFrame.cs` (the frame), `Timeline.cs` (storage, checkpoints, feature
extraction) and `StormRun.cs` (recording, seek).

## 1. What is stored and when

| Item | Cadence | Where | Size (80 x 80 km domain, either tier) |
| --- | --- | --- | --- |
| `TimelineFrame` | every 10 sim-seconds, frame `index` = t / 10 | `run.timeline` ring buffer, system RAM | about 180 KB (measured 160 KB on the test grid; see the validation report for the tier numbers) |
| `Checkpoint` | every 15 sim-minutes, from t = 0 | `run.checkpoints`, system RAM | 84 MB average tier, 185 MB high tier |

A 2-hour storm is 721 frames (about 130 MB) plus 9 checkpoints (0.75 GB average, 1.7 GB high). The ring's
capacity is the run length / 10 s + 2, so nothing is evicted in a normal run. A smaller capacity evicts the oldest
frames; `Seek` can regenerate them.

## 2. Coordinates and units

- SI throughout: m, s, m/s, K, kg/kg, 1/s. Hail sizes in mm, rain and hail accumulations in mm of water.
- **Positions are storm-relative grid coordinates**: metres from the domain's south-west corner, +x east, +y north.
  The domain is 80 x 80 km and 18 km deep.
- The grid moves over the ground at `frameMotion` (the Bunkers right-mover of the input sounding, constant for the
  run). The ground position of a grid point at time t is `(x, y) + frameMotion * t + groundOrigin`. The game picks
  `groundOrigin`, the ground position of the domain corner at t = 0.
- Winds in the frame (`anvilWindGrid`, the u and v maps) are relative to the grid. Add `frameMotion` for
  ground-relative winds. `anvilWindSR` is relative to the storm's own estimated motion `stormMotion`.
- Bearings are compass degrees: 0 = north, 90 = east, clockwise.
- Heights are above ground level.
- **1 km blocks**: the maps and volumes group the fine grid into blocks of F x F columns. F = 3 at 333 m and
  F = 4 at 250 m, so blocks are 1 km. `timeline.NCX` x `timeline.NCY` blocks (80 x 80); `timeline.blockSize` in
  m. Block (i, j) is centred at `((i + 0.5) * blockSize, (j + 0.5) * blockSize)`.
- **1 km layers**: `timeline.NL` = 19 layers centred on 0, 1, ..., 18 km. Layer L spans L - 0.5 to L + 0.5 km;
  layer 0 spans 0 to 0.5 km.

## 3. The frame (`TimelineFrame`)

Scalars and small arrays:

| Field | Meaning |
| --- | --- |
| `index`, `t`, `step` | frame number, model time (index x 10 s), model step |
| `frameMotion` | grid motion over the ground |
| `stormCentroid` | the tracked storm cell: centroid of the 40 dBZ composite echo within 25 km of the previous frame's cell, followed from frame to frame (radar-style tracking); NaN before there is an echo |
| `stormMotion` | ground-relative storm motion: least-squares drift of `stormCentroid` over the last 10 minutes along a contiguous track (no jump over 3 km between frames), plus `frameMotion`. It equals `frameMotion` until there are 3 minutes of track and is carried from the previous frame when the track breaks |
| `wMax` | strongest updraft in the domain |
| `updraftPos`, `updraftZ` | column and height of that maximum (the updraft core) |
| `wColumn[NZ+1]` | w through the core at every model face level; heights in `timeline.zw` |
| `updraftCentroid5km`, `updraftArea5km` | centroid (NaN if none) and area of w > 10 m/s on the 5 km layer within 12 km of the core |
| `meso1`, `meso4` | mesocyclone at 1 km and at 4 km, searched within 15 km of the core: `present` (block-mean vorticity at least 0.004 1/s), `pos` (vorticity-weighted centre), `zeta` (peak 1 km block-mean vertical vorticity, the mesocyclone-scale number that CM1 and 1.0 report), `zetaPeak` (grid-point peak in those blocks, grid dependent), `radius` (region above half the peak) |
| `mesoBase`, `mesoTop`, `mesoDepth` | vertical extent of the column around the 4 km meso where block vorticity stays at or above 0.01 1/s (0 if none) |
| `zeta0Peak` | grid-point vorticity peak below 500 m within 10 km of the low-level meso (for Phase 3's genesis test) |
| `w1UnderMeso` | strongest w on the 1 km layer within 5 km of the low-level meso (low-level stretching) |
| `coldPoolMin` | coldest lowest-level theta' (K, negative = cold) |
| `coldPoolArea`, `coldPoolCentroid`, `coldPoolBox` | area colder than -1 K (km^2), its centroid, and its bounding box (min x, min y, max x, max y) |
| `gustFront[36]` | gust front as a polar outline: distance from `coldPoolCentroid` to the outer edge of the -1 K region along bearings 0, 10, ..., 350 degrees |
| `updraftToGustFront` | signed distance from the low-level updraft (the 1 km meso if present, else the core) to the -1 K edge. Positive = still in warm inflow air; negative = undercut by the cold pool (the occlusion signal); 1e6 = no cold pool |
| `rfdPresent`, `rfdCentroid`, `rfdArea` | rear-flank downdraft: blocks within 12 km of the low-level meso with w < -2 m/s on the 1 km layer, surface theta' < -0.5 K, and not downshear of the meso (downshear = the 0-6 km shear vector, where the forward-flank precipitation falls). Present when the area is at least 2 km^2 |
| `rfdMinW`, `rfdMinTheta`, `rfdMeanTheta` | its strongest downdraft and its coldest and mean surface theta' (warm RFD = small deficit, which matters for the tornado model) |
| `rfdReach`, `rfdMaxWind` | farthest extent from the meso (grows during a surge) and the strongest ground-relative surface wind inside it |
| `anvilWindGrid`, `anvilWindSR` | ice-weighted mean wind at 8-14 km, relative to the grid and relative to the storm |
| `echoTop18`, `echoTop50`, `cloudTop` | highest 18 dBZ and 50 dBZ echo and the highest cloud (qc + qi > 0.01 g/kg), the echo-top proxies |
| `maxDbz`, `maxHailMm`, `maxRainMm`, `maxHailAccMm` | domain maxima: reflectivity, hail size at the ground now, accumulated rain, accumulated hail |

### 3.1 Maps: `map2D` (whole domain, 1 km, byte-quantised)

Layout `[field][j][i]`, `Map2D.Count` = 9 fields of `NCX x NCY` bytes. Decode with `Map2D.Decode(field, byte)`,
which returns `offset + byte * scale`:

| # | Field | Per block | Offset | Scale | Range |
| --- | --- | --- | --- | --- | --- |
| 0 | `CompositeDbz` | max of the column-maximum reflectivity | -20 | 0.4 | -20 to 82 dBZ |
| 1 | `Dbz1km` | max reflectivity on the level nearest 1 km | -20 | 0.4 | -20 to 82 dBZ |
| 2 | `ThetaPrimeSfc` | min lowest-level theta' | -16 | 0.1 | -16 to 9.5 K |
| 3 | `WMax` | max w in the column | 0 | 0.4 | 0 to 102 m/s |
| 4 | `WMin3km` | min w below 3 km | -51 | 0.2 | -51 to 0 m/s |
| 5, 6 | `USfc`, `VSfc` | mean lowest-level wind, grid-relative | -51 | 0.4 | -51 to 51 m/s |
| 7 | `HailSizeMm` | largest hail at the ground now | 0 | 0.5 | 0 to 127 mm |
| 8 | `RainMm` | max accumulated rain | 0 | 1 | 0 to 255 mm |

### 3.2 Volumes: `q3D` (storm-centred window, 1 km, log-quantised)

A window of `winNX x winNY` blocks (40 x 40 km by default, `FeatureExtractor.windowBlocks`) starting at block
(`winI0`, `winJ0`). It is centred on the 5 km updraft centroid, or on the core when there is none. It covers all
`NL` layers. Layout `[field][layer][j][i]` with fields `Q3D.Field` = qc, qr, qi, qh: cloud water, rain, cloud ice,
hail. Values are layer and block means. Decode with `Q3D.Decode(byte)`: 0 means below 1e-6 kg/kg; otherwise
`1e-6 * 10^((b - 1) / 56)`, which runs from 1e-6 to 3.4e-2 kg/kg in steps of 4 %.

The window moves with the storm. Compare `winI0` and `winJ0` between frames before blending two frames.

## 4. Reading the timeline

```csharp
var runner = GetComponent<SupercellRunner>();          // or new StormRun(...) + RunAheadScheduler
Timeline tl = runner.Timeline;
float playback = 1800f;                                // the game's time slider, s
if (playback <= tl.Horizon && tl.Bracket(playback, out var a, out var b, out float w))
{
    Vector2 meso = Vector2.Lerp(a.meso1.pos, b.meso1.pos, w);
    float dbz = Map2D.Decode(Map2D.Field.CompositeDbz, a.map2D[(int)Map2D.Field.CompositeDbz * tl.NCX * tl.NCY + j * tl.NCX + i]);
}
tl.FrameAdded += f => { /* called on the main thread from StormRun.Pump as frames finish */ };
```

- `Horizon` is the time of the newest finished frame. Scrubbing past it means waiting for the sim.
- `Get(index)` returns null for frames not yet computed or evicted. `AtTime(t)` returns the frame at or before t.
- Frames are immutable once stored. A replay after `Seek` stores new, identical frame objects at the same indices.

## 5. Checkpoints and seek

A checkpoint holds every prognostic field (u, v, w, theta, qv, qc, qr, qi, qs, qh) as fp16 perturbations from the
base state, two halves per `uint`, in the model's own C-grid layouts (see `SimGrid`). It also holds the surface rain
and hail accumulations in fp32 and the model time and step.

At each checkpoint the **live state is quantised in place**: the run continues from exactly the fp16 state that was
saved. So `StormRun.Seek(t)`, which restores the latest checkpoint at or before t and re-runs to t, reproduces the
original run **bit for bit** on the same GPU and driver. The validation checks this: 60 of 60 replayed frames are
identical. The quantisation error is at most about 0.03 m/s and 0.002 K, far below the model's own noise.

After `Seek(t)` the model's full GPU state is at t. Phase 2 can then read full-resolution fields, such as the wind
field for advecting the cloud volume, from `run.model.U/V/W` and the condensate buffers. Run-ahead continues from
t. Frames already computed beyond t stay valid. Re-running over them recomputes identical frames.

Determinism: the same environment, `RunSetup` (tier, seed, initiation, outflow) and GPU give the same storm. A
different GPU vendor or driver gives a storm that is statistically the same but not bitwise identical, because
fused multiply-adds and transcendental functions differ in the last bits and the flow amplifies those differences.
Replays and seeks are therefore exact only on the machine that ran the storm. Ship the timeline, not the seed, if
two machines must agree (for example, multiplayer).

## 6. Stability promise

Fields are only ever added to `TimelineFrame`, never renamed or re-scaled within format version 1. If an encoding
has to change, the version number above changes and `Map2D` / `Q3D` grow a version check.
