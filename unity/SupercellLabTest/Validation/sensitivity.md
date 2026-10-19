## Sensitivity: cold-pool investigation (average tier, 2 h, RX 7900 XT, 2026-10-18)

Collected from five `-dynSuite tune` runs. All rows use Smagorinsky cs 0.25 and n0r 1e6 for evaporation unless the row
says otherwise. "Coldest" is the coldest lowest-level theta' over the 2 h, at a grid point and as a 1 km block mean. "Median" is
the median of the frame-by-frame coldest value over 60-120 min, which is much less noisy than the single extreme. Bit-level
changes to the shader move the extremes by 0.1-0.5 K.

Diagnostic switches (`SimParams.microDiag`, never used in real runs): 1 no rain evaporation, 2 no latent cooling from hail
melting, 4 no hail sublimation, 16 no latent cooling from snow melting.

| Case | Change from the 1.0 port | Peak w, m/s | Peak 1 km meso after 20 min, 1/s | Coldest, K (grid / 1 km) | Median 60-120 min, K |
| --- | --- | --- | --- | --- | --- |
| WK | none (baseline before this work) | 69.8 | 0.0282 | -10.4 / -10.1 | -8.5 |
| DDC | none | 105.7 | 0.0338 | -12.6 / -12.2 | -10.1 |
| WK | diagnostic: no rain evaporation | 69.2 | 0.0289 | -6.7 / -6.5 | -5.3 |
| DDC | diagnostic: no rain evaporation | 103.1 | 0.0405 | -8.3 / -7.9 | -4.5 |
| WK | diagnostic: no hail-melting cooling | 67.3 | 0.0243 | -5.2 / -5.1 | -4.4 |
| DDC | diagnostic: no hail-melting cooling | 107.2 | 0.0420 | -6.7 / -6.5 | -4.6 |
| WK | diagnostic: no snow-melting cooling | 69.8 | 0.0274 | -10.3 / -9.9 | -8.6 |
| DDC | diagnostic: no snow-melting cooling | 105.7 | 0.0338 | -12.7 / -12.6 | -10.4 |
| WK | diagnostic: no hail sublimation | 69.8 | 0.0236 | -10.3 / -9.9 | -8.2 |
| DDC | diagnostic: no hail sublimation | 99.6 | 0.0324 | -12.9 / -12.8 | -10.7 |
| WK | rain fall speed with n0r 1e6 (same as evaporation) | 69.9 | 0.0277 | -11.0 / -10.7 | -9.3 |
| DDC | rain fall speed with n0r 1e6 | 101.1 | 0.0452 | -11.9 / -11.6 | -8.4 |
| WK | n0r for evaporation 2.5e5 | 69.7 | 0.0371 | -10.0 / -9.6 | -7.6 |
| DDC | n0r for evaporation 2.5e5 | 103.0 | 0.0401 | -11.6 / -11.3 | -9.9 |
| WK | hail wet-growth limit (Lin et al. 1983) | 69.6 | 0.0235 | -10.8 / -10.6 | -9.4 |
| DDC | hail wet-growth limit | 101.4 | 0.0446 | -13.6 / -12.8 | -11.8 |
| **WK** | **melting hail exchanges vapour (the fix, now the default)** | **69.9** | **0.0284** | **-9.3 / -9.2** | **-7.8** |
| **DDC** | **melting hail exchanges vapour** | **100.5** | **0.0396** | **-11.5 / -11.2** | **-7.7** |
| WK | the fix, 85 levels (250 m aloft) | 70.1 | 0.0334 | -8.7 / -8.5 | -7.7 |
| DDC | the fix, 85 levels | 103.5 | 0.0345 | -11.2 / -11.1 | -7.3 |

What it shows:

- Snow melting and hail sublimation do not matter. Hail melting is the biggest single cooler (about 5-6 K at both cases),
  then rain evaporation (about 4 K).
- A smaller rain intercept for evaporation helps less than 1 K, and 2.5e5 would mean a median drop of 4-5 mm, which is not
  physical. Using the evaporation intercept for the rain fall speed too makes no useful difference.
- The wet-growth limit sheds water the hail cannot freeze as rain, which then evaporates: colder, not warmer. A first version
  also broke down above the -40 C level (Lf + Cw Tc vanishes near -80 C), so it now acts only above -40 C. It is kept as an
  option (`hailWetGrowth`) and is off.
- The fix: LFO's hail melting rate includes the heat of vapour condensing on the 0 C stone (air with qv above the 0 C
  saturation value, which is nearly all low-level air on these days) or evaporating from it. 1.0 took all of the melting heat
  from the air's sensible heat and left qv alone. Now that vapour really condenses (or evaporates), and the air pays only the
  conduction part (Rutledge and Hobbs 1983; Morrison et al. 2009 keep this exchange as its own process). Energy and water are
  still conserved (the microphysics test passes at fp32 roundoff).
- The floor: saturated descent in the WK sounding reaches -10 K at the ground from 4-5 km, so the 1.0 port's -10.4 K meant
  near-saturated downdrafts. At Dodge City the floor is -14 to -19 K (very dry mid-levels), so -11.5 K there is physically possible.
