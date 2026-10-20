"""Build the reference case library from validation/real-storm-cases/case_seed.json.

  python -I tools/case_catalog.py [--radius-km 60 --pad-h 1]

For every seed case (anchor lat/lon + storm window in UTC) this adds, from local files only:
  - the official SPC record (data/raw/spc, Storm Data): every tornado that started within --radius-km of the anchor
    inside the window +-pad, with rating, path, width, start/end; the case's tornado count, max EF, longest track;
    and hail/wind report counts and maxima in the same box. Years past the database are marked "prelim only".
  - radars for tools/fetch_nexrad.py / radar_lifecycle.py: WSR-88D sites 30-150 km from the anchor, best first
    (a storm closer than ~30 km sits in the cone of silence; beyond ~150 km the beam overshoots the low levels)
  - the nearest active IGRA balloon stations (for tools/fetch_sounding.py igra)
  - which model analysis can give the proximity sounding: HRRR (2014-07-30+), RAP (2012-05+), else RUC
  - whether dual-pol (2013+) and GOES-16 1-min imagery (2017+) exist for it

Writes validation/real-storm-cases/catalog.json and validation/real-storm-cases/CASES.md.
Station lists are cached in data/raw/stations (NCEI HOMR nexrad-stations.txt, IGRA2 station list).
"""
import argparse
import csv
import io
import json
import math
import zipfile
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
SPC = ROOT / "data" / "raw" / "spc"
STN = ROOT / "data" / "raw" / "stations"
SPC_LAST_YEAR = 2025
PRESET_ORDER = ["plains_lp", "plains_classic", "plains_hp", "dixie_classic", "dixie_hp",
                "midwest_classic", "midwest_hp", "minisupercell"]


def km(lat1, lon1, lat2, lon2):
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp, dl = p2 - p1, math.radians(lon2 - lon1)
    a = math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 6371.0 * 2 * math.asin(math.sqrt(a))


def spc_time(row):
    """SPC database time to UTC. tz 3 = CST (all modern records), 9 = GMT."""
    t = datetime.strptime(f"{row['date']} {row['time']}", "%Y-%m-%d %H:%M:%S")
    return t + timedelta(hours=6) if row["tz"] == "3" else t


def load_spc():
    def rows(name):
        p = SPC / name
        if p.suffix == ".zip":
            with zipfile.ZipFile(p) as z:
                text = z.read(z.namelist()[0]).decode("utf-8", "replace")
        else:
            text = p.read_text(encoding="utf-8", errors="replace")
        return list(csv.DictReader(io.StringIO(text)))
    return {"torn": rows("1950-2025_actual_tornadoes.csv"), "hail": rows("1955-2025_hail.csv.zip"),
            "wind": rows("1955-2025_wind.csv.zip")}


def load_radars():
    out = []
    for line in (STN / "nexrad-stations.txt").read_text().splitlines()[2:]:
        if "NEXRAD" not in line[140:]:
            continue
        out.append({"site": line[9:13], "name": line[20:50].strip(), "lat": float(line[106:115]), "lon": float(line[116:126])})
    return out


def load_igra():
    out = []
    for line in (STN / "igra2-station-list.txt").read_text().splitlines():
        if not line.startswith("USM"):
            continue
        out.append({"id": line[0:11], "lat": float(line[12:20]), "lon": float(line[21:30]), "name": line[41:71].strip(),
                    "first": int(line[72:76]), "last": int(line[77:81])})
    return out


def ef(mag):
    return "EFU" if mag in ("-9", "") else f"EF{mag}"


def build(case, spc, radars, igra, radius_km, pad_h):
    lat, lon = case["anchor"]
    t0 = datetime.fromisoformat(case["window_utc"][0]) - timedelta(hours=pad_h)
    t1 = datetime.fromisoformat(case["window_utc"][1]) + timedelta(hours=pad_h)
    year = datetime.fromisoformat(case["window_utc"][0]).year
    c = dict(case)

    if year > SPC_LAST_YEAR:
        c["spc"] = {"status": f"prelim only: the final database ends in {SPC_LAST_YEAR}; use tools/spc_reports.py (daily reports)"}
    else:
        tors, seen = [], set()
        for r in spc["torn"]:
            if int(r["yr"]) != year and int(r["yr"]) != t1.year:
                continue
            if r["sg"] == "2":          # county segments: keep the whole-track record only
                continue
            t = spc_time(r)
            if not (t0 <= t <= t1):
                continue
            d = km(lat, lon, float(r["slat"]), float(r["slon"]))
            if d > radius_km or (r["om"], r["yr"]) in seen:
                continue
            seen.add((r["om"], r["yr"]))
            tors.append({"utc": t.strftime("%Y-%m-%dT%H:%M"), "rating": ef(r["mag"]), "state": r["st"],
                         "start": [float(r["slat"]), float(r["slon"])],
                         "end": [float(r["elat"]), float(r["elon"])] if float(r["elat"]) != 0 else None,
                         "length_mi": float(r["len"]), "width_yd": int(float(r["wid"])), "fatalities": int(r["fat"]),
                         "km_from_anchor": round(d, 1)})
        tors.sort(key=lambda x: x["utc"])

        def reports(kind):
            n, best = 0, 0.0
            for r in spc[kind]:
                if int(r["yr"]) not in (year, t1.year):
                    continue
                t = spc_time(r)
                if t0 <= t <= t1 and km(lat, lon, float(r["slat"]), float(r["slon"])) <= radius_km:
                    n += 1
                    best = max(best, float(r["mag"] or 0))
            return n, best
        nh, hmax = reports("hail")
        nw, wmax = reports("wind")
        rated = [int(x["rating"][2]) for x in tors if x["rating"] != "EFU"]
        c["spc"] = {"status": "SPC final database (Storm Data)",
                    "tornadoes": len(tors),
                    "max_rating": (f"EF{max(rated)}" if rated else ("EFU" if tors else "none")),
                    "longest_track_mi": max((x["length_mi"] for x in tors), default=0),
                    "widest_yd": max((x["width_yd"] for x in tors), default=0),
                    "hail_reports": nh, "max_hail_in": hmax, "wind_reports": nw, "max_wind_kt": wmax,
                    "tornado_list": tors}

    rs = sorted(({"site": r["site"], "name": r["name"], "km": round(km(lat, lon, r["lat"], r["lon"]), 1)} for r in radars),
                key=lambda r: r["km"])
    c["radars"] = [r for r in rs if 30 <= r["km"] <= 150][:3] or rs[:2]
    st = sorted(({"id": s["id"], "name": s["name"], "km": round(km(lat, lon, s["lat"], s["lon"]), 1)} for s in igra
                 if s["first"] <= year <= s["last"]), key=lambda s: s["km"])
    c["balloons"] = [b for b in st if b["km"] <= 400][:4] or st[:1]

    when = datetime.fromisoformat(case["window_utc"][0])
    c["sources"] = {
        "model_sounding": "hrrr" if when >= datetime(2014, 7, 30) else ("rap" if when >= datetime(2012, 5, 9) else "ruc (fetch_sounding needs a RUC reader)"),
        "dual_pol": when >= datetime(2013, 6, 1),
        "goes16": when >= datetime(2017, 5, 1),
    }
    snd = sorted(p.name for p in (ROOT / "validation" / "soundings").glob(f"{case['id']}_*.json"))
    rad = sorted(p.name for p in (CASES / "radar").glob(f"{case['id']}_*.json"))
    sat = (CASES / "satellite" / f"{case['id']}.json").exists()
    c["have"] = {"soundings": snd, "radar": rad, "satellite": sat}
    return c


def check_role(c):
    """Flag seeds whose role disagrees with the official record."""
    s = c["spc"]
    if "tornadoes" not in s:
        return None
    if c["role"] == "null" and s["tornadoes"]:
        return f"seeded as null but SPC lists {s['tornadoes']} tornado(es) within the box: confirm they were other storms"
    if c["role"] in ("calibrate", "holdout") and not c["id"].startswith("lp_") and s["tornadoes"] == 0:
        return "no SPC tornado in the box: check the anchor/window"
    return None


def markdown(cat):
    L = ["# Reference case library", "",
         "Generated by `tools/case_catalog.py` from `case_seed.json`. Official tornado facts are from the SPC final",
         "database (Storm Data), never recall; EF ratings since 2014 run conservative at the high end. Roles: **calibrate**",
         "(tune against it), **holdout** (score only, never tune), **null** (a supercell with no tornado: the genesis model",
         "must say no), **boundary** (outflow-boundary or marginal genesis). Have: S = soundings, R = radar, G = GOES.", ""]
    by = {}
    for c in cat:
        by.setdefault(c["preset"], []).append(c)
    roles = {}
    for c in cat:
        roles[c["role"]] = roles.get(c["role"], 0) + 1
    L.append("**" + f"{len(cat)} cases: " + ", ".join(f"{v} {k}" for k, v in roles.items()) + "**")
    L.append("")
    for p in PRESET_ORDER:
        if p not in by:
            continue
        L += [f"## {p}", "", "| Case | Role | Chase | SPC tornadoes / max / longest mi / widest yd | Hail max in | Radar (km) | Balloon | Model | Have | Notes |",
              "|---|---|---|---|---|---|---|---|---|---|"]
        for c in sorted(by[p], key=lambda c: (c["role"], c["id"])):
            s = c["spc"]
            spc = (f"{s['tornadoes']} / {s['max_rating']} / {s['longest_track_mi']:g} / {s['widest_yd']}"
                   if "tornadoes" in s else "prelim only")
            hail = f"{s['max_hail_in']:g}" if "max_hail_in" in s else ""
            rad = ", ".join(f"{r['site']} ({r['km']:.0f})" for r in c["radars"])
            bal = c["balloons"][0]["id"][6:] + f" ({c['balloons'][0]['km']:.0f})" if c["balloons"] else ""
            h = c["have"]
            have = "".join(x for x, ok in (("S", h["soundings"]), ("R", h["radar"]), ("G", h["satellite"])) if ok) or "-"
            note = c["notes"] + (f" **Check:** {c['flag']}" if c.get("flag") else "")
            L.append(f"| {c['id']} | {c['role']} | {'yes' if c['chase'] else ''} | {spc} | {hail} | {rad} | {bal} | "
                     f"{c['sources']['model_sounding'].split()[0]} | {have} | {note} |")
        L.append("")
    return "\n".join(L)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--radius-km", type=float, default=60)
    ap.add_argument("--pad-h", type=float, default=1)
    a = ap.parse_args()
    seed = json.loads((CASES / "case_seed.json").read_text())["cases"]
    spc, radars, igra = load_spc(), load_radars(), load_igra()
    cat = []
    for case in seed:
        c = build(case, spc, radars, igra, a.radius_km, a.pad_h)
        flag = check_role(c)
        if flag:
            c["flag"] = flag
        cat.append(c)
        s = c["spc"]
        print(f"{c['id']:32s} {c['role']:9s} " + (f"{s['tornadoes']:3d} tor  max {s['max_rating']:4s}" if "tornadoes" in s else "prelim only     ")
              + f"  radars {','.join(r['site'] for r in c['radars'])}" + (f"  !! {flag}" if flag else ""))
    (CASES / "catalog.json").write_text(json.dumps({"generated_by": "tools/case_catalog.py", "radius_km": a.radius_km,
                                                     "pad_h": a.pad_h, "cases": cat}, indent=1))
    (CASES / "CASES.md").write_text(markdown(cat), encoding="utf-8")
    print(f"wrote {len(cat)} cases")


if __name__ == "__main__":
    main()
