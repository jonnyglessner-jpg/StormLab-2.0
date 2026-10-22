# Cloud motion validation

Latest status (2026-10-22, on main): all four checks pass headless in Unity 6000.6.6f1 batch mode
(`-executeMethod CloudMotionChecks.RunFromCommandLine`, licensing connected this time): model clock
(pause, timelapse, reset), cloud-base updraft continuity, and signed GPU transport east/rising and
west/descending. Before merging, all 7 assemblies compiled with no errors and no new warnings, and the
12 DesignedChecks gave identical results apart from the 6 km updraft (38.2 -> 39.4 m/s). `SkinTests
-skinQuick` renders were pixel-identical to main's; its FAIL is the existing RTX 3060 budget estimate
(8.3 ms with these changes, 8.5 ms without, budget 8 ms). The user had earlier reported the in-editor
checks passing too. The earlier blocked attempts and readback test correction below are kept as history.

Brain visual preview: `BrainPreviewPanel` now attaches automatically on entering Play in the existing
`SupercellSkinTest` scene. It shows displayed/model/computed times, measured run-ahead speed, whether
playback is waiting, initiation status, the displayed frame's updraft and cloud top, and pending readbacks.
It provides pause, 1x/10x/30x playback, a computed-time slider, a latest-frame button, compute-ahead limits,
and an updraft camera-framing/follow control. The shader warnings shown in the screenshot were not treated
as proof of simulation failure. No governing equations, initiation parameters or source winds were changed.
The updated Runtime sources and cloud test code compile against the installed Unity assemblies (one
pre-existing unused-field warning); visual layout and live preview operation still need an in-editor review.
Diff checking also found trailing whitespace in the user's newly generated scene, which was left untouched.

Changes: cloud detail and rotation now share model time with volume advection; the analytic designed updraft
joins the positive subcloud velocity continuously at the LCL. No storm-scale governing equations were changed.

- PASS: compiled the production Runtime sources (excluding the separate Lightning assemblies) and
  `CloudMotionChecks.cs` with Unity's bundled Roslyn compiler and installed Unity/URP reference assemblies.
  One existing unused-field warning (`StormSkin.pendingShift`), no compilation errors.
- PASS: executed the production `KinematicWindField.Wind` method under .NET, checking cloud-base continuity
  from both sides, 12 m/s at the LCL for a 40 m/s maximum, unchanged 40 m/s peak, and zero at storm top.
- NOT RUN: Unity runtime clock checks and signed GPU transport centroid checks. The batch editor stalled
  during licensing initialization: missing `LicenseClient-Jonny Glessner` IPC channel and later
  `com.unity.editor.headless` entitlement error. The launched editor was stopped after repeated reconnects.
  Diagnostic log: `cloud-motion.log`. These checks have not passed yet.
- NOT RUN: visual motion sequence review, observational calibration, and GPU performance measurements.

The user's in-editor run subsequently reached the GPU transport measurement but failed in the test's CPU
readback loop: `GetData<Color>()` returned the first 32x32 slice (1024 pixels), while the loop indexed it
as a complete 32x32x32 volume. The test now reads each slice explicitly, validates the layout and cloud
mass, and also accepts a backend exposing one packed volume. Separate clock and updraft pass messages
were added. This fixes the test harness; a rerun is still needed before recording a GPU transport pass.

To run the pending checks in a licensed session:

With the project already open, use **Supercell Lab > Run Cloud Motion Checks**. This runs the same
checks in Edit mode and logs `CLOUD MOTION PASS` only after all checks succeed; it does not close the editor.
The command now shows a progress window, advances through four stages on editor updates, and displays
a final pass/failure dialog even if Console information messages are filtered. A timestamped result is
saved to the project's `Library/CloudMotionChecks.last-run.txt`, with each stage recorded before it runs
and after it passes. The command does not animate the storm scene. Concurrent runs and runs during
compilation or Play mode are disabled. The updated test code compiled successfully against the installed
Unity assemblies; the interactive progress UI and GPU result still require an in-editor run.

Retry: the original project rejected a second editor because it was already open. An isolated project
copy was then launched using the installed package cache. Licensing failed after 74.81 seconds with
`Connection to channel LicenseClient-Jonny Glessner refused`. Only the isolated editor launched for this
retry was stopped; the user's existing editor sessions were left running. No successful runtime/GPU
result has been recorded from this retry.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.6f1\Editor\Unity.exe' -batchmode -projectPath 'C:\Users\Jonny Glessner\Desktop\ClaudeCoding\supercell-lab-2\tests\SupercellLabTest' -executeMethod CloudMotionChecks.RunFromCommandLine -logFile 'C:\Users\Jonny Glessner\Desktop\ClaudeCoding\supercell-lab-2\validation\reports\cloud-motion.log'
```

Source mapping and remaining physical limitations: `docs/meteorology/cloud-motion.md`.
