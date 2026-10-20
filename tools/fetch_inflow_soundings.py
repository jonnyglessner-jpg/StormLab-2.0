"""Inflow soundings: sample the storm's own inflow from the model analysis, positioned with its radar track.

  python -I tools/fetch_inflow_soundings.py [--only <id> ...] [--km 35 --veer 105 --spacing 15] [--redo]

A single grid point an hour before the tornado can sit in the storm's forward-flank rain or on the dry side of a
boundary (2026-10-20: Lewistown came out at 326 J/kg and a 2.2 km LCL). Instead, per case:
  - time T: an hour before the strongest tornado's start; for tornado-free cases, an hour before the radar's
    "mature" time (highest 50 dBZ top). Analysis hour = T rounded down.
  - the storm's radar position at T (validation/real-storm-cases/radar/<case>_lifecycle.json; the nearest volume)
  - the inflow point: --km upstream of the storm along its storm-relative 0-1 km wind (the air the updraft ingests
    comes from there): the 0-1 km mean wind of the case's point sounding minus the radar storm motion. Falls back to
    the storm's heading + --veer degrees (right of motion and ahead) when there is no point sounding
  - five grid points: the inflow point and four more --spacing km to the N, E, S, W of it, from one analysis file
  - the member with the median MLCAPE is the case's inflow sounding; the five-member spread is kept beside it
    (min/max of each diagnostic) so a reader can see how sharp the gradients were

Writes validation/soundings/<case>_<hrrr|rap>_inflow.json (same format as fetch_sounding.py plus "inflow" and
"spread"). Cases without a radar track, or needing RUC, are skipped.
"""
import argparse
import json
import math
import sys
from datetime import datetime, timedelta
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))   # so it also runs under python -I
import fetch_sounding as fs  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
SND = ROOT / "validation" / "soundings"
KEYS = ["mlcape", "mlcin", "mllcl_m_agl", "el_m_agl", "lapse_700_500_C_per_km", "srh500", "srh1", "srh3", "shear6_kt",
        "sr_anvil_kt", "sr_mid_kt"]


def offset(lat, lon, km, bearing):
    b = math.radians(bearing)
    return (lat + km * math.cos(b) / 111.2, lon + km * math.sin(b) / (111.2 * math.cos(math.radians(lat))))


def proximity_time(c, life):
    tors = c["spc"].get("tornado_list") or []
    if tors:
        rank = lambda x: -1 if x["rating"] == "EFU" else int(x["rating"][2])  # noqa: E731
        best = sorted(tors, key=lambda x: (-rank(x), x["utc"]))[0]
        return datetime.fromisoformat(best["utc"]) - timedelta(hours=1), f"1 h before the {best['rating']} at {best['utc']}Z"
    m = datetime.fromisoformat(life["summary"]["mature_utc"])
    return m - timedelta(hours=1), f"1 h before radar maturity at {m:%Y-%m-%dT%H:%M}Z"


def inflow_bearing(cid, src, heading, speed_kmh, veer):
    """Bearing from the storm to where its 0-1 km storm-relative inflow comes from."""
    f = SND / f"{cid}_{src}.json"
    if not f.exists():
        return (heading + veer) % 360, f"heading + {veer:g} (no point sounding)"
    L = json.loads(f.read_text())["levels"]
    z0 = L[0]["z"]
    low = [x for x in L if x["z"] - z0 <= 1000]
    u = sum(-x["spd_kt"] * 0.514444 * math.sin(math.radians(x["dir"])) for x in low) / len(low)
    v = sum(-x["spd_kt"] * 0.514444 * math.cos(math.radians(x["dir"])) for x in low) / len(low)
    su = speed_kmh / 3.6 * math.sin(math.radians(heading)); sv = speed_kmh / 3.6 * math.cos(math.radians(heading))
    ru, rv = u - su, v - sv
    return (math.degrees(math.atan2(-ru, -rv)) + 360) % 360, f"upstream of the 0-1 km storm-relative wind ({math.hypot(ru, rv) / 0.514444:.0f} kt)"


def storm_at(life, t):
    vols = life["volumes"]
    v = min(vols, key=lambda v: abs((datetime.fromisoformat(v["utc"]) - t).total_seconds()))
    mot = v.get("motion") or life["summary"].get("motion_mean")
    heading = ((mot["from_deg"] + 180) % 360) if mot else 60.0
    return v["latlon"], heading, (mot["kmh"] if mot else 30.0), v["utc"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*"); ap.add_argument("--redo", action="store_true")
    ap.add_argument("--km", type=float, default=35); ap.add_argument("--veer", type=float, default=105)
    ap.add_argument("--spacing", type=float, default=15)
    a = ap.parse_args()
    cases = json.loads((CASES / "catalog.json").read_text())["cases"]
    skipped = []
    for c in cases:
        if a.only and c["id"] not in a.only:
            continue
        src = c["sources"]["model_sounding"].split()[0]
        lf = CASES / "radar" / f"{c['id']}_lifecycle.json"
        out = SND / f"{c['id']}_{src}_inflow.json"
        if src == "ruc" or not lf.exists():
            skipped.append(f"{c['id']} ({'ruc' if src == 'ruc' else 'no radar track'})")
            continue
        if out.exists() and not a.redo:
            continue
        life = json.loads(lf.read_text())
        t, why = proximity_time(c, life)
        (slat, slon), heading, speed, vt = storm_at(life, t)
        bearing, how = inflow_bearing(c["id"], src, heading, speed, a.veer)
        ilat, ilon = offset(slat, slon, a.km, bearing)
        pts = [(ilat, ilon)] + [offset(ilat, ilon, a.spacing, b) for b in (0, 90, 180, 270)]
        hour = t.replace(minute=0, second=0)
        print(f"{c['id']}: {src} {hour:%Y-%m-%dT%HZ}, storm at {slat:.2f},{slon:.2f} ({vt}Z) heading {heading:.0f}; "
              f"inflow {ilat:.2f},{ilon:.2f} (bearing {bearing:.0f}, {how})", flush=True)
        try:
            grib = fs.grib_hrrr(hour) if src == "hrrr" else fs.grib_rap(hour)
            members = []
            for (la, lo), fields in zip(pts, fs._decode_points(grib, pts)):
                snd = {"levels": fs._levels_from_fields(fields)}
                members.append(((la, lo), snd, fs.diagnose(snd)))
        except Exception as e:  # noqa: BLE001
            print(f"  FAILED: {e!r}", flush=True)
            skipped.append(f"{c['id']} (failed)")
            continue
        ok = [m for m in members if m[2].get("mlcape") is not None]
        if not ok:
            print("  FAILED: no member diagnosed", flush=True)
            skipped.append(f"{c['id']} (no diagnostics)")
            continue
        ok.sort(key=lambda m: m[2]["mlcape"])
        (la, lo), snd, d = ok[len(ok) // 2]
        spread = {k: [min(v), max(v)] for k in KEYS if (v := [m[2][k] for m in ok if m[2].get(k) is not None])}
        name = "HRRR 3 km" if src == "hrrr" else "RAP 13 km"
        snd.update({"source": f"{name} analysis {hour:%Y-%m-%d %HZ}, inflow member nearest {la:.3f},{lo:.3f}",
                    "case": c["id"], "valid_utc": hour.isoformat(), "diagnostics": d,
                    "inflow": {"why": why, "storm_latlon": [slat, slon], "storm_volume_utc": vt, "heading_deg": round(heading),
                               "offset_km": a.km, "offset_bearing_deg": round(bearing), "offset_how": how,
                               "members": [[round(m[0][0], 3), round(m[0][1], 3), m[2].get("mlcape")] for m in members],
                               "picked": "median MLCAPE of the members"},
                    "spread": spread})
        out.write_text(json.dumps(snd, indent=1))
        print(f"  MLCAPE {d['mlcape']} (members {spread.get('mlcape')}), LCL {d.get('mllcl_m_agl')}, SRH1 {d.get('srh1')} "
              f"(members {spread.get('srh1')}), S06 {d.get('shear6_kt')}", flush=True)
    print("\nskipped: " + ", ".join(skipped))


if __name__ == "__main__":
    main()
