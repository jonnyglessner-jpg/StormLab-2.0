# Cloud microphysics

Background for the brain's 6-class LFO/Gilmore ice scheme (`Runtime/Dynamics`), the cold pools it produces, and
the hail core and swath. Sources: Stull ch 4, 6, 7, 15; METR 4403 §32; CM1 notes §9. See `references.md`.

## The scheme we run, and its CM1 relatives

The Lab's scheme comes from StormLab 1.0: **single-moment, six classes** (vapour, cloud water, cloud ice, rain,
snow, hail/graupel), the Lin–Farley–Orville family as extended by Gilmore, Straka and Rasmussen. That is **CM1
ptype 4**.

| | Lab | CM1 WK supercell namelist |
|---|---|---|
| Scheme | LFO/Gilmore 1-moment (≈ ptype 4) | **Morrison 2-moment with hail (ptype 5)** |
| Energy set | constant L_v, dry c_p (≈ eqtset 1, no Kirchhoff) | eqtset 2 (moist heat capacities, T-dependent L) |
| Rain intercept for evaporation | 1×10⁶ m⁻⁴ (own value, against 1.0's cold-pool bias) | predicted (2-moment) |

- **CM1 forbids eqtset 2 with ptype 4**, so the Lab's energy treatment is consistent with how CM1 itself runs LFO ice.
- **The WK "CM1 range" for cold pools comes from Morrison runs.** Comparing a 1-moment LFO storm with it is not like
  for like. Compare against ptype 4 runs when the source is available.
- Moist heat capacity and Kirchhoff L_v would change latent heating by only 2–5 %. Low priority.

## Nucleation and growth

- **CCN:** 10⁸–10⁹ activated droplets per m³ (maritime ~10⁸, continental ~6×10⁸), about 1 mm apart. Köhler theory:
  drops below the critical radius stay as haze; above it they grow.
- **Ice nuclei are scarce:** about 1–10 per litre, against ~10⁶ droplets. Supercooled water survives to −40 °C,
  where it freezes homogeneously.
- **Condensation alone never makes rain.** The vapour is shared among too many droplets, and small drops grow fastest,
  so the spectrum narrows. Rain needs collision–coalescence (warm) or ice processes (cold).
- **Wegener–Bergeron–Findeisen:** e_s over ice < e_s over liquid, so in mixed-phase cloud ice grows while droplets
  evaporate. **Strongest at −8 to −16 °C.**
- **Habits:** plates 0 to −4 °C, columns and needles −4 to −10, plates and dendrites −10 to −22 (dendrites near
  −15, the dendritic growth zone −12 to −17), columns below −22.
- **Riming** (supercooled drops freezing on ice) makes graupel (≤ 5 mm). **Wet growth** (water spreads before
  freezing) makes hail. Aggregation is easiest above −5 °C (sticky crystals) and with interlocking dendrites.
- **Most midlatitude thunderstorm rain is melted ice.** Deeper clouds give heavier rain with bigger drops.

## Fall speeds

| Hydrometeor | Fall speed |
|---|---|
| Cloud droplets (R < 40 µm) | Stokes w_T = 1.19×10⁸·R² m/s (mm/s to cm/s) |
| Rain | rises with size, **caps near 11 m/s**; drops break up above R ≈ 2.5 mm. Stull fit: \|w_T\| = (70 kPa/p)^½·[12 − exp((2500 − R_µm)/1000)] m/s |
| Hail | \|w_T\| = [(8/3)(g/C_D)(ρ_ice/ρ_air)R]^½, C_D ≈ 0.55 (0.4–0.8); 20 cm, 0.87 kg stone ≈ 35 m/s |
| Thin air | everything falls faster aloft (the (p₀/p)^½ factor) |

**Size distribution (rain).** Marshall–Palmer N(D) = N₀ e^(−ΛD), N₀ ≈ 8×10⁶ m⁻⁴ (by diameter), Λ falling with rain
rate. Heavier rain has bigger drops. Thunderstorms have extra large drops (2–8 mm), from melting graupel and
aggregates.

## Hail

- Needs three things: an **embryo** (frozen drop or graupel) formed where it won't be blown out the anvil (flanking
  line feeders, an upshear part of the updraft, early in the storm), a **moderately strong updraft**, and
  **supercooled water** for accretion. Growth is mostly at −10 to −30 °C, peaking at −15 to −20 °C.
- **Large hail takes 40–60 min**, so only long-lived storms (supercells) make it.
- **The BWER recycles embryos.** Graupel falling from the upshear overhang re-enters the updraft.
- More CAPE doesn't always mean bigger hail: simulations find 2000–2400 J/kg optimal, and giant hail can fall with
  CAPE < 1000 J/kg in strong shear. The biggest stones come with moderate CAPE, a high maximum parcel level and strong
  effective shear.
- Hail is only 2–3 % of a storm's precipitation mass. Hailswaths are 160–320 km long and 8–24 km wide; hail streaks
  10–20 km by 0.5–3 km.
- Size classes: severe ≥ 1", significant ≥ 2". Record Vivian SD 2010, 8".
- **Our known bias: too much hail aloft.** Against KDDC the model's 50 dBZ echo reaches 14.5 km instead of 9.5, the
  strongest echo is 75 dBZ instead of 55, and the vault is too high. The renderer must not take the model's
  reflectivity literally (`model-limitations.md`).

## Cold-pool microphysics (where the cold comes from)

- **Rain evaporation dominates.** 2.5 K of cooling per g/kg evaporated; loading by 10 g/kg of rain is only ~2.9 K of
  θ_v.
- **Melting:** L_f cooling as snow and hail melt below the 0 °C level.
- **Lesson from the brain (Phase 1):** 1.0's hail-melting rate included the heat of vapour condensing on the 0 °C
  stone but never removed that vapour from the air, so melting hail supplied 5–6 K of spurious cold pool. The model
  now exchanges that vapour (Rutledge & Hobbs 1983; Morrison et al. 2009). Snow melting and hail sublimation turned
  out not to matter. LFO's hail wet-growth limit made cold pools colder and stays off.
- **The remaining extreme** is most likely the mixing, not the microphysics: see K_h = 3K_m in
  `model-limitations.md`.

## Electrification (for lightning visuals)

- **Charging zone −5 to −20 °C**, where graupel, ice crystals and supercooled droplets meet in the updraft. Rimed
  graupel turns negative and settles to the mid and lower storm; crystals carry positive charge into the anvil.
- Only ~20 % of flashes are cloud-to-ground (under 10 % on the High Plains). About 9 % of CG is positive, from the
  anvil or stratiform region.
- CPTP = (−19 °C − T_EL)(C₋₂₀ − 100)/100, with C₋₂₀ the MUCAPE between 0 and −20 °C: lightning is likely when
  CPTP ≥ 1.
- **Highest CG threat when the rain first arrives.** GLM lightning jumps can precede intensification.
