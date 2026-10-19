"""Tornado climatology from the SPC tornado database (1950-2025_actual_tornadoes.csv).

Writes data/derived/tornado_climatology.json: the numbers the game's tornado model is calibrated against.
  - path width and length percentiles per EF rating (EF era, 2007+; widths are max width since 1995)
  - the same split by region (Plains / Dixie / Midwest / other)
  - EF4-EF5 list with widths (for the rare 1 mile+ EF5)
  - start-hour distribution per region (Dixie is nocturnal, the Plains peak at 5-8 pm)
  - monthly distribution per region
  - cyclic-supercell statistics: tornadoes are chained into "families" (same storm) when the next one starts
    within CHAIN_MIN minutes of the previous one and within CHAIN_KM of where the previous one ended; the
    interval between successive tornadoes of a family is the storm's cycle time

Usage: python tools/spc_tornado_stats.py
"""
import json
import math
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "data" / "raw" / "spc" / "1950-2025_actual_tornadoes.csv"
OUT = ROOT / "data" / "derived" / "tornado_climatology.json"

REGIONS = {
    "plains": ["TX", "OK", "KS", "NE", "SD", "ND", "CO", "NM", "WY", "MT"],
    "dixie": ["MS", "AL", "TN", "GA", "AR", "LA", "KY", "SC", "NC", "FL"],
    "midwest": ["IA", "IL", "IN", "OH", "MO", "WI", "MI", "MN"],
}
CHAIN_MIN = 75      # minutes: max gap between successive tornadoes of one storm
CHAIN_KM = 45       # km: max distance from the previous tornado's end to the next one's start
PCT = [10, 25, 50, 75, 90, 95, 99]


def region_of(st):
    for r, states in REGIONS.items():
        if st in states:
            return r
    return "other"


def haversine_km(lat1, lon1, lat2, lon2):
    p = math.pi / 180
    a = 0.5 - math.cos((lat2 - lat1) * p) / 2 + math.cos(lat1 * p) * math.cos(lat2 * p) * (1 - math.cos((lon2 - lon1) * p)) / 2
    return 12742 * math.asin(math.sqrt(max(a, 0)))


def pct(series):
    s = pd.to_numeric(series, errors="coerce").dropna()
    s = s[s > 0]
    if len(s) == 0:
        return None
    return {"n": int(len(s)), "mean": round(float(s.mean()), 2), "max": float(s.max()),
            **{f"p{p}": round(float(np.percentile(s, p)), 1) for p in PCT}}


def main():
    df = pd.read_csv(RAW, dtype={"time": str, "date": str}, low_memory=False)
    df = df[df["sg"] == 1].copy()                      # one record per tornado (whole track)
    df["mag"] = pd.to_numeric(df["mag"], errors="coerce")
    df["region"] = df["st"].map(region_of)
    df["t"] = pd.to_datetime(df["date"] + " " + df["time"], errors="coerce")
    df["hour"] = df["t"].dt.hour
    rated = df[df["mag"] >= 0]
    ef = rated[rated["yr"] >= 2007]                    # EF-scale era
    wid_era = rated[rated["yr"] >= 1995]               # width = max width since 1995

    out = {"source": "SPC 1950-2025_actual_tornadoes.csv (one record per tornado, sg=1)",
           "regions": REGIONS, "chain_rule": {"max_gap_min": CHAIN_MIN, "max_distance_km": CHAIN_KM},
           "counts": {"all": int(len(df)), "ef_era_rated": int(len(ef))}}

    # --- width (yards) and length (miles) by rating ---
    out["by_rating"] = {}
    for m in range(6):
        sub = ef[ef["mag"] == m]
        out["by_rating"][f"EF{m}"] = {
            "n": int(len(sub)),
            "width_yd": pct(sub["wid"]), "length_mi": pct(sub["len"]),
            "width_yd_1995plus": pct(wid_era[wid_era["mag"] == m]["wid"]),
            "share_of_rated": round(len(sub) / len(ef), 4),
        }

    # --- by region ---
    out["by_region"] = {}
    for r in list(REGIONS) + ["other"]:
        sub = ef[ef["region"] == r]
        hours = sub["hour"].dropna().astype(int)
        hour_hist = [int((hours == h).sum()) for h in range(24)]
        months = sub["mo"].astype(int)
        out["by_region"][r] = {
            "n": int(len(sub)),
            "rating_share": {f"EF{m}": round(float((sub["mag"] == m).mean()), 4) for m in range(6)},
            "width_yd": {f"EF{m}": pct(sub[sub["mag"] == m]["wid"]) for m in range(6)},
            "length_mi": {f"EF{m}": pct(sub[sub["mag"] == m]["len"]) for m in range(6)},
            "start_hour_local_hist": hour_hist,
            "nocturnal_share_21_to_06": round(float(((hours >= 21) | (hours < 6)).mean()), 3) if len(hours) else None,
            "month_hist": [int((months == mo).sum()) for mo in range(1, 13)],
        }

    # --- violent tornadoes with widths ---
    viol = ef[ef["mag"] >= 4].sort_values("wid", ascending=False)
    out["violent_ef4_ef5"] = [
        {"date": d, "state": s, "ef": int(m), "width_yd": int(w), "length_mi": float(l), "fat": int(f)}
        for d, s, m, w, l, f in zip(viol["date"], viol["st"], viol["mag"], viol["wid"], viol["len"], viol["fat"])
    ]
    out["mile_plus_wide_count_ef_era"] = int((ef["wid"] >= 1760).sum())
    out["mile_plus_wide_by_rating"] = {f"EF{m}": int(((ef["wid"] >= 1760) & (ef["mag"] == m)).sum()) for m in range(6)}

    # --- path direction (bearing of start -> end) for tracks >= 5 mi, by region ---
    out["path_bearing_deg"] = {}
    for r in REGIONS:
        sub = ef[(ef["region"] == r) & (ef["len"] >= 5) & (ef["elat"] != 0)]
        dlat = (sub["elat"] - sub["slat"])
        dlon = (sub["elon"] - sub["slon"]) * np.cos(np.radians(sub["slat"]))
        brg = (np.degrees(np.arctan2(dlon, dlat)) + 360) % 360
        out["path_bearing_deg"][r] = {"n": int(len(brg)), "median": round(float(np.median(brg)), 1),
                                      "p25": round(float(np.percentile(brg, 25)), 1), "p75": round(float(np.percentile(brg, 75)), 1)}

    # --- cyclic supercell families ---
    fam_stats = {}
    chain = df[(df["yr"] >= 1995) & df["t"].notna()].copy()
    chain["key"] = chain["date"]
    for r in list(REGIONS) + ["all"]:
        sub = chain if r == "all" else chain[chain["region"] == r]
        intervals, sizes, max_rating, first_to_strongest = [], [], [], []
        for _, day in sub.groupby("key"):
            day = day.sort_values("t")
            rows = day.to_dict("records")
            fam = list(range(len(rows)))            # union-find parent

            def find(i):
                while fam[i] != i:
                    fam[i] = fam[fam[i]]
                    i = fam[i]
                return i

            for i in range(len(rows)):
                best, bestdt = None, None
                for j in range(i - 1, -1, -1):
                    dt = (rows[i]["t"] - rows[j]["t"]).total_seconds() / 60
                    if dt > CHAIN_MIN:
                        break
                    ej_lat = rows[j]["elat"] if rows[j]["elat"] != 0 else rows[j]["slat"]
                    ej_lon = rows[j]["elon"] if rows[j]["elon"] != 0 else rows[j]["slon"]
                    if haversine_km(ej_lat, ej_lon, rows[i]["slat"], rows[i]["slon"]) <= CHAIN_KM:
                        if best is None or dt < bestdt:
                            best, bestdt = j, dt
                if best is not None:
                    fam[find(i)] = find(best)
            groups = {}
            for i in range(len(rows)):
                groups.setdefault(find(i), []).append(rows[i])
            for g in groups.values():
                sizes.append(len(g))
                mags = [x["mag"] for x in g if x["mag"] >= 0]
                if mags:
                    max_rating.append(max(mags))
                    first_to_strongest.append(int(np.argmax(mags)))
                for a, b in zip(g, g[1:]):
                    intervals.append((b["t"] - a["t"]).total_seconds() / 60)
        sizes = np.array(sizes)
        iv = np.array([x for x in intervals if x > 0])
        fam_stats[r] = {
            "families": int(len(sizes)),
            "family_size_hist_1_to_10plus": [int((sizes == k).sum()) for k in range(1, 10)] + [int((sizes >= 10).sum())],
            "share_with_2plus": round(float((sizes >= 2).mean()), 3),
            "share_with_3plus": round(float((sizes >= 3).mean()), 3),
            "mean_size_given_2plus": round(float(sizes[sizes >= 2].mean()), 2) if (sizes >= 2).any() else None,
            "interval_min": {"n": int(len(iv)), **{f"p{p}": round(float(np.percentile(iv, p)), 1) for p in PCT}, "mean": round(float(iv.mean()), 1)} if len(iv) else None,
            "max_rating_hist_EF0_5": [int((np.array(max_rating) == m).sum()) for m in range(6)],
            "strongest_is_first_share": round(float((np.array(first_to_strongest) == 0).mean()), 3) if first_to_strongest else None,
        }
    out["cyclic_families_1995plus"] = fam_stats

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, indent=1))
    print(f"wrote {OUT}")

    # --- console summary ---
    print("\nEF era (2007+) width (yd) / length (mi) by rating:")
    for m in range(6):
        b = out["by_rating"][f"EF{m}"]
        w, l = b["width_yd"], b["length_mi"]
        print(f"  EF{m}: n={b['n']:5d}  width p50={w['p50']:6.0f} p90={w['p90']:6.0f} p99={w['p99']:6.0f} max={w['max']:5.0f}"
              f"   length p50={l['p50']:5.1f} p90={l['p90']:5.1f} p99={l['p99']:5.1f}")
    print(f"\n1 mile+ wide (EF era): {out['mile_plus_wide_count_ef_era']}  by rating {out['mile_plus_wide_by_rating']}")
    print("\nRegion: n, nocturnal share, EF2+ share, EF4+ share, EF2 width p50, path bearing median")
    for r in REGIONS:
        b = out["by_region"][r]
        ef2 = sum(b["rating_share"][f"EF{m}"] for m in range(2, 6))
        ef4 = sum(b["rating_share"][f"EF{m}"] for m in range(4, 6))
        w2 = b["width_yd"]["EF2"]["p50"] if b["width_yd"]["EF2"] else None
        print(f"  {r:8s} n={b['n']:5d} night={b['nocturnal_share_21_to_06']:.2f} EF2+={ef2:.3f} EF4+={ef4:.4f} EF2 w50={w2} brg={out['path_bearing_deg'][r]['median']}")
    print("\nCyclic families (1995+):")
    for r, f in fam_stats.items():
        iv = f["interval_min"]
        print(f"  {r:8s} families={f['families']:6d} 2+={f['share_with_2plus']:.3f} 3+={f['share_with_3plus']:.3f} "
              f"size|2+={f['mean_size_given_2plus']}  interval p25/50/75={iv['p25']}/{iv['p50']}/{iv['p75']} min  hist={f['family_size_hist_1_to_10plus']}")


if __name__ == "__main__":
    main()
