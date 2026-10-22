## Step time on this PC

GPU AMD Radeon RX 7900 XT, Direct3D12. WK supercell environment, timed after 10 min of storm (forced updraft active).

| Tier | Grid | dt | Step (no recording) | Step (with timeline) | Real time costs | Max speed at 50 % GPU | VRAM | Checkpoint |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Average | 240x240x72, 333 m, dz 100-323 m | 2 s | 8.7 ms | 10.1 ms | 5.1 ms GPU per sim-second | 47.4x real time (4 ms/frame at 60 fps) | 601 MB | 84 MB |
| High | 320x320x90, 250 m, dz 100-230 m | 1.667 s | 22.5 ms | 23.7 ms | 14.2 ms GPU per sim-second | 16.9x real time (4 ms/frame at 60 fps) | 1327 MB | 185 MB |
