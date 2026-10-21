# Supercell Lab 2.0: engine design

Status: proposal, 2026-10-18. Nothing here is built yet except the data tooling in `tools/` and the environment
physics carried over from StormLab 1.0. Read this with `DATA.md` (what the model is calibrated against).

## 1. What 1.0 was, and why 2.0 is different

StormLab 1.0 (`github.com/jonnyglessner-jpg/StormLab`, MIT) has three layers:

| Layer | What it does | Keep? |
| --- | --- | --- |
| Environment physics (`thermo`, `winds`, `environment`, `storm`) | Builds a sounding and hodograph from a preset, computes CAPE/CIN/LCL/EL, SRH, Bunkers motion, SCP/STP, storm mode, LP/classic/HP, tornado odds | Yes. Already ported to C# (`DodgeCity.Storm`), copied into this package. Upgraded to take *real* soundings, not just synthetic ones |
| Designed storm (`design.ts`) | A scripted conceptual-model supercell: every feature's position and size at minute *t*. Same life every run | Keep as the **skin**, no longer as the brain |
| Cloud model (`src/sim`, shelved) | A real anelastic cloud model with 6-class ice microphysics, verified against CM1 at 1 km. Shelved because 1 km grids look blobby | Revive as the **brain**, on a finer grid, in Unity compute shaders |
| Tornado vortex lab | Burgers-Rott / Sullivan vortex, swirl ratio regimes, corner flow, suction vortices, condensation funnel, damage swath | Yes, as the tornado's wind field |

1.0's limits are exactly the things the 2.0 brief asks for: updrafts that rise according to the environment, tornadoes
that form (or fail) for physical reasons, outflow boundaries the storm can ingest, longevity that depends on how the
precipitation interacts with the inflow, and cycling that is not on a fixed 38-minute clock. A scripted storm can
only fake those. A fluid model does them for real but cannot, at any affordable grid spacing, *look* like a supercell
(no wall cloud texture, no striations, no funnel). So 2.0 is a hybrid:

**The simulation decides what happens and where. The designed skin decides what it looks like.
The tornado is a sub-grid model fed by the simulation.**

## 2. Architecture: three scales, three methods

```
 preset / real sounding ──> ENVIRONMENT (CPU, C#)
                                 │ base state: θ(z), qv(z), u(z), v(z), p(z); optional outflow boundary
                                 ▼
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ STORM-SCALE DYNAMICS (GPU compute, 80 x 80 x 18 km, 250-333 m grid)          │
 │ anelastic, 6-class ice microphysics, storm-following frame, deterministic     │
 │ runs ahead of playback; writes a TIMELINE of compact feature states           │
 └──────────────────────────────────────────────────────────────────────────────┘
                                 │ per 10 s: updraft core & w(z), meso centre/strength/depth,
                                 │ cold pool & gust fronts, RFD surge, qr/qh/qi fields (1 km), anvil winds
                                 ▼
 ┌──────────────────────────────────────────────────────────────────────────────┐
 │ FEATURE DESIGNER (CPU, C#)  sim state ──> parameter block for the skin        │
 │ wall cloud, tail cloud, inflow tails, striations, vault, clear slot, horseshoe,│
 │ anvil sheet, overshooting top, mammatus, flanking line, FFD/RFD/hail cores    │
 └──────────────────────────────────────────────────────────────────────────────┘
          │                                   │
          ▼                                   ▼
 ┌────────────────────────┐   ┌──────────────────────────────────────────────────┐
 │ TORNADO MODEL (CPU)    │   │ VOLUMETRIC SKIN (GPU)                            │
 │ genesis / maintenance /│   │ storm volume 150 m + near-field volume 20-30 m,  │
 │ decay rules + vortex   │   │ advected by the sim's winds, ray-marched in URP; │
 │ wind field + damage    │   │ funnel drawn analytically at full detail         │
 └────────────────────────┘   └──────────────────────────────────────────────────┘
```

### 2.1 Environment layer (exists, to be extended)

Input is either a 1.0-style preset (surface T/Td, moisture depth, lapse, cap, three wind numbers) **or a real
sounding** (RAP/HRRR point profile or a RAOB: pressure, height, T, Td, wind at ~50 levels). Output is the base
state on the model's vertical grid plus the diagnostics (MLCAPE, MLCIN, LCL, LFC, EL, 0-1/0-3/0-6 km shear, SRH,
storm-relative winds at anvil level, STP, SCP, freezing level, DCAPE). Each of the eight presets in §5 is a *family*
of real soundings, not one hand-tuned profile, so the same preset can give a different storm each run.

Optional surface features: an **outflow boundary** (a line with a cold-pool depth, temperature deficit and
orientation) and a **dryline / warm front** placeholder (pipeline). The boundary is a real cold pool in the initial
state, so the storm interacts with it physically: enhanced low-level SRH and lower LCL along it, backed surface
winds, and the storm turning right along it when it anchors.

### 2.2 Storm-scale dynamics (the brain)

A Unity compute-shader port of 1.0's `src/sim` (the WGSL kernels already exist; HLSL is a syntax change), with
these upgrades:

- **Grid**: 80 x 80 km, 18 km deep, in a frame moving with the Bunkers right-mover. Average tier 240 x 240 x 72
  (333 m / 250 m), high tier 320 x 320 x 90 (250 m / 200 m). Stretched vertical grid: 100 m in the lowest 1.5 km
  (where the RFD, gust fronts and low-level meso live), 250 m aloft.
- **Pressure solver**: horizontal FFT + vertical tridiagonal instead of line relaxation. Exact, and ~10x faster at
  this size. This is what makes 250 m affordable.
- **Microphysics**: 1.0's six-class LFO/Gilmore scheme, with a hail size diagnostic (so the hail core and hail swath
  are real) and rain evaporation that produces realistic cold pools (1.0's were 2-4 K too cold).
- **Surface**: a drag layer (1.0 had free-slip; surface friction matters for low-level rotation) and the optional
  outflow boundary.
- **Initiation**: a forced warm bubble / nudged updraft (CM1 style), with a "cap break" time that depends on CIN,
  so a capped environment really waits.
- **Determinism**: same preset + same seed = same storm, bit-for-bit, which is what makes seeking and replays
  possible.

What it resolves: the updraft and its rise rate, overshooting-top height, anvil spread, the mid-level and
low-level mesocyclone (position, depth, vorticity), FFD and RFD, RFD surges, cold pool and gust fronts, occlusion
and cycling, the storm's own longevity, splitting, and interaction with an outflow boundary.
What it does not resolve: the tornado (needs ≤ 30 m), cloud texture, the funnel. Those are the next two layers.

**Speed and the time slider.** At 4-9 M cells a step costs ~8-20 ms and the time step is 1.5-2 s, so real time
needs under one step per second: negligible per frame. Timelapse at 60x would need 30-40 steps a second, half the
GPU on an average card. So the sim always **runs ahead of playback** (up to ~10-20x real time in the background)
and writes a *timeline*: a compact feature state every 10 sim-seconds (~50 KB; a 3-hour storm is ~55 MB) plus full
grid checkpoints every 15 sim-minutes in system RAM (fp16, 60-130 MB each). Any timelapse speed and any slider
position is playback of the timeline; scrubbing backwards is free; scrubbing past the computed horizon waits for
the sim. One consequence for the game: the environment is chosen at setup, not changed mid-storm (a player cannot
"add shear" at minute 40 without a re-run from the last checkpoint, which takes seconds, not minutes).

### 2.3 Feature designer (the translator)

Pure C#, no GPU. Reads a timeline frame and the environment and writes the parameter block the skin consumes (1.0's
21-slot block, grown to ~40 slots). The rules are the conceptual model (Lemon & Doswell 1979; Markowski 2002;
Markowski & Richardson 2014), but every number now comes from the sim or from data:

| Feature | Driven by (sim) | Calibrated against (data) |
| --- | --- | --- |
| Updraft tower: top, radius, rise rate, tilt | w(z) column, qc field, shear vector | GOES overshooting-top sizes and rise rates; NEXRAD echo tops |
| Overshooting top and anvil | w at EL, storm-relative anvil-level wind, qi field | GOES anvil expansion rate and shape; anvil thickness from CloudSat climatology |
| Mammatus | qi/qs under the anvil, sub-anvil dry layer | photo reference (yours) |
| Striated updraft / meso barrel | low- and mid-level vorticity, meso radius and depth | your photos; DOW/mobile radar meso diameters |
| Inflow tails (length, number, height) | low-level inflow streamlines from the sim's wind field, RH at inflow level | your photos (varying lengths); 1-3 bands |
| Inflow vault (the clear slot between meso and FFD) | gap between updraft core and the qr/qh field; depth = height of the BWER | NEXRAD BWER depth (hundreds of cases show 3-10 km); your photos |
| Wall cloud, tail cloud, scud | low-level meso + LCL + rain-cooled air from FFD (θ' and RH) | VORTEX2 wall cloud base heights; your photos |
| FFD / RFD / hail core | qr, qh, downdraft fields | NEXRAD 55/60/65 dBZ core sizes per mode; dual-pol hail signatures |
| RFD clear slot, horseshoe | RFD surge extent and dryness (θ', RH) | your photos; Markowski RFD thermodynamics |
| Flanking line | low-level convergence along the gust front | NEXRAD fine lines, photos |

The skin is not asked to invent anything that the sim can already see; it is asked to draw it well.

### 2.4 Tornado model (sub-grid)

A tornado cannot be resolved on the storm grid, so it is a model inside the model, the standard approach in
research-grade storm simulations:

- **Genesis test**, every timeline frame: near-surface vertical vorticity under the low-level updraft (from the sim),
  RFD surge buoyancy (θ' deficit; a cold RFD kills genesis; Markowski 2002), low-level stretching (w at 500 m), LCL
  (high bases lose), and the circulation available (0-500 m SRH, boundary enhancement). These feed a genesis
  probability per minute, so tornadoes form sooner, later, repeatedly, or not at all, depending on the environment.
- **Intensity**: circulation Γ from the resolved low-level meso, contracted to a core radius by the stretching;
  v_max = Γ / 2π r_c, with the swirl-ratio structure from 1.0's vortex lab (one-cell, breakdown, two-cell, multiple
  vortices). Peak winds, core radius and swirl drift with the storm's state.
- **Longevity**: maintenance needs continued inflow and a not-too-cold RFD. The tornado dies when the occluding
  gust front cuts it off, when it crosses the storm's own cold pool or another cold outflow, or when the low-level
  meso is undercut by rain-cooled air (HP storms: shorter, rain-wrapped; LP: brief and weak unless a boundary is
  there). Duration is checked against the SPC climatology (`data/derived/tornado_climatology.json`).
- **Shape**: rope / cone / stovepipe / wedge / multi-vortex / high-based funnel from core radius vs cloud base,
  condensation pressure deficit vs LCL (humid, low LCL → wedge; dry, high LCL → funnel that never reaches down),
  swirl ratio (multi-vortex), and rope-out as it occludes.
- **Rare EF5, 1 mile+**: when the resolved low-level circulation is in the top percentile and the RFD is warm and
  the LCL is low, the model can draw a core radius that the SPC data says exists: EF5 median width in the EF era is
  1,805 yd (1.03 mi), max 3,000 yd. Not a cheat path; the same rules, extreme inputs.
- **Cycling**: the sim produces occlusion and new meso formation itself; the tornado model follows whichever
  low-level meso is live. Cap of 3 tornado cycles per storm as the brief asks (the data says a third of tornado
  families produce 2+, 15 % produce 3+, with a 15-minute median gap between successive tornadoes, see `DATA.md`).
- **Damage**: ground wind field (vortex + translation + suction vortices) sampled against the world. Buildings and
  trees carry an EF-scale Damage Indicator type; the Degree of Damage is looked up from the wind they saw (the NWS
  EF DI/DoD tables, 28 indicator types). The game decides how a DoD looks; the model decides the DoD.

### 2.5 Volumetric skin (the look)

Port of 1.0's BUILD / LIGHT / RENDER passes from WGSL to HLSL as a URP `ScriptableRenderPass` (works in HDRP too),
with three changes that matter:

1. **The volume is advected.** Instead of repainting the volume from parameters every few frames, cloud water and
   ice are *carried* by the sim's wind field (upsampled), re-seeded from the painted shapes. Striations turn at the
   meso's real rotation rate, the anvil spreads at the real storm-relative anvil wind, mammatus sag at their own
   rate, the inflow tail streams at the real inflow speed. This is what "accurate cloud velocities" means in practice.
2. **Two volumes.** Storm volume: 60 x 60 x 18 km at 150 m (400 x 400 x 120, RGBA8, 77 MB; high tier 120 m).
   Near-field volume following the low-level meso: 9.6 x 9.6 x 2.9 km at 30 m (320 x 320 x 96, 39 MB; high tier
   20 m). The wall cloud, tail cloud, horseshoe lip, scud and funnel cloud base live in the near field.
3. **The funnel is analytic.** The tornado's condensation surface is evaluated in the ray-march from the vortex
   model's pressure field (1.0 does this already), so a 30 m rope stays razor-sharp at any resolution.

Anvil: drawn as thin stacked ice sheets with a flat top, backsheared upwind, elongated into an oval downwind along
the anvil-level wind, with the tower flaring into it. Overshooting top: a dome above the anvil whose height follows
w at the EL. Mammatus: switched off in 1.0 at 350 m voxels; at 150 m with the near-field volume they work.

Lighting: sun + sky with multiple-scattering approximation, precomputed per volume update on a coarse grid (as 1.0).
Rendering at half resolution with temporal reprojection; full resolution within ~2 km of the camera.

## 3. Resolution budget

"Average gaming PC" is defined here as an RTX 3060 / RX 6600 XT class card (8 GB), 6-core CPU, 16 GB RAM, 1440p
at 60 fps, with the storm allowed **8 ms of GPU time per frame and 1.2 GB of VRAM**. "High" is your RX 7900 XT.

| Component | Average tier | High tier |
| --- | --- | --- |
| Dynamics grid | 240 x 240 x 72, 333 m x 250 m (stretched to 100 m near ground), 4.1 M cells, ~250 MB | 320 x 320 x 90, 250 m x 200 m, 9.2 M cells, ~550 MB |
| Dynamics time step / cost | 2 s; ~8 ms per step; real time = 0.5 steps/s | 1.5 s; ~15 ms per step |
| Run-ahead speed (background) | ~10x real time at 50 % GPU | ~20x |
| Storm volume | 400 x 400 x 120 at 150 m, 77 MB | 512 x 512 x 160 at 120 m, 168 MB |
| Near-field volume | 320 x 320 x 96 at 30 m, 39 MB | 480 x 480 x 128 at 20 m, 118 MB |
| Ray-march | half-res + reprojection, 96-160 steps, ~4 ms | full-res, 192-256 steps |
| Timeline (system RAM) | 55 MB per 3 h + checkpoints (fp16) every 15 min, ~0.8 GB | ~1.8 GB |

Unity specifics: 1 unit = 1 m. The storm is 80 km wide, so the world needs a **floating origin** (recentre when the
camera moves 4 km) or every km of distance costs precision. The Dodge City map is 12 x 10 km; the storm has to
approach from outside it, so the terrain outside the lidar extent is procedural plains (a 100 x 100 km low-detail
skirt is enough).

## 4. Life cycle and time

Real time by default: a Plains supercell goes initiation → first tornado in 45-90 min, lives 2-4 h; a minisupercell
or an HP storm that chokes on its own outflow may be done in 75 min. Timelapse presets 2x, 5x, 10x, 30x, 60x, and
the slider. The brief's bounds (at least one tornado cycle, at most three) are enforced by the preset generator:
it draws soundings and seeds, runs the sim ahead, and rejects storms that fall outside the bounds before showing
them, so what the player gets is guaranteed in range while still being simulated.

## 5. Presets

Each preset is a region + mode, backed by a set of real proximity soundings (see `DATA.md` §3) with the parameter
ranges below as the acceptance window. Numbers are medians from the published RAP/RUC proximity sounding
climatologies (Thompson et al. 2003, 2012; Smith et al. 2012; Coffer et al. 2019), to be replaced by the actual
soundings we pull.

| Preset | MLCAPE J/kg | MLLCL m | 0-1 km SRH | 0-6 km shear kt | Anvil-level SR wind kt | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Plains LP | 1800-3200 | 1200-1900 | 35-150 | 28-46 | 17-62 (not decisive) | Revised 2026-10-18 from 3 real cases (Manchester OK, Earth TX, Hope NM; `validation/real-storm-cases/plains_lp_cases.json`): slow isolated storms (9-17 kt), steep lapse rates (7.4-8.4 C/km), core held aloft. Tornado rare (Earth: one brief EFU as it went classic) |
| Plains classic | 2000-4000 | 900-1500 | 150-350 | 40-60 | 35-55 | The Dodge City 2016 / Rozel 2013 type. Visible, cyclic |
| Plains HP | 2500-4500 | 800-1300 | 200-450 | 35-55 | < 35 | El Reno 2013 type. Rain-wrapped, big RFD, huge hail, wedge potential |
| Dixie classic | 1000-2000 | 500-900 | 250-500 | 50-75 | 35-50 | Fast (40-55 mph), low base, strongly curved hodograph |
| Dixie HP | 800-1800 | 400-800 | 300-600 | 50-75 | < 35 | Nocturnal, rain-wrapped, long-track. Mayfield 2021 type |
| Midwest classic | 1500-3000 | 800-1200 | 150-300 | 45-65 | 35-50 | Spring systems, faster motion than the Plains |
| Midwest HP | 1500-3500 | 700-1100 | 200-400 | 40-60 | < 35 | Moist, lower bases, rain-wrapped |
| Minisupercell | 300-1000 | 400-900 | 150-400 | 40-70 | any | Low-topped (EL 8-10 km), cool season or tropical, small meso, small tornadoes |

LP / classic / HP is not a switch: it emerges from the anvil-level storm-relative wind, mid-level humidity and the
microphysics, as in 1.0's `hpness`, but now the sim's own precipitation field decides where the rain falls. The
preset's job is to pick soundings that reliably produce the intended mode (the acceptance test in §4 rejects the
rest).

## 6. Pipeline

| Phase | Deliverable | Depends on |
| --- | --- | --- |
| 0. Foundation (now) | Data tooling, climatology, reference case list, design sign-off | your answers in `DATA.md` §5 |
| 1. Brain | Unity package with the dynamics on compute shaders, FFT pressure solver, real-sounding input, outflow boundary, timeline recorder, headless validation vs CM1 numbers and vs NEXRAD-derived geometry of the reference cases | Phase 0 |
| 2. Skin | URP volumetric renderer: advected volumes, anvil sheets, overshooting top, mammatus, striations, inflow tails, vault, wall cloud, clear slot / horseshoe, FFD/RFD/hail; feature designer | Phase 1 timeline format |
| 3. Tornado | Genesis/maintenance/decay model, vortex wind field, shapes, cycling, boundary-assisted LP genesis, EF5 path; damage via EF DI/DoD against world objects | Phases 1-2 |
| 4. Presets and UI | The eight presets with acceptance windows, time slider, timelapse speeds, seed/replay, performance tiers | Phases 1-3 |
| 5. Later (brief E) | Other modes (QLCS, multicell, pulse), storm interactions, mergers, boundary collisions, dryline / fronts, diurnal cycle | Phase 4 |

Phases 1 and 2 can be built in parallel once the timeline format is fixed: the skin can be driven by 1.0's scripted
plan until the brain is ready.

## 7. Risks, stated plainly

- **The sim may not reproduce a feature well** (1.0's storms starved on shallow moisture; cold pools ran cold). The
  designer has a fallback for every feature: if the sim's signal is missing, it uses 1.0's scripted rule for that
  feature. The storm never looks wrong because a physics term misbehaves; it just becomes less emergent there.
- **Tornadogenesis is parameterised**, not simulated. That is the state of the art for anything coarser than 30 m
  grids; it is calibrated to climatology and to the published genesis research, but it is a model of a process the
  grid cannot see.
- **Average-tier GPU time is tight**: two volumes plus a ray-march plus a background sim. The tier table above is a
  budget, not a measurement, until Phase 2 is profiled on a real 3060-class card.
- **Mid-storm environment changes** are not supported without a re-run from a checkpoint (seconds). Fine for a
  "set up the day, then chase" game; say so now if the game needs live knobs.

## 8. Decisions log

**2026-10-18**
- Hybrid architecture approved (simulated brain, designed skin, sub-grid tornado).
- Target: Unity 6000.6.6f1 (same as this PC), minimum GPU RTX 3060. The "average tier" budget in §3 is the
  minimum spec.
- The player can go anywhere in the world and **drive or walk into the tornado**. This adds a fourth scale to §2:

### 2.6 Tornado interior (human scale)

Within ~500 m of the vortex the near-field volume (20-30 m voxels) is too coarse to stand inside. The tornado gets
its own representation, built from the vortex model's wind and pressure fields (1.0's Burgers-Rott / Sullivan
vortex with corner flow and suction vortices), evaluated per pixel and per particle at the player's position:

- **Condensation funnel wall** as an analytic surface in the ray-march (already the plan), with the inside drawn
  too: a two-cell vortex has a clear or ragged central eye with a downdraft, a one-cell vortex is filled with cloud.
  Translucency and brightness follow the pressure-deficit condensation depth, so the wall thins and brightens from
  inside as the pressure recovers toward the axis.
- **Debris and dust** as GPU particles advected by the vortex wind field (inflow layer, corner flow jet, the ring
  of maximum winds, suction vortices), seeded from the surface type under the vortex (soil, crop, water, debris from
  damaged buildings). Visibility inside the debris cloud drops to tens of metres.
- **Local wind** exposed to the game: the vortex model gives wind vector, pressure and vertical velocity at any
  point, so the player's vehicle, body and loose objects can be pushed, lofted and thrown. Peak surface winds,
  the radius of maximum winds (typically 50-500 m) and the corner-flow amplification follow the DOW profiles listed
  in `DATA.md` §2.
- **Sound and pressure**: the pressure deficit (tens of hPa in a violent tornado) and the broadband roar scale with
  the wind field; the game owns the audio, the model supplies the numbers.
- **Floating origin** is required (player anywhere in a world crossed by an 80 km storm); the storm's frame is
  already storm-relative, so recentring costs nothing on the sim side.

- The world belongs to the friend's project. A copy is wanted for Phase 2-3 integration (render pipeline, object
  types for damage indicators), not needed for Phase 1.
- Still open: render pipeline (URP assumed, as in dodge-city-drive), multiplayer/VR (assumed neither), reference
  chase days and photo folders.
- **2026-10-18** Plains LP acceptance window revised from three real cases; the textbook window (LCL 1.8-3 km, shear 45-70 kt,
  anvil storm-relative wind above 55 kt) did not fit them. LP vs classic must come from the simulated precipitation
  field, not from anvil-level wind alone.
- **2026-10-18** Phase 1 brain built (`src/com.stormlab.supercell/Runtime/Dynamics`, README and TIMELINE.md there;
  headless validation in `tests/SupercellLabTest`, 14 of 14 pass). Decisions taken while building it: (1) Smagorinsky-Lilly
  sub-grid mixing (cs 0.25) added on top of 1.0's constant mixing; (2) rain evaporation uses its own intercept (1e6, not 8e6)
  against 1.0's cold-pool bias, which brings the WK cold pool from -11 to -10 K but leaves it 1-3 K colder than CM1;
  (3) superadiabatic layers below 1.5 km in real soundings are mixed out (the HRRR 2 m skin made the DDC storm blow up);
  (4) a step is queued in 6 pieces so the per-frame GPU cap holds; (5) checkpoints quantise the live state to fp16 in place,
  so seek replays are bit-exact on one machine. Measured on the RX 7900 XT: 10.1 ms per step average tier, 23.7 ms high tier;
  frames are ~180 KB (not ~50 KB) because of the 1 km storm-centred qc/qr/qi/qh window; the average tier's 72 levels give
  ~290-320 m aloft, not 250 m (81 levels would).
- **2026-10-18** Phase 2 skin started (`src/com.stormlab.supercell/Runtime/Designer` and `Runtime/Rendering`, READMEs
  there; test scene and headless screenshots in `tests/SupercellLabTest`). Decisions taken while building it: (1) the
  parameter block is 40 slots, with 1.0's 21 kept in place so the ported shaders read them unchanged; (2) the designer
  blends every feature over the frames around the playback time (Gaussian, σ 25 s), so it is stateless and scrubbing
  works, and it falls back to 1.0's rule per feature, recording which source drew what; (3) the skin advects with its
  own 2 km x 1 km wind history, captured from the model every 10 sim-s (the brain runs ahead, so its live wind is from
  the future), and Runtime/Dynamics is unchanged; (4) the advected volume relaxes toward the painted target with a
  60 s time constant, and sub-grid detail turns with the simulated meso but does not yet flow with the wind; (5)
  occlusion shrinks the wall cloud to 30 % rather than removing it, because the brain's cold-pool bias keeps its
  occlusion signal on from about 45 min in the Dodge City run. Measured on the RX 7900 XT, average tier, 1440p at half
  resolution: 2.7 ms per frame for the skin on an idle GPU (estimated ~7 ms on an RTX 3060, inside the 8 ms budget). Not
  done yet: the 30 m near-field volume, flow-mapped detail, and a profile on a real 3060.
- **2026-10-18** Phase 1 finished: the Dodge City run is checked against KDDC, and the cold-pool bias is traced and
  fixed (15 of 15 validation tests pass; details in `Runtime/Dynamics/README.md` and `SupercellLabTest/Validation`).
  (1) Radar: measured like `tools/fetch_nexrad.py`, the mature model storm matches the 22:48Z volume's 18 dBZ top
  (16.0 vs 15.5 km), its 40 dBZ footprint at 1 km (449 vs 226-467 km2), its 50 dBZ core at 1 km (222 vs 27-142 km2,
  high) and the presence of a vault. Its hail aloft is far too much: the 50 dBZ top is 14.5 vs 9.5 km, the vault
  4.5-9.5 vs 2.5-5 km, and the strongest echo 75 vs 55 dBZ. These two misses are reported as known biases and not
  gated, a call made after seeing the numbers. (2) Cold pools: hail melting supplied 5-6 K of the cold pool, because
  1.0 cooled the air for vapour that really condenses on melting hail. The model now exchanges that vapour
  (Rutledge and Hobbs 1983; Morrison et al. 2009): coldest -10.4 to -9.3 K for WK and -12.6 to -11.5 K for DDC, and
  the median after the first hour -7.8 and -7.7 K. Snow melting and hail sublimation do not matter. LFO's hail
  wet-growth limit made cold pools colder and stays off. (3) Vertical spacing: 250 m aloft needs 85 levels, not 81
  (81 give 268 m). 85 cost 22 % more per step and 18 % more VRAM, and push the WK updraft to 70.1 m/s, so the average
  tier stays at 72.
- **2026-10-20** Skin lighting, step 1 of the path to photoreal (physically based sunlight on cloud). The light grids
  now store the sun's optical depth (half floats, +4 MB) instead of 1.0's transmittance. The march lights cloud with
  Wrenninge et al.'s (2013) four multiple-scattering octaves and, for water cloud, Jendersie and d'Eon's (2023) Mie
  phase fit (16 µm droplets; anvil ice HG g = 0.75). Rain, shafts, ground shadow and tornado keep 1.0's two-lobe
  transmittance of the same depth. `StormSkin.physicalLighting` switches back to 1.0's lighting; `sunScale` 1.26 keeps
  the chaser view at 1.0's brightness (mean luminance within 5 %). Cost: +0.13 ms on the 70 min chaser view (3.73 vs
  3.60 ms on the RX 7900 XT). That view was already over the 3060 budget (10.8 ms estimated, against the committed
  report's 8.7 ms) before this change; the rise is in the uncommitted base / wall-cloud edits or the storm itself, not
  the lighting. Next: sky light from all directions in the light grid, then species-resolved density from the brain.
- **2026-10-20** Skin lighting, step 2: sky light from all directions, ground bounce and night. A half-resolution sky
  visibility grid (19 cone traces a point, 1/8 per frame) times the sky's radiance in the same four parts (zenith, mid,
  horizon, horizon toward the key light; integrated on the CPU once a frame) replaces the "sky overhead" term; a 2D
  ground-light map bounces light onto undersides. Night: the key light becomes the moon once the sun is 6 degrees down,
  cloud aloft keeps the sun past ground sunset, the sky follows measured twilight, and an adaptation gain shows a
  moonlit night at 8 % of day with a blue shift, a fill under the base and town light (`SkyLighting.cs`). Tuning from
  A/B renders: octave extinction 0.7 (Wrenninge's 0.5 turned the low-sun core muddy brown), diffuse sky 0.2. Cost on the
  RX 7900 XT, 70 min chaser view: march 4.17 ms (legacy lighting in the same build 3.88), sky grid 0.15 ms a frame. A
  first version that integrated the sky per pixel slowed the whole march kernel 50 % (registers); the bands are now
  constants. Next: species-resolved density from the brain; lightning for night.

- **2026-10-20** Ported live StormLab 1.0's feature shapes (website `2030265`, the 2026-10-19 fixes) into the skin. 2.0 had
  been ported from the version before `b5a4358`. New shared shape functions in `StormSkinCommon.hlsl` (`baseFlare`,
  `mesoShoulder`, `underbellyRise`, `baseLift`, `horseshoeArc`, `clearSlot`) drive the volume, the near field and the
  march; the storm body and gust-front towers are gone and the apron replaces them; wall cloud, horseshoe, inflow tail,
  anvil, flanking line and rain take the live shapes. 2.0 keeps what the simulation supplies (LCL base, simulated
  forward-flank ellipse and hook rain, overshooting top, RFD dryness, inflow bands, mammatus) and its photo-matched
  tornado. Not ported: forward-flank rain rising into the anvil (the brain's larger core made it a grey wall).
  New values: `Up.z` mid-level widening, `Up.w` late thin anvil, slot 39 `Late.x` flanking-line maturity, keyed to the
  simulated storm's age. The anvil is now `anvilAt` (shared); the march draws it analytically beyond the 60 km volume
  (`farAnvil`), the light grid traces sunlight through it so a late canopy shades the storm, anvil ice relaxes quickly
  toward its painted shape (advection had smeared the thin veil away), and canopy ice ages with distance from the root.
  The brain's anvil length takes the larger of its measured ice and the ice advected by the anvil-level wind, up to
  ~95 km late; in the Dodge City run the anvil-level storm-relative wind is only ~10 m/s, so its anvil stays ~36 km.
  Open: a faint seam where the far anvil meets the volume, clear-slot size in brain mode, the wall-cloud / slot / tornado
  ordering rules, and a performance pass (3060 estimate well over 8 ms).
- **2026-10-20** Slot, ordering and cost. Brain mode: the clear slot's radius in the base is live 1.0's compact rim (a
  little under half the updraft radius, growing with the slot; it was the 4 km distance to the simulated surge) and its
  centre is kept under the updraft; the slot opens only once low-level rotation is established and the horseshoe lags
  the slot (live 1.0's order: wall cloud, then slot, then horseshoe; Phase 3's tornado must wait for Rfd.x > ~0.3).
  Rotation: the shaders turn features by rate x clock, so a changing simulated rate made textures jump or stall; each
  angle (wall cloud, mid-level meso, low-level spin, tornado) is now integrated over the wall clock in StormSkin and the
  GPU gets phase / clock (`GpuBlock`). Checkerboard march (`checkerboard`, default on): half-width dispatch, the resolve
  fills the other parity from fresh neighbours and history; converged frames differ by 0.2/255 and the 70 min chaser
  march fell from 6.4 to 3.6 ms on the RX 7900 XT. The skin test passes (its budget is measured at the last time,
  100 min: 7.6 ms on a 3060); the 70 min chaser view is the worst seen, 11.3 ms at 1440p (about 7.6 ms at 1080p).
- **2026-10-20** Canopy seam fixed. Away from the storm (3-5 updraft radii out) the march takes the canopy's density
  from the analytic `anvilAt`, the same shape the far anvil draws, instead of the volume's copy (which thinned and
  left gaps at its edges); the light grid lights it the far anvil's way there (ice above along the sun, no sideways
  shade probes, which darkened a thin flat sheet under a low sun); the light grid's sun trace beyond the volume starts
  with short steps (one 2.5 km first step had charged a ray leaving a thin canopy with too much ice: a dark band along
  the volume's edge). The ice texture's regular layering is softened in favour of fibrous streaks. Cost: +0.5 ms on the
  70 min chaser march (4.2 ms on the RX 7900 XT; RTX 3060 estimate 12.6 ms at 1440p).
- **2026-10-20** Storm type and the brain's rain. (1) `StormPhysics.hpness` no longer keys on the anvil-level storm-relative
  wind (it was flat across 32 real storms of known mode and called Dixie HP days LP); it leans on the cloud base and
  surface moisture, the one signal the soundings carry. It is a lean, not a classifier. (2) The brain: hail intercept
  4×10⁵ (was 4×10⁴) and scalars mixed three times momentum's (CM1's Pr = ⅓). Reflectivity bias +7.0 → −2.2 dBZ over 18
  real storms; WK updraft 66.9 m/s and cold pool −7.3 K; full validation passes. Neither change, nor any hail setting
  tried, makes LP, classic and HP storms rain differently; that is open (model-limitations.md 13). Validation tools:
  `-dynN0H`, `-dynRhoH`, `-dynScalarMix`, `-dynReportTag`, `-dynCasesTag`; skin test `-skinSounding`, `-skinName`.
- **2026-10-21** Lightning (`Runtime/Lightning`, its own README). An optional module in its own assemblies: a
  deterministic Poisson flash schedule (in-cloud, negative and positive ground), branched channels with repeat strokes,
  thunder synthesized along the channel and delayed by distance, and a preview. `Integration/LightningCloudBridge` lights
  the skin: the strongest eight channel samples fill a 64 x 64 x 24 local-radiance grid that the march scatters with
  the cloud's own extinction (divided by the night gain, so a flash is not amplified like starlight); the bolt shader
  traces cloud and rain optical depth so channels inside cloud are hidden. During a flash and one frame after, the
  skin skips checkerboard and temporal blending (`IStormTransientLighting`), so flashes are crisp and leave no ghost,
  at the full-width march cost for those frames. Flash rate, polarity and charge heights are presentation settings,
  not from the brain. Night renders with a held flash: skin test `-skinNight -skinLightning` (70 min, DDC: the flash
  lights tower and anvil from inside; the bolt leaves the cloud base cleanly).
- **2026-10-21** Storm-driven lightning. `LightningSystem.ActivityAtTime` lets the storm set flash rate and flash types
  over time. The schedule thins a fixed candidate stream at `maxFlashesPerMinute` with a separate random stream. The
  alternative, piecewise fixed-rate segments, would break when rates change smoothly; thinning keeps replay, seeking
  and frame partitioning exact and leaves fixed-rate seeds unchanged. `LightningStormDriver` binds the skin's storm:
  designed storms use updraft width and speed, age, decay and a lightning jump before each tornado. The brain uses McCaul et al.
  (2009) graupel flux at −15 °C and column ice from each timeline frame, kept per frame so ring eviction doesn't change
  the history. The flash-type split follows rain arrival, updraft strength and decay. Rates are presentation
  estimates (model-limitations.md).
