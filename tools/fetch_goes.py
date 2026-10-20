"""Measure a storm's anvil and overshooting tops from GOES-16 ABI cloud-top imagery.

  python tools/fetch_goes.py --case lp_earth_20210516 [--every 1 --height-km 12]

Follows the storm with its radar track (validation/real-storm-cases/radar/<case>_lifecycle.json from radar_lifecycle.py; or give
--start/--end/--lat/--lon for a fixed point). Pulls ABI L2 Cloud and Moisture Imagery from the public noaa-goes16
bucket (anonymous HTTPS): the 1-min mesoscale sector (M1 or M2, whichever covers the storm) when there is one,
otherwise the 5-min CONUS sector. Band 13 (10.3 um) brightness temperature every scan; band 2 (0.64 um visible) for
the quick-looks while the sun is up.

Pixels are navigated at the cloud top (--height-km, default 12 km), which removes the parallax shift (~1 km of shift
per km of height at these view angles, to the north-west). Per scan:
  - coldest cloud top on the storm's side of the sky (see below), and its position (the satellite updraft)
  - overshooting tops (after Bedka et al. 2010): a local minimum <= 215 K inside the anvil, at least 6.5 K colder than
    the mean anvil 10-18 km around it; size = the area inside the half-depth contour, as an equivalent diameter;
    only OTs on the storm's side of the sky count
  - the anvil: the -50 C (223.15 K) region of this storm. A shared cloud shield is split between storms by a
    watershed on brightness temperature seeded at each storm's cold top (inside radar range the other radar cells
    mark the other storms, so cold spots on one flat anvil never split it). Area, the equivalent ellipse
    (major/minor axis = 4 sigma, orientation), the extent along the major/minor axis, the downstream direction
    (cold top -> anvil centroid), the backshear (how far the anvil edge reaches upwind of the cold top, and of the
    radar core), the share of its edge that borders other storms' anvils, and the whole connected shield (upper bound)
  - the updraft / cold top is looked for within 40 km of the radar core, on the storm's side of any neighbour: the
    overshooting top sits 0-35 km downshear of the low-level core
Summary: first own -50 C top and first 1000 km2 anvil (minutes after the radar's first 40 dBZ echo), anvil growth
(km2/min and the equivalent radial spread in m/s, from 5-scan medians), shape statistics over the scans where the anvil
is mostly free of neighbours (<= 25 % of its edge shared), the largest anvil and shield, OT frequency, episodes and size.
Redo only the summary and quick-looks with --resummarise.

Writes validation/real-storm-cases/satellite/<case>.json and three quick-look PNGs (IR with the -50 C contour + visible).
Files are cached in data/raw/goes.
"""
import argparse
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta
from pathlib import Path

import numpy as np
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "data" / "raw" / "goes"
OUT = ROOT / "validation" / "real-storm-cases" / "satellite"
RADAR = ROOT / "validation" / "real-storm-cases" / "radar"
BUCKET = "https://noaa-goes16.s3.amazonaws.com/"
R_E = 6371.0
ANVIL_K = 223.15
BOX = 300          # km half-width of the analysed area


# ---------------------------------------------------------------- listing and download
def fetch_url(url, dest=None, tries=5):
    """GET with retries (the S3 endpoint times out now and then on long runs)."""
    import time  # noqa: PLC0415
    for k in range(tries):
        try:
            if dest is None:
                return urllib.request.urlopen(url, timeout=60).read()
            urllib.request.urlretrieve(url, dest)
            return None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if k == tries - 1:
                raise
            time.sleep(5 * (k + 1))


def list_keys(prefix):
    keys, token = [], None
    while True:
        url = f"{BUCKET}?list-type=2&prefix={prefix}" + (f"&continuation-token={urllib.parse.quote(token)}" if token else "")
        xml = fetch_url(url).decode()
        keys += re.findall(r"<Key>(.*?)</Key>", xml)
        m = re.search(r"<NextContinuationToken>(.*?)</NextContinuationToken>", xml)
        if not m:
            return keys
        token = m.group(1)


def scan_time(key):
    m = re.search(r"_s(\d{4})(\d{3})(\d{2})(\d{2})(\d{2})", key)
    y, doy, hh, mm, ss = (int(g) for g in m.groups())
    return datetime(y, 1, 1) + timedelta(days=doy - 1, hours=hh, minutes=mm, seconds=ss)


def keys_for(product, band, t0, t1):
    """{scan time: key} for e.g. product 'CMIPM1' / 'CMIPC', band 13, over [t0, t1]."""
    sector_dir = "ABI-L2-CMIPM" if product.startswith("CMIPM") else "ABI-L2-" + product
    out, h = {}, t0.replace(minute=0, second=0)
    while h <= t1:
        pre = f"{sector_dir}/{h:%Y}/{h.timetuple().tm_yday:03d}/{h:%H}/OR_ABI-L2-{product}-M"
        for k in list_keys(pre):
            if f"C{band:02d}_G16" in k:
                t = scan_time(k)
                if t0 <= t <= t1:
                    out[t] = k
        h += timedelta(hours=1)
    return out


def get(key):
    f = RAW / key.split("/")[-1]
    if not f.exists():
        RAW.mkdir(parents=True, exist_ok=True)
        tmp = f.with_suffix(".part")
        fetch_url(BUCKET + key, tmp)
        tmp.replace(f)
    return f


# ---------------------------------------------------------------- navigation
def read_cmi(f):
    import xarray as xr  # noqa: PLC0415
    with xr.open_dataset(f, engine="h5netcdf") as ds:
        p = ds["goes_imager_projection"].attrs
        cmi = ds["CMI"].values.astype("float32")
        x = ds["x"].values.astype("float64"); y = ds["y"].values.astype("float64")
        proj = {k: float(np.atleast_1d(p[k])[0]) for k in ("perspective_point_height", "semi_major_axis", "semi_minor_axis",
                                                          "longitude_of_projection_origin")}
    return cmi, x, y, proj


def navigate(x, y, proj, h_km=0.0):
    """Lat/lon of the fixed-grid scan angles x, y (1-D, radians), on the ellipsoid inflated by h_km (cloud top)."""
    H = proj["perspective_point_height"] + proj["semi_major_axis"]
    req, rpol = proj["semi_major_axis"] + h_km * 1000, proj["semi_minor_axis"] + h_km * 1000
    X, Y = np.meshgrid(x, y)
    a = np.sin(X) ** 2 + np.cos(X) ** 2 * (np.cos(Y) ** 2 + (req / rpol) ** 2 * np.sin(Y) ** 2)
    b = -2 * H * np.cos(X) * np.cos(Y)
    c = H ** 2 - req ** 2
    disc = b ** 2 - 4 * a * c
    with np.errstate(invalid="ignore"):
        rs = (-b - np.sqrt(disc)) / (2 * a)
        sx = rs * np.cos(X) * np.cos(Y); sy = -rs * np.sin(X); sz = rs * np.cos(X) * np.sin(Y)
        lat = np.degrees(np.arctan((req / rpol) ** 2 * sz / np.sqrt((H - sx) ** 2 + sy ** 2)))
        lon = proj["longitude_of_projection_origin"] - np.degrees(np.arctan(sy / (H - sx)))
    return lat, lon


def to_scan_angle(lat, lon, proj):
    H = proj["perspective_point_height"] + proj["semi_major_axis"]
    req, rpol = proj["semi_major_axis"], proj["semi_minor_axis"]
    e2 = 1 - (rpol / req) ** 2
    phc = np.arctan((rpol / req) ** 2 * np.tan(np.radians(lat)))
    rc = rpol / np.sqrt(1 - e2 * np.cos(phc) ** 2)
    dl = np.radians(lon - proj["longitude_of_projection_origin"])
    sx = H - rc * np.cos(phc) * np.cos(dl); sy = -rc * np.cos(phc) * np.sin(dl); sz = rc * np.sin(phc)
    return float(np.arcsin(-sy / np.sqrt(sx ** 2 + sy ** 2 + sz ** 2))), float(np.arctan(sz / sx))


def local_xy(lat, lon, lat0, lon0):
    return (np.radians(lon - lon0) * R_E * np.cos(np.radians(lat0)), np.radians(lat - lat0) * R_E)


def crop(f, lat0, lon0, h_km, half_km=BOX):
    """Read a CMIP file and return (values, X km, Y km, pixel area km2) around (lat0, lon0), or None if not covered."""
    cmi, x, y, proj = read_cmi(f)
    xa, ya = to_scan_angle(lat0, lon0, proj)
    dx = abs(x[1] - x[0]); n = int(half_km / (dx * proj["perspective_point_height"] / 1000) * 1.0) + 2
    ix = int(round((xa - x[0]) / (x[1] - x[0]))); iy = int(round((ya - y[0]) / (y[1] - y[0])))
    margin = int(90 / (dx * proj["perspective_point_height"] / 1000))   # the point must be >= 90 km inside the sector
    if not (margin <= ix < len(x) - margin and margin <= iy < len(y) - margin):
        return None
    i0, i1 = max(iy - n, 0), min(iy + n, len(y)); j0, j1 = max(ix - n, 0), min(ix + n, len(x))
    lat, lon = navigate(x[j0:j1], y[i0:i1], proj, h_km)
    X, Y = local_xy(lat, lon, lat0, lon0)
    gxi, gxj = np.gradient(X); gyi, gyj = np.gradient(Y)
    area = np.abs(gxj * gyi - gxi * gyj)
    return cmi[i0:i1, j0:j1], X, Y, area


# ---------------------------------------------------------------- measurement
UPDRAFT_KM = 40    # km: the updraft (coldest top) is searched within this distance of the radar core
H_MIN = 4.0        # K: depth a cold core needs (below the saddle to its neighbours) to count as its own storm


def h_minima(f, h):
    """Regional minima of f deeper than h (morphological reconstruction by erosion of f + h)."""
    m = f + h
    for _ in range(3000):
        n = np.maximum(ndimage.grey_erosion(m, size=3), f)
        if np.array_equal(n, m):
            break
        m = n
    return (m - f) >= h - 1e-3


def overshooting_tops(bt, X, Y, near):
    """OT candidates (Bedka et al. 2010, adapted to 2 km pixels) whose minimum lies where `near` is True."""
    mn = ndimage.minimum_filter(bt, size=5, mode="nearest")
    cand = np.argwhere((bt == mn) & (bt <= 215.0) & near)
    ots = []
    for i, j in cand:
        d = np.hypot(X - X[i, j], Y - Y[i, j])
        ring = (d >= 10) & (d <= 18)
        rv = bt[ring]
        if rv.size == 0 or (rv <= 225.0).mean() < 0.5:
            continue
        anvil_mean = float(rv[rv <= 225.0].mean())
        depth = anvil_mean - float(bt[i, j])
        if depth < 6.5:
            continue
        lab, _ = ndimage.label((bt <= 0.5 * (anvil_mean + bt[i, j])) & (d <= 20))
        reg = lab == lab[i, j]
        diam = 2 * np.sqrt(reg.sum() * np.median(np.abs(np.gradient(X)[1] * np.gradient(Y)[0])) / np.pi)
        if diam > 20:
            continue
        ots.append({"x": float(X[i, j]), "y": float(Y[i, j]), "bt_k": round(float(bt[i, j]), 1),
                    "depth_k": round(depth, 1), "diam_km": round(float(diam), 1)})
    # merge candidates within 8 km (keep the coldest)
    ots.sort(key=lambda o: o["bt_k"])
    keep = []
    for o in ots:
        if all(np.hypot(o["x"] - k["x"], o["y"] - k["y"]) > 8 for k in keep):
            keep.append(o)
    return keep


def measure(bt, X, Y, area, sx, sy, others=None, radar_box_km=70.0, want_mask=False):
    """Everything for one scan.

    (sx, sy) = the storm's radar position in the local km frame; others = [(x, y), ...] the other radar cells
    (composite >= 40 dBZ) at that time, or None when there is no radar volume near this scan.
    """
    d = np.hypot(X - sx, Y - sy)
    # our part of the sky: within UPDRAFT_KM of our radar core and closer to it than to any other radar cell (the
    # overshooting top sits 10-35 km downshear of the low-level core: the updraft tilts and the anvil top drifts)
    ours = d <= UPDRAFT_KM
    for ox, oy in (others or []):
        ours &= d < np.hypot(X - ox, Y - oy)
    bts = ndimage.gaussian_filter(bt, 1.5)
    i, j = np.unravel_index(np.argmin(np.where(ours, bt, 999)), bt.shape)
    res = {"coldest_k": round(float(bt[i, j]), 1), "coldest_c": round(float(bt[i, j]) - 273.15, 1),
           "updraft_xy_km": [round(float(X[i, j]), 1), round(float(Y[i, j]), 1)],
           "updraft_from_radar_core_km": round(float(d[i, j]), 1)}
    ux, uy = float(X[i, j]), float(Y[i, j])
    res["ots"] = overshooting_tops(bt, X, Y, ours)
    cold = bt <= ANVIL_K
    # our own cold top: a local minimum of the smoothed field in our part of the sky (a neighbour's anvil spreading
    # over the spot slopes away from the neighbour and has none)
    lmin = (bts == ndimage.minimum_filter(bts, size=7)) & ours & cold
    if not lmin.any():
        res["anvil"] = None
        return (res, None) if want_mask else res
    a_, b_ = min(map(tuple, np.argwhere(lmin)), key=lambda c: bts[c])
    f = np.where(cold, bts, 260.0)
    core_lab, ncores = ndimage.label(h_minima(f, H_MIN) & cold)
    du = np.hypot(X - X[a_, b_], Y - Y[a_, b_])
    own = {int(core_lab[a_, b_])} - {0}
    # Split the -50 C shield between storms: watershed on lightly smoothed brightness temperature, flooded from our
    # cold top and from every other storm's. Other storms are: inside radar range, the other radar cells (marker = the
    # coldest -50 C pixel within 25 km of the cell, outside our part of the sky); beyond it, cold cores at least H_MIN K deeper than the saddle to
    # their neighbours (h-minima), and any local minimum <= 220 K more than 100 km away. Shallow cold spots on one
    # flat anvil top therefore never split it. The boundary falls on the warmer ridge where two anvils meet.
    whole = ndimage.label(cold)[0]
    mine = whole == whole[a_, b_]
    markers = np.zeros(bt.shape, np.int16)
    nxt = 3
    in_radar = d <= radar_box_km if others is not None else np.zeros(bt.shape, bool)
    for k in range(1, ncores + 1):
        reg = core_lab == k
        if k in own:
            markers[reg] = 2
        elif not in_radar[reg].any() and du[reg].min() > 30:
            markers[reg] = nxt; nxt += 1
    for ox, oy in (others or []):
        near = (np.hypot(X - ox, Y - oy) <= 25) & cold & ~ours & (du > 15)
        if near.any():
            a, b = np.unravel_index(np.argmin(np.where(near, bt, 999)), bt.shape)
            markers[a, b] = nxt; nxt += 1
    mn = ndimage.minimum_filter(np.where(cold, bts, 999), size=15)
    for a, b in np.argwhere((bts == mn) & cold & (bts <= 220.0) & (du > 100) & ~in_radar):
        if markers[a, b] == 0:
            markers[a, b] = nxt; nxt += 1
    markers[(du <= 6) & cold] = 2
    markers[a_, b_] = 2
    # warm pixels get the highest cost (they are only crossed when nothing else is left) and are dropped afterwards
    img = np.where(cold, np.clip((bts - 180.0) * 100, 0, 60000), 65535).astype(np.uint16)
    ws = np.where(cold, ndimage.watershed_ift(img, markers), 0)
    m = (ws == 2) & mine
    edge = m & ~ndimage.binary_erosion(m)
    nb_other = ndimage.binary_dilation(ws >= 3) & mine
    touch_frac = float((edge & nb_other).sum() / max(edge.sum(), 1))
    touches = touch_frac > 0
    shield = float(area[mine].sum())
    A = float(area[m].sum())
    w = area[m]
    cx, cy = float((X[m] * w).sum() / w.sum()), float((Y[m] * w).sum() / w.sum())
    cov = np.cov(np.vstack([X[m] - cx, Y[m] - cy]), aweights=w) if m.sum() > 2 else np.eye(2)
    ev, evec = np.linalg.eigh(cov)
    major_v = evec[:, 1]
    orient = (np.degrees(np.arctan2(major_v[0], major_v[1]))) % 180      # deg from north, 0-180
    pmaj = (X[m] - cx) * major_v[0] + (Y[m] - cy) * major_v[1]
    pmin = (X[m] - cx) * evec[0, 0] + (Y[m] - cy) * evec[1, 0]
    an = {"area_km2": round(A), "centroid_xy_km": [round(cx, 1), round(cy, 1)],
          "ellipse_major_km": round(4 * float(np.sqrt(max(ev[1], 0))), 1), "ellipse_minor_km": round(4 * float(np.sqrt(max(ev[0], 0))), 1),
          "major_axis_deg": round(float(orient)), "extent_major_km": round(float(pmaj.max() - pmaj.min()), 1),
          "extent_minor_km": round(float(pmin.max() - pmin.min()), 1)}
    dvx, dvy = cx - ux, cy - uy
    dl = float(np.hypot(dvx, dvy))
    if dl > 2:
        ex, ey = dvx / dl, dvy / dl
        proj = (X[m] - ux) * ex + (Y[m] - uy) * ey
        an["downstream_to_deg"] = round(float(np.degrees(np.arctan2(ex, ey)) % 360))
        an["updraft_to_centroid_km"] = round(dl, 1)
        an["downstream_reach_km"] = round(float(proj.max()), 1)
        an["backshear_km"] = round(float(max(-proj.min(), 0)), 1)
        # the same measured from the radar (low-level precipitation) core instead of the cold top
        an["upwind_of_radar_core_km"] = round(float(max(-((X[m] - sx) * ex + (Y[m] - sy) * ey).min(), 0)), 1)
    an["touches_other_anvil"] = touches
    an["edge_shared_with_other_anvils"] = round(touch_frac, 2)
    an["connected_shield_km2"] = round(shield)
    an["edge_of_box"] = bool(m[[0, -1], :].any() or m[:, [0, -1]].any())
    res["anvil"] = an
    return (res, m) if want_mask else res


def solar_zenith(t, lat, lon):
    doy = t.timetuple().tm_yday
    g = 2 * np.pi / 365 * (doy - 1 + (t.hour - 12) / 24)
    decl = 0.006918 - 0.399912 * np.cos(g) + 0.070257 * np.sin(g) - 0.006758 * np.cos(2 * g) + 0.000907 * np.sin(2 * g)
    eqt = 229.18 * (0.000075 + 0.001868 * np.cos(g) - 0.032077 * np.sin(g) - 0.014615 * np.cos(2 * g) - 0.040849 * np.sin(2 * g))
    tst = t.hour * 60 + t.minute + eqt + 4 * lon
    ha = np.radians(tst / 4 - 180)
    la = np.radians(lat)
    return float(np.degrees(np.arccos(np.sin(la) * np.sin(decl) + np.cos(la) * np.cos(decl) * np.cos(ha))))


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--case", required=True)
    ap.add_argument("--start"); ap.add_argument("--end"); ap.add_argument("--lat", type=float); ap.add_argument("--lon", type=float)
    ap.add_argument("--pad-before-min", type=float, default=30); ap.add_argument("--pad-after-min", type=float, default=60)
    ap.add_argument("--every", type=float, default=1, help="minutes between analysed scans")
    ap.add_argument("--height-km", type=float, default=12, help="cloud-top height used for the parallax correction")
    ap.add_argument("--resummarise", action="store_true", help="recompute the summary and quick-looks from the saved JSON")
    a = ap.parse_args()
    if a.resummarise:
        f = OUT / f"{a.case}.json"
        res = json.loads(f.read_text())
        lc_ = json.loads((RADAR / f"{a.case}_lifecycle.json").read_text()) if (RADAR / f"{a.case}_lifecycle.json").exists() else None
        res["summary"] = summarise(res["scans"], lc_, a)
        f.write_text(json.dumps(res, indent=1))
        print(json.dumps(res["summary"], indent=1))
        if lc_:
            for old in OUT.glob(f"{a.case}_*.png"):
                old.unlink()
            v = lc_["volumes"]
            quicklooks(res, a, *res["frame_centre_latlon"], [datetime.fromisoformat(x["utc"]) for x in v],
                       [x["latlon"][0] for x in v], [x["latlon"][1] for x in v])
        return

    lc_file = RADAR / f"{a.case}_lifecycle.json"
    if a.start:
        t0 = datetime.strptime(a.start, "%Y-%m-%dT%H:%M"); t1 = datetime.strptime(a.end, "%Y-%m-%dT%H:%M")
        tt, la, lo = [t0, t1], [a.lat, a.lat], [a.lon, a.lon]
        lc = None
    else:
        lc = json.loads(lc_file.read_text())
        v = lc["volumes"]
        tt = [datetime.fromisoformat(x["utc"]) for x in v]
        la = [x["latlon"][0] for x in v]; lo = [x["latlon"][1] for x in v]
        t0 = tt[0] - timedelta(minutes=a.pad_before_min); t1 = tt[-1] + timedelta(minutes=a.pad_after_min)
    lat0, lon0 = float(np.mean(la)), float(np.mean(lo))      # fixed local frame for the case
    secs = np.array([(t - tt[0]).total_seconds() for t in tt])
    vol_t = [datetime.fromisoformat(x["utc"]) for x in lc["volumes"]] if lc else []
    def others_at(t):
        """Other radar cells (local km) from the radar volume nearest t, or None if none within 6 min (40 min before
        the storm's first echo or after its last, when the neighbours still tell whose anvil is whose)."""
        if not vol_t:
            return None
        k = min(range(len(vol_t)), key=lambda q: abs((vol_t[q] - t).total_seconds()))
        tol = 2400 if (t < vol_t[0] or t > vol_t[-1]) else 360
        if abs((vol_t[k] - t).total_seconds()) > tol or "other_cells" not in lc["volumes"][k]:
            return None
        return [tuple(round(float(c), 1) for c in local_xy(o["latlon"][0], o["latlon"][1], lat0, lon0))
                for o in lc["volumes"][k]["other_cells"] if o["area40_comp_km2"] >= 8]
    def storm_at(t):
        s = (t - tt[0]).total_seconds()
        return float(np.interp(s, secs, la)), float(np.interp(s, secs, lo))

    print(f"{a.case}: {t0:%Y-%m-%d %H:%M} - {t1:%H:%M}Z, frame centre {lat0:.2f}, {lon0:.2f}")
    m1, m2 = keys_for("CMIPM1", 13, t0, t1), keys_for("CMIPM2", 13, t0, t1)
    conus = keys_for("CMIPC", 13, t0, t1)
    print(f"  listed {len(m1)} M1, {len(m2)} M2, {len(conus)} CONUS band-13 scans")

    # pick scan times: every a.every minutes; prefer M1, then M2, then CONUS within 3 min
    want, t = [], t0
    while t <= t1:
        want.append(t); t += timedelta(minutes=a.every)

    def nearest(d, t, tol):
        if not d:
            return None
        k = min(d, key=lambda s: abs((s - t).total_seconds()))
        return k if abs((k - t).total_seconds()) <= tol else None

    # prefetch the M1 files in parallel (the common case), then process
    first = {}
    for t in want:
        k = nearest(m1, t, 40)
        if k:
            first[t] = m1[k]
    with ThreadPoolExecutor(8) as ex:
        list(ex.map(get, sorted(set(first.values()))))

    scans, used = [], set()
    for t in want:
        slat, slon = storm_at(t)
        cand = []
        for d, tol, name in ((m1, 40, "M1"), (m2, 40, "M2"), (conus, 180, "CONUS")):
            k = nearest(d, t, tol)
            if k:
                cand.append((k, d[k], name))
        got = None
        for st, key, name in cand:
            if key in used:
                got = "dup"; break
            c = crop(get(key), lat0, lon0, a.height_km)
            if c is not None:
                # the storm itself must be well inside the crop
                bt, X, Y, area = c
                sx, sy = local_xy(slat, slon, lat0, lon0)
                if X.min() + 60 < sx < X.max() - 60 and Y.min() + 60 < sy < Y.max() - 60:
                    got = (st, key, name, bt, X, Y, area, sx, sy); break
        if got is None:
            print(f"  {t:%H:%M}  no scan covering the storm"); continue
        if got == "dup":
            continue
        st, key, name, bt, X, Y, area, sx, sy = got
        used.add(key)
        oth = others_at(st)
        r = measure(bt, X, Y, area, float(sx), float(sy), others=oth)
        r = {"utc": st.isoformat(timespec="seconds"), "sector": name, "storm_xy_km": [round(float(sx), 1), round(float(sy), 1)],
             "radar_other_cells_xy_km": oth,
             "solar_zenith_deg": round(solar_zenith(st, slat, slon), 1), **r}
        scans.append(r)
        an = r["anvil"]
        print(f"  {st:%H:%M:%S} {name:5s} coldest {r['coldest_c']:6.1f} C  OTs {len(r['ots'])}  "
              + (f"anvil {an['area_km2']:6d} km2 {an['extent_major_km']:5.0f}x{an['extent_minor_km']:4.0f} km "
                 f"axis {an['major_axis_deg']:3d}  backshear {an.get('backshear_km', '-')}  shield {an['connected_shield_km2']}"
                 f"  shared edge {an['edge_shared_with_other_anvils']:.2f}"
                 if an else "no -50 C anvil"), flush=True)

    summary = summarise(scans, lc, a)
    OUT.mkdir(parents=True, exist_ok=True)
    res = {"case": a.case, "frame_centre_latlon": [round(lat0, 3), round(lon0, 3)], "parallax_height_km": a.height_km,
           "method": "GOES-16 ABI L2 CMIP band 13; positions in km east/north of frame_centre, navigated at the cloud top "
                     "(parallax-corrected); anvil = connected <= -50 C region touching the storm; OT after Bedka et al. 2010",
           "summary": summary, "scans": scans}
    f = OUT / f"{a.case}.json"
    f.write_text(json.dumps(res, indent=1))
    print(json.dumps(summary, indent=1))
    quicklooks(res, a, lat0, lon0, tt, la, lo)
    print(f"wrote {f}")


def summarise(scans, lc, a):
    if not scans:
        return {}
    ts = [datetime.fromisoformat(s["utc"]) for s in scans]
    T = np.array([(t - ts[0]).total_seconds() / 60 for t in ts])
    A = np.array([s["anvil"]["area_km2"] if s["anvil"] else 0 for s in scans], float)
    shared = np.array([s["anvil"]["edge_shared_with_other_anvils"] if s["anvil"] else 0 for s in scans])
    # single scans still flip when a neighbour's cold core comes and goes: a 5-scan running median steadies the area
    Am = np.array([np.median(A[max(k - 2, 0):k + 3]) for k in range(len(A))])
    t_end = datetime.fromisoformat(lc["summary"]["last_40dbz_utc"]) if lc else ts[-1]
    life = np.array([t <= t_end for t in ts])
    # a -50 C top more than 10 min before the radar's first 40 dBZ echo belongs to a neighbour, not to this storm
    t_first = datetime.fromisoformat(lc["summary"]["first_40dbz_utc"]) - timedelta(minutes=10) if lc else ts[0]
    first50 = next((s["utc"] for s, t in zip(scans, ts) if s["anvil"] and t >= t_first), None)
    first1000 = next((s["utc"] for s, x, t in zip(scans, Am, ts) if x >= 1000 and t >= t_first), None)
    # isolated phase: until more than a quarter of the anvil's edge is shared with other anvils for 3 scans in a row
    iso_end = len(scans)
    for k in range(2, len(scans)):
        if (shared[k - 2:k + 1] > 0.25).all():
            iso_end = k - 2; break
    out = {"scans": len(scans), "sectors": sorted(set(s["sector"] for s in scans)),
           "first_own_minus50C_top_utc": first50, "first_anvil_1000km2_utc": first1000,
           "coldest_c": min(s["coldest_c"] for s in scans),
           "coldest_utc": min(scans, key=lambda s: s["coldest_k"])["utc"],
           "isolated_until_utc": scans[iso_end - 1]["utc"] if iso_end > 0 else None}
    if lc:
        t_echo = datetime.fromisoformat(lc["summary"]["first_40dbz_utc"])
        for k_, v_ in (("first_echo_to_minus50C_min", first50), ("first_echo_to_anvil_1000km2_min", first1000)):
            out[k_] = round((datetime.fromisoformat(v_) - t_echo).total_seconds() / 60) if v_ else None
    # growth: 10-min mean of the median-filtered area, then dA/dt and the equivalent radial spread d(sqrt(A/pi))/dt
    As = np.array([Am[(T >= t - 5) & (T <= t + 5)].mean() for t in T])
    with np.errstate(divide="ignore", invalid="ignore"):
        dA = np.gradient(As, T); dr = np.gradient(np.sqrt(As / np.pi), T) * 1000 / 60
    # the whole connected -50 C shield around our top: the anvil's upper bound (equal to it while the storm is alone)
    Sh = np.array([s["anvil"]["connected_shield_km2"] if s["anvil"] else 0 for s in scans], float)
    Shm = np.array([np.median(Sh[max(k - 2, 0):k + 3]) for k in range(len(Sh))])
    Shs = np.array([Shm[(T >= t - 5) & (T <= t + 5)].mean() for t in T])
    with np.errstate(divide="ignore", invalid="ignore"):
        dS = np.gradient(Shs, T); drS = np.gradient(np.sqrt(Shs / np.pi), T) * 1000 / 60
    onS = np.where((Shm >= 1000) & np.array([t >= t_first for t in ts]))[0]
    if len(onS):
        w = (T >= T[onS[0]]) & (T <= T[onS[0]] + 60)
        out["shield_growth_first_hour_after_1000km2"] = {"mean_km2_per_min": round(float(np.nanmean(dS[w])), 1),
                                                         "mean_radial_spread_ms": round(float(np.nanmean(drS[w])), 1)}
        lifeidx = np.where(life)[0]
        if len(lifeidx):
            k = lifeidx[np.argmax(Shm[lifeidx])]
            out["largest_connected_shield_during_radar_life"] = {"utc": scans[k]["utc"], "area_km2": round(float(Shm[k]))}
    after = np.array([t >= t_first for t in ts])
    on = np.where((Am >= 1000) & after)[0]
    if len(on):
        w = (T >= T[on[0]]) & (T <= T[on[0]] + 60) & (np.arange(len(T)) < iso_end)
        out["growth_first_hour_after_1000km2"] = {
            "mean_km2_per_min": round(float(np.nanmean(dA[w])), 1) if w.any() else None,
            "mean_radial_spread_ms": round(float(np.nanmean(dr[w])), 1) if w.any() else None,
            "minutes_used": int(w.sum() * np.median(np.diff(T))) if w.any() else 0}
        wi = (np.arange(len(T)) < iso_end) & (T >= T[on[0]] - 30)
        out["max_growth_km2_per_min_isolated"] = round(float(np.nanmax(dA[wi])), 1) if wi.any() else None
    keys = ("area_km2", "extent_major_km", "extent_minor_km", "ellipse_major_km", "ellipse_minor_km", "major_axis_deg",
            "downstream_to_deg", "backshear_km", "upwind_of_radar_core_km", "downstream_reach_km",
            "edge_shared_with_other_anvils", "edge_of_box")
    lifescans = [s for s, ok in zip(scans, life) if ok and s["anvil"]]
    free = [s for s in lifescans if s["anvil"]["edge_shared_with_other_anvils"] <= 0.25 and not s["anvil"]["edge_of_box"]]
    if lifescans:
        k = int(np.argmax([Am[scans.index(s)] for s in lifescans]))
        big = lifescans[k]
        out["largest_anvil_during_radar_life"] = {"utc": big["utc"], "area_km2_median_filtered": round(float(Am[scans.index(big)])),
                                                  **{q: big["anvil"].get(q) for q in keys}}
    if free:
        big = max(free, key=lambda s: Am[scans.index(s)])
        out["largest_mostly_free_anvil"] = {"utc": big["utc"], "area_km2_median_filtered": round(float(Am[scans.index(big)])),
                                            **{q: big["anvil"].get(q) for q in keys}}
        # consistent scans only: the scan's own area within 0.6-1.6x the 5-scan median (drops the flips)
        f1 = [s["anvil"] for s in free if s["anvil"]["area_km2"] >= 1000
              and 0.6 <= s["anvil"]["area_km2"] / max(Am[scans.index(s)], 1) <= 1.6]
        if f1:
            def med(q):
                v = [x[q] for x in f1 if x.get(q) is not None]
                return round(float(np.median(v)), 1) if v else None
            ax = np.radians([2 * x["major_axis_deg"] for x in f1])
            dirs = np.radians([x["downstream_to_deg"] for x in f1 if x.get("downstream_to_deg") is not None])
            bs = [x["backshear_km"] for x in f1 if x.get("backshear_km") is not None]
            out["shape_mostly_free_scans_over_1000km2"] = {
                "scans": len(f1), "extent_major_km_median": med("extent_major_km"), "extent_minor_km_median": med("extent_minor_km"),
                "aspect_ratio_median": round(float(np.median([x["extent_major_km"] / max(x["extent_minor_km"], 1) for x in f1])), 2),
                "major_axis_deg_mean": round(float(np.degrees(np.arctan2(np.sin(ax).mean(), np.cos(ax).mean())) / 2 % 180)),
                "downstream_to_deg_mean": round(float(np.degrees(np.arctan2(np.sin(dirs).mean(), np.cos(dirs).mean())) % 360)) if len(dirs) else None,
                "downstream_direction_consistency": round(float(np.hypot(np.sin(dirs).mean(), np.cos(dirs).mean())), 2) if len(dirs) else None,
                "backshear_km_median_p90": [round(float(np.median(bs)), 1), round(float(np.percentile(bs, 90)), 1)] if bs else None,
                "downstream_reach_km_median": med("downstream_reach_km"),
                "upwind_of_radar_core_km_median": med("upwind_of_radar_core_km")}
            up = [s["updraft_from_radar_core_km"] for s in free if s["anvil"]["area_km2"] >= 1000
                  and 0.6 <= s["anvil"]["area_km2"] / max(Am[scans.index(s)], 1) <= 1.6]
            out["cold_top_from_radar_core_km_median"] = round(float(np.median(up)), 1)
    # OTs while the storm had an anvil (>= 1000 km2) and the radar still had it
    mature = [s for s, x, ok in zip(scans, Am, life) if x >= 1000 and ok]
    if mature:
        with_ot = [s for s in mature if s["ots"]]
        nearest_ot = [min(s["ots"], key=lambda o: o["bt_k"]) for s in with_ot]
        runs, cur = [], 0
        for s in mature:
            if s["ots"]:
                cur += 1
            elif cur:
                runs.append(cur); cur = 0
        if cur:
            runs.append(cur)
        cad = np.median(np.diff([datetime.fromisoformat(s["utc"]).timestamp() for s in mature])) / 60 if len(mature) > 1 else a.every
        out["overshooting_tops"] = {
            "scans_considered": len(mature), "fraction_of_scans_with_ot": round(len(with_ot) / len(mature), 2),
            "episodes": len(runs), "median_episode_min": round(float(np.median(runs) * cad), 1) if runs else None,
            "longest_episode_min": round(float(max(runs) * cad), 1) if runs else None,
            "diam_km_median_max": [round(float(np.median([o["diam_km"] for o in nearest_ot])), 1),
                                   round(float(max(o["diam_km"] for o in nearest_ot)), 1)] if nearest_ot else None,
            "depth_k_median_max": [round(float(np.median([o["depth_k"] for o in nearest_ot])), 1),
                                   round(float(max(o["depth_k"] for o in nearest_ot)), 1)] if nearest_ot else None,
            "note": "diameter is resolution-limited: one 2 km ABI pixel is ~2.5-3.5 km here, so OTs under ~5 km look ~5 km"}
    return out


def quicklooks(res, a, lat0, lon0, tt, la, lo):
    import matplotlib  # noqa: PLC0415
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt  # noqa: PLC0415
    scans = res["scans"]; s = res["summary"]
    withan = [x for x in scans if x["anvil"]]
    if not withan:
        return
    def at(t):
        return min(withan, key=lambda x: abs((datetime.fromisoformat(x["utc"]) - t).total_seconds()))
    early = next((x for x in withan if x["anvil"]["area_km2"] >= 1000), withan[0])
    big = s.get("largest_mostly_free_anvil") or s.get("largest_anvil_during_radar_life")
    t_end = tt[-1]
    picks = [at(datetime.fromisoformat(early["utc"]) + timedelta(minutes=15)),
             at(datetime.fromisoformat(big["utc"])) if big else withan[len(withan) // 2], at(t_end)]
    # three distinct moments: if two picks fall within 20 min, use the scan halfway between the first and second instead
    t_ = [datetime.fromisoformat(x["utc"]) for x in picks]
    if abs((t_[2] - t_[1]).total_seconds()) < 1200:
        picks = [picks[0], at(t_[0] + (t_[1] - t_[0]) / 2), picks[1]]
    picks = list({x["utc"]: x for x in picks}.values())
    trx, try_ = local_xy(np.array(la), np.array(lo), lat0, lon0)
    for x in picks:
        st = datetime.fromisoformat(x["utc"])
        key13 = next(iter(keys_for({"M1": "CMIPM1", "M2": "CMIPM2", "CONUS": "CMIPC"}[x["sector"]], 13,
                                   st - timedelta(seconds=20), st + timedelta(seconds=20)).values()))
        bt, X, Y, area = crop(get(key13), lat0, lon0, a.height_km)
        _, basin = measure(bt, X, Y, area, *x["storm_xy_km"], others=x.get("radar_other_cells_xy_km"), want_mask=True)
        sun = x["solar_zenith_deg"] < 85
        fig, axs = plt.subplots(1, 2 if sun else 1, figsize=(15 if sun else 8, 7), squeeze=False)
        ax = axs[0, 0]
        im = ax.pcolormesh(X, Y, bt - 273.15, cmap="gist_ncar_r", vmin=-80, vmax=20, shading="auto")
        ax.contour(X, Y, bt, levels=[ANVIL_K], colors="k", linewidths=1.0)
        if basin is not None:
            ax.contour(X, Y, basin.astype(float), levels=[0.5], colors="w", linewidths=1.8, linestyles="--")
        fig.colorbar(im, ax=ax, label="band 13 cloud-top temperature (C)", fraction=0.046)
        for o in x["ots"]:
            ax.plot(o["x"], o["y"], "m^", ms=9, mec="k")
        ax.plot(trx, try_, "w-", lw=1.5); ax.plot(*x["storm_xy_km"], "wo", mec="k")
        ax.set_xlim(x["storm_xy_km"][0] - 200, x["storm_xy_km"][0] + 200); ax.set_ylim(x["storm_xy_km"][1] - 200, x["storm_xy_km"][1] + 200)
        ax.set_aspect("equal"); ax.set_xlabel("km east"); ax.set_ylabel("km north")
        an = x["anvil"]
        ax.set_title(f"IR {st:%H:%M}Z  coldest {x['coldest_c']} C  anvil {an['area_km2']} km2, "
                     f"{an['extent_major_km']:.0f}x{an['extent_minor_km']:.0f} km\nblack -50 C, white dashed = this storm's anvil, OT = magenta")
        if sun:
            key2 = next(iter(keys_for({"M1": "CMIPM1", "M2": "CMIPM2", "CONUS": "CMIPC"}[x["sector"]], 2,
                                      st - timedelta(seconds=20), st + timedelta(seconds=20)).values()), None)
            if key2:
                c2 = crop(get(key2), lat0, lon0, a.height_km, half_km=220)
                if c2:
                    v, X2, Y2, _ = c2
                    ax2 = axs[0, 1]
                    ax2.pcolormesh(X2, Y2, np.clip(v, 0, 1) ** 0.6, cmap="gray", vmin=0, vmax=1, shading="auto")
                    ax2.contour(X, Y, bt, levels=[ANVIL_K], colors="c", linewidths=0.8)
                    ax2.plot(trx, try_, "y-", lw=1)
                    ax2.set_xlim(ax.get_xlim()); ax2.set_ylim(ax.get_ylim()); ax2.set_aspect("equal")
                    ax2.set_title(f"visible (band 2, 0.5 km) {st:%H:%M}Z, sun zenith {x['solar_zenith_deg']:.0f} deg")
        fig.suptitle(f"{a.case}  GOES-16 {x['sector']}  (navigated at {a.height_km:.0f} km: parallax-corrected)")
        p = OUT / f"{a.case}_{st:%H%M}.png"
        fig.savefig(p, dpi=90, bbox_inches="tight"); plt.close(fig)
        print(f"  quick-look {p.name}")


if __name__ == "__main__":
    main()
