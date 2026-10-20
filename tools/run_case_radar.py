"""Run the radar life cycle and SPC report match for every case in catalog.json that has none yet.

  python -I tools/run_case_radar.py [--only <id> ...] [--workers 3] [--keep-volumes]

Per case:
  1. where the storm is at one known moment (the anchor for radar_lifecycle.py):
       tornadic: the strongest tornado's start time and point (the core is within ~10 km of it)
       tornado-free: the >= 55 dBZ cell (else >= 45) that passed closest to the case's town, scanning the window +-1 h
         every 20 min (scan_nexrad.py). Check these in the PNG: the closest strong cell is not always the chased one.
  2. radar_lifecycle.py on the first radar whose archive covers the storm (catalog radars, then any within 230 km;
     outages and unreadable files happen: up to three radars are tried), --hours either side (default 4), first-guess motion = the case's model-sounding
     Bunkers right-mover
  3. spc_reports.py on the resulting track
  4. deletes the Level II volumes this case downloaded (about 1 GB a case; the JSON keeps what is needed), unless
     --keep-volumes. Runs cases in parallel, never two on the same radar at once.
Writes validation/real-storm-cases/radar/lifecycle_runs.json (the anchor used per case and the outcome).
"""
import argparse
import json
import re
import subprocess
import sys
import threading
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
RADAR = CASES / "radar"
SND = ROOT / "validation" / "soundings"
RAW = ROOT / "data" / "raw" / "nexrad"
TOOLS = ROOT / "tools"
site_locks, print_lock = {}, threading.Lock()
HOURS = 4


def say(cid, msg):
    with print_lock:
        print(f"[{cid}] {msg}", flush=True)


def py(*args):
    r = subprocess.run([sys.executable, "-I", *map(str, args)], capture_output=True, text=True)
    return r.returncode, r.stdout + r.stderr


def bunkers(cid):
    for src in ("hrrr", "rap", "igra"):
        f = SND / f"{cid}_{src}.json"
        if f.exists():
            b = json.loads(f.read_text())["diagnostics"].get("bunkers_rm_from_deg_kt")
            if b:
                return b
    return [240, 20]


def anchor(c, site):
    tors = c["spc"].get("tornado_list") or []
    if tors:
        rank = lambda x: -1 if x["rating"] == "EFU" else int(x["rating"][2])  # noqa: E731
        t = sorted(tors, key=lambda x: (-rank(x), x["utc"]))[0]
        return t["utc"], t["start"][0], t["start"][1], f"{t['rating']} tornado start"
    # the moment a strong cell passed closest to the case's town (chase folders are named for where the storm was)
    t0 = (datetime.fromisoformat(c["window_utc"][0]) - timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M")
    t1 = (datetime.fromisoformat(c["window_utc"][1]) + timedelta(hours=1)).strftime("%Y-%m-%dT%H:%M")
    code, out = py(TOOLS / "scan_nexrad.py", "--site", site, "--start", t0, "--end", t1, "--lat", c["anchor"][0],
                   "--lon", c["anchor"][1], "--radius-km", 50, "--every", 20)
    if code:
        return None
    day = datetime.fromisoformat(c["window_utc"][0])
    cand = []
    for line in out.splitlines():
        m = re.match(r"(\d\d):(\d\d)\s", line)
        if not m:
            continue
        t = day.replace(hour=int(m.group(1)), minute=int(m.group(2)))
        if t < day - timedelta(hours=2):
            t += timedelta(days=1)
        for dbz, la, lo, dist in re.findall(r"(\d+) dBZ at ([\d.]+),(-[\d.]+) \(\s*(\d+) km", line):
            cand.append((int(dbz), float(dist), t, float(la), float(lo)))
    for floor in (55, 45):
        strong = [x for x in cand if x[0] >= floor]
        if strong:
            dbz, dist, t, la, lo = min(strong, key=lambda x: x[1])
            return t.strftime("%Y-%m-%dT%H:%M"), la, lo, f"{dbz} dBZ cell {dist:.0f} km from the town at its closest pass"
    return None


def covered(site, t):
    """True when the archive holds volumes from 2 h before to 1 h after t (radars have outages: KMAF 2021-05-17)."""
    sys.path.insert(0, str(TOOLS))
    from fetch_nexrad import list_volumes  # noqa: PLC0415
    t = datetime.fromisoformat(t)
    vs = [v[0] for d in {(t - timedelta(hours=2)).date(), (t + timedelta(hours=1)).date()}
          for v in list_volumes(site, datetime.combine(d, datetime.min.time()))]
    near = lambda a, b: any(a <= v <= b for v in vs)  # noqa: E731
    return near(t - timedelta(hours=2), t - timedelta(minutes=90)) and near(t - timedelta(minutes=10), t + timedelta(minutes=10))


def candidate_sites(c):
    """The catalog's radars first, then every other WSR-88D within 230 km, nearest first, that covers the storm."""
    sys.path.insert(0, str(TOOLS))
    from case_catalog import km, load_radars  # noqa: PLC0415
    t = c["window_utc"][0]
    tors = c["spc"].get("tornado_list") or []
    if tors:
        t = tors[0]["utc"]
    names = [r["site"] for r in c["radars"]]
    near = sorted(load_radars(), key=lambda r: km(c["anchor"][0], c["anchor"][1], r["lat"], r["lon"]))
    names += [r["site"] for r in near if r["site"] not in names and km(c["anchor"][0], c["anchor"][1], r["lat"], r["lon"]) <= 230]
    for site in names:
        try:
            if covered(site, t):
                yield site
        except Exception:  # noqa: BLE001  (a listing error: try the next site)
            continue


def one(c, keep):
    cid, rec = c["id"], {"tried": []}
    for site in candidate_sites(c):
        rec["tried"].append(site)
        ok = run_site(c, site, rec, keep)
        if ok is not None:          # success, or a failure that another radar will not fix (no storm found)
            return cid, rec
        if len(rec["tried"]) >= 3:
            break
    rec.setdefault("outcome", "no radar in the archive covers the storm")
    say(cid, rec["outcome"])
    return cid, rec


def run_site(c, site, rec, keep):
    """True on success, False when no storm was found, None when this radar's data failed (try the next one)."""
    cid = c["id"]
    with site_locks.setdefault(site, threading.Lock()):
        before = {p.name for p in RAW.glob(f"{site}*")}
        rec["site"] = site
        try:
            a = anchor(c, site)
            if not a:
                say(cid, f"{site}: no storm found near the anchor")
                rec["outcome"] = "no storm found near the anchor"
                return False
            t, lat, lon, why = a
            frm, kt = bunkers(cid)
            rec.update({"anchor_utc": t, "anchor_latlon": [lat, lon], "why": why, "first_guess_motion": [frm, kt]})
            say(cid, f"{site} anchor {t}Z {lat:.2f},{lon:.2f} ({why})")
            code, out = py(TOOLS / "radar_lifecycle.py", "--case", cid, "--site", site, "--anchor", t[:16], "--lat", lat,
                           "--lon", lon, "--hours-before", HOURS, "--hours-after", HOURS, "--motion", f"{frm},{kt}")
            if code:
                rec["outcome"] = f"{site} lifecycle failed: " + " | ".join(out.strip().splitlines()[-2:])
                say(cid, rec["outcome"])
                return None
            s = json.loads((RADAR / f"{cid}_lifecycle.json").read_text())["summary"]
            rec["outcome"] = f"{s['lifetime_h']} h, ended {s['end_reason']}"
            say(cid, "lifecycle " + rec["outcome"])
            code, out = py(TOOLS / "spc_reports.py", "--case", cid)
            rec["spc_reports"] = "ok" if code == 0 else "failed: " + out.strip().splitlines()[-1]
            return True
        finally:
            if not keep:
                for p in RAW.glob(f"{site}*"):
                    if p.name not in before:
                        p.unlink(missing_ok=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*"); ap.add_argument("--workers", type=int, default=3)
    ap.add_argument("--keep-volumes", action="store_true")
    ap.add_argument("--hours", type=float, default=4, help="search this long either side of the anchor")
    a = ap.parse_args()
    global HOURS
    HOURS = a.hours
    cases = [c for c in json.loads((CASES / "catalog.json").read_text())["cases"]
             if (not a.only or c["id"] in a.only) and c["radars"]
             and (a.only or not (RADAR / f"{c['id']}_lifecycle.json").exists())]
    print(f"{len(cases)} cases")
    log_f = RADAR / "lifecycle_runs.json"
    log = json.loads(log_f.read_text()) if log_f.exists() else {}
    with ThreadPoolExecutor(a.workers) as ex:
        for cid, rec in ex.map(lambda c: one(c, a.keep_volumes), cases):
            log[cid] = rec
            log_f.write_text(json.dumps(log, indent=1))
    print("done")


if __name__ == "__main__":
    main()
