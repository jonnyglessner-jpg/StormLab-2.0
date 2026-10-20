# Supercell Lab 2.0 dynamics validation

Run 2026-10-18 23:02, suite `tune`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| sensitivity: mixing and rain evaporation | PASS | see sensitivity.md |

1 passed, 0 failed, 1.8 min.

## Sensitivity: sub-grid mixing and microphysics (average tier, 2 h)

Diagnostic switches (never used in real runs): 1 no rain evaporation, 2 no latent cooling from hail melting, 4 no hail sublimation, 8 snow melting with LFO's vapour term, 16 no latent cooling from snow melting.

| Case | Smagorinsky cs | n0r for evaporation | n0r for fall speed | Switches | Hail wet growth | Melting-hail vapour exchange | Peak w | Peak w after 20 min | Peak 1 km meso after 20 min | Coldest cold pool (grid point / 1 km mean) | Median coldest, 60-120 min | w at 2 h | Every 15 min (min:w/cold pool) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| WK | 0.25 | 1E+6 | 8E+6 | 0 | off | on | 70.1 | 70.1 | 0.0334 | -8.7 / -8.5 | -7.7 | 58.9 | 15:17/-1.2 30:52/-4.5 45:60/-6.1 60:60/-8.0 75:59/-8.1 90:59/-7.9 105:59/-8.7 120:59/-6.6  |
| DDC | 0.25 | 1E+6 | 8E+6 | 0 | off | on | 103.5 | 103.5 | 0.0345 | -11.2 / -11.1 | -7.3 | 79.4 | 15:71/-1.5 30:86/-6.2 45:89/-8.7 60:94/-10.5 75:94/-10.0 90:83/-6.5 105:85/-7.1 120:79/-8.1  |

