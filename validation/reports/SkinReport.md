# Skin test report (Phase 2: storm volume and near field)

2026-10-18 23:54, AMD Radeon RX 7900 XT, Direct3D12, Unity 6000.6.6f1.

Storm: DDC 2016-05-24 HRRR, 240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells, fires at 0 min (MLCAPE 5121, MLCIN -1 J/kg), forced w 10.0 m/s. Skin volume 400 x 400 x 120 (150 m), 233 MB.

Near field: 320 x 320 x 96 at 30 m (9.6 x 9.6 x 2.88 km), light 160 x 160 x 48, 44 MB.

Simulated 73 min in 23 s wall; 436 timeline frames; 435 wind snapshots, 52.9 MB.

## Screenshots

| Time | Stage | From the sim | From 1.0's fallback | Base / plates / curtain | Shots |
| --- | --- | --- | --- | --- | --- |
| 70 min | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x1.20 (hpness 1.00) | skin_070min_close, skin_070min_photo17, skin_070min_photo20 |
| 70 min, low sun | Occlusion | Tower, Overshoot, Anvil, Mammatus, Striation, Wall, Tail, Vault, Rfd, Ffd, Hail, Flank, Motion, Meso, ColdPool | None | base 0.94 km, 4 plates over 1.2 km (amount 0.53), curtain x1.20 (hpness 1.00) | skin_070min_photo17_lowsun, skin_070min_photo20_lowsun |

Reference photo | render, 70 min: `compare_photo17.png`, `compare_photo20.png`, `compare_photo17_lowsun.png`, `compare_photo20_lowsun.png`. Views: photo01 = plains_lp_01.jpg (portrait, 10 km from the meso, rain 30 degrees right; the photo faces 250, this storm's camera 62); photo17 = plains_lp_17.jpg (portrait, 7 km from the meso, rain 45 degrees right, 96 degree lens; the photo faces 270, this storm's camera 47); photo20 = plains_lp_20.jpg (4:3, 14 km from the meso, rain 35 degrees right; the photo faces 315, this storm's camera 57). The low-sun variant puts the sun at 3.7 degrees, bearing 294.


Scene depth: `skin_depth_test.png` has a 600 m mast 4 km in front of the camera, standing in front of the storm.

## GPU cost (this PC)

- storm volume: full target paint (BUILD, whole volume): **2.66 ms**
- storm volume: advection (whole volume): **0.74 ms**
- storm volume: light grid (whole grid): **0.84 ms**
- storm volume work per frame (1/12 paint + 1/4 advection + 1/4 light): **0.62 ms**
- near field: full paint (whole box): **0.66 ms**
- near field: light (whole grid): **0.46 ms**
- near field work per frame (1/8 paint + 1/4 light): **0.20 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, chaser view, 24 km: **2.66 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, close view, 12 km: **0.88 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo01 view, 10 km: **1.39 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo17 view, 7 km: **1.65 ms**
- ray-march + resolve, 2560x1440 at 0.5 scale, photo20 view, 14 km: **1.15 ms**
- ray-march + resolve, 1920x1080 at 0.5 scale, chaser view: **1.57 ms**
- total per frame at 1440p (storm volume + near field + the dearest march, chaser view, 24 km): **3.48 ms**
- estimate on an RTX 3060 (2.5x, from compute and bandwidth, not measured): **8.7 ms** of the 8 ms storm budget

<!-- Notes below this line are kept when the test rewrites the report. -->

