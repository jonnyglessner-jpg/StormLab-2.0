# Tornadogenesis

The physics behind the sub-grid tornado model (DESIGN §2.4, phase 3) and the designed tornado
(`Runtime/Design/StormGenome.cs`). Sources: METR 4403 §21–27, 35–36 (Halbert, Thompson, Xue); Stull ch 15; CM1
testcase 17. See `references.md`.

## Where the rotation comes from, by height

| Level | Source | Notes |
|---|---|---|
| Mid-level meso (3–7 km) | tilting of environmental streamwise vorticity, then storm-relative advection into the updraft | `storm-dynamics.md` |
| Low-level meso (~0.5–1 km) | environmental low-level SRH, plus vorticity generated inside the storm | stronger low-level shear gives a stronger, lower meso and more dynamic lift |
| Near-surface ζ (lowest ~100 m) | **disputed**, see below | this is the step a 333 m grid cannot see |

## Near-surface vorticity: the mechanisms

1. **Baroclinic, along the forward-flank gust front** (Rotunno & Klemp 1985, Klemp 1987). The 30-year paradigm: the
   FFD's buoyancy gradient makes horizontal vorticity that descending air carries to the ground. Variants: tilting
   during descent with "slippage" (Davies-Jones & Brooks 1993); purely horizontal near-surface vorticity tilted and
   stretched (Rotunno et al. 2017).
2. **Downdraft vortex-line pairs** (Trapp & Weisman 2003; Dahl 2015 in supercells): a downdraft pushes vortex lines
   into a counter-rotating pair and the updraft stretches the cyclonic one.
3. **Surface friction (Xue's group).** In a 50 m ARPS simulation of the 8 May 2003 OKC F4, **surface drag made most
   of the horizontal vorticity reaching the tornado, and baroclinity was nearly zero** (Schenkman, Xue & Hu 2014).
   The "river-bend" model (Roberts et al. 2016): drag slows the inflow at the surface, making crosswise vorticity;
   as the flow curves left into the convergence it turns streamwise; the updraft tilts it up and stretches it.
4. **Surface-layer turbulence** (Markowski 2024): with turbulent inflow, the first vortex grows from ζ-streaks in
   the lowest ~50 m. About 70 % of the circulation comes from the background flow and 30 % from turbulence and
   friction, and baroclinity is negligible. **What matters most is how close the updraft is to the ground** (∂w/∂z).

**What this means for the model.** Friction is first order: **no tornado formed with C_d = 0** in Roberts, Xue &
Dawson (2020). Tornadoes formed *earlier* as C_d rose from 2×10⁻³ to 2×10⁻², and later and weaker at 5×10⁻². The
brain's bulk drag C_d = 0.003 at 50 m (z₀ ≈ 0.03 m, open prairie) sits at the low end of that range, so the
genesis test should not depend on baroclinic vorticity alone. Simulations without drag overstate baroclinity.

## Downdraft temperature: the Goldilocks rule

- **Markowski & Richardson (2014):** the best case is **strong dynamic lift with weak to intermediate negative
  buoyancy.** Outflow that is too cold builds circulation the mesocyclone can't lift. Outflow that is too warm
  doesn't build enough.
- Markowski (2002) observations: tornadic RFDs are warmer than non-tornadic ones. A **cold RFD kills genesis**.
- Xue: because they are dynamically forced, **downdrafts in tornadic supercells are often warm, not cold.**
- STP uses the **LCL as a proxy for cold-pool buoyancy**: a low LCL means a humid boundary layer, so less evaporative
  cooling.
- Consequence for us: the brain's cold-pool bias (a little too cold at the extreme) pushes genesis the wrong way.
  The genesis test should read the RFD θ′ deficit, not just the low-level vorticity.

## Genesis, intensification, decay

- **Genesis:** the ~1 km low-level meso's **spin term** (dynamic lift) drives the first intensification. The
  **splat term** (near-surface convergence/deformation) keeps the upward pressure gradient and stretching going
  (Huang & Xue 2025b). After formation, the spin term gives a *downward* PGF and the central downdraft.
- **Maintenance:** continued inflow, a not-too-cold RFD, the low-level meso staying over the vortex.
- **Decay** (Huang & Xue 2026): the mid-level updraft and meso separate from the ~1 km LLM and the tornado. The LLM
  weakens, the downward dynamic PGF grows, near-surface stretching is lost, and the occlusion downdraft joins the
  central downdraft. Then the tornado dies. Visually: shrinking, then rope-out.
- **Cycling:** the next tornado forms under the same base to the east and southeast as the old one occludes.
  SPC data: a third of tornado families produce 2+, 15 % produce 3+, with a **15-minute median gap** between
  successive tornadoes (`DATA.md`). The brief caps cycles at three.

## Environment discriminators (what the genesis probability should lean on)

| Ingredient | Discriminates | Notes |
|---|---|---|
| 0–500 m SRH | best (Coffer et al. 2019) | better than 0–1 km or effective SRH |
| 0–1 km SRH, effective SRH | strong | eSRH > 200 m²/s² marks the likeliest areas; recompute with the actual storm motion |
| MLLCL | strong | < 1000 m favourable, > 2000 m nearly prohibitive (STP term) |
| Effective inflow base at the ground | required for STP | elevated storms rarely produce significant tornadoes |
| LFC height | moderate | tornadoes likelier with z_LFC < 2 km |
| nCAPE (low-level buoyancy) | moderate | fat low CAPE helps stretching |
| MLCIN | limits | some CIN is fine (nocturnal tornadoes) |
| STP | composite | driven mostly by the overlap of MLCAPE and 0–1 km SRH; same STP can mean very different environments |

**Regional and seasonal context.** Plains supercells have more CAPE and higher LCLs; the Southeast lower LCLs, higher
shear and more QLCS tornadoes. Parameter magnitudes are relative to place and season. This is why the presets are
families of real soundings, not one profile.

## Intensity, size and structure

- **Winds:** 18 to more than 140 m/s tangential, **strongest 15–150 m AGL** (DOW climatology: winds may be strongest
  right near the ground; Kosiba & Wurman 2023). Updrafts 25–60 m/s in the outer wall.
- **Rankine vortex:** solid-body inside the core radius R₀, M ∝ 1/R outside. Cyclostrophic balance ΔP_max ≈ ρM_max².
  Core pressure deficit up to ~100 hPa (Tuscaloosa simulation: over 100 hPa, winds over 430 km/h).
- **Friction** slows the near-surface wind below cyclostrophic balance, so air is sucked in at the bottom (the corner
  flow).
- **Peak ground wind** is on the right of the track (NH): M_tan,max + translation.
- **Fewer than 2 % are anticyclonic.**
- **Swirl ratio** S ≈ M_tan/M_rad (lab vortices; CM1 testcase 17 uses S_r = ΩL_r/W):

| S | Structure |
|---|---|
| 0.1–0.3 | one-cell, smooth funnel, updraft through the core; core radius 5–25 % of the updraft radius |
| rising | vortex breakdown bubble descends from the top; the vortex widens above it |
| ≈ 0.45 | breakdown reaches the ground: two-cell vortex, central downdraft, turbulent helical annulus |
| ≳ 0.8 | 2 suction vortices, rising to ~6 as S → 3+. **They carry the highest winds** and leave cycloidal marks |

- **Why multiple vortices form:** a hollow vorticity ring meets the condition for barotropic instability and grows
  **vortex Rossby waves**. The most unstable wavenumber is set by the ring's hollowness and thickness (Schubert et
  al. 1999). Thinner, more hollow rings give higher wavenumbers. In the Funing EF4 simulation (hollowness 0.47,
  thickness 0.73) wavenumber 5 dominated, as predicted.
- **Trailing horizontal vortices:** friction in strong RFD outflow makes crosswise vorticity that organizes into
  visible horizontal tubes ESE of strong tornadoes. They do their own ground damage. One of the 1965 Elkhart "twin
  funnels" was most likely one of these.

## Ratings: EF scale and what the data means

| EF | 3 s gust (mph) | m/s (Stull fit M ≈ 29.1 + 8.75·EF^1.2) | US share |
|---|---|---|---|
| 0 | 65–85 | 29–38 | 29 % |
| 1 | 86–110 | 38–49 | 40 % |
| 2 | 111–135 | 49–62 | 24 % |
| 3 | 136–165 | 62–75 | 6 % |
| 4 | 166–200 | 75–89 | 2 % |
| 5 | > 200 | ≥ 89 | < 1 % |

- EF2+ is "significant".
- **EF ratings are a lower bound on true intensity, especially for strong tornadoes.** Since 2014, EF3s are down 31 %
  and EF4s 52 % against 1977–2006, from survey practice rather than weaker tornadoes (Lyza 2025). Only one EF5 since
  2014 (Enderlin ND, 20 Jun 2025). The SPC climatology the designed storm is calibrated to
  (`data/derived/tornado_climatology.json`) carries that bias at the high end.
- Damage: the game decides how a Degree of Damage looks; the model decides the DoD from the wind each object saw
  (28 EF damage indicators).
- **Official ratings come from SPC/NWS data, not recall** (house rule for the chase site and for any case we
  validate against).

## Other vortex types (outside the supercell model)

Landspouts (EF0–2, boundary shear stretched by a growing updraft), waterspouts, gustnadoes (on the gust front, ≤ EF0,
no updraft stretching), cold-air funnels (rarely reach the ground), QLCS mesovortex tornadoes (strongest winds on the
south side, where the rotation adds to the rear-inflow jet), tropical-cyclone tornadoes (mini-supercells, mostly
EF0–EF3, in the right-front quadrant).
