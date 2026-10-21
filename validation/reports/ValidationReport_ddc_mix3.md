# Supercell Lab 2.0 dynamics validation

Run 2026-10-20 22:53, suite `ddc`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| Dodge City 2016-05-24 HRRR sounding, 2 h (average tier) | PASS | a storm forms and runs 2 h: peak updraft 94.7 m/s (25-110; parcel theory gives 101), peak 1 km meso 0.0408 1/s (at most 0.08), coldest cold pool -8.6 K (no colder than -15), no NaN: yes; storm motion after 30 min: median from 228 at 28 kt (Bunkers from 238 at 24 kt), 12 of 19 samples within 10 kt of the median |
| Dodge City storm vs KDDC radar geometry | PASS | mature storm (60-120 min, 13 volumes), model median against KDDC 22:48Z: 18 dBZ top 16.0 km (15.5), 50 dBZ top 15.0 km (9.5), 40 dBZ area at 1 km 527 km2 (226; 467 at 23:35Z), 50 dBZ core at 1 km 185 km2 (27; 142), vault in 13 of 13 volumes, median 2.0-8.5 km (2.5-5.0), strongest echo 73.3 dBZ (55.1; not tested); known biases outside the radar values (reported, not gated): 50 dBZ echo top, km |

2 passed, 0 failed, 0.7 min.

## Dodge City 2016-05-24, HRRR 22Z sounding, average tier

240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells; dt 2 s; MLCAPE 5121 J/kg, MLCIN -1 J/kg, 0-1/0-3 km SRH 83/176 m2/s2, 0-6 km shear 32 kt; grid moves with the Bunkers right-mover, from 238 at 24 kt; fires at 0 min (MLCAPE 5121, MLCIN -1 J/kg), forced w 10.0 m/s; 44 s wall for 2 h; timeline 130 MB (180 KB per frame), checkpoints 752 MB (9).

Every 5 minutes: peak updraft, its height, mesocyclone vorticity at 1 km (1 km block mean, and grid-point peak) and 4 km, mesocyclone depth, coldest surface theta' in the cold pool, max reflectivity, 18 dBZ echo top, largest hail at the ground, and the storm's ground-relative motion (from the drift of the 5 km updraft over the frame).

| t_min | wmax_ms | updraft_x_km | updraft_y_km | updraft_z_m | meso1_zeta | meso1_zeta_peak | meso4_zeta | meso_depth_m | coldpool_K | dbz_max | echo_top18_m | hail_mm | storm_from_deg | storm_kt |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | 9.2 | 35.8 | 40.2 | 1725 | 0.0025 | 0.0046 | 0.0010 | 0 | 0.0 | 42 | 3686 | 0 | 238 | 24 |
| 10 | 13.0 | 29.2 | 44.2 | 4432 | 0.0032 | 0.0060 | 0.0049 | 0 | 0.0 | 55 | 5886 | 0 | 217 | 23 |
| 15 | 63.7 | 27.8 | 42.2 | 7017 | 0.0063 | 0.0076 | 0.0222 | 4000 | -1.3 | 61 | 11701 | 0 | 211 | 29 |
| 20 | 83.7 | 28.5 | 44.8 | 11216 | 0.0104 | 0.0172 | 0.0175 | 5000 | -2.4 | 65 | 16546 | 0 | 193 | 28 |
| 25 | 88.1 | 28.5 | 43.8 | 10570 | 0.0138 | 0.0274 | 0.0299 | 12000 | -2.6 | 69 | 16223 | 0 | 222 | 25 |
| 30 | 82.5 | 27.8 | 44.5 | 12185 | 0.0242 | 0.0424 | 0.0299 | 7000 | -2.6 | 69 | 16546 | 7 | 243 | 31 |
| 35 | 87.4 | 18.8 | 53.5 | 10570 | 0.0154 | 0.0249 | 0.0256 | 10000 | -4.7 | 68 | 15900 | 11 | 220 | 32 |
| 40 | 83.9 | 26.5 | 44.5 | 10247 | 0.0216 | 0.0364 | 0.0354 | 11000 | -5.1 | 68 | 15900 | 16 | 206 | 31 |
| 45 | 81.9 | 27.5 | 43.2 | 11862 | 0.0140 | 0.0239 | 0.0262 | 9000 | -6.4 | 68 | 15900 | 12 | 204 | 23 |
| 50 | 89.9 | 20.2 | 63.2 | 11216 | 0.0132 | 0.0233 | 0.0389 | 10000 | -6.3 | 68 | 14931 | 15 | 218 | 21 |
| 55 | 81.0 | 24.5 | 46.5 | 9924 | 0.0234 | 0.0355 | 0.0280 | 10500 | -7.7 | 67 | 15254 | 13 | 203 | 21 |
| 60 | 92.9 | 25.5 | 44.2 | 12185 | 0.0147 | 0.0342 | 0.0291 | 11000 | -8.6 | 67 | 16869 | 14 | 208 | 28 |
| 65 | 85.2 | 21.8 | 72.8 | 13154 | 0.0155 | 0.0395 | 0.0373 | 5000 | -8.2 | 67 | 17192 | 9 | 227 | 27 |
| 70 | 92.8 | 21.8 | 46.5 | 11216 | 0.0147 | 0.0292 | 0.0406 | 12000 | -7.6 | 68 | 16869 | 11 | 230 | 23 |
| 75 | 85.7 | 31.8 | 58.2 | 11862 | 0.0279 | 0.0627 | 0.0423 | 14000 | -6.7 | 68 | 15254 | 12 | 227 | 23 |
| 80 | 71.9 | 21.2 | 77.5 | 11862 | 0.0128 | 0.0210 | 0.0218 | 5000 | -7.6 | 69 | 15577 | 13 | 222 | 25 |
| 85 | 78.9 | 30.5 | 60.8 | 12508 | 0.0101 | 0.0212 | 0.0279 | 10000 | -8.0 | 68 | 15900 | 9 | 231 | 27 |
| 90 | 81.9 | 28.8 | 63.5 | 11539 | 0.0133 | 0.0275 | 0.0293 | 12000 | -6.6 | 67 | 15577 | 12 | 241 | 25 |
| 95 | 65.4 | 26.5 | 50.8 | 8955 | 0.0138 | 0.0310 | 0.0324 | 11000 | -6.3 | 67 | 14608 | 15 | 254 | 24 |
| 100 | 76.1 | 33.5 | 63.2 | 8632 | 0.0149 | 0.0294 | 0.0353 | 6000 | -6.7 | 67 | 16546 | 13 | 252 | 26 |
| 105 | 86.4 | 26.8 | 54.5 | 11862 | 0.0116 | 0.0218 | 0.0488 | 13000 | -7.0 | 67 | 15577 | 11 | 246 | 30 |
| 110 | 80.6 | 32.5 | 65.8 | 12185 | 0.0176 | 0.0294 | 0.0433 | 11000 | -7.6 | 68 | 16546 | 13 | 237 | 38 |
| 115 | 76.6 | 37.2 | 60.5 | 9924 | 0.0180 | 0.0416 | 0.0475 | 10000 | -7.4 | 69 | 15577 | 7 | 235 | 43 |
| 120 | 77.4 | 10.2 | 44.5 | 9924 | 0.0180 | 0.0298 | 0.0275 | 4000 | -8.1 | 69 | 16223 | 11 | 229 | 36 |

## Dodge City 2016-05-24: model storm against the KDDC radar

The model's rain, snow and hail are turned into reflectivity with the model's own formula, averaged onto the radar tool's 1 km x 1 km x 500 m grid in a 60 x 60 km box around the strongest echo at 500 m, and measured with the same rules as `tools/fetch_nexrad.py` (echo tops, core areas, vault search). Model: every 5 minutes from 60 to 120 min (13 volumes), the mature storm. The model's clock starts at the 22Z sounding with the storm already firing, so it is not matched to a real time. At 22:48Z the storm was 30 km south-west of KDDC, a good view. At 23:35Z it sat on the radar, so only that volume's low-level numbers are good.

| Quantity | KDDC 22:48Z | KDDC 23:35Z | Model median | Model range | Tolerance | Within |
| --- | --- | --- | --- | --- | --- | --- |
| 18 dBZ echo top, km | 15.5 | 12.0 (cone of silence) | 16.0 | 14.5 to 17.5 | 15.5 +/- 2 | yes |
| 30 dBZ echo top, km | 15.5 | 10.5 (cone of silence) | 15.5 | 14.5 to 17.0 |  | not tested |
| 50 dBZ echo top, km | 9.5 | 9.0 (cone of silence) | 15.0 | 13.5 to 16.5 | 9.5 +/- 2.5* | **no**, known bias (not gated) |
| 40 dBZ area at 1 km, km2 | 226 | 467 | 527 | 425 to 680 | 113-934 | yes |
| 50 dBZ core at 1 km, km2 | 27 | 142 | 185 | 142 to 289 | 14-284 | yes |
| 55 dBZ core at 1 km, km2 | 0 | 24 | 75 | 50 to 167 |  | not tested |
| 60 dBZ core at 1 km, km2 | 0 | 0 | 28 | 16 to 83 |  | not tested |
| 40 dBZ area at 4 km, km2 | 134 | 185 | 822 | 614 to 1149 |  | not tested |
| 50 dBZ core at 4 km, km2 | 14 | 38 | 489 | 340 to 744 |  | not tested |
| 40 dBZ area at 7 km, km2 | 58 | 82 (cone of silence) | 917 | 636 to 1360 |  | not tested |
| 50 dBZ core at 7 km, km2 | 5 | 30 (cone of silence) | 522 | 372 to 853 |  | not tested |
| Strongest echo, dBZ | 55.1 | 59.4 | 73.3 | 71.7 to 75.3 |  | not tested |
| Height of the strongest echo, km | 1.5 | 1.5 | 3.0 | 2.5 to 3.5 |  | not tested |
| Vault (bounded weak echo) found | yes | yes (cone of silence) | 13 of 13 volumes | | at least half | yes |
| Vault base, km | 2.5 | 4.0 | 2.0 | 2.0 to 7.0 |  | not tested |
| Vault top, km | 5.0 | 7.5 | 8.5 | 3.0 to 10.5 |  | not tested |
| Vault depth, km | 2.5 | 3.5 | 4.5 | 1.0 to 8.5 | 1.0-4.5* | yes |
| Echo capping the vault, dBZ | 34.5 | 46.8 | 49.9 | 31.9 to 57.4 |  | not tested |

Every 5 minutes from 30 min (also in `ddc_20160524_radar.csv`):

| t_min | max_dbz | max_dbz_z_m | top18_m | top30_m | top50_m | a40_1km | a50_1km | a55_1km | a60_1km | a40_4km | a50_4km | a40_7km | a50_7km | vault_base_m | vault_top_m | vault_cap_dbz |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 30 | 75.1 | 3000 | 16500 | 16500 | 15500 | 134 | 66 | 43 | 14 | 501 | 329 | 703 | 385 | 2000 | 4500 | 50.0 |
| 35 | 75.5 | 3500 | 16000 | 16000 | 15000 | 259 | 125 | 62 | 23 | 668 | 426 | 687 | 416 | 2000 | 3000 | 47.8 |
| 40 | 74.7 | 3000 | 16000 | 15500 | 15000 | 452 | 227 | 132 | 49 | 635 | 378 | 660 | 410 | 2000 | 9000 | 60.1 |
| 45 | 74.6 | 3000 | 16000 | 15500 | 15000 | 469 | 190 | 96 | 28 | 683 | 436 | 630 | 388 | 2000 | 5500 | 61.8 |
| 50 | 73.4 | 3500 | 15000 | 14500 | 14000 | 437 | 199 | 105 | 46 | 628 | 350 | 616 | 425 | 3500 | 10500 | 43.6 |
| 55 | 73.5 | 2500 | 15500 | 15500 | 14500 | 460 | 203 | 111 | 56 | 630 | 415 | 758 | 509 | 3000 | 10000 | 43.3 |
| 60 | 73.3 | 3000 | 17000 | 17000 | 16000 | 439 | 221 | 139 | 75 | 722 | 456 | 825 | 492 | 2000 | 9500 | 40.4 |
| 65 | 73.6 | 3000 | 17500 | 17000 | 16500 | 527 | 233 | 124 | 45 | 819 | 503 | 923 | 587 | 2500 | 8000 | 46.9 |
| 70 | 73.8 | 3000 | 17000 | 16500 | 15500 | 579 | 223 | 76 | 19 | 883 | 550 | 917 | 522 | 2000 | 10500 | 31.9 |
| 75 | 74.5 | 3000 | 15500 | 15500 | 14500 | 513 | 178 | 74 | 33 | 822 | 477 | 964 | 598 | 2000 | 8500 | 46.2 |
| 80 | 73.2 | 3000 | 15500 | 15500 | 14500 | 425 | 142 | 68 | 32 | 767 | 495 | 896 | 431 | 2000 | 3000 | 55.3 |
| 85 | 72.7 | 3000 | 16000 | 15500 | 15000 | 615 | 185 | 92 | 27 | 960 | 489 | 844 | 427 | 2000 | 3000 | 52.3 |
| 90 | 72.8 | 3000 | 15500 | 15500 | 15000 | 602 | 159 | 75 | 28 | 832 | 439 | 746 | 373 | 7000 | 9500 | 45.7 |
| 95 | 73.8 | 2500 | 14500 | 14500 | 13500 | 488 | 166 | 50 | 16 | 706 | 381 | 636 | 372 | 2000 | 3500 | 53.1 |
| 100 | 71.7 | 3500 | 16500 | 16500 | 15500 | 455 | 163 | 64 | 25 | 614 | 340 | 732 | 439 | 3000 | 10500 | 49.9 |
| 105 | 73.0 | 3000 | 15500 | 15500 | 14500 | 445 | 178 | 66 | 26 | 791 | 440 | 921 | 588 | 4000 | 8500 | 47.5 |
| 110 | 73.0 | 3000 | 16500 | 16500 | 15500 | 530 | 189 | 67 | 27 | 916 | 589 | 1176 | 737 | 2000 | 3000 | 57.4 |
| 115 | 73.8 | 3500 | 15500 | 15500 | 14500 | 607 | 272 | 113 | 49 | 1057 | 695 | 1266 | 790 | 2500 | 5000 | 50.7 |
| 120 | 75.3 | 3000 | 16500 | 16000 | 15000 | 680 | 289 | 167 | 83 | 1149 | 744 | 1360 | 853 | 2000 | 8500 | 55.2 |

