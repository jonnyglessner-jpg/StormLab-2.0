# Visual realism

What real storms look like, as rules for the skin (`Runtime/Rendering`), the feature designer and the designed
tornado. Physical sizes come from the other docs; this file is about appearance. Primary source: the 67-photo
reference library in `data/reference/` (its `NOTES.md` files); also Stull ch 6, 8, 14, 15 and METR 4403 §11, 14.

## The rule

**The sim decides what happens and where; the skin draws it.** The skin should never invent a feature the sim can
already see, and must never draw a feature the physics rules out (a wall cloud with no low-level meso, a hail shaft
with no updraft). When the sim's signal is missing, the designer falls back to 1.0's rule for that feature and
records which source drew what (DESIGN §7).

## Clouds

- **Cumuliform aspect ratio ~1:** a fair-weather Cu ~1 km across, a thunderstorm stem 10–15 km.
- **Bases are flat at the LCL** (z_LCL ≈ 125 m per °C of T − T_d); **tops sit at the EL**, with an overshooting dome
  above it in strong updrafts.
- **Below the LFC cloud looks smooth and laminar; above it, cauliflower turrets.** Castellanus (turrets in a
  mid-level layer) is the classic precursor sign.
- **Fractal edges:** cloud shadow perimeters have dimension D ≈ 1.35, so the surface has D ≈ 2.35. Cloud sizes are
  lognormal with a long tail of large ones. Useful targets for noise octaves.
- **Anvil:** a thin ice sheet with a flat top, blown downwind by the storm-relative anvil wind (an oval up to
  100 × 300 km in strong shear), back-sheared a little upwind, with the tower flaring into it. Calvus (fuzzy top, no
  fibrous ice yet) turns capillatus (glaciated, fibrous).
- **Mammatus:** pouches under the anvil, most likely when the air below is warm and dry. They **don't** mean severity.
- **Stratiform layers** form by advection up isentropes. Their base is not the surface LCL.
- **Kelvin–Helmholtz billows** run perpendicular to the shear on a sheared stable layer.

## Supercell structure as seen by a chaser

Classic supercell viewed from the southeast, looking northwest (the chaser's normal position, beside the path):

- Overshooting top and the anvil streaming northeast.
- **Striations**: screw-thread ridges on the rotating updraft.
- **Flanking line** of Cu stepping up toward the updraft from the southwest, along the RFD gust front.
- **Rain-free base** under the updraft, darker and sharper than the paler, diffuse precipitation area.
- **Wall cloud**: an isolated lowering under the rain-free base. **Tail cloud** pointing toward the forward-flank
  precipitation, with scud racing into it. **Beaver tail**: a smooth, flat band along the inflow/FFD boundary.
- **Inflow tails**, varying in length, 1–3 bands.
- **Clear slot and horseshoe**: the RFD's dry air carving a slot that wraps around the wall cloud.
- **Vault**: the clear gap between the meso and the FFD.
- **Hail shaft** next to the main updraft, white; rain shafts beyond it.
- Arc/shelf cloud sits *above* the gust front, not in it. Haboobs in dry regions.

**Mode changes the look.** LP: high base, tilted thin updraft, sculpted, very visible, little rain. HP: rain in
both flanks; the meso may sit ahead of the rain; a rain-wrapped vault (the "bear's cage"). Overtaken by a storm,
a chaser sees anvil cirrus, then mammatus, then the gust front and arc cloud, then rain, then hail, then any
tornado, then rapid clearing.

## Tornado appearance (from the photos)

- **Smooth, never grooved.** No photo shows vertical grooves. Texture, where it exists, is faint banding winding
  round the funnel, nearly horizontal to ~30°. The user called the old vertical grooves "very unrealistic".
- **Shaded like a soft cylinder**: brighter on the lit side, edges fading a little. Shading, not texture, makes it
  round.
- **Clean, slightly soft edges** on cones, stovepipes and ropes. Ragged, scuddy texture only where the funnel meets
  the cloud and at the edges of wedges.
- **The top flares smoothly** like a trumpet into a lowering 3–6× the funnel's width. No step.
- **Ropes and elephant trunks** are smooth, nearly uniform tubes that snake.
- **Wedges are dark, soft-edged masses**, often wider than tall, with ragged scud at their edges and horizontal
  motion streaks. Their outline is still a smooth, broad funnel flaring into the base, about twice as wide at the top
  as at the ground; the softness is at the rim, not lumps in the outline. (The user, 2026-10-21: a widened drillbit
  looked far more like a wedge than our wedge did, which was a near-cylinder heaped with cauliflower lumps from noise
  scaled 1.8× on the envelope.)
- **Multiple vortices** are separate thin, translucent white tubes, slanted and twisting round each other inside a
  dusty, wider circulation. **Satellites** are thin ropes beside the main tornado.
- **A tornado can be invisible.** The condensation funnel reaches down only to where the pressure drop brings the
  air to its LCL pressure: p_cf = p[1 − 1.225(T − T_d)/T]^3.5. Dry air and a high LCL give a funnel that never
  reaches the ground, with a debris cloud below it. Humid air and a low LCL give a fat, low wedge.

## Brightness and colour

| Lighting | Funnel looks |
|---|---|
| In direct sun, sun behind the viewer | near white, brighter than the base above |
| In direct sun, seen side-on | white on the sunlit half, grey on the other: a shaded cylinder |
| In the storm's shadow | grey, never white: lighter with the bright horizon behind the viewer, darker toward the light |
| Backlit | mid to dark grey silhouette, with a brighter rim only if the sun reaches it |
| Debris- or rain-loaded, or under a dark base | dark grey to near black |
| Low sun | peach to orange-tan on the lit side |

**Lighting lessons from the renderer:** the light grid shades everything under the base, so the funnel needs its
own sun test and horizon light or it vanishes against the base. White comes only from the sun (the user: a funnel is
white only while it is actively sunlit). Out of the sun its side is a vertical surface lit by the horizon it faces, and
the side the viewer sees faces the viewer, so its tone depends on where the viewer stands: the march takes that
horizon's brightness and visibility from the sky bands and SkyVis, by side (brighter toward the sun's bearing). A
uniform "side light" all round made the funnel pale from every angle. The RFD horseshoe lip must not hang down over the
funnel. Test cameras must follow a long-lived tornado (it travels ~50 km).

## Debris

- **Big and opaque:** 2–6 funnel widths across, 100–400 m tall, often sheathing the lower third of the funnel and
  hiding its condensation.
- **Colour follows the soil:** tan to red-brown on the Plains, dark grey-brown over wet Midwest fields, near black
  with vegetation and building debris.
- Ragged outer edge with clumps and flying debris; a smoother inner sheath hugging the funnel.

## Motion: what "accurate cloud velocities" means

The skin's volume is **advected by the sim's winds** (DESIGN §2.5): striations turn at the meso's real rotation
rate, the anvil spreads at the real storm-relative anvil wind, inflow tails stream at the real inflow speed, mammatus
sag at their own rate. Timelapse makes the motion obvious, so speeds wrong by a factor of two will show.

Reference speeds: storm motion 10–25 m/s (Dixie 40–55 mph); low-level storm-relative inflow 7–20 m/s; gust fronts
5–15 m/s; tornado tangential winds 30–140 m/s; anvil-level storm-relative wind 15–35 m/s.

## Sky and atmosphere

- Rainbows appear opposite the sun (to the east after a storm passes).
- Clear-air returns and dust show boundaries; for visuals, a dryline or outflow boundary can be marked by a change
  in haze, dust and cumulus.
- Altitude matters for colour: sunlit anvil tops stay bright after sunset at the ground.

## Night

- **What lights a storm at night:** the moon (a full moon is about 1/200,000 of the sun, slightly redder), the moonlit
  sky, starlight and airglow, town light (sodium and LED glow on the haze and on low cloud bases, often brighter than
  moonlight under cloud), and lightning, which is how storms are mostly seen at night.
- **Twilight:** the sky falls about tenfold by 6 degrees below the horizon and reaches starlight by 18. Cloud high up
  sees the sun for longer: the horizon dips by sqrt(2z/R), 3.5 degrees at 12 km, so anvils glow pink after ground sunset.
- **Seeing in the dark:** below about 0.01 lux the eye's rods take over: no colour, a blue-green bias (Purkinje shift),
  less detail. A game shows night far brighter than physical (the skin's default is 8 % of day for a moonlit night) and
  keeps it blue-grey, with the deepest shadows lifted.
- **Lightning** (`Runtime/Lightning`): in-cloud flashes light the storm from inside, the light scattering through the
  same cloud the sun lights; ground strikes are hidden where they pass through cloud or heavy rain. Real nights: most
  flashes are in-cloud (the default is 25 % ground), a positive strike's flash is longer and comes from the anvil, and
  each flash flickers through several return strokes over a few tenths of a second.

