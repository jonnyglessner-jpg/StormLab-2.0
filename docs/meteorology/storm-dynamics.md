# Storm dynamics

How a supercell works, written for the brain (`Runtime/Dynamics`) and the parts that read it: the timeline, the
feature designer and the designed storm (`Runtime/Design`). Sources: METR 4403 §14–15, 18–19, 28–29; Stull ch 10,
14, 15; CM1 notes. See `references.md`.

## Governing equations

What the brain solves, in CM1 form (the Lab matches unless noted):

```
Momentum (anelastic):  ∂u/∂t + ∇φ = F,   ∇·(ρ₀u) = 0,   ∇·(ρ₀∇φ) = ∇·(ρ₀F),   φ = p′/ρ₀
Vertical:              dw/dt = −∂φ/∂z + B,   B = g[θ′/θ₀ + 0.608 q_v′ − Σq_hydro]
Vorticity (Boussinesq): ∂ζ/∂t = −V·∇ζ + ω_h·∇_h w + ζ ∂w/∂z   (+ friction, baroclinic, Coriolis terms dropped)
                                  advection   tilting         stretching
```

The anelastic set (CM1 psolver 4) has no sound waves and allows big time steps. CM1's anelastic density current
runs **slightly slower** than the compressible one, and Bryan couldn't explain why. Keep that in mind when comparing
gust-front speeds.

## Where the mesocyclone comes from

Linearize about an environment with shear S = ∂V̄/∂z, in the storm-relative frame:

```
(∂ζ′/∂t)_SR = −(V̄ − c)·∇_h ζ′  +  (S × ∇_h w′)·k̂
                SR advection        tilting
```

- **Tilting** makes a vortex couplet straddling the updraft: cyclonic to the right of the shear vector, anticyclonic
  to the left. The net rotation over the updraft is zero.
- **Crosswise vorticity** (storm-relative wind parallel to the shear): the couplet is only shifted downshear, so the
  updraft centre doesn't rotate.
- **Streamwise vorticity** (storm-relative wind parallel to ω_h): advection moves the cyclonic member into the updraft,
  **so the updraft rotates: a mesocyclone.** This needs storm-relative winds of more than about 10 m/s.
- SRH = ∫|V − c| ω_streamwise dz measures exactly this.

## Pressure perturbations

```
p′ ∝ [(∂u′/∂x)² + (∂v′/∂y)² + (∂w′/∂z)²] − ½ζ′²  +  2S·∇_h w′  −  ∂B/∂z
      splat (deformation)                 spin       linear (shear)  buoyancy
```

- **Spin term −½ζ′²:** low pressure whatever the sign of rotation. With a straight hodograph the mid-level vortices
  sit on the updraft's flanks, so lows aloft pull up new updrafts on both flanks. Then stretching strengthens the
  vortices and the lows: **splitting** into a right mover (RM) and left mover (LM). This is nonlinear.
- **Linear term 2S·∇w′:** high pressure upshear of the updraft and low downshear at each level. With a clockwise
  hodograph the couplet turns with height, so on the right flank low lies over high: **upward VPPGF and dynamic
  lift on the right, suppression on the left. RM enhanced, no split.** Rule: updrafts are favoured on the
  **concave side** of the hodograph.
- **Deviant motion:** in curved hodographs it comes from the linear term, in straight ones from splitting. Larger,
  stronger supercells deviate more.
- **Dynamic lift through CIN:** the lows aloft lift inflow to the LFC even through a cap. That is why supercells
  outlive ordinary cells after dark.

The brain can split p′ the CM1 way (`output_pdcomp`) if a diagnosis of the updraft drivers is ever needed. The
parcel diagnostics (buoyancy vs VPPGF acceleration along trajectories) are the cleanest test of what drives the
updraft.

## Storm anatomy (what the feature designer draws)

| Feature | Physics | Typical size / numbers |
|---|---|---|
| Updraft tower | buoyant core; overshooting dome above the EL means a strong updraft | stem 10–15 km wide; overshoot up to 5 km above the tropopause |
| Mesocyclone | rotating updraft, 3–10 km across | mid-level ζ 0.01–0.04 s⁻¹ (WK spec window) |
| BWER / vault | updraft so fast droplets can't grow to radar size | NEXRAD BWER 3–10 km deep |
| FFD | precipitation carried forward (NE) aloft and falling out: hail nearest the updraft, then rain, then the anvil | |
| RFD | downdraft on the rear flank, forced partly dynamically, chilled by evaporation; its gust front feeds the flanking line | |
| Hook echo | rotation wraps precipitation around the meso | |
| Meso-low | at the cusp of the forward- and rear-flank gust fronts, like a small frontal cyclone | where tornadoes favour |
| Wall cloud | lowering under the rain-free base, from humid rain-cooled air drawn up | |
| Tail cloud, beaver tail | tail points toward the FFD with scud racing in; beaver tail is a smooth flat band along the inflow/FFD boundary (most common in HP) | |
| Flanking line | Cu building toward the updraft along the RFD gust front | |
| Anvil | blown downwind by the storm-relative anvil wind | up to 100 km wide and 300 km long in strong shear |
| Mammatus | static instability under the anvil (IR cooling, evaporation) | **don't correlate with severity** |
| Striations | screw-thread ridges on the rotating updraft | |

**LP / classic / HP.** LP: high bases, strongly tilted updraft, little rain, big hail, great visibility. HP: rain in
both FFD and RFD; the meso can sit ahead of the rain; a fully rain-wrapped vault is the "bear's cage". The textbook
discriminator is anvil-level storm-relative wind (< 18 m/s HP, 18–28 classic, > 28 LP). It did not fit our three
real Plains LP cases, so in StormLab mode **emerges from the simulated precipitation field** (DESIGN §5).

## Life cycle and mode

- **Ordinary cell:** lifetime ≈ H/w₀ + H/v_t, 30–60 min. Without shear a storm lasts 15–60 min and chokes on its own
  outflow.
- **Multicell:** new cells form on the downshear (fuel) side of the cold pool. System motion = cell motion +
  propagation.
- **Supercell:** quasi-steady for hours in strong deep shear. **High CAPE is not required.**
- **Mode in the first hours after initiation** (Lyons):
  1. **Strength of forcing first.** Weak lift (dryline, weak outflow) gives few, widely spaced, discrete cells.
     Strong lift (a surging cold front) gives many cells and upscale growth even when shear and motion favour
     discrete storms.
  2. Then boundary-relative storm motion, boundary-relative deep shear, and anvil-level winds (whether they seed
     neighbours).
  3. The cap modulates: a weak cap limits the first storms (discrete); no cap lets many form.
- **Warm-sector residence time:** a storm moving into cooler air or quickly out of a narrow warm sector won't
  mature. That is the physical reason for storm longevity varying with the day (brief item).
- **Mergers:** 120+ studied supercell mergers show **no consistent constructive or destructive trend**. Strong cells
  tended to weaken and weak ones to strengthen (Flournoy et al. 2020). Interactions are ambiguous, which justifies
  leaving them to the sim (DESIGN §6 phase 5).

## Cold pools and outflow

- **Evaporation dominates loading.** 10 g/kg of rain weighs θ_v down by about 2.9 K; evaporating it all cools by
  about 25 K. Dry high-Plains air gives strong downbursts even from virga.
- **Downdraft:** w_down ≈ √(2|DCAPE|)/2. DCAPE is much less reliable than CAPE: the downdraft's starting level, rain
  content and thermodynamic path are unknown.
- **Gust front:** 100–1000 m deep, 5–15 m/s, a 1–3 K drop at a typical station (much more inside a supercell RFD).
  Colder outflow is faster and shallower. Speed scales as (g·Δθ_v/θ·depth)^½.
- **RKW balance:** the cold pool's strength C slightly greater than the low-level shear Δu is optimal for a line; C ≫ Δu
  undercuts it. CM1 measures cold pools as C² = −2∫₀ʰ B dz with depth h, against the initial sounding. **Compare our
  cold pools that way as well as by the coldest point**, which is the noisiest statistic.
- **Our numbers:** WK coldest −9.3 K (CM1 −4 to −8 K), typical −7 to −8.5 K after the first hour (inside CM1's
  range). DDC extreme −11.5 K, which is plausible in that very dry sounding. The top suspect for the remaining
  extreme is one eddy K for momentum and scalars (see `model-limitations.md`).

## MCS and derecho (later modes, DESIGN phase 5)

- **Corfidi motion:** propagation = −V_LLJ. Back-building MCS: V_CL − V_LLJ (training, floods). Forward-propagating
  (cold-pool-driven): 2V_CL − V_LLJ. **Moving faster than the mean wind marks a cold-pool-driven system.**
- **Bow echo:** front-to-rear flow carries hydrometeors back; melting and evaporation build the cold pool; the
  buoyancy gradient drives the rear-inflow jet. Bookend vortices focus the RIJ at the apex. **After ~3 h Coriolis
  makes the northern (cyclonic) bookend dominant.**
- **Derecho (2025 definition):** one cold-pool MCS moving faster than the mean wind, swath ≥ 400 km, ≥ 5 gusts ≥ 33 m/s
  (3 measured), gaps ≤ 1 h and ≤ 200 km. About 3 per year.

## Scale facts for the numerics

- Rossby number of a tornado ~10⁴ (Coriolis irrelevant), a supercell ~1 (Coriolis matters over hours: bookends, the
  cyclonic preference), a synoptic cyclone ~0.1.
- Effective resolution is about **5–7 Δx**. A 333 m grid resolves features of about 2–2.5 km. The 1 km low-level meso
  is barely resolved; the tornado (10–1000 m) is not at all. That is why the tornado is a sub-grid model.
