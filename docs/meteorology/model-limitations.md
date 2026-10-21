# Model limitations

What StormLab's physics does not do, does approximately, or gets measurably wrong, and what to try next. Keep this
current: when a bias is fixed, move it to "Fixed" with the date and the evidence. Numbers are from
`validation/reports/` and `src/com.stormlab.supercell/Runtime/Dynamics/README.md` (RX 7900 XT, 2026-10-18 to
2026-10-20) unless noted.

## What the brain is

Anelastic, Arakawa C grid, GPU compute. Semi-Lagrangian MacCormack advection with a limiter. 6-class LFO/Gilmore
single-moment ice. Smagorinsky mixing (C_s 0.25) over constant floors. Sine-transform pressure solve. Bulk drag
C_d = 0.003 at the lowest level (50 m). Rayleigh damping above 13 km (τ 300 s, 18 km top). Open sides with a relaxation
sponge. CIN-gated forced updraft. 2 s step at 333 m. Grid: 80 × 80 × 18 km in a frame moving with the Bunkers right
mover; average tier 240 × 240 × 72.

## By design (not bugs)

| Limit | Why | Consequence |
|---|---|---|
| **Tornado is sub-grid** | resolving it needs ≤ 30 m | genesis, intensity and shape are parameterized from the resolved low-level meso and RFD (`tornadogenesis.md`) |
| **Effective resolution ≈ 5–7Δx** | numerics filter 2Δx waves | at 333 m the model resolves features of ~2 km; the low-level meso is marginal |
| No cloud texture, striations or funnel | grid too coarse to look right | the skin draws them (DESIGN §2.5) |
| Environment fixed at setup | the sim runs ahead to fill the timeline | mid-storm environment changes need a re-run from a checkpoint (seconds) |
| One storm-following domain | cost | other storms, mergers and boundary collisions are later work (DESIGN phase 5) |
| No radiation, no surface fluxes | storm lifetimes of hours | no diurnal cycle; no evening stabilization unless the sounding carries it |
| No Coriolis | as in the CM1 WK case | over 2–4 h a real supercell's cyclonic preference grows slightly; MCS bookend asymmetry won't appear |
| Single-moment microphysics | cost | no size sorting (ZDR arcs), fixed intercepts; cold pools and reflectivity are sensitive to the intercepts |
| Bit-exact replay only on one machine | GPU floating point | another vendor or driver gives a similar, not identical, storm |

## Measured biases (open)

1. **Cold pools: now inside CM1's range at the WK case (improved 2026-10-20).** WK coldest −7.3 K against CM1's −4 to
   −8 K (was −9.3 K); DDC −8.6 K (was −10.4). The fix was the top suspect: scalars now mix three times harder than
   momentum (`SimParams.scalarMix` = 3, CM1's K_h = K_m/Pr with Pr = ⅓; one diffusivity for all had given about 0.64× CM1's
   scalar mixing). Over 18 real storms (2 h each) the median coldest cold pool went from −5.2 to −3.3 K.
   - Still to do: compare cold pools the CM1 way (C and h from buoyancy, against the initial sounding), not only the coldest
     point.
   - The CM1 WK numbers come from Morrison 2-moment runs, not LFO ice. Part of the gap may be the microphysics.
2. **Updrafts: back inside the window (improved 2026-10-20).** WK peak 66.9 m/s (window 50–70; it was 69.9, and 71.1 with
   the smaller hail of (3) alone); DDC 94.7 m/s against parcel theory's 101 (was 102.5). Same fix as (1): more entrainment.
3. **Too much hail aloft (reflectivity improved 2026-10-20).** The hail/graupel class's intercept is now 4×10⁵ m⁻⁴
   (`SimParams.n0Hail`; LFO's 4×10⁴ meant few, large stones). Over 18 real storms the strongest echo went from +7.0 to
   −1.0 dBZ of radar and the 50 dBZ tops from +0.4 to +0.1 km (with (1)'s mixing: −2.2 dBZ, −0.1 km), with cold pools,
   updrafts and lifetimes unchanged. Against KDDC the mature storm's 50 dBZ top is still 15.0 vs 9.5 km and its vault
   too high (2.0–8.5 vs 2.5–5 km). **The skin must not use model reflectivity directly** to pick colours or densities.
4. **Not mass-conserving.** Water drifts by 1.6 % of the water that cycled through the storm in 25 min (closed box),
   because semi-Lagrangian advection isn't conservative. CM1 uses flux-form 5th-order with WENO, which closes the
   budget.
5. **Storm-motion estimate noisy on short windows.** It is the 10-minute drift of the 40 dBZ centroid. After 30 min
   its median agrees with Bunkers (WK 270°/25 kt vs 276°/28 kt; DDC 223°/24 kt vs 238°/24 kt), but single 5-minute
   samples scatter. Smooth further if it drives anything visible.
6. **Low end of the drag range.** C_d = 0.003 (z₀ ≈ 0.03 m) is physical for open prairie, but tornadogenesis in
   Roberts, Xue & Dawson (2020) came earliest around C_d 2×10⁻². This matters once the tornado model reads near-surface
   vorticity from the brain.
7. **Lab diagnostics differ from SPC's.** ML CAPE uses a 1 km layer (SPC: 100 hPa), Bunkers 2000 (SHARPpy: 2014), the
   largest-area CAPE, and a hybrid STP. Lab ML CAPE on DDC is 5 % above SHARPpy's on the same definition, unresolved.
   See `atmospheric-physics.md`.
8. **Low-level rotation barely responds to the environment (real-storm library, 2026-10-20).** Over 50 real storms
   (`validation/reports/ValidationReport_cases.md`, 3 h each from the inflow soundings), the environment's own 0-1 km
   SRH separates EF2+ from tornado-free storms far better (AUC 0.89) than the brain's 1 km meso vorticity (0.70),
   near-ground vorticity (0.61) or 4 km meso (0.51). Medians: 1 km zeta 0.028 (EF2+) vs 0.021 1/s (none). The RFD
   signal has the right sign (coldest RFD θ′ −4.4 K tornadic vs −5.3 K null; AUC 0.67), as in Markowski et al. 2002.
   Likely causes: 333 m grid (the low-level meso is marginal at 5–7Δx), the low drag (6), and only 3 h. **Until fixed,
   the tornado model must weight the environment (SRH1, LCL, STP) at least as much as the brain's rotation.** Try:
   high tier on a subset of EF2+ / null pairs; C_d 0.01–0.02; a stretched grid finer near the ground.
9. **Storms deviate too little to the right.** Model motion comes from 12° further counter-clockwise than radar
   (median, p25–p75 −24 to −1°; 49 cases), 2.6 kt fast; Bunkers itself is within 4° / 2.4 kt. Worst in Plains classic
   (−20°) and Midwest HP (−24°). The storm-following frame moves with Bunkers, so this is the storm's own propagation:
   suspect weak low-level dynamic lifting (linked to 8).
10. **High-shear low-CAPE storms are too weak.** Beauregard (model MLCAPE 165), Apple Creek (123), Gainesville (463),
    Beaver (344) and Brule (621) peak at 17–29 m/s; Lewistown (757) never initiates: its inflow sounding carries MLCIN −191 J/kg, so the cap rule holds (a frontal or
    elevated storm the CIN-gated bubble cannot represent; the sounding, not the dynamics, is the first suspect). Real storms there were long-lived
    tornadic supercells driven by dynamic lift. Check the initiation thresholds (`Initiation.cs`: CAPE floor, forcing
    strength) against low-CAPE soundings before blaming the dynamics.
11. **Reflectivity hot, footprint small.** Max dBZ +4.5 dBZ (p25–p75 +2.4 to +9.3), consistent with (3). Largest 40 dBZ
    area at 1 km is ~2,000 km2 below the radar's composite (apples to oranges in part: the radar's composite includes
    merged neighbours; the model has one storm). 50 dBZ tops match (+0.1 km); 18 dBZ tops 0.9 km low.
13. **Precipitation mode (LP / classic / HP) is not represented (2026-10-20).** Over 34 real storms of known mode the
    model's largest 40 dBZ area at 1 km has no rank relation to the radar's (Spearman 0.06): LP 158, classic 212, HP
    193 km² against observed 402 / 2163 / 4927 (the radar's composite includes merged neighbours, which inflates HP most).
    Neither the hail class (intercept 4×10⁴ to 4×10⁶, density 400–900; 18 storms) nor three times the scalar mixing
    separates them (rank correlation with mode 0.04–0.30). LP storms rain only 20–35 % less than classic, HP no more
    than classic. Suspects: 333 m (a narrow LP updraft and its dry-air entrainment are under-resolved), 1-moment
    microphysics (no size sorting), and the single-storm domain (no mergers). Until then the skin's mode comes from
    the environment (`StormPhysics.hpness`: cloud base and moisture; the anvil-level-wind rule fit none of the cases).
    Try: the high tier on LP / HP pairs; a 2-moment rain and hail scheme.
12. **Minisupercells too tall.** 50 dBZ top 11.4 vs 8.5 km observed; Plains LP storms move too fast (14 vs 9.5 kt) and
    weaken early (strong until 82 min median, against 120-180 for classic/HP).

## Budget and performance

- Average-tier vertical spacing is 323 m aloft, not 250 m (250 m needs 85 levels: +22 % step time, +18 % VRAM, and it
  pushes the WK updraft to 70.1 m/s). Tier stays at 72 levels.
- VRAM: 601 MB average tier (estimate was ~250 MB), 1.33 GB high tier. The scratch copy of every field is most of it.
- Timeline frames ~180 KB, not ~50 KB (the 1 km qc/qr/qi/qh window). A 3 h storm is ~200 MB of frames plus 1.0 GB
  (average) or 2.2 GB (high) of checkpoints.
- A checkpoint frame (~84 MB quantise and read-back every 15 sim-min) is not budgeted.
- **Only measured on the RX 7900 XT.** The average-tier budget assumes an RTX 3060; it hasn't been profiled on one.
  Only D3D12 has been run.

## Renderer

- Distant cauliflower detail is grainy (fine noise octaves alias at half resolution).
- March cost depends on the view: inside the storm costs more than 24 km away.
- Not built yet: the 30 m near-field volume, flow-mapped detail.
- Close-range wedges read as pale haze rather than dark masses.
- **Lightning is only loosely tied to the storm (2026-10-21).** With `LightningStormDriver`, flash rate and flash types
  follow the storm's age and strength (Lightning README, "Storm-driven activity"). The designed storm's curve is a
  hand-shaped parametric rule set to plausible magnitudes (Plains supercells ~60/min, ~110 in a jump, pulse storms
  ~18), not calibrated against measured flash rates. The brain's rate follows McCaul et al. (2009) but from 1 km block means, column-maximum w scaled by the core's profile, and no snow. Its absolute rate has
  not been compared with real storms; the 0.2 ice coefficient is unverified. The polarity rules are heuristics with no
  charge structure. Charge heights are still settings, though cloud-connected flashes take their origins from the cloud
  field. A flash doesn't light the ground or anything beyond 6 km of it; the far anvil outside the 60 km volume hides
  channels but isn't lit. Light-field and per-fragment channel costs unprofiled, as is the anchor readback at
  storm-driven rates near the 120/min ceiling.

## Fixed

- **2026-10-20 · Reflectivity 7 dBZ hot; cold pools cold and updrafts strong at the WK case.** Hail intercept 4×10⁵
  (was LFO's 4×10⁴) and scalar mixing three times momentum's (as CM1). See (1)-(3) for the numbers; the sweep results
  are `validation/real-storm-cases/brain_cases_results_sweep_*.csv`.

- **2026-10-18 · Melting-hail cold pools (−5 to −6 K spurious).** 1.0 cooled the air for vapour that really condenses
  on melting hail. The model now exchanges that vapour. WK coldest went from −11 to −9.3 K.
- **2026-10-18 · Real-sounding blow-ups.** The 2 m skin temperature made the lowest layers superadiabatic (DDC HRRR
  +3.6 K in 170 m). The loader mixes out superadiabatic layers below 1.5 km.
- **2026-10-18 · Rain evaporation intercept.** 1e6 m⁻⁴ instead of 8e6, against 1.0's cold-pool bias (−11 → −10 K before
  the melting fix).

## Benchmarks not yet run

- **CM1 testcase 17 (Fiedler chamber):** swirl-ratio sweep for the sub-grid tornado's one-cell / two-cell /
  multi-vortex regimes.
- **CM1 testcase 18 (BR14):** 2D cold pool in shear, a clean benchmark for the outflow code.
- **CM1 LFO-ice (ptype 4) WK run:** like-for-like cold-pool comparison. Needs the CM1 source (license click-through,
  not downloaded) and WSL or Linux.
- **Real-storm library at the high tier:** the cases suite at 250 m on EF2+ / null pairs, to see whether bias 8 is
  resolution.
- **VORTEX2 Goshen County 2009:** compare the low-level meso and RFD with DOW dual-Doppler once the EOL orders arrive
  (`validation/README.md`).
