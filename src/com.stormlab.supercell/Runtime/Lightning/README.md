# Independent lightning prototype

An optional Unity module with its own assembly and random streams. The core stays independent; the cloud adapter
adds a small transient-lighting hook to `StormSkin` and its march shader. Dynamics, genome, case studies, renderer
feature assets and saved scene files are unchanged. The package metadata declares Unity's built-in audio module for
thunder (the host previously omitted it). One world unit is one metre: x east, y up, z north.

## Preview

In Unity, choose **Supercell Lab > Lightning > Open Independent Preview**, then Play. With a saved scene open,
the menu creates an unsaved additive scene. With an untitled scene open it adds the preview objects to that scene
(Unity cannot open another scene additively beside an unsaved scene). It never removes existing objects.
The preview camera clears over other cameras and temporarily disables other audio listeners during Play.
For minimum GPU use, run the preview alone rather than alongside an active storm simulation.

The preview starts on a frozen negative-ground return stroke. Its panel offers **Hold peak**, **Next flash**,
direct flash-type selection, **Replay 1x**, **Replay 0.1x**, and **8 / 16 / 30 km** views. A width slider adjusts
the physical channel width; a separate minimum apparent width preserves distant visibility. The ground has a
dark matte material, and the channel shader combines a white core with a restrained blue-violet halo.
Internal channels are exposed for inspection; there is no cloud volume in this scene.

Outside Play mode, **Supercell Lab > Lightning > Capture Review Frames** saves fourteen GPU-rendered PNGs under
`validation/lightning/out/review/`: four stages of one negative-ground flash at three distances, plus in-cloud and
positive-ground peaks. Capture uses the current preview settings. It does not save or alter the cloud test scenes.

For an existing scene, add `LightningSystem` to a GameObject at the storm centre. Assign `observer` to the
player/camera. `groundY` is the ground plane's world altitude; charge heights are measured above it.
The component advances a local real-time clock by default. No scene hookup is automatic.

## Cloud illumination and occlusion

The independent `StormLab.Lightning` assembly still has no dependency on `StormLab.Supercell`.
The optional `StormLab.Lightning.Clouds` assembly in `Integration/` connects the two. Add `LightningCloudBridge`
to the lightning GameObject, then assign its `lightning` and `skin` references. The skin's URP renderer must already
have `StormSkinFeature`. No existing scene, case study, cloud shape or storm random stream is changed automatically.

For a prepared night storm and the same hold/replay controls, choose **Supercell Lab > Lightning > Open Cloud
Lightning Preview**, then Play. This adds unsaved preview objects, holds the designed cloud at 30 minutes, and uses
a separate lightning clock for repeated inspection. Its camera temporarily suppresses other preview cameras and
panels during Play. The independent preview remains available.

For a game clock rather than the preview clock:

```csharp
var bridge = lightning.gameObject.AddComponent<LightningCloudBridge>();
bridge.lightning = lightning;
bridge.skin = skin;
bridge.Connect(); // Install cloud anchoring before the first SampleAt call.
lightning.advanceClock = false;
// Supply the storm's position AT emission time, in world metres, for moving-storm replay.
lightning.CellCentreAtTime = seconds => StormCentreAt(seconds);
lightning.TerrainHeight = point => GroundHeightAt(point.x, point.z);
// After the skin clock advances, before rendering:
lightning.SampleAt(skin.playbackTime);
// The bridge uploads light and attenuation inputs before the camera's cloud march.
```

`StormCentreAt`/`GroundHeightAt` above represent game-owned adapters, not included APIs. Centre y is the base
altitude for charge heights. Without a history adapter, the component uses its current transform x/z and `groundY`.
Ground attachment can sample terrain, and intermediate channel vertices are kept above sampled ground.
Segments do not continuously trace terrain or buildings for collision.

Cloud-connected flashes now obtain their origins from the skin's current cloud field rather than fixed charge
heights. A deterministic GPU search checks 128 source candidates and, for in-cloud flashes, a second set of 128
terminal candidates. An accepted point needs cloud extinction of at least 0.5/km both at the point and 150 metres
away along each axis; rain alone does not qualify. If no suitable cloud anchor exists, the scheduled event is
suppressed, including its light and thunder. The standalone geometry preview still uses synthetic charge heights.

Positive-ground flashes preferentially sample the upper cloud and the anvil flanking the tilted updraft above
the mesocyclone. `positiveAnvilFraction` defaults to 0.7 as a tunable presentation choice, not an observed universal
polarity statistic. The search can fall back to a valid upper-core origin; the ground endpoint follows the selected
origin's horizontal displacement and samples terrain. A single connected channel spans the full descent.
See [NOAA's positive lightning explanation](https://www.noaa.gov/jetstream/lightning/positive-and-negative-side-of-lightning).

Anchoring uses the same coarse cloud, analytic anvil and near-field medium as channel occlusion; fine billow edges
can still differ from the full cloud march. It performs one 4 KiB synchronous GPU readback per newly considered
event, with none on ordinary held frames. Its cost at high flash rates has not been profiled. Replay requires the
same cloud snapshot at emission, as well as the same seed and terrain. Advance the skin to the desired time before
sampling lightning. This remains a presentation model, not a charge/electric-field solver.

`FlashStarted` supplies the complete immutable-by-convention channel geometry and event metadata. `Illumination`
supplies active source positions, kinds and relative brightness, without allocating a new list each frame.
There are four samples along each active main channel. The bridge keeps the strongest eight and builds a bounded
64 x 64 x 24 half-float local-radiance field. It traces effective optical depth from each grid point toward each source,
then the cloud march scatters that local radiance using its existing extinction. Brightness, soft radius and effective
multiple-scattering extinction are look controls, not calibrated photometry or a full multiple-scattering solver.
Lightning is compensated for the night-adaptation gain so it is not amplified like starlight.

The transparent channel shader now traces view-to-channel optical depth and multiplies radiance by `exp(-tau)`.
The shared medium includes coarse cloud/ice, the analytic anvil (including beyond the macro volume), near-field
replacement, low cloud features and rain. Opaque geometry still uses normal depth testing. Without a bridge,
volumetric attenuation stays off and the standalone preview works as before. In-cloud channels remain hidden by
default; enable `showInCloudChannels` when using the bridge to allow optically visible portions of internal paths.

The skin accepts an `IStormTransientLighting` adapter, without referencing the lightning assembly. It skips temporal
blending and checkerboard sampling during a flash and for the first frame after it ends. This preserves brief pulses
and removes their history without changing normal no-lightning rendering.

Current limits: the light field covers the stored 60 x 60 x 18 km cloud volume, so the far analytic anvil can hide
channels but is not yet illuminated outside that volume. Attenuation uses coarse extinction thresholds; fine billows,
striations and analytic tornado details can differ at their edges. Source shadows do not trace buildings or terrain.
Near-field/far-anvil sampling is implemented, but the automated review concentrates on macro cloud and rain.
Per-camera light builds and per-fragment channel traces need GPU profiling before a production performance claim.

Outside Play, **Capture Cloud Integration Review** runs GPU/image and origin checks and writes seven PNGs plus `checks.txt`
under `validation/lightning/out/clouds/`. These include real cloud flash-off/on/after images and actual bolt-shader
clear/buried/foreground images. Tests use temporary objects and restore the previous active skin. On D3D12 the review
passed: empty-air/foreground transmission 1, opaque-cloud transmission below 0.000001, and one kilometre of the rain
fixture transmitted about 0.189. The first frame after a pulse returned close to the unlit image.
The origin checks reject rain-only locations, replay anchors across 12 seeds of each flash type, preserve the actual
channel endpoints, and render `positive_anvil_to_ground.png` from a ground-level viewpoint. The anvil example starts
about 12.3 km above ground beside the tower. **Check Channel Lifecycle** also checks that rejected origins emit no
channels or illumination and are not queried again every frame.

## Behaviour and replay

- Separate in-cloud, negative-ground and positive-ground presentation styles; charge-region endpoints in metres,
  multiscale channel bends, tapered unconnected side leaders, a brief faint leader and repeated strokes reusing
  the same channel. Positive-ground flashes use a higher origin, wider reach and a longer dim tail.
- A Poisson schedule in independent ten-second bins. The same settings/seed and requested time window return
  the same events, regardless of frame rate or earlier playback. Geometry also needs the same centre and terrain.
  Neither the storm genome nor Unity's global random state is used.
- Rendering uses peak intensity over the last exposure interval to preserve short strokes at normal frame
  rates. This is a display approximation, not a resolved return-stroke propagation calculation.
- Scrubs, first sampling, and jumps over 0.25 seconds rebuild the current view and cancel pending thunder.
  Paused playback holds its current instantaneous flash. Use real-time playback for judging timing and audio.
- Thunder is synthesized from twelve samples along the main channel, each with its own travel time. Distant
  contributions lose high frequencies and amplitude. It is scheduled on Unity's audio clock, with up to sixteen
  pending/playing voices. Travel speed uses `331.3 + 0.606 * temperatureC` m/s. The listener is frozen at emission;
  refraction, wind, echoes, obstacles and listener motion during propagation are not modelled.
- Thunder is suppressed on seeks and when `timeScale` differs from one. With an external clock, also set
  `timeScale` to the external playback speed so accelerated playback remains silent.
- Visuals are capped at `maxVisibleFlashes`, newest first. Each flash has at most twelve channels; geometry and
  audio allocate at emission and are released after use. Already considered events are not rebuilt every frame.
  This version is not yet pooled or GPU-profiled.

Changing rate, fractions, charge geometry, seed or origin changes the procedural history; call `ResetPlayback()`
after edits. For evolving rates, record fixed-setting segments and their start times in a future storm adapter.
Flash rates, polarity fractions, heights and reach are tunable presentation assumptions. They are **not** inferred
from CAPE, EF rating, case-study identity or the current hydrometeor fields. No charge-separation or electric-field
solver is implemented. The positive/negative styles represent broad appearance differences, not complete polarity physics.

## Verification

Run `validation/lightning/check.ps1` from PowerShell. It uses Unity's bundled .NET SDK, executes deterministic
schedule/geometry/exposure/acoustic checks, then compiles the runtime and preview against Unity's real C# references.
The script does not launch Unity or judge the final cloud composite. The standalone core project also runs with a
normal .NET 8 SDK. The visual tuning pass was separately run in Unity 6000.6.6f1 on D3D12: fourteen review images at
1600 x 900, plus the interactive Play-mode preview. The 2,562 core checks passed after tuning. Distant channels
are now sampled as continuous bright cores rather than faint dotted lines. Minimum screen width is a display
approximation; this pass does not establish physical photometry, cloud visibility, or performance budgets.

Scientific grounding: [NSSL lightning types](https://www.nssl.noaa.gov/education/svrwx101/lightning/types/),
[NWS negative flashes](https://www.weather.gov/safety/lightning-science-negative-charged-flash), and
[NSSL thunder FAQ](https://www.nssl.noaa.gov/education/svrwx101/lightning/faq/). These support leader branching,
repeat-stroke flicker and distributed thunder arrivals. Procedural constants here are adjustable approximations,
not a calibration to a measured storm.
