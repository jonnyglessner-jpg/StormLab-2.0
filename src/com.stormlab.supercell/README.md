# com.stormlab.supercell

Unity package for Supercell Lab 2.0. See `../../docs/DESIGN.md` for the architecture.

Layout (Environment and Design carried over from StormLab 1.0; Dynamics built in Phase 1):

| Folder | Layer | Status |
| --- | --- | --- |
| `Runtime/Environment` | Sounding + hodograph physics, CAPE/SRH/STP, storm mode diagnosis (`StormPhysics.cs`) | from 1.0; takes real soundings via `Dynamics/SoundingFile.cs` |
| `Runtime/Design` | Scripted storm plan and per-minute feature block (`StormDesign.cs`) | from 1.0; becomes the designer's fallback rules |
| `Runtime/Dynamics` | Compute-shader cloud model (anelastic, 6-class ice microphysics, sine-transform pressure solve), timeline recorder, run-ahead scheduler | built, see `Runtime/Dynamics/README.md` and `TIMELINE.md` |
| `Runtime/Designer` | Sim state -> skin parameter block | Phase 2 |
| `Runtime/Rendering` | URP ScriptableRenderPass: advected storm + near-field volumes, ray-march, analytic funnel | Phase 2 |
| `Runtime/Tornado` | Genesis / maintenance / decay, vortex wind field, shapes, cycling, damage (EF DI/DoD) | Phase 3 |
| `Runtime/Presets` | The eight presets with sounding families and acceptance windows | Phase 4 |
| `Runtime/Lightning` | Optional lightning: flash schedule and channels, thunder, a preview; `Integration/` lights and hides it in the skin's cloud (`LightningCloudBridge`) | built, see `Runtime/Lightning/README.md` |
| `Editor` | Headless validation runs (`DynamicsValidation.cs`, run from `../SupercellLabTest`) | built |

Install: Unity > Window > Package Manager > + > Add package from disk > this folder's `package.json`.
