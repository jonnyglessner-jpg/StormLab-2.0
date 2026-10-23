# CLAUDE.md

StormLab 2.0 (Supercell Lab 2.0) is a Unity supercell simulation for a friend's game. Hybrid design: a GPU cloud model
(the brain) decides what the storm does, a volumetric skin draws it, and a sub-grid tornado model handles the
tornado. Read `docs/DESIGN.md` for the architecture and decisions log before changing behaviour.

## Layout

- `src/com.stormlab.supercell/`: the Unity package, namespace `StormLab.Supercell`.
  - `Runtime/Environment`: soundings and diagnostics (StormPhysics.cs, ported from StormLab 1.0)
  - `Runtime/Dynamics`: the brain (anelastic, LFO ice, timeline). README and TIMELINE.md there
  - `Runtime/Design`: the designed storm track (StormGenome, DesignedStorm, DesignedStormRunner)
  - `Runtime/Designer`, `Runtime/Rendering`: feature designer and volumetric skin
  - `Runtime/Viewer`: the Storm Test Bench
  - `Runtime/Lightning`: optional lightning and thunder (own assemblies; `Integration/` lights the skin's cloud).
    README there. Flash rates are presentation settings unless `LightningStormDriver` takes them from the designed
    storm or the brain
  - `Editor/`: DynamicsValidation, DesignedValidation, DesignedChecks
- `tests/SupercellLabTest/`: Unity 6000.6.6f1 project hosting the package (`file:../../../src/com.stormlab.supercell`).
  Skin and designed-skin render tests are in its `Assets/Editor`.
- `validation/`: inputs and outputs of validation. Soundings, real storm cases, CM1 comparisons, reports.
- `tools/`: Python data tools. `data/`: reference photos, derived calibration, raw downloads (gitignored).
- `docs/meteorology/`: the science. **Read the relevant file before touching the physics**:
  - environment diagnostics → `atmospheric-physics.md`
  - brain, cold pools, mode → `storm-dynamics.md`, `cloud-microphysics.md`
  - tornado model → `tornadogenesis.md`
  - radar comparisons → `radar-physics.md`
  - anything the player sees → `visual-realism.md`
  - known biases and what to try next → `model-limitations.md` (keep it current)

## Running things

- Validation, headless (**never with `-nographics`**, the compute shaders need a GPU):
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe" -batchmode -projectPath tests/SupercellLabTest -executeMethod StormLab.Supercell.EditorTools.DynamicsValidation.RunFromCommandLine -dynSuite all -logFile validation.log
  ```
  Suites: `all | quick | wk | ddc | perf`. Reports land in `validation/reports/`, the WK CSV in
  `validation/cm1-comparisons/`, DDC CSVs in `validation/real-storm-cases/`.
  Designed storm: `StormLab.Supercell.EditorTools.DesignedValidation.RunFromCommandLine`.
- Fast compile loop for pure C#: Unity's bundled dotnet (`Editor/Data/DotNetSdk/dotnet.exe`) against a Unity stub.
  The designed-storm checks (incl. game pacing and the game scenario) run without Unity:
  `dotnet run -c Release --project validation/designed/DesignedChecks.csproj`.
- Lightning checks: `validation/lightning/check.ps1` (no Unity); the in-editor review is **Supercell Lab > Lightning >
  Capture Cloud Integration Review**. Night skin renders with a flash: the skin test with `-skinNight -skinLightning`.
- Python tools: `python -I tools/<tool>.py`. Soundings go to `validation/soundings/`; download caches to `data/raw/`.
- Sounding checks against SPC definitions: `python -I ../meteorology/SHARPpy/tools/check_sounding.py
  validation/soundings/<file>.json` (the `meteorology/` library sits next to this repo, not in it).
- After changing `data/derived/tornado_climatology.json`, regenerate the C# table with
  `python tools/gen_climatology_cs.py`.

## Rules

- **Official storm stats come from SPC/NHC data**, never recall. EF ratings since 2014 are conservative at the high
  end; say so where it matters.
- **Funnels are smooth.** No vertical grooves (the user called them very unrealistic). Follow
  `data/reference/tornadoes/NOTES.md`.
- **Lab diagnostics are not SPC's.** 1 km mixed layer, Bunkers 2000, largest-area CAPE and a hybrid STP. Label them,
  and expect SHARPpy CAPE 5–8 % above MetPy's.
- Changing the RNG draws in StormGenome changes every existing seed's storm. New draws get their own stream.
- Keep the repo **private**: `data/reference/` holds other photographers' images.
- Don't paste text from Stull (CC BY-NC-SA) or the CM1 equations PDF (all rights reserved) into code or docs.
  Implement the equations.
- The user also has a clean clone at `C:\Users\Jonny Glessner\StormLab-2.0`. This working copy is the one to test.
- Remote: `github.com/jonnyglessner-jpg/StormLab-2.0` (private), branch `main`. `gh` is at
  `C:\Program Files\GitHub CLI\gh.exe`.
