# Designed storm render test

2026-10-23 16:14, AMD Radeon RX 7900 XT, Unity 6000.6.6f1.

## Game look (HDRP sky, sun 100,000 lux, EV 13, ACES, fog, volumetric clouds)

Skin luminance 15915 nits per unit.

| Shape | Axis change with HDRP clouds on/off (100 / 300 / 600 m) | Marker behind: change | Marker in front: change |
| --- | --- | --- | --- |
| MultiVortex | 0% / 0% / 0% | 0% | 109% |
| MultiVortex control, facing away | HDRP clouds change 3.4% of the view | | |
| Wedge | 0% / 0% / 0% | 0% | 89% |
| Wedge control, facing away | HDRP clouds change 3.2% of the view | | |

`designed_game.png`: per row, one shape from 1 km: HDRP clouds on, off, red marker behind the tornado; then facing away with HDRP clouds on and off.
