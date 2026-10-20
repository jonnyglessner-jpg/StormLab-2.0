"""SPC hail, wind and tornado reports near a storm's radar track.

  python tools/spc_reports.py --case lp_earth_20210516 [--radius-km 50 --pad-min 30]

Reads the track from validation/real-storm-cases/radar/<case>_lifecycle.json (radar_lifecycle.py) and keeps every report within
--radius-km of the track and within the storm's life +-pad. Two sources:
  - the final SPC database (Storm Data, the official record): data/raw/spc/1955-2025_hail.csv.zip, _wind.csv.zip and
    1950-2025_actual_tornadoes.csv (downloaded from https://www.spc.noaa.gov/wcm/data/ when missing)
  - the preliminary daily storm reports (https://www.spc.noaa.gov/climo/reports/YYMMDD_rpts_filtered.csv), which
    carry the spotter comments; times there are UTC
Each report gets its distance to the nearest point of the track and to the storm's position at the report time
("at_time"): a report far from the storm at that moment probably belongs to a neighbouring storm.

Writes validation/real-storm-cases/radar/<case>_spc_reports.json.
"""
import argparse
import csv
import io
import json
import urllib.error
import urllib.request
import zipfile
from datetime import datetime, timedelta
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
SPC = ROOT / "data" / "raw" / "spc"
DER = ROOT / "validation" / "real-storm-cases" / "radar"
WCM = "https://www.spc.noaa.gov/wcm/data/"
DAILY = "https://www.spc.noaa.gov/climo/reports/"
R_E = 6371.0


def km(lat1, lon1, lat2, lon2):
    p1, p2 = np.radians(lat1), np.radians(lat2)
    a = np.sin((p2 - p1) / 2) ** 2 + np.cos(p1) * np.cos(p2) * np.sin(np.radians(lon2 - lon1) / 2) ** 2
    return 2 * R_E * np.arcsin(np.sqrt(a))


def final_db(kind):
    name = "1950-2025_actual_tornadoes.csv" if kind == "tornado" else f"1955-2025_{kind}.csv.zip"
    f = SPC / name
    if not f.exists():
        SPC.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(WCM + name, f)
    if f.suffix == ".zip":
        z = zipfile.ZipFile(f)
        return list(csv.DictReader(io.TextIOWrapper(z.open(z.namelist()[0]), encoding="utf-8")))
    return list(csv.DictReader(open(f, encoding="utf-8")))


def db_time_utc(r):
    t = datetime.strptime(r["date"] + " " + r["time"], "%Y-%m-%d %H:%M:%S")
    tz = r["tz"].strip()
    if tz == "3":            # CST
        return t + timedelta(hours=6)
    if tz == "9":            # GMT
        return t
    return t + timedelta(hours=6)   # 0 / unknown: SPC treats these as CST too


def daily(day):
    """Preliminary reports for the SPC day starting 12Z on `day` (times UTC)."""
    try:
        url = f"{DAILY}{day:%y%m%d}_rpts_filtered.csv"
        txt = urllib.request.urlopen(url, timeout=60).read().decode("latin-1").splitlines()
    except urllib.error.HTTPError:   # older days (2011 and before) only have the unfiltered file
        url = f"{DAILY}{day:%y%m%d}_rpts.csv"
        txt = urllib.request.urlopen(url, timeout=60).read().decode("latin-1").splitlines()
    out, kind = [], None
    for line in txt:
        if line.startswith("Time,"):
            kind = {"F_Scale": "tornado", "F-Scale": "tornado", "Speed": "wind", "Size": "hail"}[line.split(",")[1]]
            continue
        row = next(csv.reader([line]))
        if len(row) < 8:
            continue
        hhmm = int(row[0])
        t = day.replace(hour=hhmm // 100, minute=hhmm % 100)
        if hhmm < 1200:
            t += timedelta(days=1)
        out.append({"kind": kind, "utc": t, "mag": row[1], "location": f"{row[2]}, {row[3]} {row[4]}",
                    "lat": float(row[5]), "lon": float(row[6]), "comments": row[7]})
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--case", required=True); ap.add_argument("--radius-km", type=float, default=50)
    ap.add_argument("--pad-min", type=float, default=30)
    a = ap.parse_args()
    lc = json.loads((DER / f"{a.case}_lifecycle.json").read_text())
    vols = lc["volumes"]
    tt = [datetime.fromisoformat(v["utc"]) for v in vols]
    la = np.array([v["latlon"][0] for v in vols]); lo = np.array([v["latlon"][1] for v in vols])
    t0, t1 = tt[0] - timedelta(minutes=a.pad_min), tt[-1] + timedelta(minutes=a.pad_min)
    secs = np.array([(t - tt[0]).total_seconds() for t in tt])

    def where(t, lat, lon):
        d_track = float(km(lat, lon, la, lo).min())
        s = (t - tt[0]).total_seconds()
        plat, plon = np.interp(s, secs, la), np.interp(s, secs, lo)
        return round(d_track, 1), round(float(km(lat, lon, plat, plon)), 1)

    final = []
    for kind in ("hail", "wind", "tornado"):
        for r in final_db(kind):
            if not (t0.year <= int(r["yr"]) <= t1.year):
                continue
            t = db_time_utc(r)
            if not (t0 <= t <= t1):
                continue
            lat, lon = float(r["slat"]), float(r["slon"])
            d_track, d_now = where(t, lat, lon)
            if d_track > a.radius_km:
                continue
            e = {"kind": kind, "utc": t.isoformat(), "lat": lat, "lon": lon, "state": r["st"],
                 "km_from_track": d_track, "km_from_storm_at_time": d_now}
            if kind == "hail":
                e["size_in"] = float(r["mag"])
            elif kind == "wind":
                e["gust_kt"] = float(r["mag"]); e["measured"] = r.get("mt", "").strip() in ("MG", "MS")
            else:
                e["ef"] = "EFU" if int(r["mag"]) < 0 else int(r["mag"]); e["length_mi"] = float(r["len"]); e["width_yd"] = float(r["wid"])
            final.append(e)
    prelim = []
    days = {(t0 - timedelta(hours=12)).date(), (t1 - timedelta(hours=12)).date()}
    for d in sorted(days):
        for r in daily(datetime(d.year, d.month, d.day, 12)):
            if not (t0 <= r["utc"] <= t1):
                continue
            d_track, d_now = where(r["utc"], r["lat"], r["lon"])
            if d_track > a.radius_km:
                continue
            prelim.append(dict(r, utc=r["utc"].isoformat(), km_from_track=d_track, km_from_storm_at_time=d_now))
    final.sort(key=lambda e: e["utc"]); prelim.sort(key=lambda e: e["utc"])

    def summary(rs, near_km):
        rs = [r for r in rs if r["km_from_storm_at_time"] <= near_km]
        hail = [r for r in rs if r["kind"] == "hail"]
        wind = [r for r in rs if r["kind"] == "wind"]
        return {"hail_reports": len(hail), "wind_reports": len(wind),
                "tornado_reports": sum(r["kind"] == "tornado" for r in rs),
                "largest_hail_in": max((r["size_in"] for r in hail), default=None),
                "strongest_gust_kt": max((r["gust_kt"] for r in wind), default=None)}
    out = {"case": a.case, "radius_km": a.radius_km, "window_utc": [t0.isoformat(), t1.isoformat()],
           "note": "final = SPC database (Storm Data, official); preliminary = SPC daily filtered reports. "
                   "'this storm' counts only reports within 25 km of the storm's radar position at the report time.",
           "this_storm_final": summary(final, 25), "all_within_radius_final": summary(final, 1e9),
           "final": final, "preliminary": prelim}
    f = DER / f"{a.case}_spc_reports.json"
    f.write_text(json.dumps(out, indent=1))
    print(json.dumps({k: out[k] for k in ("this_storm_final", "all_within_radius_final")}, indent=1))
    for r in final:
        mag = r.get("size_in", r.get("gust_kt", r.get("ef")))
        print(f"  {r['utc'][11:16]}Z {r['kind']:7s} {mag}  {r['km_from_track']:5.1f} km from track, "
              f"{r['km_from_storm_at_time']:5.1f} km from the storm then")
    print(f"wrote {f}  ({len(final)} final, {len(prelim)} preliminary)")


if __name__ == "__main__":
    main()
