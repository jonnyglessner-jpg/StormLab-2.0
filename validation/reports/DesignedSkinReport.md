# Designed storm render test

2026-10-21 02:13, AMD Radeon RX 7900 XT, Unity 6000.6.6f1.

## Tornado shapes

| Shape | EF | Width | Funnel radius ground / top | Condensation reach | Shot |
| --- | --- | --- | --- | --- | --- |
| Rope | EF1 | 90 m | 12 / 86 m | 0.98 | `designed_tornado_Rope.png` |
| Cone | EF2 | 350 m | 62 / 338 m | 1.00 | `designed_tornado_Cone.png` |
| Stovepipe | EF3 | 450 m | 151 / 221 m | 1.00 | `designed_tornado_Stovepipe.png` |
| Wedge | EF4 | 1400 m | 611 / 959 m | 1.00 | `designed_tornado_Wedge.png` |
| MultiVortex | EF3 | 900 m | 166 / 375 m | 1.00 | `designed_tornado_MultiVortex.png` |
| Drillbit | EF1 | 140 m | 22 / 176 m | 0.98 | `designed_tornado_Drillbit.png` |
| ElephantTrunk | EF2 | 220 m | 43 / 205 m | 1.00 | `designed_tornado_ElephantTrunk.png` |
| DustWhirl | EF1 | 180 m | 36 / 197 m | 0.30 | `designed_tornado_DustWhirl.png` |
| Sidewinder | EF1 | 110 m | 13 / 91 m | 0.98 | `designed_tornado_Sidewinder.png` |

Sheet: `designed_tornado_sheet.png`, in the order Rope, Cone, Stovepipe / Wedge, MultiVortex, Drillbit / ElephantTrunk, DustWhirl, Sidewinder.

## Variety

| Tile | Preset / region | Seed | Tornado | Life | Look | Shot at |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | plains / Plains | 10 | EF3 Stovepipe 162 m | Stovepipe | Ragged, brightness 0.81, red soil, dirt 0.41, lean 62 m | 0.45 of its life, Stovepipe tornado |
| 2 | plains / Plains | 28 | EF2 Cone 197 m | Cone | Laminar, brightness 0.86, red soil, dirt 0.16, lean 147 m | 0.45 of its life, Cone tornado |
| 3 | plains / Plains | 46 | EF1 ElephantTrunk 77 m | ElephantTrunk > Rope | Ragged, brightness 0.78, tan soil, dirt 0.14, lean 218 m | 0.45 of its life, Elephant-trunk tornado |
| 4 | ohio / Midwest | 65 | EF0 ElephantTrunk 59 m | ElephantTrunk > Rope | Laminar, brightness 0.96, dark soil, dirt 0.05, lean 52 m | 0.45 of its life, Elephant-trunk tornado |
| 5 | dixie / Dixie | 84 | EF1 Cone 225 m | Cone | Ragged, brightness 0.78, red clay soil, dirt 0.11, lean 69 m | 0.45 of its life, Cone tornado |
| 6 | dryline / Plains | 124 | EF1 Sidewinder 124 m | ElephantTrunk > Sidewinder | Ragged, brightness 1.05, red soil, dirt 0.21, lean 408 m | 0.45 of its life, Sidewinder tornado |

Sheets: `designed_tornado_variety.png` (chaser south-east, in the inflow, looking into the storm) and `designed_tornado_variety_sunlit.png` (chaser toward the sun, bearing -88), left to right, top to bottom.

## Life of one tornado

Seed 43, EF5 2202 m, 79 min: Stovepipe > Wedge > Stovepipe, satellite at 0.42; Ragged, brightness 0.63, red soil, dirt 0.54, lean 25 m.

| Tile | Fraction of life | Stage | Funnel radius ground / top |
| --- | --- | --- | --- |
| 1 | 0.06 | Multiple-vortex tornado | 227 / 678 m |
| 2 | 0.20 | Stovepipe tornado | 305 / 778 m |
| 3 | 0.40 | Wedge tornado | 909 / 1357 m |
| 4 | 0.62 | Wedge tornado | 950 / 1400 m |
| 5 | 0.78 | Stovepipe tornado | 756 / 1356 m |
| 6 | 0.93 | Tornado roping out | 361 / 789 m |

Sheet: `designed_tornado_life.png`, left to right, top to bottom.

## Timelapse

Seed 3: life 240 min; cycles: wall cloud at 60 min, EF2 ElephantTrunk 483 m from 71 to 81 min; wall cloud at 119 min, EF1 Drillbit 218 m from 128 to 130 min; wall cloud at 149 min, EF3 MultiVortex 1477 m from 162 to 182 min; wall cloud at 190 min, EF0 Sidewinder 27 m from 202 to 205 min


| Time | Stage | Shot |
| --- | --- | --- |
| 10 min | Towering cumulus | tile 1 |
| 20 min | Cumulonimbus | tile 2 |
| 30 min | Supercell organising | tile 3 |
| 40 min | Supercell organising | tile 4 |
| 50 min | Supercell organising | tile 5 |
| 60 min | Supercell | tile 6 |
| 70 min | Rear-flank downdraft | tile 7 |
| 80 min | Tornado roping out | tile 8 |
| 90 min | Supercell | tile 9 |
| 100 min | Supercell | tile 10 |
| 110 min | Supercell | tile 11 |
| 120 min | Supercell | tile 12 |
| 130 min | Rear-flank downdraft | tile 13 |
| 140 min | Supercell | tile 14 |
| 150 min | Supercell | tile 15 |
| 160 min | Rear-flank downdraft | tile 16 |
| 170 min | Multiple-vortex tornado | tile 17 |
| 180 min | Tornado roping out | tile 18 |
| 190 min | Supercell | tile 19 |
| 200 min | Rear-flank downdraft | tile 20 |
| 210 min | Supercell | tile 21 |
| 220 min | Supercell | tile 22 |
| 230 min | Dissipating | tile 23 |

Contact sheet: `designed_timelapse.png`, 10 min apart, left to right, top to bottom.

