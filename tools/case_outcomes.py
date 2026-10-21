"""What each reference storm actually did: the observed side the brain is scored against.

  python -I tools/case_outcomes.py

Joins, per case in validation/real-storm-cases/catalog.json:
  - the radar life cycle (radar/<case>_lifecycle.json): first echo, maturity, end and how it ended, lifetime, mean
    motion, highest 50 dBZ top, largest 40 dBZ area, splits
  - the SPC reports matched to that track (radar/<case>_spc_reports.json): this storm's tornado / hail / wind reports
  - the catalog's SPC tornado list (the box around the case) and the case's primary sounding (inflow if present)
  - GOES cloud tops (satellite/<case>.json from fetch_goes.py): coldest top, share of scans with an overshooting top,
    median anvil size while it was mostly free of neighbours
and runs track-quality checks, because the tracker follows "the nearest 40 dBZ cell" and can lose or swap storms:
  - the strongest tornado must fall inside the tracked life (else the track ended early or followed a neighbour)
  - the box's tornadoes should mostly be matched to the track (else another storm made them, or the track drifted)
  - a storm within 30 km of its radar sits in the cone of silence (tops and areas are unreliable)
Writes validation/real-storm-cases/outcomes.json and OUTCOMES.md.
"""
import json
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
RADAR = CASES / "radar"
SND = ROOT / "validation" / "soundings"
PRESETS = ["plains_lp", "plains_classic", "plains_hp", "dixie_classic", "dixie_hp", "midwest_classic", "midwest_hp", "minisupercell"]


def primary_sounding(cid):
    for src in ("hrrr_inflow", "rap_inflow", "hrrr", "rap", "igra"):
        f = SND / f"{cid}_{src}.json"
        if f.exists():
            return src, json.loads(f.read_text())["diagnostics"]
    return None, {}


def lab_stp(e):
    """Fixed-layer STP after Thompson et al. 2003, with mixed-layer CAPE/LCL (the lab's diagnostics, not SPC's)."""
    c, lcl, h, sh = e.get("mlcape"), e.get("mllcl_m_agl"), e.get("srh1"), e.get("shear6_kt")
    if None in (c, lcl, h, sh):
        return None
    lt = 1.0 if lcl < 1000 else (0.0 if lcl > 2000 else (2000 - lcl) / 1000)
    sm = sh * 0.514444
    return c / 1500 * lt * h / 150 * (0.0 if sm < 12.5 else min(sm, 30) / 20)


def outcome_groups(rows):
    """Environment percentiles for significant-tornado, weak-tornado and null storms (model soundings only)."""
    g = {"EF2+ tornado": [], "EF0-EF1 / EFU tornado": [], "null (supercell, no tornado)": []}
    for r in rows:
        if r["sounding"] in (None, "igra"):
            continue
        m = r["box_max"]
        key = ("null (supercell, no tornado)" if r["role"] == "null" else "EF2+ tornado" if m in ("EF2", "EF3", "EF4", "EF5")
               else "EF0-EF1 / EFU tornado" if m in ("EF0", "EF1", "EFU") else None)
        if key:
            g[key].append(dict(r["env"], stp=lab_stp(r["env"])))
    L = ["## Tornadic vs null environments", "",
         "Primary model soundings grouped by the SPC outcome. Values are p25 / median / p75. STP is the lab's fixed-layer",
         "version (Thompson et al. 2003 form, mixed-layer CAPE and LCL), not SPC's effective-layer STP.", "",
         "| Group | n | MLCAPE | MLLCL m | 0-1 km SRH | 0-6 km shear kt | Lab STP |", "|---|---|---|---|---|---|---|"]
    for name, es in g.items():
        cells = []
        for k in ("mlcape", "mllcl_m_agl", "srh1", "shear6_kt", "stp"):
            x = sorted(e[k] for e in es if e.get(k) is not None)
            if not x:
                cells.append("-"); continue
            q = lambda f: x[min(len(x) - 1, int(f * len(x)))]  # noqa: E731
            fmt = (lambda v: f"{v:.1f}") if k == "stp" else (lambda v: f"{v:.0f}")
            cells.append(f"{fmt(q(.25))} / {fmt(q(.5))} / {fmt(q(.75))}")
        L.append(f"| {name} | {len(es)} | " + " | ".join(cells) + " |")
    return L + [""]


def sat(g):
    if not g:
        return "-"
    a = g["anvil_major_minor_km"]
    anv = f"{a[0]:.0f}x{a[1]:.0f}" if a and None not in a else "-"
    otf = "-" if g["ot_fraction"] is None else f"{g['ot_fraction']:.0%}"
    return f"{g['coldest_c']} / {otf} / {anv}"


def main():
    cat = json.loads((CASES / "catalog.json").read_text())["cases"]
    runs = json.loads((RADAR / "lifecycle_runs.json").read_text()) if (RADAR / "lifecycle_runs.json").exists() else {}
    rows = []
    for c in cat:
        cid = c["id"]
        lf, rf = RADAR / f"{cid}_lifecycle.json", RADAR / f"{cid}_spc_reports.json"
        row = {"case": cid, "preset": c["preset"], "role": c["role"], "flags": []}
        tors = c["spc"].get("tornado_list") or []
        row["box_tornadoes"] = len(tors)
        row["box_max"] = c["spc"].get("max_rating", "prelim")
        if lf.exists():
            life = json.loads(lf.read_text())
            s = life["summary"]
            row.update(site=life["site"], first=s["first_40dbz_utc"][:16], mature=s["mature_utc"][:16],
                       last=s["last_40dbz_utc"][:16], start_reason=s["start_reason"], end=s["end_reason"], life_h=s["lifetime_h"],
                       motion=s.get("mean_motion"), top50_km=s["max_top50_km"], top18_km=s["max_top18_km"],
                       area40_km2=s["max_area40_comp_km2"], max_dbz=s["max_dbz"], splits=len(s.get("splits_utc", [])))
            t0, t1 = datetime.fromisoformat(s["first_40dbz_utc"]), datetime.fromisoformat(s["last_40dbz_utc"])
            if tors:
                rank = lambda x: -1 if x["rating"] == "EFU" else int(x["rating"][2])  # noqa: E731
                best = sorted(tors, key=lambda x: (-rank(x), x["utc"]))[0]
                tb = datetime.fromisoformat(best["utc"])
                if not (t0 <= tb <= t1):
                    row["flags"].append(f"strongest tornado ({best['utc'][11:]}Z) outside the tracked life")
            if s["start_reason"] == "end_of_window" or s["end_reason"] == "end_of_window":
                row["flags"].append("track hit the 4 h search window: life is a lower bound")
            ranges = [v["range_km"] for v in life["volumes"] if v.get("range_km") is not None]
            if ranges and min(ranges) < 30:
                row["flags"].append(f"passed {min(ranges):.0f} km from {life['site']} (cone of silence)")
        else:
            row["flags"].append("no radar track")
        if rf.exists():
            r = json.loads(rf.read_text())["this_storm_final"]
            row.update(storm_tornado_reports=r["tornado_reports"], storm_hail_reports=r["hail_reports"],
                       storm_wind_reports=r["wind_reports"], storm_max_hail_in=r["largest_hail_in"], storm_max_gust_kt=r["strongest_gust_kt"])
            if tors and r["tornado_reports"] == 0:
                row["flags"].append("none of the box's tornadoes matched to this track")
            if c["role"] == "null" and r["tornado_reports"]:
                row["flags"].append("null case, but tornado reports match this track")
        sf = CASES / "satellite" / f"{cid}.json"
        if sf.exists():
            g = json.loads(sf.read_text())["summary"]
            ot = g.get("overshooting_tops") or {}
            shp = g.get("shape_mostly_free_scans_over_1000km2") or {}
            row["goes"] = {"coldest_c": g.get("coldest_c"), "echo_to_minus50C_min": g.get("first_echo_to_minus50C_min"),
                           "echo_to_anvil_1000km2_min": g.get("first_echo_to_anvil_1000km2_min"),
                           "ot_fraction": ot.get("fraction_of_scans_with_ot"), "ot_diam_km_median": (ot.get("diam_km_median_max") or [None])[0],
                           "anvil_major_minor_km": [shp.get("extent_major_km_median"), shp.get("extent_minor_km_median")],
                           "backshear_km_median": (shp.get("backshear_km_median_p90") or [None])[0]}
        src, d = primary_sounding(cid)
        row["sounding"] = src
        row["env"] = {k: d.get(k) for k in ("mlcape", "mllcl_m_agl", "srh1", "srh3", "shear6_kt", "sr_anvil_kt")}
        row["anchor_why"] = runs.get(cid, {}).get("why")
        rows.append(row)

    L = ["# Reference storms: what they did", "",
         "Generated by `tools/case_outcomes.py`. Radar = `radar_lifecycle.py` (composite 40 dBZ cell, tops km above radar",
         "level); reports = SPC final database matched to the track (within 25 km of the storm at the report time); box =",
         "every SPC tornado within 60 km of the case anchor. Env = the primary sounding (inflow median where present). **Flags**",
         "mark tracks to check against the PNG (`radar/<case>_lifecycle.png`) before tuning to them.", ""]
    flagged = sum(1 for r in rows if r["flags"])
    L += [f"{len(rows)} cases, {flagged} flagged.", ""] + outcome_groups(rows)
    for p in PRESETS:
        pr = [r for r in rows if r["preset"] == p]
        if not pr:
            continue
        L += [f"## {p}", "", "| Case | Role | Radar life h (end) | Motion from° kt | Top50 km | A40 km² | Tor matched / box (max) | Hail in | "
              "Coldest top °C / OT share / anvil km | MLCAPE / LCL / SRH1 / S06 | Flags |", "|---|---|---|---|---|---|---|---|---|---|---|"]
        for r in sorted(pr, key=lambda r: (r["role"], r["case"])):
            mot = r.get("motion")
            e = r["env"]
            env = " / ".join("-" if e.get(k) is None else f"{e[k]}" for k in ("mlcape", "mllcl_m_agl", "srh1", "shear6_kt"))
            L.append(f"| {r['case']} | {r['role']} | {r.get('life_h', '-')} ({r.get('end', '-')}) | "
                     f"{f'{mot['from_deg']} / {mot['kt']}' if mot else '-'} | {r.get('top50_km', '-')} | {r.get('area40_km2', '-')} | "
                     f"{r.get('storm_tornado_reports', '-')} / {r['box_tornadoes']} ({r['box_max']}) | {r.get('storm_max_hail_in', '-')} | "
                     f"{sat(r.get('goes'))} | {env} | {'; '.join(r['flags'])} |")
        L.append("")
    (CASES / "outcomes.json").write_text(json.dumps(rows, indent=1))
    (CASES / "OUTCOMES.md").write_text("\n".join(L), encoding="utf-8")
    print(f"{len(rows)} cases, {flagged} flagged")
    for r in rows:
        if r["flags"]:
            print(f"  {r['case']:30s} " + "; ".join(r["flags"]))


if __name__ == "__main__":
    main()
