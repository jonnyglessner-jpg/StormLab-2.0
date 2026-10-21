# Skin test report (Phase 2: storm volume and near field)

2026-10-20 21:27, AMD Radeon RX 7900 XT, Direct3D12, Unity 6000.6.6f1.

Sounding: `validation/soundings/classic_selden_20210524_hrrr_inflow.json`.

Storm: DDC 2016-05-24 HRRR, 240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells, fires at 0 min (MLCAPE 1914, MLCIN -2 J/kg), forced w 10.0 m/s. Skin volume 400 x 400 x 120 (150 m), 237 MB.

Near field: 320 x 320 x 96 at 30 m (9.6 x 9.6 x 2.88 km), light 160 x 160 x 48, 49 MB.

Simulated 103 min in 31 s wall; 616 timeline frames; 615 wind snapshots, 74.8 MB.

## Screenshots

| Time | Stage | From the sim | From 1.0's fallback | Base / plates / curtain | Shots |
| --- | --- | --- | --- | --- | --- |
| 25 min | Mature supercell | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.71 km, 7 plates over 1.8 km (amount 1.00), curtain x0.70 (hpness 0.00) | skin_selden_025min_chaser, skin_selden_025min_wide, skin_selden_025min_close, skin_selden_025min_aerial |
| 45 min | Mature supercell | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.71 km, 7 plates over 1.8 km (amount 1.00), curtain x0.70 (hpness 0.00) | skin_selden_045min_chaser, skin_selden_045min_wide, skin_selden_045min_close, skin_selden_045min_aerial |
| 70 min | Mature supercell | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.71 km, 7 plates over 1.8 km (amount 1.00), curtain x0.70 (hpness 0.00) | skin_selden_070min_chaser, skin_selden_070min_wide, skin_selden_070min_close, skin_selden_070min_aerial |
| 100 min | Mature supercell | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.71 km, 6 plates over 1.7 km (amount 1.00), curtain x0.70 (hpness 0.00) | skin_selden_100min_chaser, skin_selden_100min_wide, skin_selden_100min_close, skin_selden_100min_aerial |


Scene depth: `skin_depth_test.png` has a 600 m mast 4 km in front of the camera, standing in front of the storm.

## GPU cost (this PC)

- storm volume: full target paint (BUILD, whole volume): **1.68 ms**
- storm volume: advection (whole volume): **0.87 ms**
- storm volume: light grid (whole grid): **0.53 ms**
- storm volume: sky visibility (whole grid): **1.02 ms**
- storm volume: ground light: **0.00 ms**
- storm volume work per frame (1/12 paint + 1/4 advection + 1/4 light + 1/8 sky + ground): **0.62 ms**
- near field: full paint (whole box): **0.78 ms**
- near field: light (whole grid): **0.44 ms**
- near field work per frame (1/8 paint + 1/4 light): **0.21 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, chaser view, 24 km: **1.76 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, close view, 12 km: **1.82 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo01 view, 10 km: **2.05 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo17 view, 7 km: **1.98 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo20 view, 14 km: **2.22 ms**
- ray-march + resolve, 1920x1080 at 0.5 scale, chaser view: **1.10 ms**
- total per frame at 1440p (storm volume + near field + the dearest march, photo20 view, 14 km): **3.05 ms**
- estimate on an RTX 3060 (2.5x, from compute and bandwidth, not measured): **7.6 ms** of the 8 ms storm budget

<!-- Notes below this line are kept when the test rewrites the report. -->

