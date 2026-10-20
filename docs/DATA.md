# Supercell Lab 2.0: real-world data plan

What the model is calibrated against, where it comes from, what is already here, and what I need from you.

## 1. Already in hand

| Dataset | Where | Used for |
| --- | --- | --- |
| SPC tornado database 1950-2025 (73,458 tornado records) | `data/raw/spc/1950-2025_actual_tornadoes.csv` | Tornado width, length, rating, timing, region and cycling climatology |
| Derived climatology | `data/derived/tornado_climatology.json` (from `tools/spc_tornado_stats.py`) | The tornado model's acceptance windows and distributions |
| StormLab 1.0 (your repo) | cloned for reference; C# environment port in `src/com.stormlab.supercell/Runtime/Environment` | Environment physics, scripted storm, cloud model kernels, vortex lab |
| Dodge City world | `../dodge-city-drive` | 1 m lidar terrain, NAIP imagery, OSM roads, `GeoReference`; the first world the storm damages |
| Your chase log, 2019-2026 (78 chase days) | `../storm-chase-site` with photos and SPC stats per day | Reference cases and visual calibration |
| Plains LP reference images (20, with per-image notes) | `data/reference/plains_lp/` + `NOTES.md` | Renderer and designer targets for the LP preset: high laminar base, stacked plates, sculpted tower, translucent precipitation |
| Dodge City 2016-05-24 case, first pass | `validation/soundings/ddc_20160524_{igra,hrrr}.json`, `validation/real-storm-cases/radar/ddc_20160524_KDDC_{2248,2335}.{json,png}` | Proves the fetch tools; first Plains-classic reference numbers (below) |

### First reference case: Dodge City, 2016-05-24 (Plains classic, cyclic)

Soundings (`tools/fetch_sounding.py`):

| | DDC special launch 19Z (IGRA2) | HRRR analysis 22Z, 37.55 N 100.15 W |
| --- | --- | --- |
| Surface T / Td | 25.3 / 20.1 C | 30.1 / 19.1 C |
| MLCAPE / MLCIN | 2,043 / -103 J/kg (still capped) | 4,575 / -2 J/kg |
| MLLCL | 1,000 m | 933 m |
| EL | 11.1 km | 12.4 km |
| 0-1 / 0-3 km SRH | 176 / 230 | 77 / 171 |
| 0-1 / 0-6 km shear | 22 / 39 kt | 11 / 32 kt |
| Bunkers right-mover | from 241 at 23 kt | from 236 at 22 kt |
| Storm-relative anvil wind | 16 kt | 15 kt |

That is the known character of the day: huge CAPE, low base, slow storm motion and weak anvil-level flow (which is why
the storm stayed classic rather than LP, and why the tornadoes were so visible and slow-moving), with the boundary
near Dodge City supplying the low-level shear the HRRR point does not show. The two sources disagree on SRH by a
factor of two; every preset will carry several soundings for exactly this reason.

Radar (`tools/fetch_nexrad.py`, KDDC, 1 km grid, first-look numbers): at 22:48Z the storm was 15 km SW of the radar
with 50 dBZ to 9.5 km, 18 dBZ to 15.5 km, a 226 km2 40 dBZ footprint at 1 km, and a weak-echo column from 2.5 to
5 km capped by 35 dBZ. At 23:35Z the storm sat on top of the radar (cone of silence), so that volume is only good
for the low-level picture. Lesson for the case list: pick a radar 30-120 km from the storm, or a second site.
The vault / BWER detector and the couplet measurement are rough and will be reworked once the case list is fixed.

### What the SPC data already says (EF era, 2007-2025, one record per tornado)

| Rating | Count | Width yd p50 / p90 / p99 / max | Length mi p50 / p90 / p99 |
| --- | --- | --- | --- |
| EF0 | 11,568 | 50 / 150 / 407 / 1,400 | 0.7 / 4.3 / 11.0 |
| EF1 | 8,306 | 126 / 450 / 1,100 / 2,464 | 2.9 / 10.0 / 23.3 |
| EF2 | 2,108 | 300 / 880 / 1,893 / 3,520 | 6.7 / 19.9 / 40.0 |
| EF3 | 526 | 600 / 1,590 / 2,910 / 4,576 | 13.2 / 38.0 / 82.0 |
| EF4 | 98 | 975 / 1,955 / 3,106 / 3,960 | 23.5 / 75.2 / 149.6 |
| EF5 | 10 | 1,805 / 2,280 / 2,928 / 3,000 | 31.0 / 70.0 / 125.8 |

- 129 tornadoes a mile or wider in the EF era: 19 EF1, 39 EF2, 44 EF3, 21 EF4, 6 EF5. Width is not rating.
- Region (EF era): Plains 14 % of tornadoes at night, Dixie 26 %, Midwest 18 %. EF2+ share: Plains 10.9 %, Dixie
  14.7 %, Midwest 11.4 %. Median path bearing: Plains 56°, Dixie and Midwest 62° (so "north-east" everywhere, Dixie
  slightly more east).
- Cycling (1995+, tornadoes chained into a family when the next starts within 75 min and 45 km of the previous
  one's end): 31 % of families produce 2 or more tornadoes, 15 % produce 3 or more; given 2+, the mean family is
  3.5 tornadoes; the gap between successive tornadoes has a median of 15 min (quartiles 7 and 27 min). That gap is
  shorter than the textbook 40-minute mesocyclone cycle because families include satellite and short-lived
  tornadoes between the main ones; the model will treat the ~40-minute cycle as the meso's and the shorter gaps as
  multiple tornadoes per cycle.

## 2. Public datasets I will fetch myself (no account needed)

| Dataset | Access | What it calibrates |
| --- | --- | --- |
| NEXRAD Level II archive | AWS `noaa-nexrad-level2` (anonymous HTTPS), read with Py-ART | For each reference case: hook echo and FFD geometry, 55/60/65 dBZ core sizes, BWER / vault depth, echo top, meso diameter and depth from velocity, storm motion, anvil extent (in reflectivity), cycle timing from the velocity couplet |
| NEXRAD dual-pol (2013+) | same | ZDR arc, KDP foot, TBSS: where big hail and small drops fall relative to the updraft (sets the FFD / hail core layout per mode) |
| GOES-16/18 ABI (2017+) | AWS `noaa-goes16` / `noaa-goes18` (anonymous), netCDF | Overshooting top size and lifetime, anvil expansion rate and oval shape, above-anvil cirrus plumes, cloud-top rise rate during initiation (1-minute mesoscale sectors on many of your chase days) |
| HRRR analysis (2014+), RAP (2012+) | AWS `noaa-hrrr-bdp-pds`, NOMADS, NCEI | Point proximity soundings for each reference case: the real base state for each preset |
| Radiosondes (RAOB) | U. Wyoming / IGRA2 | 00Z soundings on reference days; sanity check for the model soundings |
| SPC storm reports and mesoanalysis archive | spc.noaa.gov | Hail/wind reports to validate cores and swaths; archived parameter maps |
| NWS Damage Assessment Toolkit (DAT) | ArcGIS REST service, public | Surveyed damage points with DI/DoD and EF for real tornadoes: calibrates the damage model and width-vs-intensity |
| EF-scale DI/DoD tables | NWS / Texas Tech | Wind range per degree of damage for 28 damage indicator types (houses, barns, trees, poles, ...) |
| Published proximity-sounding climatologies | Thompson et al. 2003, 2012; Smith et al. 2012; Coffer et al. 2019, 2020; Grams et al. 2012 | Parameter windows per region and mode (table in `DESIGN.md` §5) |
| Published tornado wind profiles | Wurman & Alexander 2005; Kosiba & Wurman 2013; Wurman et al. 2014 (El Reno); Lewellen LES | Radius of max wind vs height, corner-flow amplification, near-ground wind structure |
| Published updraft / overshooting top observations | Bedka et al.; Homeyer; VORTEX2 | Updraft speeds 30-60 m/s typical, 90+ extreme; OT height 1-3 km above the tropopause |
| Mobile mesonet / VORTEX2 RFD thermodynamics | Markowski 2002; Markowski et al. 2012 | θ' deficits of tornadic vs non-tornadic RFDs (the genesis test) |

Python packages needed: `pyart`, `xarray`, `netCDF4`, `cfgrib` or `pygrib`, `metpy`, `s3fs`. I will install them in
the Python already on this PC.

## 3. Reference cases (proposed; you confirm or swap)

**Superseded 2026-10-20 by the case library:** `validation/real-storm-cases/CASES.md` (51 cases: calibrate,
holdout, null and boundary roles, SPC facts attached), built by `tools/case_catalog.py` from `case_seed.json`;
soundings by `tools/fetch_case_soundings.py`. The table below is the original proposal.

One or two real storms per preset. I pull radar, satellite and HRRR for each; your photos and video from the day are
the visual ground truth. Cases marked * are days from your chase log.

| Preset | Proposed cases | Why |
| --- | --- | --- |
| Plains LP | 2022-06-07 Lamar CO *; 2023-05-26 Clovis NM * (if those were LP; you tell me) | High Plains, dry, sculpted |
| Plains classic | 2016-05-24 Dodge City KS (cyclic, 12+ tornadoes, the map's own town); 2022-04-29 Hope KS *; 2024-05-06 Enid OK * | Visible cyclic classics |
| Plains HP | 2013-05-31 El Reno OK (2.6 mi wide); 2022-05-23 Morton TX *; 2023-05-18 Amarillo TX * | Rain-wrapped, giant RFD |
| Dixie classic | 2011-04-27 Smithville / Philadelphia MS; 2023-03-24 Rolling Fork MS | Fast, low-based, violent |
| Dixie HP | 2021-12-10 Mayfield KY; 2021-03-17 Moundville AL * | Nocturnal, long-track |
| Midwest classic | 2013-11-17 Washington IL; 2024-03-14 Indian Lake OH * | Spring/autumn systems |
| Midwest HP | 2024-05-21 Greenfield IA; 2023-04-04 Lewistown IL * | Moist, cyclic |
| Minisupercell | 2024-10-09 Hurricane Milton FL outbreak; 2024-02-04 Gainesville FL * | Low-topped |
| Outflow-boundary tornado in a marginal / LP setup | 2022-05-23 Morton TX *? 2023-05-27 Taiban NM *? (you know which days had a boundary) | The rare case the brief asks for |

## 4. What only you can provide

1. **Which of your chase days to use** for the table above, and which storms on those days (time, town, radar
   site) you were on. You saw the storm; the database did not. Swap in better ones freely.
2. **Photos and video stills by feature**, from your chase folders: inflow tails (short, long, multiple), striated
   mesos, the vault, wall clouds with tail clouds, clear slots and horseshoes, mammatus, overshooting tops, anvils
   from a distance, LP vs classic vs HP bases, every tornado shape you have. Point me at folders or tag filenames; I
   will build the reference sheet. Ten to twenty shots per feature is plenty.
3. **Anything non-public you have access to**: DOW / VORTEX2 data, a RadarScope or GR2Analyst archive, paid radar
   archives, mobile mesonet logs, Spotter Network tracks.
4. **A machine for CM1** (optional, strong). CM1 is the research cloud model 1.0's kernels were checked against. It
   needs Linux (WSL is fine; this PC does not have WSL installed, it is one command). CM1 runs of each preset
   sounding at 250 m and one tornado-resolving run at 50 m would give reference fields to validate the brain and to
   tune the tornado model. Half a day of setup, overnight runs.

## 5. Decisions needed before Phase 1 (answered items struck through)

1. ~~**Architecture sign-off**~~ Hybrid approved 2026-10-18.
2. ~~**Your friend's game constraints**~~ Unity 6000.6.6f1, RTX 3060 minimum, player goes anywhere including inside
   the tornado (2026-10-18). Still open: URP or HDRP.
3. ~~**The world**~~ The friend builds the world. Yes to a copy: it is needed for Phase 2 (render pipeline, floating
   origin, where the storm sits relative to the map) and Phase 3 (object types for damage). Not needed for Phase 1.
4. **Interaction model**: set up the environment first and then watch/chase (recommended, enables the time slider),
   or live knobs mid-storm (needs re-runs from checkpoints).
5. **Multiplayer or VR?** Both change the renderer budget.
6. **Damage targets**: what objects the world will have (houses, barns, trees, poles, vehicles, grain bins) so the
   EF damage indicators map onto them.
