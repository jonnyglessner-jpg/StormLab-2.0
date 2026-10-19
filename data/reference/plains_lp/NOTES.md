# Plains LP reference set

20 images sent 2026-10-18, tagged "Plains LP" by Jonny. Files `plains_lp_01..20`. Images 11-15 are low-resolution
web images by other photographers (one carries a Fine Art America watermark): reference only, never redistributed
or shipped in the game. The rest are Jonny's own (17 shows his Subaru).

## Cases (from Jonny, 2026-10-18)

| Case | Images | Where | Local time, facing | Storm |
| --- | --- | --- | --- | --- |
| Manchester OK, 2022-05-13 | 01, 16, 17, 18, 19, 20 | near Manchester OK (chase log: Wakita OK) | 01: WSW ~8 pm; 16-18: W ~6-7 pm; 19: NW ~9 pm; 20: NW ~8 pm (CDT) | LP supercell, 2.75 in hail and heavy lightning in the chase log; no tornado in the SPC database (OK or KS) |
| Earth TX, 2021-05-16 | 02, 04, 05, 06, 07, 08 | near Earth TX, Lamb County | 02: NE ~8 pm; 04: E ~8 pm; 05-08: NW ~6 pm (CDT) | Slow LP supercell (radar: moving east at 16-20 km/h). SPC: one tornado at 6:40 pm CST (7:40 pm CDT), 34.112 N 102.439 W, 0.97 mi, 50 yd, rating unknown (EFU). The tornado images 05-07 are therefore probably ~7:40 pm CDT, not 6 pm. Chase log: 4.25 in hail |
| Hope NM, 2022-06-03 | 09, 10 | near Hope NM | W ~4 pm (MDT) | High-based LP supercell; chase log: 2 in hail |
| Other photographers | 11, 12 (W), 13, 14 (N), 15 (W) | unknown | unknown | reference only |
| Unassigned | 03 | unknown | unknown | reads as a marginal/low-topped storm, probably not LP |

## What the set says about the LP preset (renderer and designer targets)

- **Base is high and flat-bottomed**, dark, with a razor edge; measured cloud base 1.2-1.9 km AGL in the three cases.
  The base is laminar, not ragged: no scud, no lowering except a small, smooth one at the meso.
- **Striations / stacked plates** are the signature: 3-8 concentric laminar bands wrapping the lower 2-3 km of
  the updraft, tilting up toward the inflow side, strongest at sunset lighting. The barrel is 4-8 km across.
- **The updraft tower is a sculpted bell or corkscrew** above the plates: crisp cauliflower on the sunlit side,
  smooth on the shaded side, leaning downshear, with a narrow waist where the plates end and the tower begins.
- **Precipitation is translucent**: a thin, back-lit curtain of rain/hail to one side (forward flank), often with
  the sun or sky visible through it; the ground stays lit under most of the storm. Virga and a sharp hail shaft,
  not a rain core. Crepuscular rays through the updraft are common.
- **Visibility is 50+ miles**: the whole storm sits in a clear, dry sky with blue around it; the anvil is thin and
  well above the main tower.
- **Tornadoes are visible, slender and dusty** (cone, elephant trunk, rope) with a small debris cloud, under a
  laminar base, not rain-wrapped.
- **Lightning**: anvil crawlers over the updraft and long CGs in the translucent precipitation.

## Measured environment and radar (2026-10-18)

Full numbers: `data/derived/plains_lp_cases.json`; soundings in `data/raw/soundings/lp_*`, radar quick-looks in
`data/derived/radar/lp_*.png`. Times UTC.

| | Manchester OK | Earth TX | Hope NM |
| --- | --- | --- | --- |
| Inflow sounding used | HRRR 23Z + OUN balloon 00Z | HRRR 23Z (AMA balloon was in cooler air 150 km north) | HRRR 21Z + MAF balloon 00Z |
| MLCAPE | 3,100-3,200 J/kg | 2,570 | 1,830-1,890 |
| MLLCL (cloud base) | 1,190-1,230 m | 1,525 m | 1,700-1,920 m |
| Storm top (EL / 18 dBZ echo top) | 12.1 / 14-15.5 km | 11.0 / 15.5 km | 11.0 / 17.5 km |
| 700-500 mb lapse rate | 7.4-7.9 C/km | 7.7 | 8.1-8.4 |
| 0-1 km SRH | 42-147 | 47 | 35-43 |
| 0-6 km shear | 40 kt | 28 kt | 31-46 kt |
| Storm-relative anvil wind (9-11 km) | 17-29 kt | 43 kt | 35-62 kt |
| Storm motion (Bunkers) | from 309-334 at 14-17 kt | from 308 at 12 kt | from 318-358 at 9-11 kt |
| Radar | KICT/KVNX 00:55: hail core hung aloft (max 60-64 dBZ at 6-7.5 km; 50 dBZ area 13-25 km2 at 1 km vs 22-33 at 7 km); weak-echo vault 2-3 km up to 8-10 km | KLBB 00:38 (tornado): 50 dBZ from ground to 13 km, compact (129 km2 at 1 km), vault 3-6 km, 20 m/s couplet 710 m across at the tornado | KFDX 22:16 at 236 km (beam 4-5 km above the storm base): small, 40 dBZ area 45-80 km2, tops 17.5 km |
| Track | 7-18 km W-WNW of Manchester 00:25-00:55, then drifted S-SSE | slow, east at 16-20 km/h (not stationary: see the life cycle below) | 15 km WSW of Hope 21:45-22:15; the SSE-moving storm after 22:45 was a new cell |

**What this changes in the LP preset.** All three are northwest-flow days with very slow storms (9-17 kt, moving
south-east), steep lapse rates and only moderate deep shear. The textbook LP window I wrote into `DESIGN.md` (LCL
1,800-3,000 m, shear 45-70 kt, anvil storm-relative wind above 55 kt) is wrong for these storms: their bases are
1,200-1,900 m, shear is 28-46 kt, and Manchester was LP with a 17-29 kt anvil wind. What they share is a slow,
isolated storm with a compact core held aloft. The preset window is revised in `DESIGN.md` §5, and the LP/classic
decision in the sim cannot rest on anvil-level wind alone. Earth shows the LP-to-classic transition at tornado time
(full-depth 50 dBZ column), which matches images 05-07 (tornado under a laminar base with a rain curtain behind).

## Satellite, life cycle and storm reports (2026-10-18)

Numbers: the `life_cycle`, `satellite` and `spc_reports` blocks per case in `data/derived/plains_lp_cases.json`; full
per-volume / per-scan data in `data/derived/radar/lp_*_lifecycle.json`, `data/derived/satellite/lp_*.json` and
`data/derived/radar/lp_*_spc_reports.json`; pictures in `data/derived/radar/lp_*_lifecycle.png` and
`data/derived/satellite/lp_*_HHMM.png` (the anvil at three times, infrared and visible). Tools:
`tools/radar_lifecycle.py`, `tools/fetch_goes.py` (GOES-16, 1-min mesoscale scans, parallax-corrected),
`tools/spc_reports.py`. Times UTC; echo tops are above the radar (within ~0.2 km of above ground here).

| | Manchester OK | Earth TX | Hope NM |
| --- | --- | --- | --- |
| Radar used | KICT, 93-107 km | KLBB, 52-94 km | KFDX, 222-253 km (beam 4-5 km up: weakest case) |
| First 40 dBZ echo | 00:16 | 22:48 | 18:26, over the Sacramento Mountains ~60 km W of Hope |
| Most mature (highest 50 dBZ top) | 01:42, 11.9 km (86 min after first echo) | 00:29, 14.5 km (101 min); tornado 00:40 | 19:42, 9.2 km (76 min) |
| End | 02:20, merged with a cell that formed beside it | 02:36, core collapsed; a new cell grew on its outflow 6 km SSE and became the stronger 03-04 storm | 22:53, weakened in place while new cells grew 20-30 km to its SE |
| Lifetime | 2.1 h | 3.8 h | 4.5 h (perhaps a chain of short pulses: 6 separate 50 dBZ cores rose above 5 km) |
| Max dBZ / 18 dBZ top | 67.5 / 14.7 km | 71 / 15.8 km | 58.5 / 13.6 km |
| Max 40 dBZ area, lowest tilt | 217 km2 | 382 km2 | 47 km2 |
| Motion, first / middle / last third (from, km/h) | 285 at 16 / 356 at 13 / 337 at 18 | 234 at 20 / 284 at 16 / 277 at 21 | 255 at 7 / 283 at 25 / 276 at 15 |
| First echo to its own -50 C top | 12 min | can't be separated: it grew under a neighbour's -55 C anvil; first clear own top (with an overshooting top) 39 min | about 0 min, but tops sat at -50 to -52 C for 90 min |
| First echo to a 1,000 km2 anvil | 34 min | (as above) | 92 min |
| Anvil length x width | 108 x 75 km typical (aspect 1.5) | 121 x 70 km at tornado time (aspect 1.7) | 139 x 53 km typical, 190 x 62 km at most (aspect 3) |
| Anvil axis / spreads toward | 31 deg / NE (46 deg) | 101 deg / E (93 deg) | 79 deg / ENE (81 deg) |
| Anvil reaches upwind of the radar core | 7 km | 17 km | 3 km typical, 23 km at its largest |
| Largest anvil of its own | 10,900 km2 at the merger (02:19) | 5,600 km2 at 00:38 (afterwards hemmed in by other storms' anvils) | 7,200 km2 at 21:15 |
| Anvil growth, start to largest | 110 km2/min (radius +8.6 m/s in the first hour) | 60 km2/min (a lower bound: hemmed in) | 70 km2/min |
| Coldest top while on radar | -66 C | -71 C (00:32, 8 min before the tornado) | -69 C |
| Overshooting tops (share of mature scans; typical / longest episode; size; colder than anvil) | 29 %; 3.5 / 5 min; 9 km (max 11); 7 K (max 11) | 44 %; 7 / 18 min; 8 km (max 17); 9 K (max 17) | 50 %; 7 / 11 min; 8 km (max 9); 9 K (max 12) |
| SPC reports (official, within 25 km of the storm) | 4 hail, largest 1.25 in (1 SW Manchester, 01:39); no wind, no tornado | 1.5 in hail (1 S Earth, 00:29) and the EFU tornado (00:40); no wind | 1.0 in hail (20:50) and 0.75 in (20:59); no wind |
| Largest hail in the chase log | 2.75 in | 4.25 in | 2 in |

**What this says for the model, in plain words.**

- **Life span.** These LP storms lived 2-4.5 hours and were at their tallest 75-100 minutes after the first radar
  echo. None of them simply drifted away while still strong. Each ended differently: Manchester merged with a cell
  that formed next to it; Earth's core collapsed and a new storm took over on its outflow; Hope faded in place while new
  cells grew to its south-east. For the sim, a 2-4 hour life with a 1.5 hour climb to maturity is the target. Merger
  and outflow-driven replacement are the usual ways an LP ends.
- **Anvil shape follows the wind at anvil level.** The two storms with weak storm-relative anvil wind (Manchester
  17-29 kt, Earth 43 kt) had compact anvils, 1.5-1.7 times as long as wide. Hope (35-62 kt) trailed a narrow plume 3
  times as long as wide, 190 km long at its peak. In all three the anvil reaches only 3-17 km upwind of the
  precipitation core: very little backshear. (The `backshear_km` numbers in the JSON are measured from the coldest
  cloud top, which on Hope often sits out on the plume, so Hope's 23 km is not real upwind spread.)
- **Anvil timing and growth.** On a clean case (Manchester) the -50 C top showed up 12 minutes after the first echo and
  the anvil reached 1,000 km2 after 34 minutes, then grew about 110 km2 a minute (the edge moving out at ~9 m/s). Hope,
  the high-based dry-air storm, took 92 minutes to build a 1,000 km2 anvil and then grew 70 km2 a minute.
- **Overshooting tops come and go.** One shows in 30-50 % of the scans of a mature storm. A typical episode lasts 4-7
  minutes (18 at Earth, around the tornado). They are 8-9 km across, which is about as small as the 2-3 km satellite
  pixels can show, so real domes may be smaller. They are 7-9 K colder than the anvil around them, up to 17 K at
  Earth. Earth's coldest top (-71 C) came 8 minutes before its tornado.
- **Motion.** All three moved east to south-south-east at 7-25 km/h and turned right during their lives (Manchester
  from ESE to S, Earth from ENE to E). Earth was not stationary, as these notes said before: it moved about 65 km in
  its 3.8 hours.
- **Hail.** The official reports top out at 1-1.5 in, the chase log at 2-4.25 in. Rural LP hail is sparsely reported,
  so treat 1.5 in as the documented size and the chase-log stones as the upper end, not the typical size.
- **Hope is the weak case.** KFDX sees the storm only from 4-5 km up at 230-250 km range. Its 4.5-hour "life" may be a
  string of short pulses: a new 50 dBZ core rose above 5 km about every 35-40 minutes. The satellite numbers for Hope
  are solid; its radar numbers are a sketch.
- **Photo times.** At Manchester the radar shows no 40 dBZ echo within 60 km before 00:07Z (7:07 pm CDT), but images
  16-18 are tagged ~6-7 pm. Either the camera was an hour off, or those photos show the towering-cumulus stage.

## Per-image notes

| File | Shows | Feature tags | Note for the model |
| --- | --- | --- | --- |
| 01 | Portrait: dark stacked-plates meso over grassland, sunset behind, thin precip to the right | striations, laminar base, LP/classic hybrid | 5-6 plates; the barrel tilts right (downshear); base dark and flat |
| 02 | Sunset-lit updraft with a vertical cleft between two sculpted columns, cropland | updraft tower, cauliflower texture, inflow notch, flanking tower | Two columns = main tower plus a flanking tower; the cleft is the inflow notch / vault from this angle |
| 03 | Weak, low-contrast storm over green corn with a small lowered base at right | marginal base, lowering, Midwest scene | Reads as a marginal classic / low-topped storm, not LP; keep as a "weak storm" reference |
| 04 | Massive sunset cumulonimbus: crisp cauliflower tower, pink top, laminar plates below, dark base | updraft tower, overshooting region, striations, base | The best tower-texture reference in the set: hard cauliflower lobes 1-2 km across |
| 05 | Striated "mothership" base with a cone tornado underneath, flanking towers at left, sunset glow | striations, tornado (cone), flanking line, rain-free base | Tornado under a laminar base with no rain-wrap: LP/classic hybrid |
| 06 | Same storm as 05 in black and white, slender funnel | tornado (rope/needle), striations | Shows funnel vs base contrast; funnel width ~1/50 of the base width |
| 07 | Elephant-trunk tornado with dust at the ground, orange sky, translucent rain behind | tornado (elephant trunk), debris cloud, translucent precip | Funnel curves downshear; dust fan at the ground wider than the funnel |
| 08 | Huge laminar mothership base, no tornado, sunset, rain-free | striations, mothership base, backshear | Base spans the frame; the top plates flare out like a cap (the anvil's backshear from below) |
| 09 | Portrait: high-based LP in the desert (mountains on the horizon), flat dark base | high base, LP, dry air | Base ~3 km AGL; precip visible only as a faint shaft on the right |
| 10 | Same storm from a highway, wide base, blue sky around | high base, LP, isolated cell | The whole storm fits in a wide lens: small LP cell |
| 11 | Low-res web image: bell-shaped LP updraft, crepuscular rays, translucent curtain | updraft bell, rays, translucent precip | Other's image; the canonical LP silhouette |
| 12 | Low-res web image (watermarked): stack of plates at twilight, flying-saucer base | striations, saucer base | Other's image; 6+ plates, very smooth |
| 13 | Low-res web image: sunset LP tower with laminar base and anvil glow | tower, laminar base, anvil | Other's image |
| 14 | Low-res web image: saucer base with glowing tower and lightning | striations, lightning, tower | Other's image |
| 15 | Low-res web image: laminar striations with a CG strike and translucent rain | lightning (CG), striations, translucent precip | Other's image |
| 16 | Backlit LP updraft at sunset, crepuscular rays, thin precipitation, blue sky | updraft, rays, LP, thin precip | Jonny's; the backlit case for the lighting model |
| 17 | Portrait: stacked plates over a red dirt road with the Subaru, sunset | striations, barrel, scale reference | The car gives scale; base edge extremely sharp |
| 18 | Striated base with a translucent rain curtain on the right, windmill | striations, translucent precip, LP/classic hybrid | Rain curtain brightness: you can see the sunset through it |
| 19 | Anvil crawler lightning over a striated updraft at dusk | lightning (crawler), updraft, precip core | The precip core is the darker column at centre, still translucent |
| 20 | Dark striated barrel against sunset, windmill | striations, barrel, backshear | Barrel diameter vs height ratio ~2:1 |

## Still wanted for this preset (updated 2026-10-18)

From Jonny:
1. **2-4 more LP days in other setups.** All three cases are slow northwest-flow storms. Wanted: south-west-flow
   dryline LPs on the High Plains (faster storms, stronger winds aloft). Log days that may fit: 2021-05-22 Fort
   Sumner NM, 2021-05-23 Burlington CO, 2022-06-07 Lamar CO, 2023-05-16 Yuma CO, 2023-05-26 Clovis NM, 2024-06-01
   Sterling CO, 2024-06-08 Lindon CO, 2024-06-10 Aladdin WY / Badlands SD, 2025-05-23 Akron CO. Say which were LP
   and send 3-5 photos each with time and facing.
2. **One storm's whole life in order.** Earth TX is ideal. The folder for the whole day is enough: times come from
   the photo files. Say whether the camera clock was on local time or Ohio time.
3. **LP features not in the set yet:** the hail shaft and hail on the ground with a size reference (Earth had
   4.25 in), the wall cloud before the Earth tornado, an inflow tail on an LP, the anvil and overshooting top from
   far away, mammatus, and how each storm ended.
4. **A few lines per case:** roughly when it started and died, whether it tried to cycle (the Earth storm came
   close to a second tornado), and what you think kept it LP.
5. **An LP that produced a tornado only after hitting a boundary**, if you have one (the rare path in the brief).
6. **Image 03:** which day it is from.

7. **Manchester photo times (images 16-18):** the radar has no 40 dBZ echo there before 7:07 pm CDT. Was the camera an
   hour off, or are these the towering-cumulus stage?

Fetched by Claude (done 2026-10-18, see "Satellite, life cycle and storm reports"): GOES anvil shape, spread rate and
overshooting tops; each storm's radar life cycle; SPC hail, wind and tornado reports. Still to come: a run of the
Manchester sounding through the Phase 1 model compared against the Wichita radar, once the brain's calibration is done.
