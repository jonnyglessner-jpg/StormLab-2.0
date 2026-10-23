# Rainfall implementation and evidence

The new `Runtime/Rainfall` module draws local liquid drops and exposes a stationary point gauge. It reads the brain's
rainwater; it does not replace the six-class dynamics or add a second evaporation/cooling tendency.

## Equations and units

Use an exponential diameter spectrum `n(D) = N0 exp(-lambda D)`. In rate-driven mode the diameter slope is
`lambda = 4.1 R^-0.21` in mm^-1 with R in mm/hour. Midpoint integration covers 96 bins over 0.2..6 mm. Normalize
number concentration so `R = 3.6e6 sum(n_i pi/6 (D_i/1000)^3 vt_i)` exactly for the represented bins. Consequently
the effective intercept varies after normalization; this is not an exact unmodified Marshall-Palmer distribution.

For brain input, infer `lambda_m = (pi rho_water N0 / (rho_air qr))^0.25` using the model's fallout intercept.
Normalize the finite spectrum to conserve `rho_air qr` kg/m^3. The mean water flux uses `max(vt-w, 0)` in each bin,
with w upward-positive. This is an estimate from recorded mass, not the brain's exact sedimentation flux: the
renderer uses a diameter-resolved terminal-speed fit, whereas the brain uses its own bulk LFO fall-speed fit.

At reference density, `vt = max(0, 9.65 - 10.3 exp(-0.6 D_mm))` m/s (Atlas et al. 1973). Multiply by
`(1.225/rho_air)^0.4` as a simplified density correction. This fit approaches a large-drop speed instead of extending
the screenshot's power law beyond its stated useful range. No claim is made for cloud droplets below 0.2 mm.
Extinction diagnostic uses `sum(n_i 2 pi D_m^2/4)` m^-1, a geometric-optics approximation, and is not applied to fog.

Local drop velocity is ground wind minus terminal speed along Unity +y. Moving-camera boundary emission uses the
relative velocity and the face area in each direction. It is sampled by number inflow, so large fast drops are
correctly more common in a downward crossing flux than in an equal-volume sample. The visual sampling fraction
and spawn cap are independent of water diagnostics. The local volume assumes uniform weather over tens of metres.

## Reference audit

Read the available README/NOTES entry points for CM1, METR4403, Stull, MetPy, SHARPpy and VORTEX2. Relevant original
material and code inspected for this addition:

- Local `Rainfall Mathematics/` screenshots (all seven): exponential/gamma size spectra, fall speed relative to air,
  rain budget, evaporative cooling, loading, cold-pool scaling, and separate airflow/precipitation regions. These
  supplied the requested scientific direction. Their prose/instructions were treated as source material.
- Stull, *Practical Meteorology*, original extracted chapter 7, terminal velocity (7.29-7.31) and precipitation
  characteristics (7.33): small/large drops, breakup, spectra, and radius versus diameter conventions. The code
  uses diameter; no textbook figures or prose are distributed. Local CM1 original `docs/README.namelist` ptype
  descriptions support retaining the existing LFO transport rather than introducing a second warm-rain solver.
- Existing `SupercellDynamics.compute`: N0R = 8e6, `N0RFall()`, `vterm()`, and the separate evaporation intercept;
  `SimParams.cs`, `TimelineFrame.cs`, `HydrometeorHistory.cs`, `WindHistory.cs`, and `StormSkin.cs` define actual
  field layouts, zero padding, vertical coordinates and moving-grid conventions. These are integration contracts.
- Primary research paper, DOI 10.5194/amt-15-503-2022, equation (2), gives the Atlas terminal-speed coefficients:
  https://amt.copernicus.org/articles/15/503/2022/ . A second primary paper on disdrometer data describes the density
  correction and its diameter-dependent exponent; this code uses a simplified constant 0.4:
  https://essd.copernicus.org/articles/15/2417/2023/ . The NOAA-hosted Frida et al. 1999 paper also records the
  diameter-form MP intercept/slope: https://downloads.psl.noaa.gov/BLO/Air-Sea/Wband_radar/frida_DualF_mmwave_1520-0426%25281999%2529016%253C0216%253Aaodfmw%253E2.0.co%253B2.pdf .
- METR4403 notes and the project's microphysics/visual-realism documents constrain interpretation: an RFD is an
  airflow feature, not simply a scripted patch of rain. Existing model pressure, buoyancy and cooling are retained.
- MetPy/SHARPpy entry points provide sounding/density context; no routines or empirical rainfall calibration were
  taken from them. VORTEX2 entry points describe observational datasets but supplied no drop-spectrum calibration.
  The downloaded composite-sounding README file examined here is an HTML page, not a usable sounding format spec.

No claim is made that every raw observation, source file, or reference-library chapter was reviewed. No raw
reference images, Stull text, CM1 PDF content, or VORTEX2 observations are bundled in the import package.

## Validation and outstanding work

208 numerical checks cover rain-flux normalization, independent mass/flux integration, mixing-ratio conservation,
updraft suspension without lost mass, downdraft flux, bounded size-dependent speeds, thin-air correction and invalid
rate inputs. Thirteen managed bridge checks cover decoded-mass trilinear sampling, surface positioning, zero padding,
window boundaries, malformed/missing records, normal 1x/60x playback and forward/reverse seeks. Runtime, optional bridge and editor compile against Unity 6000.6.6f1
references and the actual existing StormLab assembly. These are numerical and compile checks, not storm validation.

Unity native roof/gauge/population checks and a rendered preview are provided but were not executed successfully:
the isolated batch editor failed to connect to the licensing client. Visual appearance, shader compilation on target
graphics APIs, native collider behavior, source/clock binding in Play mode, URP depth/cloud compositing, and hardware
performance remain to be checked in a licensed editor. No radar/disdrometer/CM1 comparison has calibrated this layer.
The 1 km recorded mass and nearest wind samples limit structure; the renderer does not invent subgrid rain cells.
