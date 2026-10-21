# Supercell Lab 2.0 dynamics validation

Run 2026-10-20 23:27, suite `perf`, GPU AMD Radeon RX 7900 XT (Direct3D12), Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| step time at both tiers | PASS | Average 10.6/11.6 ms per step, 41.3x at 4 ms/frame; High 27.3/28.1 ms per step, 14.2x at 4 ms/frame |

1 passed, 0 failed, 0.3 min.

## Step time on this PC

GPU AMD Radeon RX 7900 XT, Direct3D12. WK supercell environment, timed after 10 min of storm (forced updraft active).

| Tier | Grid | dt | Step (no recording) | Step (with timeline) | Real time costs | Max speed at 50 % GPU | VRAM | Checkpoint |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Average | 240x240x72, 333 m, dz 100-323 m | 2 s | 10.6 ms | 11.6 ms | 5.8 ms GPU per sim-second | 41.3x real time (4 ms/frame at 60 fps) | 601 MB | 84 MB |
| High | 320x320x90, 250 m, dz 100-230 m | 1.667 s | 27.3 ms | 28.1 ms | 16.9 ms GPU per sim-second | 14.2x real time (4 ms/frame at 60 fps) | 1327 MB | 185 MB |

