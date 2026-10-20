# References

Where the meteorology in these docs comes from, what each source is good for, and what it lets us do.

The study notes live **outside this repo**, in `Desktop/ClaudeCoding/meteorology/` (from the repo root,
`../meteorology/`). A clean clone won't have them. Each folder has a `NOTES.md`, and the originals sit beside it.

## Local reference library

| Folder | What | Best for | License |
|---|---|---|---|
| `METR4403/` | OU METR 4403/5403 Severe Thunderstorm Forecasting, spring 2026 (Ming Xue; SPC instructors). Lectures, exams, exercises | storm dynamics, p′ decomposition, tornadogenesis with friction (§23–24), composites, mode, hail, CAM biases, EF scale | course materials; for study, not redistribution |
| `Practical_Meteorology_Stull/` | Stull, *Practical Meteorology* v1.02b, chs 3–8, 10, 14, 15, 18, 20 | textbook thermodynamics, microphysics and fall speeds, radar, boundary layer, NWP numerics | **CC BY-NC-SA 4.0: non-commercial.** Formulas are facts and fine to implement; don't copy the text or figures into a commercial game |
| `CM1/` | CM1 r22 docs: equations PDF (v10), README.namelist, test pages, example namelists | the reference cloud model: equation sets, closures, boundaries, testcases 17 and 18; §12 compares it with the Lab | equations PDF is all rights reserved (Bryan); the source is MIT-style but needs a click-through (not downloaded) |
| `SHARPpy/` | SPC's NSHARP in Python (v1.4.0 clone) + `tools/check_sounding.py` | the SPC definitions of every severe parameter; checking StormPhysics.cs | BSD 3-clause |
| `MetPy/` | MetPy 1.7.1 clone + `tools/cape_variants.py` | general thermodynamics, exact LCL, NEXRAD/GOES readers | BSD 3-clause |
| `VORTEX2/` | VORTEX2 field data (2009–2010) from NCAR EOL; SPC reports for both seasons | in-situ soundings, mobile mesonet, StickNet, tornado pods; DOW-6/7 radar for Goshen County 2009 | EOL data policy: cite each dataset (citations come with the orders) |

**EOL orders pending (2026-10-20):** the VORTEX2 datasets come by emailed download link. See
`../meteorology/VORTEX2/README.md` for the status table.

## Key papers, by topic

**Supercell dynamics:** Rotunno & Klemp 1982, 1985 (p′ and splitting); Weisman & Klemp 1982 (the WK sounding);
Bunkers et al. 2000, 2014 (storm motion); Rasmussen & Straka 1998 (LP/classic/HP); Lemon & Doswell 1979 (the RFD);
Browning 1964.

**Tornadogenesis:** Markowski 2002 (RFD thermodynamics); Markowski & Richardson 2014 (Goldilocks outflow); Davies-Jones
& Brooks 1993; Rotunno et al. 2017; Trapp & Weisman 2003; Schenkman, Xue & Hu 2014; Roberts et al. 2016; Roberts &
Xue 2017; **Roberts, Xue & Dawson 2020 (drag sweep: no tornado at C_d = 0)**; Markowski 2016, 2024; Huang & Xue
2023 (vortex Rossby waves), 2025a,b, 2026 (spin/splat; decay); Coffer et al. 2019 (0–500 m SRH); Oliveira et al.
2022 (trailing horizontal vortices).

**Environments and composites:** Thompson et al. 2003, 2007, 2012 (STP, SCP, effective layer); Smith et al. 2012;
Coffer et al. 2019; Gensini & Bravo de Guenni 2019; Peters et al. 2023 (ECAPE).

**Mixing and numerics:** Deardorff 1980; Stevens et al. 1999; Shi et al. 2019 (closures at km scale; read before
touching the Lab's 250–333 m mixing); Bryan & Fritsch 2002 (moist energy set); Bryan & Rotunno 2014 (BR14 cold
pool); Bryan et al. 2017 and Rotunno et al. 2016 (Fiedler chamber).

**Microphysics:** Gilmore, Straka & Rasmussen 2004 (the LFO family); Rutledge & Hobbs 1983 and Morrison et al. 2009
(melting-hail vapour exchange); Marshall & Palmer 1948.

**Ratings and climatology:** WSEC 2006 / the EF-scale spec (TTU); Lyza et al. 2024, Lyza 2025 (EF ratings as a lower
bound); Kosiba & Wurman 2023 (DOW tornado winds); Thompson et al. 2017 (Vrot vs EF).

**VORTEX2 Goshen County 2009:** Wakimoto et al. 2011; Atkins et al. 2012; Markowski et al. 2012a,b; Kosiba et al.
2013 (all *Monthly Weather Review*).

## Data sources used by the tools

| Source | Tool | Notes |
|---|---|---|
| SPC severe-weather database (1950–2025) | `tools/spc_tornado_stats.py`, `tools/spc_reports.py` | official ratings; EF-era high-end ratings are conservative |
| IGRA2 radiosondes (NCEI) | `tools/fetch_sounding.py igra` | keep wind-only levels |
| RAP / HRRR analyses (NCEI THREDDS, AWS) | `tools/fetch_sounding.py rap/hrrr` | HRRR overmixes warm-season boundary layers and dries the dryline zone; RAP via `model-rap130anl-old`; eccodes needs multi-field support for NCEP u/v |
| NEXRAD Level 2 (Unidata S3 mirror) | `tools/fetch_nexrad.py`, `scan_nexrad.py`, `radar_lifecycle.py` | the NOAA bucket refuses anonymous listing |
| GOES-16/18 ABI | `tools/fetch_goes.py` | parallax-correct before overlaying |
| NCAR EOL VORTEX2 archive | manual orders | order-only; see `../meteorology/VORTEX2/README.md` |

## House rules that come from these sources

- **Official stats beat memory.** Max EF, wind and hail for any case come from SPC/NHC data within 300 miles; mark
  estimated winds with ~.
- Implement equations; don't paste text from the all-rights-reserved or non-commercial sources into the friend's
  game.
- Reference photos in `data/reference/` are by other photographers. Keep the repo private.
