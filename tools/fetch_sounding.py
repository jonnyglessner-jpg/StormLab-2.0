"""Fetch a real proximity sounding for a reference case and save it as JSON the game can read.

Sources:
  igra  NCEI IGRA2 radiosonde archive (period-of-record file per station, cached; 00Z / 12Z launches).
        Station ids like USM00072451 (Dodge City). https://www.ncei.noaa.gov/products/weather-balloon/integrated-global-radiosonde-archive
  rap   RAP 13 km analysis (hourly, 2012+) from the NCEI THREDDS archive, full file (~50 MB), point by nearest cell.
  hrrr  HRRR 3 km analysis (hourly, 2014-07-30+) from AWS noaa-hrrr-bdp-pds; only the needed GRIB messages are
        pulled by byte range using the .idx file (~150 MB instead of 345).

Output: data/raw/soundings/<case>_<source>.json with levels of p (hPa), z (m MSL), T (C), Td (C), wind dir (deg) and
speed (kt), plus MetPy diagnostics (MLCAPE/MLCIN/LCL/LFC/EL, 0-1 / 0-3 km SRH, 0-1 / 0-6 km shear, Bunkers motion),
so each case can be checked against the published proximity-sounding climatology.

Usage:
  python tools/fetch_sounding.py igra --station USM00072451 --time 2016-05-25T00 --case ddc_20160524
  python tools/fetch_sounding.py rap  --lat 37.55 --lon -100.15 --time 2016-05-24T22 --case ddc_20160524
  python tools/fetch_sounding.py hrrr --lat 37.55 --lon -100.15 --time 2016-05-24T22 --case ddc_20160524
"""
import argparse
import io
import json
import re
import ssl
import sys
import urllib.request
import zipfile
from datetime import datetime
from pathlib import Path

import certifi
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "raw" / "soundings"
UA = {"User-Agent": "supercell-lab-2 research fetch"}
CTX = ssl.create_default_context(cafile=certifi.where())


def get(url, rng=None, to_file=None):
    h = dict(UA)
    if rng:
        h["Range"] = f"bytes={rng[0]}-{rng[1]}"
    req = urllib.request.Request(url, headers=h)
    with urllib.request.urlopen(req, timeout=600, context=CTX) as r:
        if to_file:
            with open(to_file, "wb") as f:
                while chunk := r.read(1 << 20):
                    f.write(chunk)
            return None
        return r.read()


def wind_from_uv(u, v):
    return (np.degrees(np.arctan2(-u, -v)) + 360) % 360, np.hypot(u, v) / 0.514444


# ---------------------------------------------------------------- IGRA2
def fetch_igra(station, t):
    cache = OUT / "igra"; cache.mkdir(parents=True, exist_ok=True)
    zf = cache / f"{station}-data.txt.zip"
    if not zf.exists():
        url = f"https://www.ncei.noaa.gov/data/integrated-global-radiosonde-archive/access/data-por/{station}-data.txt.zip"
        print(f"  downloading {url} (period of record, ~90 MB, cached)")
        get(url, to_file=zf)
    want = f"#{station} {t:%Y %m %d %H}"
    levels = []
    with zipfile.ZipFile(zf) as z, z.open(z.namelist()[0]) as fh:
        hit = False
        for raw in io.TextIOWrapper(fh, encoding="ascii", errors="replace"):
            if raw.startswith("#"):
                if hit:
                    break
                hit = raw.startswith(want)
                continue
            if not hit:
                continue
            # IGRA2 data record: LVLTYP ETIME PRESS[flag] GPH[flag] TEMP[flag] RH DPDP WDIR WSPD; flags A/B glued to numbers
            tok = re.sub(r"(?<=\d)[AB]", " ", raw).split()
            if len(tok) < 9:
                continue
            press, gph, temp, dpdp, wdir, wspd = (int(tok[i]) for i in (2, 3, 4, 6, 7, 8))
            if press <= 0:
                continue
            T = temp / 10 if temp > -8000 else float("nan")
            Td = T - dpdp / 10 if (dpdp > -8000 and np.isfinite(T)) else float("nan")
            levels.append({"p": press / 100, "z": gph if gph > -8000 else float("nan"), "T": T, "Td": Td,
                           "dir": wdir if wdir >= 0 else float("nan"), "spd_kt": wspd / 10 / 0.514444 if wspd >= 0 else float("nan")})
    if len(levels) < 20:
        sys.exit(f"sounding {want} not found or too short ({len(levels)} levels)")
    # Fill missing heights hydrostatically, anchored at the first reported height and integrated both up and down
    # (some records lack the surface height). Temperature gaps (wind-only levels) are interpolated in ln p for the
    # integration only; the reported values are left as they are.
    P = np.array([L["p"] for L in levels]); Z = np.array([L["z"] for L in levels], dtype=float)
    Tk = np.array([L["T"] for L in levels], dtype=float) + 273.15
    ok = np.isfinite(Tk)
    if ok.sum() >= 2 and np.isfinite(Z).any():
        lp = np.log(P)
        Ti = np.interp(-lp, -lp[ok], Tk[ok])
        k0 = int(np.argmax(np.isfinite(Z)))
        for i in range(k0 + 1, len(levels)):
            if not np.isfinite(Z[i]):
                Z[i] = Z[i - 1] + 287.04 * 0.5 * (Ti[i] + Ti[i - 1]) / 9.81 * np.log(P[i - 1] / P[i])
        for i in range(k0 - 1, -1, -1):
            if not np.isfinite(Z[i]):
                Z[i] = Z[i + 1] - 287.04 * 0.5 * (Ti[i] + Ti[i + 1]) / 9.81 * np.log(P[i] / P[i + 1])
        for L, z in zip(levels, Z):
            L["z"] = round(float(z), 1)
    levels = [L for L in levels if np.isfinite(L["z"])]
    return {"source": f"IGRA2 {station} {t:%Y-%m-%d %HZ}", "levels": levels}


# ---------------------------------------------------------------- model soundings (GRIB)
def _decode_point(data, lat, lon):
    import eccodes  # noqa: PLC0415
    eccodes.codes_grib_multi_support_on()      # NCEP packs UGRD and VGRD into one multi-field message
    fields, idx, pos = {}, None, 0
    while pos < len(data):
        h = eccodes.codes_new_from_message(data[pos:])
        size = eccodes.codes_get_message_size(h)
        var = eccodes.codes_get(h, "shortName"); lev = eccodes.codes_get(h, "level"); ltype = eccodes.codes_get(h, "typeOfLevel")
        keep = (ltype == "isobaricInhPa" and var in ("gh", "t", "r", "u", "v")) or \
               (ltype == "surface" and var in ("sp", "orog")) or \
               (ltype == "heightAboveGround" and var in ("2t", "2d", "10u", "10v"))
        if keep:
            if idx is None:
                lats = eccodes.codes_get_array(h, "latitudes"); lons = eccodes.codes_get_array(h, "longitudes")
                lons = np.where(lons > 180, lons - 360, lons)
                idx = int(np.argmin((lats - lat) ** 2 + ((lons - lon) * np.cos(np.radians(lat))) ** 2))
                print(f"  nearest grid point {lats[idx]:.3f}, {lons[idx]:.3f}")
            fields[(var, ltype, int(lev))] = float(eccodes.codes_get_array(h, "values")[idx])
        eccodes.codes_release(h)
        pos += size
    return fields


def _levels_from_fields(fields):
    plev = sorted({k[2] for k in fields if k[1] == "isobaricInhPa"}, reverse=True)
    levels = []
    for p in plev:
        vals = [fields.get((v, "isobaricInhPa", p)) for v in ("t", "r", "gh", "u", "v")]
        if None in vals:
            continue
        T, rh, z, u, v = vals
        Tc = T - 273.15
        es = 6.112 * np.exp(17.67 * Tc / (Tc + 243.5)); e = max(rh, 1) / 100 * es
        Td = 243.5 * np.log(e / 6.112) / (17.67 - np.log(e / 6.112))
        d, spd = wind_from_uv(u, v)
        levels.append({"p": p, "z": round(z, 1), "T": round(Tc, 2), "Td": round(float(Td), 2), "dir": round(float(d), 1), "spd_kt": round(float(spd), 1)})
    sp = fields.get(("sp", "surface", 0)); zs = fields.get(("orog", "surface", 0))
    t2 = fields.get(("2t", "heightAboveGround", 2)); d2 = fields.get(("2d", "heightAboveGround", 2))
    u10 = fields.get(("10u", "heightAboveGround", 10)); v10 = fields.get(("10v", "heightAboveGround", 10))
    if None not in (sp, zs, t2, d2, u10, v10):
        levels = [L for L in levels if L["p"] < sp / 100 - 1]
        d, spd = wind_from_uv(u10, v10)
        levels.insert(0, {"p": round(sp / 100, 1), "z": round(zs, 1), "T": round(t2 - 273.15, 2), "Td": round(d2 - 273.15, 2),
                          "dir": round(float(d), 1), "spd_kt": round(float(spd), 1)})
    return levels


def fetch_rap(lat, lon, t):
    cache = OUT / "grib"; cache.mkdir(parents=True, exist_ok=True)
    name = f"rap_130_{t:%Y%m%d_%H}00_000.grb2"
    f = cache / name
    if not f.exists():
        coll = "model-rap130anl-old" if t < datetime(2020, 5, 15) else "model-rap130anl"
        url = f"https://www.ncei.noaa.gov/thredds/fileServer/{coll}/{t:%Y%m}/{t:%Y%m%d}/{name}"
        print(f"  downloading {url}")
        get(url, to_file=f)
    fields = _decode_point(f.read_bytes(), lat, lon)
    return {"source": f"RAP 13 km analysis {t:%Y-%m-%d %HZ} nearest {lat},{lon}", "levels": _levels_from_fields(fields)}


def fetch_hrrr(lat, lon, t):
    base = f"https://noaa-hrrr-bdp-pds.s3.amazonaws.com/hrrr.{t:%Y%m%d}/conus/hrrr.t{t:%H}z.wrfprsf00.grib2"
    inv = get(base + ".idx").decode().splitlines()
    want = []
    for i, line in enumerate(inv):
        f = line.split(":"); start = int(f[1]); var = f[3]; lev = f[4]
        end = int(inv[i + 1].split(":")[1]) - 1 if i + 1 < len(inv) else ""
        if (var in ("HGT", "TMP", "RH", "UGRD", "VGRD") and lev.endswith(" mb")) or \
           (var in ("PRES", "HGT") and lev == "surface") or (var in ("TMP", "DPT") and lev == "2 m above ground") or \
           (var in ("UGRD", "VGRD") and lev == "10 m above ground"):
            want.append((start, end))
    print(f"  {len(want)} GRIB messages by byte range from {base.split('/')[-1]}")
    buf = io.BytesIO()
    for n, (start, end) in enumerate(want):
        buf.write(get(base, (start, end)))
        if n % 40 == 39:
            print(f"    {n + 1}/{len(want)}")
    fields = _decode_point(buf.getvalue(), lat, lon)
    return {"source": f"HRRR 3 km analysis {t:%Y-%m-%d %HZ} nearest {lat},{lon}", "levels": _levels_from_fields(fields)}


# ---------------------------------------------------------------- diagnostics
def diagnose(snd):
    import metpy.calc as mpcalc  # noqa: PLC0415
    from metpy.units import units  # noqa: PLC0415

    L = [x for x in snd["levels"] if np.isfinite(x["T"]) and np.isfinite(x["Td"])]
    p = np.array([x["p"] for x in L]) * units.hPa; z = np.array([x["z"] for x in L]) * units.m
    T = np.array([x["T"] for x in L]) * units.degC; Td = np.array([x["Td"] for x in L]) * units.degC
    W = [x for x in snd["levels"] if np.isfinite(x["dir"]) and np.isfinite(x["spd_kt"]) and np.isfinite(x["z"])]
    wp = np.array([x["p"] for x in W]) * units.hPa; wz = np.array([x["z"] for x in W]) * units.m
    u, v = mpcalc.wind_components(np.array([x["spd_kt"] for x in W]) * units.knots, np.array([x["dir"] for x in W]) * units.deg)
    out = {"n_levels": len(snd["levels"]), "sfc_p_hPa": float(p[0].m), "sfc_T_C": float(T[0].m), "sfc_Td_C": float(Td[0].m)}

    def z_of(pp):
        if pp is None or not np.isfinite(pp.m):
            return None
        return round(float(np.interp(np.log(pp.to("hPa").m), np.log(p.m[::-1]), z.m[::-1]) - z.m[0]))

    try:
        ml_p, ml_T, ml_Td = mpcalc.mixed_parcel(p, T, Td, depth=100 * units.hPa)
        prof = mpcalc.parcel_profile(p, ml_T, ml_Td).to("degC")
        cape, cin = mpcalc.cape_cin(p, T, Td, prof)
        lcl_p, _ = mpcalc.lcl(ml_p, ml_T, ml_Td); lfc_p, _ = mpcalc.lfc(p, T, Td, prof); el_p, _ = mpcalc.el(p, T, Td, prof)
        out.update(mlcape=round(float(cape.m)), mlcin=round(float(cin.m)), mllcl_m_agl=z_of(lcl_p), lfc_m_agl=z_of(lfc_p), el_m_agl=z_of(el_p))
        sb = mpcalc.parcel_profile(p, T[0], Td[0]).to("degC"); sbc, sbcin = mpcalc.cape_cin(p, T, Td, sb)
        out.update(sbcape=round(float(sbc.m)), sbcin=round(float(sbcin.m)))
        out["freezing_level_m_agl"] = round(float(np.interp(0, T.m[::-1], z.m[::-1]) - z.m[0]))
        out["lapse_700_500_C_per_km"] = round(float((np.interp(700, p.m[::-1], T.m[::-1]) - np.interp(500, p.m[::-1], T.m[::-1])) /
                                                    ((np.interp(500, p.m[::-1], z.m[::-1]) - np.interp(700, p.m[::-1], z.m[::-1])) / 1000)), 2)
    except Exception as e:  # noqa: BLE001
        out["thermo_error"] = repr(e)
    try:
        agl = wz - wz[0]
        rm, lm, mean = mpcalc.bunkers_storm_motion(wp, u, v, agl)
        for top, name in ((500, "srh500"), (1000, "srh1"), (3000, "srh3")):
            srh = mpcalc.storm_relative_helicity(agl, u, v, depth=top * units.m, storm_u=rm[0], storm_v=rm[1])
            out[name] = round(float(srh[2].m))
        for top, name in ((1000, "shear1_kt"), (3000, "shear3_kt"), (6000, "shear6_kt")):
            su, sv = mpcalc.bulk_shear(wp, u, v, height=agl, depth=top * units.m)
            out[name] = round(float(np.hypot(su, sv).to("knots").m))
        d, spd = wind_from_uv(rm[0].to("m/s").m, rm[1].to("m/s").m)
        out["bunkers_rm_from_deg_kt"] = [round(float(d)), round(float(spd))]
        # storm-relative wind at anvil level (9-11 km) and mid level (4-6 km): the LP / classic / HP discriminator
        for lo, hi, name in ((9000, 11000, "sr_anvil_kt"), (4000, 6000, "sr_mid_kt"), (0, 1000, "sr_low_kt")):
            m = (agl.m >= lo) & (agl.m <= hi)
            if m.any():
                su = u.to("m/s").m[m].mean() - rm[0].to("m/s").m; sv = v.to("m/s").m[m].mean() - rm[1].to("m/s").m
                out[name] = round(float(np.hypot(su, sv) / 0.514444))
    except Exception as e:  # noqa: BLE001
        out["wind_error"] = repr(e)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source", choices=["igra", "rap", "hrrr"])
    ap.add_argument("--station", default="USM00072451"); ap.add_argument("--lat", type=float); ap.add_argument("--lon", type=float)
    ap.add_argument("--time", required=True, help="UTC, YYYY-MM-DDTHH"); ap.add_argument("--case", required=True)
    a = ap.parse_args()
    t = datetime.strptime(a.time, "%Y-%m-%dT%H")
    snd = {"igra": lambda: fetch_igra(a.station, t), "rap": lambda: fetch_rap(a.lat, a.lon, t), "hrrr": lambda: fetch_hrrr(a.lat, a.lon, t)}[a.source]()
    snd["case"] = a.case; snd["valid_utc"] = t.isoformat(); snd["diagnostics"] = diagnose(snd)
    OUT.mkdir(parents=True, exist_ok=True)
    f = OUT / f"{a.case}_{a.source}.json"
    f.write_text(json.dumps(snd, indent=1))
    print(f"wrote {f} ({len(snd['levels'])} levels)")
    print(json.dumps(snd["diagnostics"], indent=1))


if __name__ == "__main__":
    main()
