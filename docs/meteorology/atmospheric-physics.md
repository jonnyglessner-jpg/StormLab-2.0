# Atmospheric physics

The thermodynamics and environment diagnostics the storm is built on: what `Runtime/Environment` (StormPhysics.cs),
`Runtime/Dynamics/SoundingFile.cs` and `tools/fetch_sounding.py` compute, and the definitions to check them against.
Sources are in `references.md`. Full notes: `../../../meteorology/` (outside this repo: Stull ch 3–5, 10, 14;
METR 4403 §01–02, 09, 15; SHARPpy and MetPy notes).

## Constants and basic relations

| Quantity | Value / formula | Note |
|---|---|---|
| R_d, c_pd, c_vd | 287.0, 1004, 717 J kg⁻¹ K⁻¹ | MetPy uses 287.047 and 1004.67 |
| R_d/c_p | 0.28571 | |
| L_v, L_f, L_s | 2.5×10⁶, 3.34×10⁵, 2.83×10⁶ J/kg | the Lab holds L_v constant (CM1 uses Kirchhoff: L_v ≈ 2.43×10⁶ at 30 °C, 2.59×10⁶ at −40 °C) |
| ε = R_d/R_v | 0.622 | |
| g | 9.80665 m/s² | SHARPpy uses 9.80665, its DCAPE uses 9.8 |
| θ | T(1000 hPa/p)^0.28571 | |
| θ_v | θ(1 + 0.61r − r_L − r_I) | condensate **loading** is in the −r_L − r_I terms |
| θ_e | ≈ θ + 2.5 K per g/kg of r | Bolton 1980 for precise work |
| Saturation vapour pressure | e_s = 0.6113 exp[5423(1/273.15 − 1/T)] kPa (liquid), 6139 over ice | SHARPpy uses an 8th-power polynomial and MetPy uses Bolton, so expect small differences |
| Mixing ratio | r = εe/(p − e) | |
| LCL height | ≈ 125 m per °C of (T − T_d) | the Lab uses this; MetPy uses the exact Romps (2017) LCL, which agrees with SHARPpy's to within 0.4 hPa |
| Dry adiabat | Γ_d = g/c_p = 9.8 K/km | |
| Moist adiabat | Γ_s ≈ 4–7 K/km low down, approaching Γ_d aloft | see "Moist adiabat" below |

## Stability classes (lapse rate γ = −∂T/∂z)

γ < Γ_s is absolutely stable. Γ_s < γ < Γ_d is **conditionally unstable**, the normal thunderstorm environment.
γ > Γ_d is absolutely unstable (superadiabatic). The autoconvective lapse rate is 34.2 K/km.

**Superadiabatic surface layers in real soundings.** Model and analysis soundings start at the 2 m skin temperature.
The Dodge City HRRR sounding is 3.6 K superadiabatic in its lowest 170 m, which blew the model up until the loader
mixed it out (`SoundingFile`: superadiabatic layers below 1.5 km are mixed out, as in common CM1 practice). The
diagnostics should be computed on the same mixed profile the model runs, or they won't match.

## Buoyancy, CAPE and CIN

B = g(T_v,parcel − T_v,env)/T_v,env, minus loading in the model: B = g[θ′/θ₀ + 0.608q_v′ − Σq_hydrometeors] (the CM1
form; the Lab matches it).

**Parcel choice.**

| Parcel | Definition | Use |
|---|---|---|
| SB | the surface parcel | quick, usually too unstable |
| ML | mean θ and q of the lowest **100 hPa** (SPC, SHARPpy, MetPy) | default for severe weather. **The Lab uses the lowest 1 km**, which is close on the Plains but not identical |
| MU | max θ_e in the lowest 300 hPa | elevated storms |
| Effective | levels whose own parcel has CAPE ≥ 100 J/kg and CIN ≥ −250 J/kg | defines the effective inflow layer |

**CAPE has three definitions in our tools. Know which you are comparing.**

| Code | CAPE integrates | CIN |
|---|---|---|
| SHARPpy / SPC | **every** positive area up the column | negative areas below 500 hPa only |
| MetPy `cape_cin` | LFC → EL, **including** negative notches between them | everything from the start to the LFC |
| Lab StormPhysics.cs | the **largest** positive area | negatives below that area's LFC |

They agree on single-hump soundings and can differ a lot on elevated or double-hump ones.

**Moist adiabat.** SHARPpy's Wobus fit runs warmer aloft than MetPy's integrated pseudo-adiabat for the same LCL:
+0.36 K at 500 hPa, +1.1 K at 200 hPa on the DDC parcel. **So SHARPpy's CAPE is 5–8 % higher than MetPy's on every
Lab sounding. Treat a 5–8 % gap as agreement.** It is not virtual temperature: MetPy 1.7.1 also uses T_v.

**MetPy CIN gotcha.** `cape_cin(p, T, Td, parcel_profile(p, mlT, mlTd))` (what `fetch_sounding.py` does today) gives
the right ML CAPE but **ML CIN 15–60 J/kg too weak**. Use `mixed_layer_cape_cin(p, T, Td, depth=100*units.hPa)` and
`surface_based_cape_cin`. Not yet applied in the tool.

**Rules of thumb.**
- w_max = √(2·CAPE) (1000 J/kg → 45 m/s). It **overestimates**: the perturbation-pressure gradient (wide
  updrafts), entrainment (narrow ones) and loading all reduce it. Stull's "likely" value is about w_max/2. A
  well-resolved model supercell reaches 85–95 % of parcel theory in very high CAPE (our Dodge City run).
- **CAPE shape matters.** For the same CAPE, fat CAPE concentrated low (nCAPE = CAPE/(z_EL − z_LFC) ≥ 0.3 m/s²)
  accelerates parcels harder near the ground, resists entrainment and favours tornadoes. Tall, skinny CAPE
  (≤ 0.1) means heavy rain and tornadoes are unlikely.
- MLCAPE: under 300 J/kg mostly stable, 1000–2500 moderate, 2500–3500 strong, 3500+ extreme. None of these
  thresholds discriminates sharply.
- CIN: 0 to −20 is a weak cap, **−20 to −60 the ideal moderate cap**, −60 to −100 strong, below −100 intense.
  Supercells can survive CIN worse than −200 after dark because their dynamic lift pulls capped inflow up
  (7 Jun 2022, Pueblo).
- Entraining CAPE (ECAPE, Peters et al. 2023) is lower in dry environments. Failed initiation comes from weak
  forcing + skinny CAPE + a dry layer.

## The environment sounding

**The loaded gun.** A deep elevated mixed layer (EML) from the Mexican Plateau or the southern Rockies rides over
Gulf moisture: a moist boundary layer, a strong cap at the EML base, then steep 700–500 hPa lapse rates (often
7.5–8.5 K/km on the Plains). Lapse-rate plumes can be tracked for days.

**Lapse-rate tendency** (why the cap erodes):
∂γ/∂t = −V·∇γ − w∂γ/∂z + (∂V/∂z)·∇T + (∂w/∂z)(Γ_d − γ) − (1/c_p)∂q/∂z.
The stretching term (∂w/∂z > 0 in a stable layer steepens it) is **how lift erodes the cap**. On the mesoscale it
is about 10× horizontal advection. Failed towers moisten and cool their path, so later attempts succeed more often.
That is the physical basis for "a capped environment really waits" (DESIGN §2.2).

**Moisture.** Daytime mixing lowers surface dewpoints when it reaches deeper than the moist layer. Moisture
return takes several days of modification over the Gulf. Dewpoint ≥ 16 °C allows heavy rain; ≥ 21 °C greater
severity. Supercell boundary layers average θ_w ≈ 24 °C (θ_e ≈ 351 K).

**Diurnal timing.** Boundary-layer heat and moisture peak about 30 min before sunset. The nocturnal low-level jet
(an inertial oscillation after sunset decoupling, period 2π/f ≈ 17 h, plus sloping terrain) raises low-level shear
and SRH in the evening.

## Wind profile diagnostics

| Quantity | Definition | Gotchas |
|---|---|---|
| Bulk shear (BWD) | \|V_top − V_bottom\| | 0–6 km ≈ 20 kt separates severe from non-severe; > 30 kt organized/supercells |
| Effective BWD | inflow base → **50 %** of the depth to the MU EL | SHARPpy ConvectiveProfile and SPC use this. SHARPpy's docs tutorial computes it over the inflow layer itself; that is a different number |
| Bunkers motion | 0–6 km mean wind ± 7.5 m/s perpendicular to the deep shear | Bunkers 2000 (non-parcel, the Lab and MetPy) vs 2014 parcel-based (SHARPpy default: effective base → 65 % of MU EL). DDC HRRR: 238°/24 kt vs 236°/18 kt. **SRH and every composite inherit the difference** |
| Layer means | MetPy Bunkers is mass-weighted (∫A dp/∫dp); SHARPpy is pressure-weighted (∫A p dp/∫p dp) | 2–3 kt and up to 15° in the Bunkers vector |
| SRH | ∫(V − c)·ω_h dz = −2 × the area swept by the storm-relative wind | **depends on the storm motion assumed**: recompute it with the storm's actual motion. 0–1 km (or 0–500 m, Coffer et al. 2019) matters most for tornadoes |
| Storm-relative winds | V − c | low-level inflow > 40 kt big supercells, < 25 kt mini-supercells. Anvil-level (9–12 km): < 18 m/s HP, 18–28 classic, > 28 LP (Rasmussen & Straka 1998). **For our three Plains LP cases this did not hold** (DESIGN §5); LP vs classic has to come from the simulated precipitation |
| Hodograph shape | straight: mirror-image splits; clockwise curve: right mover dominant; half-circle: right mover only; counter-clockwise: left mover | WAA veers, CAA backs. Inflections **inside the effective inflow layer** degrade both members and favour clustering |

**Keep wind-only levels.** IGRA soundings carry many levels with wind but no T or T_d. Dropping them made the MAF
0–1 km shear 2 kt instead of 12 and SRH −3 instead of 30.

## Composite parameters

```
STP_eff = (MLCAPE/1500)(ESRH/150)(EBWD/20)((2000−MLLCL)/1000)((200+MLCIN)/150)
          EBWD: 0 < 12.5 m/s, 1.5 cap > 30 m/s; LCL: 1 < 1000 m, 0 > 2000 m; CIN: 1 > −50, 0 < −200;
          0 if the effective inflow base is above the ground
STP_fix = (SBCAPE/1500)(SBLCL term)(SRH0-1/150)(BWD0-6 term)      [MetPy significant_tornado = this, no CIN]
SCP     = (MUCAPE/1000)(ESRH/50)(EBWD term: 0 < 10, cap 20 m/s, /20)   [SHARPpy: no CIN term]
SHIP    = MUCAPE · r_MU(g/kg) · LR700-500 · (−T500) · BWD0-6(m/s) / 4.2e7   (with SHARPpy's clamps)
EHI     = CAPE·SRH/160000
BRN     = CAPE / (½ΔM²), ΔM = 0-6 km mean wind − 0-0.5 km mean wind: < 10 too sheared, 10–45 supercells, > 50 multicells
```

- Shear terms are in **m/s**. SHARPpy wind routines return knots.
- **The Lab's own STP is a hybrid** (ML CAPE and CIN with fixed-layer SRH and shear) and matches neither SPC version.
  On DDC HRRR it gives 1.5, against effective STP 3.0 and fixed 0.8. Label it as the Lab's STP wherever it is shown.
- Very different environments give the same STP (MLCAPE 500 / ESRH 500 and MLCAPE 5000 / ESRH 50 both give 1.1).
  Always check the inputs.

## Checking our numbers

`../../../meteorology/SHARPpy/tools/check_sounding.py validation/soundings/<file>.json` prints the SPC set, the same
quantities computed with Lab definitions, and SHARPpy vs MetPy. Known results on DDC 2016-05-24 HRRR 22Z: Lab
Bunkers 238°/24 kt, 0–1 km SRH 83 and 0–6 km shear 32 kt all reproduce. Lab 1 km ML CAPE is 5,121 against SHARPpy's
4,874 J/kg on the same definition (+5 %, unresolved: likely the mixed-out surface layer, Bolton vs Wobus, and the
125(T − T_d) LCL).
