# StormLab 2.0 (Supercell Lab 2.0)

An environment-driven supercell simulation for Unity, built on real-world data: a storm-scale cloud model on
compute shaders decides what the storm does, a designed volumetric skin decides what it looks like, and a sub-grid
tornado model produces tornadoes that vary with the environment and damage the world.

```
StormLab-2.0/
├── CLAUDE.md                 how to work in this repo
├── docs/                     DESIGN.md (architecture), DATA.md (data plan), REFERENCE_CHECKLIST.md
│   └── meteorology/          the science the code relies on, distilled for this project
├── validation/
│   ├── soundings/            the real soundings the model and tests read (tools/fetch_sounding.py output)
│   ├── real-storm-cases/     NEXRAD and GOES measurements of reference storms, model-vs-radar CSVs
│   ├── cm1-comparisons/      Weisman–Klemp runs against CM1's numbers
│   └── reports/              reports the validation suites write
├── src/
│   └── com.stormlab.supercell/   the Unity package (Unity 6, URP)
├── tests/
│   └── SupercellLabTest/     the Unity project that hosts the package for tests, renders and the test bench
├── tools/                    Python data tools (soundings, radar, satellite, SPC)
└── data/
    ├── reference/            photo reference library with per-image notes, one folder per preset
    ├── derived/              calibration tables the game reads (tornado_climatology.json)
    └── raw/                  downloaded source data, gitignored (re-fetch with tools/)
```

| | |
| --- | --- |
| `docs/DESIGN.md` | Architecture, resolution budget, presets, pipeline, risks, decisions log |
| `docs/DATA.md` | Data sources, reference cases, open questions |
| `docs/meteorology/` | Atmospheric physics, storm dynamics, tornadogenesis, microphysics, radar, visual realism, model limitations, references |
| `tools/spc_tornado_stats.py` | Tornado climatology from the SPC database |
| `tools/fetch_sounding.py` | Real soundings: IGRA2 balloons, RAP or HRRR model points, with MetPy diagnostics |
| `tools/scan_nexrad.py` | Finds a photographed storm: strongest cells near a point across a time window |
| `tools/fetch_nexrad.py` | Measures a storm in one radar volume (echo tops, core sizes by height, vault, couplet) |
| `src/com.stormlab.supercell/` | Environment physics, the dynamics brain, the designed storm, the skin and the viewer |

Open `tests/SupercellLabTest` in Unity 6000.6.6f1; it references the package at `../../../src/com.stormlab.supercell`.
Menu **Supercell Lab** has the validation suites and the test bench (Ctrl+Shift+T toggles Play).

Predecessors: StormLab 1.0 (https://github.com/jonnyglessner-jpg/StormLab, MIT) and the Dodge City Drive world
(`../dodge-city-drive`), whose lidar terrain is the first world the storm will cross.
