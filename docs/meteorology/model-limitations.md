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

1. **Cold pools a little cold at the extreme.** WK coldest −9.3 K against CM1's −4 to −8 K (typical −7 to −8.5 K after
   the first hour is inside the range). DDC extreme −11.5 K (plausible in that dry sounding).
   - **Top suspect, untested: one eddy diffusivity for momentum and scalars.** CM1 mixes θ and q three times harder
     than momentum (K_h = K_m/Pr, Pr = ⅓) in both its closures. With C_s 0.25 against CM1's 0.18, the Lab has about
     1.9× CM1's momentum mixing but only 0.64× its scalar mixing. Weak scalar mixing stops downdrafts from entraining
     warm air. Fix to try: one extra multiply in DiffRelax for scalar layouts.
   - Also compare cold pools the CM1 way (C and h from buoyancy, against the initial sounding), not only the coldest
     point.
   - The CM1 WK numbers come from Morrison 2-moment runs, not LFO ice. Part of the gap may be the microphysics.
2. **Updrafts strong.** WK peak 69.9 m/s, at the top of the 50–70 m/s window (typical 55–65). DDC runs at 85–95 % of
   parcel theory. Related to (1): under-mixed scalars mean less entrainment.
3. **Too much hail aloft.** Against KDDC: 50 dBZ top 14.5 vs 9.5 km, strongest echo 75 vs 55 dBZ, vault 4.5–9.5 vs
   2.5–5 km. Storm size, echo top and low-level footprint are right. **The skin must not use model reflectivity
   directly** to pick colours or densities; take the vault as too high.
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

## Fixed

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
- **VORTEX2 Goshen County 2009:** compare the low-level meso and RFD with DOW dual-Doppler once the EOL orders arrive
  (`validation/README.md`).
