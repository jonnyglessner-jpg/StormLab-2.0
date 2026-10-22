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
