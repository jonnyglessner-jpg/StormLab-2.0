"""Write the manifest the brain's `cases` validation suite reads: one flat record per case.

  python -I tools/brain_case_manifest.py

Unity's JsonUtility cannot read catalog.json / outcomes.json (nested dicts, nulls), so this flattens what the suite
needs into validation/real-storm-cases/brain_cases.json: the case id, preset and role, the sounding to start from (the
inflow sounding, else the point model sounding, else the balloon) and the observed outcome to score against. Missing
numbers are written as -999.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "validation" / "real-storm-cases"
SND = ROOT / "validation" / "soundings"
NA = -999.0


def num(v):
    return NA if v is None else float(v)


def main():
    rows = {r["case"]: r for r in json.loads((CASES / "outcomes.json").read_text())}
    out = []
    for c in json.loads((CASES / "catalog.json").read_text())["cases"]:
        r = rows[c["id"]]
        snd = next((f"{c['id']}_{s}.json" for s in ("hrrr_inflow", "rap_inflow", "hrrr", "rap", "igra")
                    if (SND / f"{c['id']}_{s}.json").exists()), None)
        if not snd:
            continue
        mot = r.get("motion") or {}
        g = r.get("goes") or {}
        e = r.get("env") or {}
        rating = r["box_max"]
        out.append({
            "id": c["id"], "preset": c["preset"], "role": c["role"], "sounding": snd,
            "tornadoes": int(r["box_tornadoes"]), "maxEF": -1 if rating in ("none", "prelim") else (0 if rating == "EFU" else int(rating[2])),
            "matchedTornadoes": int(r.get("storm_tornado_reports") or 0),
            "lifeH": num(r.get("life_h")), "end": r.get("end", ""),
            "motionFrom": num(mot.get("from_deg")), "motionKt": num(mot.get("kt")),
            "top50Km": num(r.get("top50_km")), "top18Km": num(r.get("top18_km")), "area40Km2": num(r.get("area40_km2")),
            "maxDbz": num(r.get("max_dbz")), "hailIn": num(r.get("storm_max_hail_in")),
            "coldestTopC": num(g.get("coldest_c")), "otFraction": num(g.get("ot_fraction")),
            "mlcape": num(e.get("mlcape")), "lclM": num(e.get("mllcl_m_agl")), "srh1": num(e.get("srh1")), "shear6Kt": num(e.get("shear6_kt")),
            "flags": "; ".join(r["flags"]),
        })
    (CASES / "brain_cases.json").write_text(json.dumps({"cases": out}, indent=1))
    print(f"{len(out)} cases -> {CASES / 'brain_cases.json'}")


if __name__ == "__main__":
    main()
