# Validation

| Folder | What | Written by |
|---|---|---|
| `soundings/` | Processed real soundings (p, z MSL, T, Td, wind) with MetPy diagnostics. DDC 2016-05-24 (HRRR 22Z, IGRA 19Z) and the three Plains LP cases | `tools/fetch_sounding.py` |
| `real-storm-cases/radar/` | One-volume NEXRAD measurements, lifecycle tracks and SPC report matches per case | `tools/fetch_nexrad.py`, `radar_lifecycle.py`, `spc_reports.py` |
| `real-storm-cases/satellite/` | GOES overshooting-top and anvil measurements | `tools/fetch_goes.py` |
| `real-storm-cases/case_seed.json`, `catalog.json`, `CASES.md` | The case library: 51 storms across the eight presets plus null (no tornado) and boundary cases, with SPC facts, radars, balloon stations | `case_seed.json` by hand; the rest `tools/case_catalog.py` |
| `soundings/<case>_<hrrr|rap>_inflow.json` | The case's primary sounding: median of five model points 35 km upstream of the storm's radar position along its storm-relative 0-1 km wind, with the five-member spread | `tools/fetch_inflow_soundings.py` |
| `real-storm-cases/ENVIRONMENTS.md`, `environments.json` | Every case's soundings against its preset's DESIGN.md §5 window | `tools/case_environments.py` |
| `real-storm-cases/OUTCOMES.md`, `outcomes.json` | What each storm did (radar life, motion, tops, matched SPC reports), track-quality flags, tornadic vs null environment percentiles | `tools/case_outcomes.py` |
| `real-storm-cases/radar/lifecycle_runs.json` | Which radar and anchor each case's track used | `tools/run_case_radar.py` (batch driver for radar_lifecycle + spc_reports) |
| `soundings/proximity_choices.json` | Where and when each case's proximity sounding was taken | `tools/fetch_case_soundings.py` |
| `real-storm-cases/plains_lp_cases.json` | Summary of the three Plains LP cases (Manchester OK, Earth TX, Hope NM) | by hand from the above |
| `real-storm-cases/ddc_20160524_*.csv` | The model's Dodge City run against KDDC | `DynamicsValidation` (`ddc` suite) |
| `cm1-comparisons/weisman_klemp.csv` | Weisman–Klemp supercell time series against CM1's published ranges | `DynamicsValidation` (`wk` suite) |
| `reports/` | Markdown reports from every suite | `DynamicsValidation`, `DesignedValidation`, `SkinTests`, `DesignedSkinTests` |

## Cases waiting on data

**VORTEX2, Goshen County WY, 5 Jun 2009** (SPC: EF2, 22:02–22:31 UTC, 8.3 mi, 100 yd). It is the best-observed
tornado in the archive. DOW-6/DOW-7 dual-Doppler radar and the mobile mesonet, StickNet, tornado pods and soundings
were ordered from NCAR EOL on 2026-10-20 and will land in `../../meteorology/VORTEX2/`. Use them for:
- the near-storm sounding as model input (into `soundings/`)
- low-level meso size and strength, RFD θ′ from the mesonets, against the brain and the tornado model
