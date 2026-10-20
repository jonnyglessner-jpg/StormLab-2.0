# Supercell Lab 2.0 dynamics validation

Run 2026-10-20 02:31, suite `quick`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

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
| run-ahead scheduler respects budget and speed cap | PASS | average tier, 4 ms per 60 fps frame: step split into 6 pieces (advect theta qv qc qr 1.6, advect qi qs qh u 1.5, advect v w 1.2, microphysics, fall, buoyancy 1.0, mixing, sponge, damping 2.5, drag, nudging, pressure 2.4; diagnostics 1.0 ms); estimated GPU time per frame at most 3.4 ms (largest piece 4.0 ms); measured per frame (queue to GPU done, includes sync) mean 2.4, 95th percentile 3.8, max 6.0 ms; budget alone runs 30x real time; with the 10x cap 9.5x |

11 passed, 0 failed, 0.1 min.

