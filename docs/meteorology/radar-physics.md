# Radar physics

How to compare the model with NEXRAD (`tools/fetch_nexrad.py`, `tools/radar_lifecycle.py`,
`validation/real-storm-cases/radar/`), and what any in-game radar display would need to imitate. Sources: Stull ch
8; METR 4403 §25, 30; MetPy notes (Level 2 readers). See `references.md`.

## Basics

| Quantity | Formula / value |
|---|---|
| Range | R = ct/2 |
| Max unambiguous range | R_max = c/(2·PRF) |
| Max unambiguous velocity (Nyquist) | V_max = λ·PRF/4 |
| Doppler dilemma | R_max·V_max = cλ/8: higher PRF gives more velocity range and less distance range |
| Beam width | Δβ ≈ 71.6°·λ/d; the WSR-88D is under 1°, so the beam is ~1.7 km wide at 100 km |
| Beam height | standard refraction uses an effective Earth radius of 4/3·R₀ |
| WSR-88D | S band, 10 cm, 9 m dish, ~460 km reflectivity range, 159 radars, volume scans every 4–10 min, 0.5–19.5° |

**What a radar can't see:** anything below the lowest beam. Steep lapse rates cause sub-refraction (the beam samples
higher than assumed); inversions cause super-refraction (ground clutter). Distant storms are sampled high up and
smoothed.

## Reflectivity

- Z = ΣD⁶ per unit volume (mm⁶ m⁻³); dBZ = 10 log₁₀ Z. Rayleigh scattering needs D < λ/3, so **large hail is
  non-Rayleigh** and Z–R fails there.
- |K|² is 0.93 for liquid and 0.208 for ice, so dry ice looks ~7 dB weaker than the same mass of water.
- Typical: light rain or dry snow 25–30 dBZ, heavy rain 40–50, **hail 60–75**.
- WSR-88D Z–R: Z = 300·R^1.4.
- **Bright band:** melting snow at the 0 °C level reflects 4–9× the rain below.
- Hail algorithms flag ≥ 40 dBZ above the freezing level and ≥ 50 dBZ at T ≤ −20 °C.

### Computing model reflectivity

The model's 1-moment fields give Z by integrating D⁶ over each class's assumed exponential distribution, with the
dielectric factor for ice classes and wet-hail handling near 0 °C. The fields are 1 km means in the storm-centred
window of the timeline (`TIMELINE.md`), so **compare footprints and echo tops, not single-gate maxima**.

`fetch_nexrad.py` defines the geometry rules (echo tops at 18/30/40/50 dBZ, core areas by height, the vault search,
the couplet), and `DynamicsValidation` measures the model storm with the same rules. Results for DDC 22:48Z:

| | KDDC | Model | Verdict |
|---|---|---|---|
| 18 dBZ top | 15.5 km | 16.0 km | match |
| 40 dBZ area at 1 km | 226–467 km² | 449 km² | match |
| 50 dBZ area at 1 km | 27–142 km² | 222 km² | high |
| 50 dBZ top | 9.5 km | 14.5 km | **known bias** |
| Vault | 2.5–5 km | 4.5–9.5 km | **known bias** |
| Strongest echo | 55 dBZ | 75 dBZ | too much hail aloft |

## Doppler velocity

- Radial velocity only, **toward the radar negative (green), away positive (red)**. Velocities beyond V_max fold.
- **Couplet / rotation:** **Vrot = (max inbound + max outbound)/2**, using gates co-located with the hook and 1–2 nm
  apart. Above ~40 kt is notable; it correlates with EF rating (Thompson et al. 2017).
- TVS: adjacent strong in/out gates with the zero isodop running radially through the vortex. A mesocyclone is the
  broader couplet.
- Downburst / gust front: the zero isodop crosses the radial, with flow toward the radar nearer to it.
- **VAD:** radial velocity against azimuth is a sine wave whose amplitude and phase give the wind; repeated by height
  it gives the VWP hodograph.
- **Range and height matter:** a distant circulation sampled high up gets lower tornado probabilities. Even the
  Plains have radar gaps.

## Dual polarization

| Variable | Meaning | Rain | Hail | Debris |
|---|---|---|---|---|
| ZDR (dB) | horizontal/vertical reflectivity ratio: shape | +, large drops high (to +6) | ≈ 0 (tumbling) | low |
| KDP (°/km) | differential phase rate: liquid water content | + | 0 | — |
| CC / ρHV | correlation: how uniform the targets are | ≥ 0.97 | 0.90–0.95 (big hail down to 0.5) | **< 0.8** |

- **Tornado debris signature (TDS):** CC < 0.8, Z > 20 dBZ and low ZDR, co-located with the couplet. "Don't bite on
  bugs in the inflow": insects also show low CC.
- Non-uniform beam filling lowers CC down-radial; differential attenuation biases ZDR low behind heavy cores.
- Hydrometeor classification combines all of these (fuzzy logic).

## Mobile radars (VORTEX2)

The research radars in `../../../meteorology/VORTEX2/` (once the EOL orders arrive) sample at hundreds of metres or
less, close to the storm. That is the only data that resolves the low-level meso and the tornado:

- **DOW-6, DOW-7** (X band, CSWR): dual-Doppler of the Goshen County, WY tornado, 5 Jun 2009 (official SPC rating
  EF2, 22:02–22:31 UTC).
- Also archived but not ordered: UMass X-Pol and W band, TTU Ka, NOXP, SMART-R, MWR-05XP.
- DOW tornado climatology (Kosiba & Wurman 2023): winds may be strongest very near the ground. Radius of maximum
  winds is typically 50–500 m.

Short wavelengths attenuate in heavy rain and hail. Mobile radar reflectivity is not comparable to NEXRAD's without
attenuation correction.

## Satellite (for the anvil and overshooting top)

- Visible (0.64 µm, 0.5 km), clean IR (10.3 µm, cloud-top temperature day and night), water vapour (6.2/6.9/7.3 µm,
  mid/upper troposphere, not the boundary layer).
- **Overshooting tops** and **above-anvil cirrus plumes** mean strong updrafts; anvil expansion rates come from IR
  loops. `tools/fetch_goes.py` measures these for the reference cases (`validation/real-storm-cases/satellite/`).
- **Parallax:** tall clouds appear displaced away from the sub-satellite point. Correct for it before overlaying on
  radar.
