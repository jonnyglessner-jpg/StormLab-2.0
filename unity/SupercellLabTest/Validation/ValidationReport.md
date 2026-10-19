# Supercell Lab 2.0 dynamics validation

Run 2026-10-18 22:59, suite `all`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| calm atmosphere stays at rest | PASS | after 15 steps max abs w 0.0E+000 m/s, u 0.0E+000 m/s (limit 1E-4) |
| uniform wind carried unchanged | PASS | u=12, v=-5 m/s after 10 steps: max change u 0.0E+000, v 0.0E+000, w 0.0E+000 m/s (limit 1E-3) |
| pressure solver: flow is mass-conserving | PASS | moist bubble after 20 steps: max w 1.5 m/s, max abs div(rho u)/rho 7.5E-009 1/s (limit 1E-5; 1.0's relaxation solver allowed 2E-5) |
| microphysics conserves water and energy | PASS | 4 cases x 20 steps: worst relative water change 3.3E-007 (fp32 limit 2E-6), energy 0.0001 K (limit 0.02), negative mixing ratios: none |
| water mass conserved by the full model | PASS | WK moist storm in a closed 64 km box (walls, no sponge, no upper damping), 25 min: total water change 144.0 kt = 1.59 % of the condensate aloft plus precipitation on the ground (9054.8 kt, of which 1734.9 kt on the ground; limit 5 %) |
| dry warm bubble rises | PASS | 2 K bubble at 2 km after 300 s: warm core at 2400 m (must pass 2,400 m), max w 4.4 m/s |
| capped sounding stays quiet | PASS | 1.0 'cap holds' preset, 30 min with 0.1 K noise: no initiation: the cap would break after the end of the run (MLCAPE 2124, MLCIN -228 J/kg); max qc 0.0E+000, qi 0.0E+000 kg/kg (limit 1E-6) |
| CIN delays initiation | PASS | DDC 19Z special sounding: fires at 87 min (MLCAPE 2365, MLCIN -112 J/kg), forced w 12.0 m/s. HRRR 22Z: fires at 0 min (MLCAPE 5121, MLCIN -1 J/kg), forced w 10.0 m/s |
| outflow boundary in the initial state | PASS | -4 K, 1.2 km deep pool north of a west-east line: frame 0 coldest theta' -3.7 K, -1 K area 2600 km2 (expected about 2560), south edge 48.1 km; after 10 min the gust front has spread to 2508 km2 |
| seek restores a checkpoint and replays bit for bit | PASS | test grid, WK storm (peak w 47 m/s): 2 checkpoints (0 and 15 min); seek to 1100 s restored 900 s and re-ran; 60 of 60 replayed frames identical to the originals; timeline 24.0 MB for 151 frames, 6.0 MB of checkpoints |
| run-ahead scheduler respects budget and speed cap | PASS | average tier, 4 ms per 60 fps frame: step split into 6 pieces (advect theta qv qc qr 1.9, advect qi qs qh u 1.8, advect v w 1.6, microphysics, fall, buoyancy 1.6, mixing, sponge, damping 4.5, drag, nudging, pressure 3.1; diagnostics 1.2 ms); estimated GPU time per frame at most 4.5 ms (largest piece 4.5 ms); measured per frame (queue to GPU done, includes sync) mean 3.7, 95th percentile 6.6, max 12.1 ms; budget alone runs 29x real time; with the 10x cap 9.5x |
| Weisman-Klemp sounding makes a supercell (average tier) | PASS | peak updraft 69.9 m/s (50-70); peak 1 km meso vorticity after 20 min 0.0284 1/s (0.01-0.04), 4 km 0.0460 1/s; coldest cold pool -9.3 K; storm motion after 30 min: median from 269 at 24 kt (Bunkers from 276 at 28 kt), 12 of 19 samples within 10 kt of the median |
| Dodge City 2016-05-24 HRRR sounding, 2 h (average tier) | PASS | a storm forms and runs 2 h: peak updraft 100.5 m/s (25-110; parcel theory gives 101), peak 1 km meso 0.0396 1/s (at most 0.08), coldest cold pool -11.5 K (no colder than -15), no NaN: yes; storm motion after 30 min: median from 226 at 24 kt (Bunkers from 238 at 24 kt), 15 of 19 samples within 10 kt of the median |
| Dodge City storm vs KDDC radar geometry | PASS | mature storm (60-120 min, 13 volumes), model median against KDDC 22:48Z: 18 dBZ top 16.0 km (15.5), 50 dBZ top 14.5 km (9.5), 40 dBZ area at 1 km 449 km2 (226; 467 at 23:35Z), 50 dBZ core at 1 km 222 km2 (27; 142), vault in 12 of 13 volumes, median 4.5-9.5 km (2.5-5.0), strongest echo 75.4 dBZ (55.1; not tested); known biases outside the radar values (reported, not gated): 50 dBZ echo top, km, Vault depth, km |
| step time at both tiers | PASS | Average 8.7/9.9 ms per step, 48.5x at 4 ms/frame; High 22.3/23.7 ms per step, 16.9x at 4 ms/frame |

15 passed, 0 failed, 2.1 min.

## Weisman-Klemp supercell, average tier

240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells; dt 2 s; frame motion 14.2, -1.6 m/s; fires at 0 min (MLCAPE 2464, MLCIN -20 J/kg), forced w 10.0 m/s; 41 s wall for 2 h.

| t_min | wmax_ms | updraft_x_km | updraft_y_km | updraft_z_m | meso1_zeta | meso1_zeta_peak | meso4_zeta | meso_depth_m | coldpool_K | dbz_max | echo_top18_m | hail_mm | storm_from_deg | storm_kt |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | 9.2 | 35.8 | 40.2 | 1725 | 0.0015 | 0.0022 | 0.0010 | 0 | -0.1 | 40 | 3686 | 0 | 276 | 28 |
| 10 | 9.3 | 35.8 | 40.2 | 1851 | 0.0022 | 0.0058 | 0.0040 | 0 | -0.2 | 54 | 4917 | 0 | 258 | 20 |
| 15 | 16.7 | 33.5 | 39.2 | 4432 | 0.0025 | 0.0070 | 0.0148 | 1000 | -1.2 | 58 | 7178 | 0 | 253 | 24 |
| 20 | 40.7 | 34.2 | 42.5 | 7663 | 0.0036 | 0.0051 | 0.0205 | 4000 | -2.4 | 72 | 11701 | 0 | 252 | 34 |
| 25 | 52.9 | 29.5 | 44.2 | 9924 | 0.0047 | 0.0069 | 0.0154 | 8000 | -3.1 | 82 | 14285 | 0 | 249 | 33 |
| 30 | 52.0 | 29.2 | 54.8 | 11539 | 0.0181 | 0.0294 | 0.0234 | 6000 | -4.6 | 80 | 14931 | 37 | 252 | 28 |
| 35 | 60.6 | 24.2 | 58.2 | 12508 | 0.0096 | 0.0268 | 0.0234 | 6000 | -5.6 | 79 | 14931 | 35 | 254 | 28 |
| 40 | 66.5 | 21.5 | 61.5 | 9278 | 0.0167 | 0.0316 | 0.0251 | 7000 | -6.7 | 78 | 14931 | 32 | 247 | 24 |
| 45 | 59.1 | 27.2 | 44.2 | 8632 | 0.0160 | 0.0327 | 0.0298 | 5000 | -6.1 | 79 | 15254 | 35 | 247 | 20 |
| 50 | 63.6 | 26.5 | 44.2 | 9278 | 0.0133 | 0.0259 | 0.0348 | 5000 | -6.8 | 79 | 14931 | 36 | 237 | 17 |
| 55 | 56.9 | 14.2 | 68.8 | 10570 | 0.0118 | 0.0285 | 0.0295 | 11000 | -7.5 | 80 | 15254 | 32 | 239 | 16 |
| 60 | 57.2 | 24.2 | 45.5 | 8632 | 0.0155 | 0.0258 | 0.0333 | 8000 | -7.7 | 79 | 14931 | 37 | 249 | 12 |
| 65 | 64.9 | 8.8 | 73.8 | 11216 | 0.0153 | 0.0307 | 0.0264 | 7000 | -7.8 | 79 | 14931 | 37 | 274 | 13 |
| 70 | 61.3 | 20.5 | 49.2 | 11862 | 0.0185 | 0.0413 | 0.0303 | 9000 | -8.0 | 78 | 14931 | 34 | 272 | 17 |
| 75 | 57.3 | 22.5 | 47.5 | 10570 | 0.0137 | 0.0417 | 0.0162 | 10000 | -8.5 | 79 | 14931 | 32 | 281 | 18 |
| 80 | 53.2 | 19.5 | 45.5 | 9601 | 0.0180 | 0.0296 | 0.0254 | 7000 | -7.0 | 77 | 14931 | 29 | 288 | 26 |
| 85 | 60.7 | 18.8 | 43.8 | 11216 | 0.0163 | 0.0287 | 0.0215 | 10000 | -7.2 | 80 | 14931 | 31 | 293 | 26 |
| 90 | 62.7 | 16.8 | 51.8 | 12185 | 0.0234 | 0.0437 | 0.0214 | 6000 | -7.8 | 81 | 14931 | 43 | 299 | 27 |
| 95 | 61.6 | 16.5 | 49.5 | 11862 | 0.0232 | 0.0556 | 0.0311 | 4000 | -9.3 | 79 | 14931 | 40 | 292 | 27 |
| 100 | 60.0 | 19.8 | 42.2 | 9278 | 0.0261 | 0.0433 | 0.0366 | 13500 | -8.8 | 79 | 14931 | 37 | 276 | 27 |
| 105 | 55.6 | 21.2 | 42.8 | 11216 | 0.0202 | 0.0349 | 0.0300 | 12000 | -7.4 | 80 | 14931 | 33 | 270 | 28 |
| 110 | 60.4 | 19.8 | 46.8 | 11539 | 0.0167 | 0.0374 | 0.0248 | 14000 | -7.9 | 78 | 14931 | 28 | 261 | 30 |
| 115 | 60.2 | 16.5 | 51.5 | 10893 | 0.0183 | 0.0383 | 0.0410 | 13000 | -7.8 | 78 | 14931 | 31 | 266 | 31 |
| 120 | 59.9 | 19.2 | 48.8 | 11216 | 0.0191 | 0.0366 | 0.0252 | 9000 | -7.6 | 80 | 14931 | 34 | 275 | 31 |

## Dodge City 2016-05-24, HRRR 22Z sounding, average tier

240x240x72, dx 333 m, dz 100-323 m, 80 x 80 x 18 km, 4.1 M cells; dt 2 s; MLCAPE 5121 J/kg, MLCIN -1 J/kg, 0-1/0-3 km SRH 83/176 m2/s2, 0-6 km shear 32 kt; grid moves with the Bunkers right-mover, from 238 at 24 kt; fires at 0 min (MLCAPE 5121, MLCIN -1 J/kg), forced w 10.0 m/s; 64 s wall for 2 h; timeline 130 MB (180 KB per frame), checkpoints 752 MB (9).

Every 5 minutes: peak updraft, its height, mesocyclone vorticity at 1 km (1 km block mean, and grid-point peak) and 4 km, mesocyclone depth, coldest surface theta' in the cold pool, max reflectivity, 18 dBZ echo top, largest hail at the ground, and the storm's ground-relative motion (from the drift of the 5 km updraft over the frame).

| t_min | wmax_ms | updraft_x_km | updraft_y_km | updraft_z_m | meso1_zeta | meso1_zeta_peak | meso4_zeta | meso_depth_m | coldpool_K | dbz_max | echo_top18_m | hail_mm | storm_from_deg | storm_kt |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | 9.2 | 35.8 | 40.2 | 1725 | 0.0025 | 0.0046 | 0.0010 | 0 | 0.0 | 42 | 3424 | 0 | 238 | 24 |
| 10 | 13.8 | 29.5 | 44.2 | 4755 | 0.0032 | 0.0060 | 0.0050 | 0 | 0.0 | 55 | 5886 | 0 | 218 | 22 |
| 15 | 68.8 | 28.2 | 41.8 | 7340 | 0.0063 | 0.0077 | 0.0223 | 5000 | -1.5 | 69 | 12024 | 0 | 210 | 29 |
| 20 | 89.9 | 28.2 | 48.5 | 12508 | 0.0116 | 0.0173 | 0.0190 | 3000 | -2.7 | 77 | 16546 | 0 | 195 | 27 |
| 25 | 89.4 | 28.2 | 43.5 | 10247 | 0.0122 | 0.0295 | 0.0308 | 6000 | -3.1 | 81 | 16869 | 31 | 227 | 27 |
| 30 | 83.0 | 27.2 | 44.2 | 13154 | 0.0227 | 0.0450 | 0.0415 | 8000 | -6.3 | 79 | 15900 | 37 | 236 | 30 |
| 35 | 86.6 | 20.8 | 50.8 | 11539 | 0.0300 | 0.0790 | 0.0388 | 13000 | -7.7 | 79 | 15577 | 34 | 220 | 28 |
| 40 | 94.6 | 29.2 | 43.5 | 10893 | 0.0222 | 0.0524 | 0.0389 | 5000 | -8.3 | 79 | 15577 | 36 | 217 | 26 |
| 45 | 89.3 | 26.8 | 45.8 | 12508 | 0.0164 | 0.0379 | 0.0373 | 13000 | -8.1 | 77 | 15577 | 37 | 213 | 22 |
| 50 | 96.6 | 26.5 | 44.5 | 12831 | 0.0160 | 0.0274 | 0.0370 | 5000 | -10.7 | 77 | 16223 | 38 | 205 | 22 |
| 55 | 95.1 | 24.2 | 43.8 | 12508 | 0.0201 | 0.0320 | 0.0248 | 14000 | -11.2 | 79 | 17192 | 32 | 204 | 28 |
| 60 | 88.7 | 20.8 | 73.2 | 11862 | 0.0107 | 0.0177 | 0.0298 | 6000 | -10.9 | 78 | 16869 | 36 | 207 | 29 |
| 65 | 97.2 | 23.5 | 43.5 | 13154 | 0.0285 | 0.0572 | 0.0278 | 5000 | -10.4 | 78 | 17515 | 34 | 209 | 29 |
| 70 | 92.2 | 19.5 | 74.2 | 13154 | 0.0159 | 0.0326 | 0.0366 | 11000 | -9.9 | 77 | 16869 | 36 | 212 | 26 |
| 75 | 89.8 | 24.2 | 45.8 | 10893 | 0.0396 | 0.0757 | 0.0391 | 6000 | -9.3 | 78 | 16546 | 34 | 223 | 19 |
| 80 | 84.6 | 23.8 | 45.5 | 12508 | 0.0161 | 0.0370 | 0.0306 | 11000 | -8.6 | 77 | 16546 | 33 | 233 | 16 |
| 85 | 81.3 | 22.8 | 62.2 | 12508 | 0.0148 | 0.0371 | 0.0306 | 9000 | -7.6 | 77 | 16869 | 34 | 248 | 20 |
| 90 | 87.6 | 26.5 | 45.2 | 12185 | 0.0132 | 0.0370 | 0.0273 | 9000 | -7.9 | 79 | 15577 | 37 | 266 | 23 |
| 95 | 93.9 | 23.2 | 68.2 | 11216 | 0.0124 | 0.0203 | 0.0299 | 3000 | -7.1 | 77 | 15254 | 36 | 243 | 23 |
| 100 | 78.6 | 22.5 | 69.5 | 11216 | 0.0123 | 0.0262 | 0.0296 | 4000 | -7.3 | 78 | 15577 | 30 | 241 | 23 |
| 105 | 89.8 | 23.8 | 54.5 | 11539 | 0.0130 | 0.0315 | 0.0257 | 7000 | -6.8 | 76 | 15254 | 30 | 259 | 19 |
| 110 | 91.5 | 20.8 | 39.5 | 11539 | 0.0205 | 0.0374 | 0.0243 | 12000 | -7.0 | 76 | 15577 | 31 | 274 | 23 |
| 115 | 81.1 | 21.5 | 38.5 | 9601 | 0.0175 | 0.0422 | 0.0380 | 8000 | -7.2 | 78 | 15900 | 34 | 244 | 26 |
| 120 | 85.0 | 19.5 | 37.5 | 12508 | 0.0207 | 0.0494 | 0.0314 | 5000 | -8.6 | 77 | 16223 | 34 | 226 | 30 |

## Dodge City 2016-05-24: model storm against the KDDC radar

The model's rain, snow and hail are turned into reflectivity with the model's own formula, averaged onto the radar tool's 1 km x 1 km x 500 m grid in a 60 x 60 km box around the strongest echo at 500 m, and measured with the same rules as `tools/fetch_nexrad.py` (echo tops, core areas, vault search). Model: every 5 minutes from 60 to 120 min (13 volumes), the mature storm. The model's clock starts at the 22Z sounding with the storm already firing, so it is not matched to a real time. At 22:48Z the storm was 30 km south-west of KDDC, a good view. At 23:35Z it sat on the radar, so only that volume's low-level numbers are good.

| Quantity | KDDC 22:48Z | KDDC 23:35Z | Model median | Model range | Tolerance | Within |
| --- | --- | --- | --- | --- | --- | --- |
| 18 dBZ echo top, km | 15.5 | 12.0 (cone of silence) | 16.0 | 15.0 to 17.5 | 15.5 +/- 2 | yes |
| 30 dBZ echo top, km | 15.5 | 10.5 (cone of silence) | 15.5 | 14.5 to 17.5 |  | not tested |
| 50 dBZ echo top, km | 9.5 | 9.0 (cone of silence) | 14.5 | 13.5 to 16.5 | 9.5 +/- 2.5* | **no**, known bias (not gated) |
| 40 dBZ area at 1 km, km2 | 226 | 467 | 449 | 322 to 574 | 113-934 | yes |
| 50 dBZ core at 1 km, km2 | 27 | 142 | 222 | 171 to 318 | 14-284 | yes |
| 55 dBZ core at 1 km, km2 | 0 | 24 | 152 | 110 to 221 |  | not tested |
| 60 dBZ core at 1 km, km2 | 0 | 0 | 91 | 65 to 143 |  | not tested |
| 40 dBZ area at 4 km, km2 | 134 | 185 | 675 | 467 to 860 |  | not tested |
| 50 dBZ core at 4 km, km2 | 14 | 38 | 345 | 295 to 536 |  | not tested |
| 40 dBZ area at 7 km, km2 | 58 | 82 (cone of silence) | 564 | 487 to 954 |  | not tested |
| 50 dBZ core at 7 km, km2 | 5 | 30 (cone of silence) | 373 | 267 to 532 |  | not tested |
| Strongest echo, dBZ | 55.1 | 59.4 | 75.4 | 73.4 to 76.6 |  | not tested |
| Height of the strongest echo, km | 1.5 | 1.5 | 3.0 | 2.5 to 4.0 |  | not tested |
| Vault (bounded weak echo) found | yes | yes (cone of silence) | 12 of 13 volumes | | at least half | yes |
| Vault base, km | 2.5 | 4.0 | 4.5 | 2.0 to 7.5 |  | not tested |
| Vault top, km | 5.0 | 7.5 | 9.5 | 5.5 to 11.0 |  | not tested |
| Vault depth, km | 2.5 | 3.5 | 5.5 | 1.0 to 7.5 | 1.0-4.5* | **no**, known bias (not gated) |
| Echo capping the vault, dBZ | 34.5 | 46.8 | 48.2 | 32.3 to 59.3 |  | not tested |

Every 5 minutes from 30 min (also in `ddc_20160524_radar.csv`):

| t_min | max_dbz | max_dbz_z_m | top18_m | top30_m | top50_m | a40_1km | a50_1km | a55_1km | a60_1km | a40_4km | a50_4km | a40_7km | a50_7km | vault_base_m | vault_top_m | vault_cap_dbz |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 30 | 77.5 | 3500 | 16000 | 16000 | 15000 | 200 | 115 | 85 | 56 | 469 | 232 | 434 | 322 | 7500 | 10500 | 50.9 |
| 35 | 75.8 | 2500 | 15500 | 15500 | 14500 | 299 | 190 | 140 | 109 | 468 | 316 | 491 | 292 | 2000 | 9500 | 45.6 |
| 40 | 76.5 | 3000 | 15500 | 15000 | 14000 | 354 | 220 | 167 | 109 | 461 | 298 | 489 | 279 | 7000 | 9500 | 48.6 |
| 45 | 75.4 | 1500 | 15500 | 15500 | 14500 | 352 | 194 | 129 | 85 | 469 | 272 | 478 | 303 | 7500 | 11000 | 33.7 |
| 50 | 74.5 | 3000 | 16000 | 15500 | 15000 | 376 | 206 | 144 | 106 | 521 | 288 | 532 | 319 | 2000 | 11000 | 44.4 |
| 55 | 74.5 | 3500 | 17500 | 17000 | 16000 | 415 | 209 | 151 | 100 | 564 | 335 | 589 | 354 | 2000 | 8500 | 61.9 |
| 60 | 76.6 | 4000 | 17000 | 16500 | 15500 | 425 | 212 | 152 | 108 | 614 | 386 | 642 | 386 | 7500 | 11000 | 50.0 |
| 65 | 76.1 | 3500 | 17500 | 17500 | 16500 | 447 | 256 | 174 | 92 | 684 | 396 | 797 | 465 | 5000 | 9500 | 38.4 |
| 70 | 75.4 | 2500 | 17000 | 16500 | 15500 | 473 | 213 | 129 | 80 | 719 | 338 | 541 | 373 | 2000 | 8500 | 59.3 |
| 75 | 76.0 | 3000 | 15000 | 14500 | 13500 | 485 | 222 | 158 | 96 | 583 | 345 | 564 | 359 | 2000 | 5500 | 56.1 |
| 80 | 75.6 | 3000 | 16500 | 16500 | 16000 | 560 | 318 | 212 | 129 | 733 | 456 | 754 | 400 |  |  |  |
| 85 | 75.0 | 3000 | 16500 | 16500 | 15500 | 469 | 223 | 134 | 65 | 675 | 306 | 536 | 297 | 4500 | 10000 | 48.2 |
| 90 | 75.8 | 3000 | 15500 | 15000 | 14500 | 402 | 172 | 110 | 77 | 524 | 316 | 564 | 321 | 4000 | 10000 | 48.2 |
| 95 | 75.4 | 3000 | 15000 | 15000 | 14000 | 413 | 209 | 143 | 92 | 592 | 340 | 560 | 267 | 5500 | 7500 | 32.3 |
| 100 | 74.9 | 3000 | 15500 | 15000 | 14500 | 405 | 206 | 112 | 70 | 536 | 298 | 518 | 347 | 3000 | 9500 | 47.3 |
| 105 | 73.4 | 2500 | 15500 | 15000 | 14500 | 322 | 171 | 114 | 73 | 467 | 295 | 487 | 295 | 2000 | 9500 | 49.1 |
| 110 | 74.6 | 3500 | 15500 | 15500 | 14500 | 449 | 270 | 189 | 88 | 708 | 448 | 809 | 432 | 4500 | 11000 | 57.5 |
| 115 | 75.0 | 3500 | 16000 | 15500 | 14500 | 498 | 263 | 165 | 91 | 856 | 494 | 850 | 504 | 4500 | 8000 | 40.8 |
| 120 | 74.9 | 3500 | 16500 | 16000 | 15500 | 574 | 313 | 221 | 143 | 860 | 536 | 954 | 532 | 7500 | 8500 | 37.8 |

## Step time on this PC

GPU AMD Radeon RX 7900 XT, Direct3D12. WK supercell environment, timed after 10 min of storm (forced updraft active).

| Tier | Grid | dt | Step (no recording) | Step (with timeline) | Real time costs | Max speed at 50 % GPU | VRAM | Checkpoint |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Average | 240x240x72, 333 m, dz 100-323 m | 2 s | 8.7 ms | 9.9 ms | 5.0 ms GPU per sim-second | 48.5x real time (4 ms/frame at 60 fps) | 601 MB | 84 MB |
| High | 320x320x90, 250 m, dz 100-230 m | 1.667 s | 22.3 ms | 23.7 ms | 14.2 ms GPU per sim-second | 16.9x real time (4 ms/frame at 60 fps) | 1327 MB | 185 MB |


## Cold-pool investigation and the 81/85-level experiment (separate runs, 2026-10-18)

The cold-pool fix (melting hail exchanges vapour with the air) and the runs behind it are in `sensitivity.md`. Coldest
cold pool before and after: Weisman-Klemp -10.4 to -9.3 K, Dodge City -12.6 to -11.5 K. Median after the first hour:
-8.5 to -7.8 K and -10.1 to -7.7 K.

Vertical levels at the average tier (`-dynSuite perf -dynNZ 81|85`, and `tune -dynNZ 85`):

| Levels | dz aloft | Step / with timeline | VRAM | Checkpoint | WK peak w, 1 km meso, coldest | DDC peak w, coldest |
| --- | --- | --- | --- | --- | --- | --- |
| 72 (the tier) | 323 m | 8.7 / 9.9 ms | 601 MB | 84 MB | 69.9 m/s, 0.028 1/s, -9.3 K | 100.5 m/s, -11.5 K |
| 81 | 268 m | 10.3 / 11.5 ms | 675 MB | 94 MB | not run | not run |
| 85 | 249 m | 10.9 / 12.1 ms | 708 MB | 99 MB | 70.1 m/s, 0.033 1/s, -8.7 K | 103.5 m/s, -11.2 K |
