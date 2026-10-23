# StormLab rainfall

Near-camera liquid rain for Unity 6, with a separate assembly that has no dependency on the cloud simulation or URP.
One Unity unit is one metre. World axes are x east, y up, z north. Wind is ground relative in m/s.

## Standalone import

In Unity's Package Manager, choose **+ > Install package from tarball** (called **Add package from tarball** in some
versions) and select `com.stormlab.rainfall-0.1.0.tgz`. Create a scene using
**Supercell Lab > Rainfall > Create Standalone Demo Scene**, then press Play.
The demo asks before replacing a scene with unsaved changes.

For an existing scene, add `LocalRainfall` to an empty GameObject, assign the player camera, set Rain Mm Per Hour,
and enter ground-relative wind. Keep Ground Height at the world's ground elevation. Assign a collision mask that
includes roofs and terrain but excludes the player and trigger volumes. Roofs and terrain need colliders.
Move the demo camera below the shelter roof to check blocking. Add a `RainGauge` at a fixed point to measure water;
assign Fallback Rainfall or a weather source. Gauge accumulation is in mm, independent of visible drop count.

The included streak shader uses transparent geometry and works through a standard forward unlit pass; it requires
no textures or renderer feature. The code compiles against Unity 6000.6.6f1. Rendering and physics must still be
checked in the target project; URP camera stacking, XR, HDRP, and console/mobile builds are not verified.

## Existing Supercell Lab project

The **Brain Full Cycle Test** scene now includes `LocalRainfall` and `BrainRainSource`, bound to its observer camera
and storm skin. Open **Supercell Lab > Open Full Brain Cycle Preview** and press Play. In the right-hand panel,
**Enter recorded surface rain core** places the camera 2 m above the wettest recorded surface rainwater cell.
If no surface rain has formed yet, advance playback and try again. **Reset camera to storm** restores the wide view.
The panel includes a rain visibility toggle, local estimated mm/hour and active drop count. Scene rebuilding also
preserves this integration. At 30x/60x, weather follows the storm clock while individual nearby drops move in real time.

The full package already contains this module. Do not install the standalone tarball into a project that already
uses the full package: it would define the same classes twice.

Add `LocalRainfall` and `BrainRainSource` to an object. Assign the same `StormSkin` used for clouds, assign the
rainfall component, and set its player camera. The bridge samples recorded `qr`, air density and winds at the
camera's world position. It follows the skin's playback time and moving model grid. A seek resets visible drops.
Near drops move in real seconds, even while cloud playback is paused or sped up: this keeps local rain readable in
timelapse. Missing/invalid records and designed-only storms produce no rain. Designed storms need a separate
`IRainWeatherSource`; a rain shaft's drawn density is not interpreted as a water measurement.

Rain Intercept = 0 follows the run's `n0rFall`, using 8e6 m^-4 when that setting is zero. The model's 1e6 evaporation
intercept is deliberately not used for this purpose. If wind history is unavailable, the bridge uses the base wind
profile; otherwise it uses the nearest recorded wind sample. Grid frame motion is added once to get ground wind.

## A friend's weather system

Implement `IRainWeatherSource` on a MonoBehaviour and assign it as Weather Source:

```csharp
public RainWeather Sample(Vector3 worldPosition, float timeSeconds)
{
    return new RainWeather {
        rateMmPerHour = 20f,
        windMps = new Vector3(8, -1, 2),
        airDensity = 1.15f
    };
}
```

The source is sampled once per local volume per frame. Rate input means a still-air rainfall flux. For a modeled
rainwater field, set `useMixingRatio = true`, `qrKgPerKg`, `airDensity`, `interceptPerM4`, and three-dimensional wind.
Then downward diagnostic flux includes vertical wind, including suspension by strong updrafts. Keep a gauge fixed
in world space; moving it measures exposure along a path, not a stationary accumulated rainfall map.

## Quality and limits

The default budget is 2048 representative drops in a 40 x 24 x 40 m volume. Drops enter the upstream faces according
to number concentration, wind, terminal speed and camera velocity. Streak length follows speed times shutter time;
width is diameter times an explicit visual multiplier. CPU swept collider checks prevent crossing thin roofs or
terrain between frames. New drops are checked for overhead shelter up to Shelter Probe Height (500 m by default).
Wind can still carry drops beneath a roof edge, as it should.

The spawn cap and drop budget limit graphics cost. They do not change the spectrum's water/flux diagnostics.
Warm-up takes a few seconds. Teleporting resets local drops. Long frame stalls cap motion at 0.1 seconds; the source
may still advance independently. Avoid extremely small volumes or fast cameras that exceed the spawn cap. Profile
the collider and mesh costs on the target hardware; no frame-rate target has been measured.

This is an exponential single-moment reconstruction, not measured convective drop spectra or a two-moment solver.
It represents liquid diameters 0.2..6 mm, preserving input mass or requested flux over that finite interval. It does
not simulate drop collision/breakup, hail, freezing rain, splash decals, puddles, rain sound, or distant rain curtains.
Existing storm-model evaporation/cooling and distant precipitation rendering remain responsible for those scales.
The optional extinction diagnostic is not injected into global fog, which avoids duplicating the cloud skin's rain.
Streak tint/opacity/width are presentation controls; physical validation does not establish visual realism.

## Validation

Run `validation/rainfall/check.ps1` from the repository. It needs the bundled Unity .NET SDK and a previously compiled
host project for bridge checks. It runs numerical conservation tests and managed interpolation tests using the real
Unity/StormLab assemblies, then compiles runtime, bridge and editor code against Unity references.

`validation/rainfall/RainfallUnityChecks.cs` is a native Unity harness for a separate scratch project with this
standalone module. Run `-batchmode -executeMethod RainfallUnityChecks.Run` without `-nographics` to check shader,
population, roofs, gauges and a preview. Do not place that harness in a player's build. Native checks were blocked
in this authoring session by failure to connect to Unity's licensing client; they have not passed yet.
