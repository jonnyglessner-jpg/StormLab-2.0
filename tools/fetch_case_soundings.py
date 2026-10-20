"""Fetch the proximity soundings for every case in validation/real-storm-cases/catalog.json that lacks them.

  python -I tools/fetch_case_soundings.py [--only <id> ...] [--skip-igra]

Per case, two soundings through tools/fetch_sounding.py:
  - model: HRRR (2014-07-30+) or RAP (2012-05+) at the proximity point, the analysis hour before it
      tornadic cases: the start of the highest-rated tornado (earliest on ties), one hour earlier, rounded down
        (after Thompson et al. 2003: the storm's own outflow must not be in the column)
      tornado-free cases: the anchor, one hour after the window opens
  - balloon: the nearest IGRA station that launched (up to 4 within 400 km), 12Z if the window opens before 18Z, else the next 00Z
Cases needing RUC (before RAP) are skipped and listed at the end. Existing files are kept.
"""
import argparse
import json
import subprocess
import sys
from datetime import datetime, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CAT = ROOT / "validation" / "real-storm-cases" / "catalog.json"
SND = ROOT / "validation" / "soundings"


def proximity(c):
    tors = c["spc"].get("tornado_list") or []
    if tors:
        rank = lambda x: -1 if x["rating"] == "EFU" else int(x["rating"][2])
        best = sorted(tors, key=lambda x: (-rank(x), x["utc"]))[0]
        t = datetime.fromisoformat(best["utc"]) - timedelta(hours=1)
        return best["start"][0], best["start"][1], t.replace(minute=0), f"1 h before the {best['rating']} at {best['utc']}Z"
    t = datetime.fromisoformat(c["window_utc"][0]) + timedelta(hours=1)
    return c["anchor"][0], c["anchor"][1], t.replace(minute=0), "anchor, window start + 1 h"


def balloon_time(c):
    t = datetime.fromisoformat(c["window_utc"][0])
    if t.hour < 18:
        return t.replace(hour=12, minute=0)
    return (t + timedelta(days=1)).replace(hour=0, minute=0)


def run(args):
    print("  $ fetch_sounding.py " + " ".join(args), flush=True)
    r = subprocess.run([sys.executable, "-I", str(ROOT / "tools" / "fetch_sounding.py"), *args], capture_output=True, text=True)
    tail = (r.stdout + r.stderr).strip().splitlines()
    print("    " + ("ok: " + tail[0] if r.returncode == 0 and tail else "FAILED: " + " | ".join(tail[-3:])), flush=True)
    return r.returncode == 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--skip-igra", action="store_true")
    a = ap.parse_args()
    cases = json.loads(CAT.read_text())["cases"]
    out = ROOT / "validation" / "soundings" / "proximity_choices.json"
    old_log = json.loads(out.read_text()) if out.exists() else {}
    log, ruc, failed = {}, [], []
    for c in cases:
        if a.only and c["id"] not in a.only:
            continue
        src = c["sources"]["model_sounding"].split()[0]
        lat, lon, t, why = proximity(c)
        print(f"{c['id']}: {src} {t:%Y-%m-%dT%HZ} at {lat:.2f},{lon:.2f} ({why})", flush=True)
        entry = {"model": {"source": src, "valid": f"{t:%Y-%m-%dT%H}", "lat": lat, "lon": lon, "why": why}}
        if src == "ruc":
            ruc.append(c["id"])
        elif not (SND / f"{c['id']}_{src}.json").exists():
            if not run([src, "--lat", f"{lat:.3f}", "--lon", f"{lon:.3f}", "--time", f"{t:%Y-%m-%dT%H}", "--case", c["id"]]):
                failed.append(f"{c['id']} {src}")
        if not a.skip_igra and c["balloons"]:
            bt = balloon_time(c)
            if (SND / f"{c['id']}_igra.json").exists():
                entry["balloon"] = old_log.get(c["id"], {}).get("balloon")
            else:
                # nearest station first; fall back outward when a site did not launch at that hour
                for b in c["balloons"]:
                    if run(["igra", "--station", b["id"], "--time", f"{bt:%Y-%m-%dT%H}", "--case", c["id"]]):
                        entry["balloon"] = {"station": b["id"], "valid": f"{bt:%Y-%m-%dT%H}", "km": b["km"]}
                        break
                else:
                    failed.append(f"{c['id']} igra")
        log[c["id"]] = entry
    old_log.update(log)
    out.write_text(json.dumps(old_log, indent=1))
    print(f"\nneed RUC: {ruc}\nfailed: {failed}")


if __name__ == "__main__":
    main()
