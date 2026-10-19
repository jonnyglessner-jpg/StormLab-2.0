# Supercell Lab 2.0

An environment-driven supercell simulation for Unity, built on real-world data: a storm-scale cloud model on
compute shaders decides what the storm does, a designed volumetric skin decides what it looks like, and a sub-grid
tornado model produces tornadoes that vary with the environment and damage the world.

| | |
| --- | --- |
| `docs/DESIGN.md` | Architecture, resolution budget, presets, pipeline, risks |
| `docs/DATA.md` | Data sources, what is already here, reference cases, and the questions that need answers before Phase 1 |
| `tools/spc_tornado_stats.py` | Tornado climatology from the SPC database (`python tools/spc_tornado_stats.py`) |
| `tools/fetch_sounding.py` | Real soundings: IGRA2 balloons, RAP or HRRR model points, with MetPy diagnostics |
| `tools/scan_nexrad.py` | Finds a photographed storm: strongest weather cells near a point across a time window |
| `tools/fetch_nexrad.py` | Measures a storm in one radar volume (echo tops, core sizes by height, weak-echo vault, couplet) |
| `data/reference/` | Photo reference library with per-image notes, one folder per preset |
| `data/raw/` | Downloaded source data (SPC database; radar, satellite and HRRR to come) |
| `data/derived/` | Calibration tables the game reads (`tornado_climatology.json`) |
| `unity/com.stormlab.supercell/` | The Unity package (Unity 6, URP). Environment physics and the 1.0 designed storm are in; dynamics, renderer and tornado layers are the next phases |

Predecessors: StormLab 1.0 (https://github.com/jonnyglessner-jpg/StormLab, MIT) and the Dodge City Drive world
(`../dodge-city-drive`), whose lidar terrain is the first world the storm will cross.
