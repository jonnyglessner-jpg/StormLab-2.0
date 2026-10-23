# Handoff: Supercell Lab replaces the game's weather

**If you are an AI coding agent (or a developer) working in a game that has this package installed, read this file
before you touch anything to do with weather, sky, clouds, storms, tornadoes, lightning, thunder or rain.**

This package (`com.stormlab.supercell`, Supercell Lab 2.0) is the game's weather system: the storms, their clouds,
tornadoes, lightning, thunder and rain. It is not an add-on to the game's existing weather. The goal of the
integration is to **remove the game's own weather system completely** and drive everything weather-related from
this package. Two weather systems running at once is the main thing that has gone wrong so far. A test build drew
the storm with the game's own background clouds showing through it, and used the game's own older lightning
instead of this package's.

The package is HDRP-only (Unity 6, HDRP 17). It was built and tested on Unity 6000.6.6f1 with HDRP 17.6.0, D3D12.

## 1. What this package provides

| Weather part | Component / API | Notes |
| --- | --- | --- |
| A whole game: storms on a 30 x 30 mile map, one hour | `DesignedScenarioRunner` | Two supercells per game, lanes about 26 km apart, each with at least one tornado. One seed lays out the game. |
| One storm | `DesignedStormRunner` | Spawn from an environment, a spawn point and a seed. |
| The storm's look (clouds, rain shafts, base, wall cloud, tornado, debris) | `StormSkin` | One per storm (the scenario runner adds them). Drawn by an HDRP custom pass the skin installs itself. |
| Lightning and thunder | `LightningSystem` (+ `LightningCloudBridge`, `LightningStormDriver`) | Hooked up automatically to every designed storm when it spawns in Play, unless `enableLightning` is off. |
| Near-camera rain | `LocalRainfall` | On the player camera. |
| Tornado wind for physics | `DesignedScenarioRunner.TornadoWindWorld(worldPos)` | Ground-relative wind, m/s, world x/z. Sums every storm's tornado. |
| Where things are | `Tornado(i)`, `TornadoGroundWorld(i)`, `UpdraftWorld(i)`, `Alive(i)`, `StormTime(i)`, `GameTime`, `Runners[i].Stage` | For the HUD, minimap, warnings, missions and damage. `Tornado(i)` gives `on`, `ef`, `shape`, radii, `vmax` and more. |

Units and frames: 1 Unity unit = 1 m, +x east, +z north, +y up. The storm environment's frame is km, x east and y
north, with the map's south-west corner at (0, 0). `worldOriginKm` on the runner is the environment point that sits
at Unity world (0, 0). The tornado and storm queries return Unity world positions.

## 2. Remove the game's own weather

Work through this in order. **Show the user the list from step 1 and get a yes before deleting anything.** Disable
first, test, then delete, and keep the deletion in its own commit so it can be reverted.

1. **Inventory.** Search the game's project (scripts, prefabs, scenes, volume profiles, materials, shaders, audio) for
   everything that makes weather. Typical names: weather, storm, cloud, supercell, tornado, funnel, twister, vortex,
   lightning, thunder, bolt, flash, rain, precipitation, hail, wind, sky, fog, overcast, cumulus, anvil. Typical
   pieces:
   - cloud meshes, billboards, particle systems or cloud shaders, including any wrapper that tried to draw this
     package's storm itself (a custom pass, a renderer feature or a full-screen effect);
   - tornado or funnel meshes, particles and debris effects;
   - a lightning system (line renderers, flash lights, sky flashes) and thunder audio;
   - rain particles or rain screen effects;
   - HDRP **Volumetric Clouds** and **Cloud Layer** overrides in volume profiles;
   - scripts that darken the sky, change fog or set the sun for "storm mode";
   - scripts that move a storm or tornado object, or compute tornado wind or damage.
2. **Keep** what is not weather: terrain, roads, towns, vehicles, the player, the day/night clock and the sun
   `Light` it turns, and the HUD. Their **weather inputs** must come from this package from now on (section 4).
3. **Remove or disable** every weather piece found in step 1. Then search again for references to the removed
   classes, prefabs and assets, and fix or remove them. When you are done, no script in the game should create
   clouds, tornadoes, lightning, thunder or rain except this package.
4. In every HDRP volume profile the game uses: **turn Volumetric Clouds off** and **turn Cloud Layer off**. HDRP's
   clouds are fair-weather clouds, not a supercell sky. The storm hides them where it is opaque, but they show
   wherever it is thin.

## 3. Set the package up

1. **Install.** Add the package from the private repository (the owner has to give access):
   `https://github.com/jonnyglessner-jpg/StormLab-2.0.git?path=src/com.stormlab.supercell#<commit>`. Pin a commit and
   do not edit the package's files. Report problems to the package owner instead.
2. **HDRP settings.**
   - HDRP asset: custom passes on (the default). MSAA off: the storm reads the camera's depth buffer as a single
     sample per pixel.
   - Cameras: the Custom Pass frame setting on (the default). The storm's pass draws in the Game view and the Scene
     view.
   - Exposure: automatic exposure works. The storm's light is in physical units and follows the camera's exposure.
   - Sky: Physically Based Sky is recommended. The storm draws over whatever sky the game uses.
   - Fog: keep HDRP fog light (a mean free path of tens of km). HDRP fog is applied before the storm is drawn, so it
     fogs the terrain but not the storm, which has its own haze.
3. **Scene.** Add one GameObject with `DesignedScenarioRunner`:
   - `sun` = the game's directional light (the sun). The storm is lit from its direction and takes its brightness
     from its intensity in lux. If the game's sun is not physically based (not lux), set `luminanceScale` on each
     storm's skin by eye instead.
   - `worldOriginKm` = where Unity world (0, 0) is on the 30 x 30 mile map (48.3 km square).
   - `preset`, `region`, `seed`, or call `Spawn(environmentSource, seed, region)` from game code.
   - `enableLightning` on.
   - Leave each skin's `toneMap`, `drawBackdrop` and `nightAdaptation` off. Those are for the package's own preview
     scenes; in a game they paint a fake sky and ground over the game's world.
4. **The storm's custom pass.** `StormSkin` adds a hidden global `CustomPassVolume` with `StormSkinCustomPass` at
   `BeforeTransparent` when it is enabled, unless the scene already has one. Do not move it earlier. HDRP's sky, fog
   and volumetric clouds all come before `BeforeTransparent`, and they treat any pixel without depth as open sky. An
   earlier injection point gets them painted over the storm. That is the "background clouds show through the storm"
   bug. The pass writes the storm's depth where it is opaque, so the game's transparents (particles, glass, water)
   behind the storm are hidden and those in front of it still show.
5. **Lightning and thunder.** These connect themselves when each storm spawns in Play. The player camera should be
   `Camera.main` (lightning uses it as the observer) and carry the scene's one `AudioListener`.
6. **Rain.** Put `LocalRainfall` on a GameObject near the player and set `targetCamera` to the player camera. There
   is no designed-storm rain source yet. Until there is, set its rain rate from the player's distance to the nearest
   storm's core (`UpdraftWorld(i)`) in game code, and say so to the package owner.

## 4. Point the game's weather inputs at the package

Anything in the game that reacted to the old weather now reads the package:

- **Warnings, HUD, minimap:** `Tornado(i).on` and `.ef`, `TornadoGroundWorld(i)`, and the storm's motion
  (`Runners[i].MotionWorld`). These drive polygons, warning text, the tornado icon and distance readouts.
- **Damage and physics:** `TornadoWindWorld(playerOrObjectPosition)` for the wind on vehicles, debris and the player.
  The tornado's peak wind is `Tornado(i).vmax` and its rating is `ef`.
- **Missions and scoring** (photos of a tornado, intercepts): the same queries, plus `Runners[i].Stage`.
- **Clock:** `DesignedScenarioRunner.GameTime` is the storms' clock (seconds into the game). The game's time of day
  stays the game's own. Keep the sun `Light` turning with it, and the storm follows that light.

## 5. Check the integration

All of these should hold in the game's own scene. Show the user screenshots.

1. Looking at a storm, nothing of the game's sky clouds shows through its base or a tornado, from 300 m to 30 km.
2. A transparent object behind the tornado is hidden, and one between the camera and the tornado shows.
3. Lightning flashes in and below the cloud, lights the cloud from inside, and thunder follows at the right delay.
4. No old weather remains: no second set of clouds, rain, lightning or tornado, and nothing logs about missing old
   weather objects.
5. Frame time on the minimum spec (RTX 3060) is acceptable. If it is too high, lower each skin's `renderScale`, set
   `tier` to Low on the scenario runner, or turn `nearField` off, in that order.

The package's own checks for 1 and 2 are `DesignedSkinTests -designedOnly game` in its test project. That test uses
HDRP's physically based sky, a 100,000 lux sun, ACES tonemapping, fog, volumetric clouds and a lit ground.

## 6. Known gaps (tell the user; do not work around them in the game)

- **The storm does not shadow the game's world.** HDRP's sun still lights the terrain under the storm. Until the
  package exports a shadow, the game may dim its sun and ambient by the player's position under the storm. Agree on
  that with the package owner first.
- **Refraction** (rough glass, water) samples HDRP's colour pyramid, which is built before the storm is drawn, so
  refractive surfaces do not show the storm.
- **Funnel edges:** big wedge and multi-vortex funnels can show a fine sawtooth along their sides. This is a package
  issue being fixed upstream.
- **GPU cost** is above the 8 ms storm budget on an RTX 3060 at 1440p in close views. See the package's
  `validation/reports/SkinReport.md` for the current numbers.
- **Floating origin** is not built in. The runners take `worldOriginKm` when the storms spawn. If the game shifts its
  world origin, ask the package owner for support rather than moving the storms' objects.
