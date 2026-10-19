"""Fetch a NEXRAD Level II volume from the public AWS archive (Unidata mirror) and measure the supercell in it.

  python tools/fetch_nexrad.py --site KDDC --time 2016-05-24T23:30 --case ddc_20160524 [--lat 37.6 --lon -100.1]

Downloads the volume scan nearest the requested time (anonymous HTTPS, no account), then with Py-ART:
  - grids reflectivity to a 1 km x 1 km x 500 m Cartesian box around the storm (nearest the given lat/lon, or the
    reflectivity maximum)
  - measures: max reflectivity and its height, 18 / 30 / 50 dBZ echo tops, the areas of the 40 / 50 / 55 / 60 dBZ
    cores at 1 km and 4 km AGL (FFD and hail core footprints), the bounded weak echo region (a reflectivity minimum
    at 3-7 km AGL capped by stronger echo above: the vault / BWER and its depth), and the storm-relative velocity
    couplet strength at the lowest tilt (mesocyclone rotational velocity and diameter)
  - writes data/derived/radar/<case>_<site>_<time>.json and a quick-look PNG (0.5 deg reflectivity + velocity,
    and an east-west cross-section through the updraft)

The numbers feed DESIGN.md's feature calibration table (vault depth, core sizes, meso diameter).
"""
import argparse
import json
import re
import urllib.request
from datetime import datetime
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "data" / "raw" / "nexrad"
DER = ROOT / "data" / "derived" / "radar"
BUCKET = "https://unidata-nexrad-level2.s3.amazonaws.com/"   # Unidata mirror of the NOAA archive; the NOAA bucket refuses anonymous listing


def list_volumes(site, t):
    url = f"{BUCKET}?list-type=2&prefix={t:%Y/%m/%d}/{site}/"
    xml = urllib.request.urlopen(url, timeout=60).read().decode()
    keys = re.findall(r"<Key>(.*?)</Key>", xml)
    out = []
    for k in keys:
        m = re.search(rf"{site}(\d{{8}})_(\d{{6}})", k)
        if m and not k.endswith("_MDM"):
            out.append((datetime.strptime(m.group(1) + m.group(2), "%Y%m%d%H%M%S"), k))
    return sorted(out)


def fetch(site, t):
    vols = list_volumes(site, t)
    if not vols:
        raise SystemExit("no volumes listed")
    vt, key = min(vols, key=lambda x: abs((x[0] - t).total_seconds()))
    RAW.mkdir(parents=True, exist_ok=True)
    f = RAW / Path(key).name
    if not f.exists():
        print(f"  downloading {key}")
        urllib.request.urlretrieve(BUCKET + key, f)
    return f, vt


def measure(f, lat0, lon0, case, site, vt, search_km=None):
    import pyart  # noqa: PLC0415
    import matplotlib  # noqa: PLC0415
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt  # noqa: PLC0415

    radar = pyart.io.read_nexrad_archive(str(f))
    rlat, rlon = radar.latitude["data"][0], radar.longitude["data"][0]
    # storm location: given, or the column-max reflectivity on the lowest sweep
    sw0 = radar.get_slice(0)
    ref0 = radar.fields["reflectivity"]["data"][sw0]
    gx, gy, _ = radar.get_gate_x_y_z(0)
    if lat0 is None:
        i = np.unravel_index(np.ma.argmax(ref0), ref0.shape)
        sx, sy = float(gx[i]), float(gy[i])
    else:
        sx, sy = _xy_from_latlon(lat0, lon0, rlat, rlon)
        if search_km:
            near = np.hypot(gx - sx, gy - sy) <= search_km * 1000
            masked = np.ma.masked_where(~near, ref0)
            i = np.unravel_index(np.ma.argmax(masked), ref0.shape)
            sx, sy = float(gx[i]), float(gy[i])
            print(f"  strongest low-level echo within {search_km} km: {float(ref0[i]):.0f} dBZ")
    print(f"  storm at x={sx/1000:.1f} km, y={sy/1000:.1f} km from {site}")
    half, dz, zt = 30000, 500, 18000
    grid = pyart.map.grid_from_radars(
        radar, grid_shape=(int(zt / dz) + 1, int(2 * half / 1000) + 1, int(2 * half / 1000) + 1),
        grid_limits=((0, zt), (sy - half, sy + half), (sx - half, sx + half)),
        fields=["reflectivity"], weighting_function="Barnes2", roi_func="dist_beam", min_radius=1000.0)
    Z = grid.fields["reflectivity"]["data"].filled(-32)           # (z, y, x)
    zs = grid.z["data"]; ys = grid.y["data"]; xs = grid.x["data"]
    cmax = Z.max(axis=0)
    iy, ix = np.unravel_index(np.argmax(cmax), cmax.shape)
    col = Z[:, iy, ix]
    res = {"case": case, "site": site, "volume_time_utc": vt.isoformat(), "file": f.name,
           "storm_xy_km_from_radar": [round(sx / 1000, 1), round(sy / 1000, 1)],
           "storm_latlon": [round(float(rlat + np.degrees(sy / 6371000.0)), 3), round(float(rlon + np.degrees(sx / (6371000.0 * np.cos(np.radians(rlat))))), 3)],
           "range_km": round(float(np.hypot(sx, sy) / 1000), 1),
           "max_dbz": round(float(Z.max()), 1), "max_dbz_height_m": float(zs[int(np.argmax(col))])}
    def top(th):
        any_ = (Z >= th).any(axis=(1, 2)); k = np.where(any_)[0]
        return float(zs[k.max()]) if len(k) else None
    res["echo_top_m"] = {f"{th}dBZ": top(th) for th in (18, 30, 50)}
    for z_agl in (1000, 4000, 7000):
        k = int(round(z_agl / dz)); lvl = Z[k]
        res[f"core_area_km2_at_{z_agl}m"] = {f"{th}dBZ": int((lvl >= th).sum()) for th in (40, 50, 55, 60, 65)}
    # BWER / vault: within 12 km of the max-reflectivity column, look for columns where reflectivity at 3-7 km is
    # at least 15 dBZ below the maximum above it (capped weak echo). Report the deepest such cap.
    best = None
    yy, xx = np.meshgrid(ys, xs, indexing="ij")
    near = np.hypot(yy - ys[iy], xx - xs[ix]) <= 12000
    for j, i in zip(*np.where(near)):
        c = Z[:, j, i]
        for k in range(int(2000 / dz), int(8000 / dz)):
            above = c[k + 1:].max() if k + 1 < len(c) else -32
            if above - c[k] >= 15 and c[k] < 30 and c[: k].max() >= 35 if k > 0 else False:
                # weak echo at k, bounded above and with echo below (a true bounded WER, not just the echo edge)
                depth = 0
                kk = k
                while kk < len(c) - 1 and c[kk] < above - 10:
                    depth += dz; kk += 1
                cand = {"x_km": round(float(xs[i] / 1000), 1), "y_km": round(float(ys[j] / 1000), 1),
                        "base_m": float(zs[k]), "top_m": float(zs[k] + depth), "depth_m": depth, "cap_dbz": round(float(above), 1)}
                if best is None or cand["top_m"] > best["top_m"]:
                    best = cand
                break
    res["bwer_vault"] = best
    # low-level rotation: storm-relative velocity couplet on the lowest tilt within 10 km of the storm
    vel = radar.fields.get("velocity")
    if vel is not None:
        try:
            dv = pyart.correct.dealias_region_based(radar, vel_field="velocity")
            radar.add_field("velocity", dv, replace_existing=True)
            vel = radar.fields["velocity"]
        except Exception as e:  # noqa: BLE001
            print("  dealias failed, using raw velocity:", e)
        # the lowest velocity sweep is usually sweep 1 (split cut); find the first sweep that carries velocity
        for s in range(radar.nsweeps):
            sl = radar.get_slice(s); v = vel["data"][sl]
            if np.ma.count(v) > 0.05 * v.size:
                vx, vy, _ = radar.get_gate_x_y_z(s)
                d = np.hypot(vx - sx, vy - sy)
                m = (d <= 10000) & ~np.ma.getmaskarray(v)
                if m.sum() > 50:
                    vin, vout = float(v[m].min()), float(v[m].max())
                    imin = np.unravel_index(np.ma.argmin(np.ma.masked_where(~m, v)), v.shape)
                    imax = np.unravel_index(np.ma.argmax(np.ma.masked_where(~m, v)), v.shape)
                    diam = float(np.hypot(vx[imin] - vx[imax], vy[imin] - vy[imax]))
                    res["low_level_couplet"] = {"sweep_elev_deg": round(float(radar.fixed_angle["data"][s]), 2),
                                                "vin_ms": round(vin, 1), "vout_ms": round(vout, 1),
                                                "rotational_velocity_ms": round((vout - vin) / 2, 1), "diameter_m": round(diam),
                                                "note": "region-based dealiased; max/min within 10 km of the storm, a first look, not a meso detection"}
                break
    DER.mkdir(parents=True, exist_ok=True)
    stem = DER / f"{case}_{site}_{vt:%H%M}"
    Path(str(stem) + ".json").write_text(json.dumps(res, indent=1))
    print(json.dumps(res, indent=1))
    # quick-look figure
    fig = plt.figure(figsize=(16, 5))
    disp = pyart.graph.RadarDisplay(radar)
    ax1 = fig.add_subplot(131); disp.plot_ppi("reflectivity", 0, vmin=-10, vmax=75, ax=ax1, colorbar_label="dBZ")
    ax1.set_xlim((sx - half) / 1000, (sx + half) / 1000); ax1.set_ylim((sy - half) / 1000, (sy + half) / 1000)
    if vel is not None:
        ax2 = fig.add_subplot(132); disp.plot_ppi("velocity", 1, vmin=-40, vmax=40, cmap="BuDRd18", ax=ax2, colorbar_label="m/s")
        ax2.set_xlim((sx - half) / 1000, (sx + half) / 1000); ax2.set_ylim((sy - half) / 1000, (sy + half) / 1000)
    ax3 = fig.add_subplot(133)
    im = ax3.pcolormesh(xs / 1000, zs / 1000, Z[:, iy, :], vmin=-10, vmax=75, cmap="NWSRef", shading="auto")
    ax3.set_title(f"E-W section through max dBZ (y={ys[iy]/1000:.0f} km)"); ax3.set_xlabel("x km"); ax3.set_ylabel("z km AGL")
    fig.colorbar(im, ax=ax3, label="dBZ")
    fig.suptitle(f"{site} {vt:%Y-%m-%d %H:%M}Z  {case}")
    fig.savefig(str(stem) + ".png", dpi=110, bbox_inches="tight"); plt.close(fig)
    print(f"wrote {stem}.json / .png")


def _xy_from_latlon(lat, lon, rlat, rlon):
    R = 6371000.0
    x = R * np.radians(lon - rlon) * np.cos(np.radians(0.5 * (lat + rlat)))
    y = R * np.radians(lat - rlat)
    return float(x), float(y)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--site", required=True); ap.add_argument("--time", required=True, help="UTC YYYY-MM-DDTHH:MM")
    ap.add_argument("--case", required=True); ap.add_argument("--lat", type=float); ap.add_argument("--lon", type=float)
    ap.add_argument("--search-km", type=float, help="find the storm as the strongest 0.5 deg echo within this radius of --lat/--lon")
    a = ap.parse_args()
    t = datetime.strptime(a.time, "%Y-%m-%dT%H:%M")
    f, vt = fetch(a.site, t)
    print(f"  volume {f.name} at {vt:%H:%M:%S}Z")
    measure(f, a.lat, a.lon, a.case, a.site, vt, a.search_km)


if __name__ == "__main__":
    main()
