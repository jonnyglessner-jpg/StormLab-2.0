"""Scan a radar over a time window and track the strongest low-level cell near a point.

Used to pin down when and where a photographed storm was before measuring it with fetch_nexrad.py.

  python tools/scan_nexrad.py --site KVNX --start 2022-05-13T22:00 --end 2022-05-14T03:00 --lat 37.0 --lon -98.04 --radius-km 100 --every 20

For every volume (subsampled to one per --every minutes) prints the time, the strongest 0.5 deg reflectivity within
the radius, its lat/lon, range from the point and bearing from the point. Volumes are cached in data/raw/nexrad.
"""
import argparse
from pathlib import Path
import sys
from datetime import datetime, timedelta

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))   # so it also runs under python -I
from fetch_nexrad import RAW, BUCKET, list_volumes  # noqa: E402
import urllib.request


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--site", required=True); ap.add_argument("--start", required=True); ap.add_argument("--end", required=True)
    ap.add_argument("--lat", type=float, required=True); ap.add_argument("--lon", type=float, required=True)
    ap.add_argument("--radius-km", type=float, default=100); ap.add_argument("--every", type=float, default=20, help="minutes between sampled volumes")
    a = ap.parse_args()
    import pyart  # noqa: PLC0415

    t0 = datetime.strptime(a.start, "%Y-%m-%dT%H:%M"); t1 = datetime.strptime(a.end, "%Y-%m-%dT%H:%M")
    vols = []
    d = t0.replace(hour=0, minute=0)
    while d <= t1:
        vols += [v for v in list_volumes(a.site, d) if t0 <= v[0] <= t1]
        d += timedelta(days=1)
    picked, last = [], None
    for vt, key in sorted(vols):
        if last is None or (vt - last).total_seconds() >= a.every * 60:
            picked.append((vt, key)); last = vt
    RAW.mkdir(parents=True, exist_ok=True)
    print(f"{len(picked)} volumes from {a.site}; point {a.lat}, {a.lon}; radius {a.radius_km} km")
    print("time UTC   strongest weather cells within the radius: dBZ at lat,lon (distance, bearing from the point)")
    for vt, key in picked:
        f = RAW / key.split("/")[-1]
        if not f.exists():
            urllib.request.urlretrieve(BUCKET + key, f)
        r = pyart.io.read_nexrad_archive(str(f))
        rlat, rlon = r.latitude["data"][0], r.longitude["data"][0]
        sl = r.get_slice(0); z = r.fields["reflectivity"]["data"][sl]
        gx, gy, _ = r.get_gate_x_y_z(0)
        lat = rlat + np.degrees(gy / 6371000.0); lon = rlon + np.degrees(gx / (6371000.0 * np.cos(np.radians(rlat))))
        dx = (lon - a.lon) * 111.2 * np.cos(np.radians(a.lat)); dy = (lat - a.lat) * 111.2
        dist = np.hypot(dx, dy)
        # weather only: correlation coefficient >= 0.85 drops ground clutter, wind farms and terrain (hail cores
        # can dip to ~0.85; biological targets are below 0.8)
        cc = r.fields.get("cross_correlation_ratio")
        bad = dist > a.radius_km
        if cc is not None:
            ccv = cc["data"][sl]
            bad = bad | np.ma.getmaskarray(ccv) | (ccv.filled(0) < 0.85)
        zz = np.ma.masked_where(bad | (z.filled(-99) < 40), z)
        if zz.count() == 0:
            print(f"{vt:%H:%M}      no weather echo >= 40 dBZ"); continue
        # up to three distinct cells (peaks at least 15 km apart)
        flat = np.argsort(zz.filled(-99), axis=None)[::-1]
        cells = []
        for idx in flat[:4000]:
            i = np.unravel_index(idx, z.shape)
            if zz.mask[i]:
                break
            if all(np.hypot(dx[i] - dx[c], dy[i] - dy[c]) > 15 for c in cells):
                cells.append(i)
            if len(cells) == 3:
                break
        parts = []
        for i in cells:
            brg = (np.degrees(np.arctan2(dx[i], dy[i])) + 360) % 360
            parts.append(f"{float(z[i]):4.0f} dBZ at {lat[i]:.2f},{lon[i]:.2f} ({dist[i]:4.0f} km, {brg:3.0f} deg)")
        print(f"{vt:%H:%M}   " + "  |  ".join(parts))

if __name__ == "__main__":
    main()
