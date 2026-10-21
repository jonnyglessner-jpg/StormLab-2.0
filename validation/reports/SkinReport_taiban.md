# Skin test report (Phase 2: storm volume and near field)

2026-10-20 21:27, AMD Radeon RX 7900 XT, Direct3D12, Unity 6000.6.6f1.

Sounding: `validation/soundings/lp_taiban_20230527_hrrr_inflow.json`.

Storm: DDC 2016-05-24 HRRR, 240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells, fires at 0 min (MLCAPE 2123, MLCIN -11 J/kg), forced w 10.0 m/s. Skin volume 400 x 400 x 120 (150 m), 237 MB.

Near field: 320 x 320 x 96 at 30 m (9.6 x 9.6 x 2.88 km), light 160 x 160 x 48, 49 MB.

Simulated 103 min in 32 s wall; 616 timeline frames; 615 wind snapshots, 74.8 MB.

## Screenshots

| Time | Stage | From the sim | From 1.0's fallback | Base / plates / curtain | Shots |
| --- | --- | --- | --- | --- | --- |
| 25 min | Mature supercell | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 1.28 km, 6 plates over 1.7 km (amount 1.00), curtain x0.88 (hpness 0.25) | skin_taiban_025min_chaser, skin_taiban_025min_wide, skin_taiban_025min_close, skin_taiban_025min_aerial |
| 45 min | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 1.28 km, 6 plates over 1.7 km (amount 1.00), curtain x0.88 (hpness 0.25) | skin_taiban_045min_chaser, skin_taiban_045min_wide, skin_taiban_045min_close, skin_taiban_045min_aerial |
| 70 min | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 1.28 km, 6 plates over 1.7 km (amount 1.00), curtain x0.88 (hpness 0.25) | skin_taiban_070min_chaser, skin_taiban_070min_wide, skin_taiban_070min_close, skin_taiban_070min_aerial |
| 100 min | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 1.28 km, 6 plates over 1.7 km (amount 1.00), curtain x0.88 (hpness 0.25) | skin_taiban_100min_chaser, skin_taiban_100min_wide, skin_taiban_100min_close, skin_taiban_100min_aerial |


Scene depth: `skin_depth_test.png` has a 600 m mast 4 km in front of the camera, standing in front of the storm.

## GPU cost (this PC)

- storm volume: full target paint (BUILD, whole volume): **1.70 ms**
- storm volume: advection (whole volume): **0.85 ms**
- storm volume: light grid (whole grid): **0.58 ms**
- storm volume: sky visibility (whole grid): **1.00 ms**
- storm volume: ground light: **0.01 ms**
- storm volume work per frame (1/12 paint + 1/4 advection + 1/4 light + 1/8 sky + ground): **0.63 ms**
- near field: full paint (whole box): **0.68 ms**
- near field: light (whole grid): **0.45 ms**
- near field work per frame (1/8 paint + 1/4 light): **0.20 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, chaser view, 24 km: **1.55 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, close view, 12 km: **1.47 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo01 view, 10 km: **1.65 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo17 view, 7 km: **1.76 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo20 view, 14 km: **1.64 ms**
- ray-march + resolve, 1920x1080 at 0.5 scale, chaser view: **1.03 ms**
- total per frame at 1440p (storm volume + near field + the dearest march, photo17 view, 7 km): **2.58 ms**
- estimate on an RTX 3060 (2.5x, from compute and bandwidth, not measured): **6.5 ms** of the 8 ms storm budget

<!-- Notes below this line are kept when the test rewrites the report. -->

