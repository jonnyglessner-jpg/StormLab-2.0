# Cloud motion and the reference library

## Required sources

User requirement: use the whole library at `C:\Users\Jonny Glessner\Desktop\ClaudeCoding\meteorology`
as the reference set for building and editing the brain. The six collections have different roles;
an unrelated observational dataset is not a source for a numerical constant.

| Collection | Role in motion work | Entry points consulted in this audit |
| --- | --- | --- |
| CM1 | Governing transport equations, moving frames, microphysical fallout and numerical diffusion | README.md; NOTES.md governing-equation summary and advection section |
| Practical_Meteorology_Stull | Condensation, cloud boundaries, buoyancy and hydrometeor fall speeds | NOTES.md; text/Ch06-clouds-v102b.txt |
| METR4403 | Shear, storm-relative inflow, organization and limitations of CAPE-derived updraft speeds | NOTES.md quick reference and storm dynamics |
| MetPy | Unit-aware wind components and environmental diagnostic checks | NOTES.md; repo/src/metpy/calc/basic.py wind_components |
| SHARPpy | Sounding checks, storm motion and storm-relative wind conventions | NOTES.md; repo/sharppy/sharptab/winds.py sr_wind |
| VORTEX2 | Observational verification with soundings, mobile mesonets and radar | README.md; NOTES.md instrument, units and aliasing caveats |

This is a source map, not a claim that all raw observations, PDFs, or upstream code were read.
Follow the applicable original material when changing a corresponding part of the brain. In particular,
VORTEX2 raw aliased Doppler velocity is not a three-dimensional cloud-motion vector and must not be
used as one. The current changes do not constitute a VORTEX2 calibration or CM1 comparison run.

## Physical contract

At 1x, one playback second represents one atmospheric second. A uniform wind of 20 m/s carries
cloud material 200 m in ten seconds; vertical velocity of 10 m/s carries it upward 100 m.
The skin uses km internally, so transport displacement is velocity in m/s times model dt divided by 1000.
Cloud outline growth also depends on condensation and evaporation; a visible cloud boundary is not
necessarily a material parcel. Do not translate the entire storm upward to imitate rising turrets.

Brain winds are grid-relative in the moving simulation frame. The world transform already adds frame
motion. Designed winds are ground-relative in their fixed environment frame. Neither path should add
storm translation twice. Wind convention is +x east, +y north, +z up inside the shaders;
Unity world coordinates are +x east, +z north, +y up.

## Changes

`StormSkin.StepTo(t, clock)` now drives all atmospheric shader clocks and integrated rotation with
model time `t`. The caller's wall clock is still exposed as `WallClock`, but no longer sets cloud flow.
Pause and waiting for the brain's horizon freeze detail as well as transport; timelapse scales both.
GPU parameter blocks for the near field and march retain the same model time as the storm volume.
Rotation resets use the same 60-second seek threshold as volume transport and also reset after rebinding
or explicitly resetting the volume. Seeking reconstructs an instantaneous phase, rather than replaying
the complete rotation history.

The designed updraft now joins its subcloud 0.3*wmax velocity continuously at the LCL. Its previous
sine started at zero immediately above the LCL despite a positive inflow velocity immediately below.
A phase-shifted sine retains the same peak speed and zero velocity at storm top. This is an analytic
presentation field; it does not upgrade the designed storm into a fluid solver.

## Validation and remaining limits

### Feature relocation review (2026-10-22)

The user's 13:39 recording shows the visible updraft/base relocating while an older
column remains in the live density. Camera continuity alone does not test this.
The diagnostic low meso selects the strongest vorticity peak within 15 km of the
strongest updraft; this is not an object identity. Using its 25 s weighted position
as the whole tower anchor allows rapid travel between different maxima.

The brain-driven skin now builds a presentation path in timeline order. Its low
anchor has a 120 s response and a 20 m/s **grid-relative visual** speed limit;
upper/meso/RFD offsets use a 90 s response and 10 m/s relative limit. The wall
cloud and low meso share the anchor. These constants are visual tuning, not a
measurement, physical validation, or a change to the stored diagnostic positions,
storm-motion estimate, fluid winds, or model equations. This can delay or displace
the drawn feature relative to a rapidly changing diagnostic, and it does not
resolve multiple simultaneous cells or identify true mesocyclone cycling.

Substantial disagreement between old cloud and the current painted envelope gets
an 8 model-second condensation/erosion response, while the interior retains the
configured relaxation. Transport still uses the simulated wind; pure transport
with relaxation disabled remains unchanged. At timelapse, geometry and near-field
slab refresh budgets are capped to approximately two model seconds (or one render
frame if that is longer). Faster refresh can cost more GPU time at high timelapse.

`FeatureMotionReview` tests full-run feature speeds at one-model-second intervals,
compares the previous Gaussian path on the same frames, verifies independent seek
order, and captures a 30x rendering sequence over the interval in the recording.
`CloudMotionChecks` additionally tests old-envelope erosion/new-envelope growth
on the GPU. These checks complement, rather than replace, visual review of the
captured sequence. No new meteorological calibration is claimed for these changes.

Run Unity with `-batchmode -executeMethod CloudMotionChecks.RunFromCommandLine` against
`tests/SupercellLabTest`, with a GPU (omit `-nographics`). Checks exercise the production skin with
different model/wall clocks, pause, ten-second steps, reset, updraft continuity and peak, and the actual
GPU Advect kernel with signed constant winds. The GPU test measures the transported cloud centroid
against displacement in physical units, with a one-metre tolerance.

The library still has limits: 2 km by 1 km wind snapshots smooth local motions, byte encoding clips
individual components outside +/-51 m/s and cannot encode exact zero (nearest bins +/-0.2 m/s),
coarse cloud volume relaxation alters material persistence, and subgrid flow is a two-phase visual
approximation. The march only flow-maps fine turbulent detail within 8 km; distant coarse billows and
analytic base features are not a fully advected material texture. Rotation and local wind can both
affect subgrid detail. These changes fix temporal consistency and cloud-base continuity; they do not
establish exact real-world motion for every visible feature. Wider wind encoding, transported detail
coordinates and observational sequence comparisons need separate implementation and validation.

### Tail, flanking-line and precipitation review (2026-10-22, 14:26 recording)

The recording shows feeder features moving together as orientation changes, and an
exposed, flat-topped rain curtain. The old tail root used storm forward/left vectors;
every flanking tower was reconstructed from one direction, and maturity changed their
spacing. Analytic precipitation was explicitly faded out just above the LCL.

The brain presentation now keeps four tail-spine knots and eight separate flanking
tower positions in grid coordinates, built in timeline order alongside the existing
low-core track. Sections have distinct response times (tail 60--180 model seconds;
towers 120--260 seconds) and a 20 m/s grid-relative presentation speed limit. Tail roots
use the inflow direction, and the gust-front search uses a fixed environmental-shear
sector rather than the changing storm-propagation heading. Maturity changes tower
growth, not their locations. Both coarse and near-field tail shaders project onto the
same spine; the connecting flanking cloud follows the tracked tower positions.
These are visual continuity choices, not measured parcel velocities or a new cell tracker.

Brain-driven rain now samples the recorded Qr/Qh volumes at their actual heights,
with independent window origins for the two bracketing frames. Padded window edges
fade to zero; layer zero extends to the surface. Sampling is in grid coordinates,
so neither frame translation nor volume recentering is added twice. The cloud envelope remains under the feature designer; coarse Qc/Qi diagnostics are not added as cloud geometry. Recorded rain now anchors a continuously transported presentation volume; see the rain-motion correction below. The old analytic path remains
for designed storms or unavailable hydrometeor data. Extinction conversion (400 per
kg/kg for rain, bounded 12% hail contribution)
is visual tuning. It is not a radar or rainfall-rate calibration.

Reference set consulted: all six collections' README/NOTES entry points where present.
Relevant original material: Stull chapter 7, section 7.6.1 (hydrometeors falling
relative to air); METR4403 text/14_Convective Mode.txt (shear separates precipitation
from updraft and inflow interacts with gust fronts); SHARPpy winds.py sr_wind and
MetPy basic.py wind-component conventions. CM1 moisture/fallout equation summary
provides the transport contract. VORTEX2 notes supply observational caveats, not
numerical tuning constants. No fluid equations, sounding, or microphysics were changed.

Validation: Supercell Lab > Run Storm Structure Checks passes section-speed,
nonrigid-tail, seek-order, recenter, and real-GPU recorded-rain tests (rain at 5 km
with two LCL settings, temporal interpolation, moving crop, surface, empty sky and
binding reset). Supercell Lab > Run Cloud Motion Checks passes all six existing
clock, updraft, recenter, envelope-response and signed GPU transport regressions.
These synthetic checks establish continuity and coordinate correctness. They do not
establish meteorological realism across every storm stage; full-cycle visual review
and the recorded 1 km hydrometeor resolution remain limitations.

### Correction after visual review: excess cloud matter

The first rain update also promoted the recorded 1 km Qc/Qi cells into the drawn
cloud volume. Visual review found detached, blocky masses outside the intended
storm. That addition is removed: hydrometeor sampling now changes precipitation
only, leaving cloud density and material to the existing feature envelope.
Qr/Qh still determine rain at their recorded heights, without an LCL cap.
StormStructureChecks now includes high Qc/Qi in its synthetic precipitating source
and asserts that this cannot create cloud geometry in otherwise empty air.
This is a rendering correction; no meteorological equations or tuning of the
brain changed. Existing stored live density must be repainted after shader reload
(e.g. seek backward on the timeline), so old injected cloud is not left frozen
in a paused volume.
Validation of the correction: Run Storm Structure Checks passed in Unity, including the new assertion that Qc/Qi cannot add cloud geometry while rain remains present at 5 km above the LCL.

### Rain-motion correction after playback review

The initial recorded-rain display replaced the live rain channel with an Eulerian
snapshot blend every update, while the march added a separate vertically scrolling
noise pattern without horizontal wind. That made intensity appear/disappear in place
and disconnected the visible pattern from rain transport.

The Build kernel now traces the earlier Qr/Qh snapshot forward and the later one
backward to displayed model time using local grid-relative air velocity minus the
configured rain terminal fall speed. The live rain channel is semi-Lagrangian
transported with that same velocity and corrected toward the measured field with
a 30-model-second response. This is a presentation correction, not a second
sedimentation step on the stored timeline. Constant-velocity translating data have
the same displayed trajectory in target and live density.

Weak, vertically elongated density variations are seeded in stable grid coordinates
and carried inside the rain channel. Brain-mode marching no longer overlays the
independent scrolling pattern. Camera motion/recentering does not reseed it.
Pause freezes both transport and source response. Reset/seek reconstructs the
target at the requested time. No cloud-water/ice geometry is added.

Reference basis remains Stull chapter 7 section 7.6.1 (fall speed relative to air),
METR4403 convective-mode material (wind separates precipitation and updraft), and
the CM1 moisture/fallout transport contract in the source map above. The 7 m/s
default already configured in SkinVolume is a representative visual rain speed,
not a size-resolved drop spectrum. Local constant-velocity tracing over snapshots,
1 km hydrometeor data, interpolation diffusion, and the 30 s correction are limits.
Validation: StormStructureChecks passed in Unity after this correction. A synthetic source falling at 10 m/s retains its intensity halfway between snapshots; live-density centroid checks match signed air wind minus 7 m/s fall speed to within 2 m over 10 s, including a strong updraft, pause and volume recenter. Existing rain-height, no-added-cloud, seek and coordinate checks also pass. These are numerical/GPU checks, not observational rain-motion calibration.
The six existing CloudMotionChecks also passed after the rain-motion correction. The preview was restarted for live review; no simulation equations were changed.

### 2026-10-23: rain shroud and detached-volume correction

Full-cycle screenshots exposed a problem that local drop rendering did not address.
The storm-scale volume combined Qr with 12% of Qh (bounded at 0.001 kg/kg) and then
rendered both as liquid rain. A hail-only reservoir could therefore produce a
deep rain shroud. The live density also retained a separately advected rain copy
with a 30-second correction, and that correction used staggered cloud-target slices.
This could retain displaced/evaporated precipitation beyond its recorded support.

The distant liquid-rain channel now uses Qr only. Build and Advect share the same
motion-compensated observation sampler, with the latter binding its own current
snapshot textures and sampling all live rain texels at displayed time. There is
no extra 30-second rain mass history. A missing brain record yields no rain instead
of reverting to analytic ellipses. Designed storms retain their analytic path.
Paused rendering remains frozen, and the pure-transport test path is retained.
Wind used to trace between observations is independent of cloud-advection enable,
including reset/seek. The march no longer applies the designed ellipse cutoff or
precipitation-mode opacity multiplier to measured rain; its continuous extinction
matches the rain used for lighting. Hail is still simulated, but is no longer
masquerading as liquid rain in the volume. A dedicated hail renderer is separate work.

This supersedes the 30-second rain-motion response described above. Reference basis
is the same six-collection source map, particularly Stull ch 7's separate liquid/ice
categories and fall speed relative to air, and CM1's already-conservative model
transport contract. No storm dynamics, sounding, rainwater budget, or phase-change
equations changed. The revised 80 per (kg/kg) conversion remains visual tuning (previously 400), and the 1 km
mass/2 km wind grids still limit shaft detail. Numerical transport/coordinate tests
alone do not establish full-cycle visual realism.

StormStructureChecks now also exercises hail-only observations, disappearance of Qr
while cloud-target slabs are stale, and missing records. RainLifecycleReview adds
paired rain-on/cloud-only captures at six recorded storm ages plus actual Qr/Qh
areas/centroids by height; it never labels an image an automatic realism pass.

The paired lifecycle captures also exposed solid, nested contours in the lower
liquid field. The revised display response is `1-exp(-80*Qr)` rather than the
previous `1-exp(-400*(Qr+hailTerm))`. This opacity change is explicitly visual
tuning, not an inferred physical extinction coefficient. The march limits steps
inside measured rain and adds low-contrast, vertically stretched filaments through
two continuous clock phases, moving with grid-relative wind minus the same terminal
fall speed used by the observation sampler. Their coordinates include the volume
origin so recentering does not reseed detail. They modulate existing support only.
The light volume uses the revised mean extinction; fine streak shadows are not
resolved there. This refinement does not shrink, move, or delete the model's Qr.


### Designed inflow tail at the FFD boundary (2026-10-23)

The designed beaver/inflow tail now uses four knots along the inflow-facing edge of the same swept Gaussian footprint that draws its forward-flank precipitation. The edge's normal offset responds to the existing kinematic FFD outflow scale relative to storm-relative inflow. The head is projected from the updraft's inflow side; the remaining knots follow the footprint tangent. The shader rounds the path with Catmull-Rom interpolation. Moving/strengthening the FFD or changing inflow therefore changes the tail rather than rotating a fixed ramp from storm-forward offsets.

The coarse volume and near-field cloud now use the same designed-tail density function. Its base and depth vary with smooth, inward-travelling cloud noise on model time; the band tapers away from the updraft, with soft edges rather than a solid wedge. The wall-cloud tail connection aims at this new spine. The brain's tracked spine and legacy shape paths are retained.

Source support: the existing visual-realism.md distinction between a wall-cloud tail and a beaver tail along the inflow/FFD boundary, and METR4403 original text/14_Convective Mode.txt discussion of storm-relative inflow interacting with gust fronts and precipitation/downshear separation. This is designed presentation geometry, not a thermodynamic front solver. The Gaussian contour, outflow/inflow offset, smoothing, widths and cloud-depth coefficients are visual choices, not observationally calibrated constants. No sounding, RNG stream, tornado genealogy or brain equations changed.

Validation: DesignedChecks includes sixty preset/mode/age boundary cases, checking contour attachment, the inflow-facing side, updraft attachment, curvature, seek independence, coordinate recentering and response to shifted FFD/inflow balance. Existing whole-life, mode, wind and tornado checks remain in the suite. Use Supercell Lab > Capture Designed Inflow Review in Play mode to capture four designed variants with rain-on/cloud-only pairs under the test project's Screenshots/DesignedInflow directory.
