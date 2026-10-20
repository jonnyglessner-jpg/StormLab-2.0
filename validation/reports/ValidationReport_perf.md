# Supercell Lab 2.0 dynamics validation

Run 2026-10-18 17:55, suite `perf`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| step time at both tiers | PASS | Average 7.8/9.0 ms per step, 53.1x at 4 ms/frame; High 19.9/21.1 ms per step, 18.9x at 4 ms/frame |

1 passed, 0 failed, 0.2 min.

## Step time on this PC

GPU AMD Radeon RX 7900 XT, Direct3D12. WK supercell environment, timed after 10 min of storm (forced updraft active).

| Tier | Grid | dt | Step (no recording) | Step (with timeline) | Real time costs | Max speed at 50 % GPU | VRAM | Checkpoint |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Average | 240x240x72, 333 m, dz 100-323 m | 2 s | 7.8 ms | 9.0 ms | 4.5 ms GPU per sim-second | 53.1x real time (4 ms/frame at 60 fps) | 568 MB | 84 MB |
| High | 320x320x90, 250 m, dz 100-230 m | 1.667 s | 19.9 ms | 21.1 ms | 12.7 ms GPU per sim-second | 18.9x real time (4 ms/frame at 60 fps) | 1253 MB | 185 MB |

