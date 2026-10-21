"""Run fetch_goes.py (anvil and overshooting tops) for every case from the GOES-16 era that has a radar track.

  python -I tools/run_case_goes.py [--only <id> ...] [--every 1] [--keep-raw]

Cases need radar/<case>_lifecycle.json (fetch_goes follows the radar track) and a date with GOES-East 1-min/5-min
imagery (catalog "goes16": May 2017 on; GOES-19 is used automatically from April 2025). Cases with a satellite JSON
are skipped. After each case the ABI files it downloaded are deleted (a few hundred MB a case) unless --keep-raw.
Writes validation/real-storm-cases/satellite/goes_runs.json (outcome per case).
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
SAT = CASES / "satellite"
RAW = ROOT / "data" / "raw" / "goes"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*"); ap.add_argument("--every", type=float, default=1)
    ap.add_argument("--keep-raw", action="store_true")
    a = ap.parse_args()
    cases = [c for c in json.loads((CASES / "catalog.json").read_text())["cases"]
             if (not a.only or c["id"] in a.only) and c["sources"]["goes16"]
             and (CASES / "radar" / f"{c['id']}_lifecycle.json").exists()
             and (a.only or not (SAT / f"{c['id']}.json").exists())]
    print(f"{len(cases)} cases", flush=True)
    log_f = SAT / "goes_runs.json"
    log = json.loads(log_f.read_text()) if log_f.exists() else {}
    RAW.mkdir(parents=True, exist_ok=True)
    for c in cases:
        before = {p.name for p in RAW.iterdir()}
        r = subprocess.run([sys.executable, "-I", str(ROOT / "tools" / "fetch_goes.py"), "--case", c["id"], "--every", str(a.every)],
                           capture_output=True, text=True)
        out = (r.stdout + r.stderr).strip().splitlines()
        if r.returncode == 0 and (SAT / f"{c['id']}.json").exists():
            s = json.loads((SAT / f"{c['id']}.json").read_text())["summary"]
            msg = f"ok: coldest {s.get('coldest_c')} C, first -50 C top {s.get('first_echo_to_minus50C_min')} min after first echo"
        else:
            msg = "failed: " + " | ".join(out[-2:])
        log[c["id"]] = msg
        log_f.write_text(json.dumps(log, indent=1))
        print(f"[{c['id']}] {msg}", flush=True)
        if not a.keep_raw:
            for p in RAW.iterdir():
                if p.name not in before:
                    p.unlink(missing_ok=True)
    print("done")


if __name__ == "__main__":
    main()
