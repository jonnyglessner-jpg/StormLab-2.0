# Skin test report (Phase 2: storm volume and near field)

2026-10-21 00:30, AMD Radeon RX 7900 XT, Direct3D12, Unity 6000.6.6f1.

Storm: DDC 2016-05-24 HRRR, 240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells, fires at 0 min (MLCAPE 5121, MLCIN -1 J/kg), forced w 10.0 m/s. Skin volume 400 x 400 x 120 (150 m), 237 MB.

Near field: 320 x 320 x 96 at 30 m (9.6 x 9.6 x 2.88 km), light 160 x 160 x 48, 49 MB.

Simulated 73 min in 23 s wall; 436 timeline frames; 435 wind snapshots, 52.9 MB.

## Screenshots

| Time | Stage | From the sim | From 1.0's fallback | Base / plates / curtain | Shots |
| --- | --- | --- | --- | --- | --- |
| 70 min | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x0.96 (hpness 0.36) | skin_ltg_070min_chaser, skin_ltg_070min_wide |
| 70 min, dusk | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x0.96 (hpness 0.36) | skin_ltg_070min_dusk_chaser, skin_ltg_070min_dusk_wide, skin_ltg_070min_dusk_chaser_ltg_cg, skin_ltg_070min_dusk_wide_ltg_cg, skin_ltg_070min_dusk_chaser_ltg_ic |
| 70 min, night | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x0.96 (hpness 0.36) | skin_ltg_070min_night_chaser, skin_ltg_070min_night_wide, skin_ltg_070min_night_chaser_ltg_cg, skin_ltg_070min_night_wide_ltg_cg, skin_ltg_070min_night_chaser_ltg_ic |
| 70 min, moonless | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x0.96 (hpness 0.36) | skin_ltg_070min_moonless_chaser, skin_ltg_070min_moonless_wide, skin_ltg_070min_moonless_chaser_ltg_cg, skin_ltg_070min_moonless_wide_ltg_cg, skin_ltg_070min_moonless_chaser_ltg_ic |


Scene depth: `skin_depth_test.png` has a 600 m mast 4 km in front of the camera, standing in front of the storm.

## GPU cost (this PC)

- storm volume: full target paint (BUILD, whole volume): **2.46 ms**
- storm volume: advection (whole volume): **0.76 ms**
- storm volume: light grid (whole grid): **0.58 ms**
- storm volume: sky visibility (whole grid): **1.07 ms**
- storm volume: ground light: **0.00 ms**
- storm volume work per frame (1/12 paint + 1/4 advection + 1/4 light + 1/8 sky + ground): **0.67 ms**
- near field: full paint (whole box): **0.75 ms**
- near field: light (whole grid): **0.46 ms**
- near field work per frame (1/8 paint + 1/4 light): **0.21 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, chaser view, 24 km: **2.10 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, close view, 12 km: **1.84 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo01 view, 10 km: **2.38 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo17 view, 7 km: **2.33 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo20 view, 14 km: **2.28 ms**
- ray-march + resolve, 1920x1080 at 0.5 scale, chaser view: **1.36 ms**
- total per frame at 1440p (storm volume + near field + the dearest march, photo01 view, 10 km): **3.26 ms**
- estimate on an RTX 3060 (2.5x, from compute and bandwidth, not measured): **8.1 ms** of the 8 ms storm budget

<!-- Notes below this line are kept when the test rewrites the report. -->

