"""Follow one storm through its whole radar life: first 40 dBZ echo to death, merger or leaving range.

  python tools/radar_lifecycle.py --case lp_manchester_20220513 --site KICT --anchor 2022-05-14T00:54 --lat 37.03 --lon -98.16

Starting from a volume where the storm is known (--anchor time, --lat/--lon near its core), the tool steps through
every volume forward and backward in time. In each volume it grids the CC-filtered reflectivity of all tilts to a 1 km
box around the predicted position (column maximum = composite) and picks the composite >= 40 dBZ cell nearest the
prediction. Per volume it records time, position, max dBZ, 18 / 30 / 50 dBZ echo tops over the cell (beam-centre
heights above the radar), the cell's 40 dBZ area at the lowest tilt and in the composite, range from the radar, and
storm motion (smoothed over ~20 min). It stops when the cell is gone for --gap volumes in a row, when it merges with
a comparable cell, when its core collapses and a new tall core takes over beside it, or beyond --max-range-km. Going
backward through a split, it follows the parent storm.

Writes validation/real-storm-cases/radar/<case>_lifecycle.json (+ a PNG: track map and time series). Each volume also lists the
other composite >= 40 dBZ cells in the 160 km box (fetch_goes.py uses them to split a shared anvil between storms). Initiation is the first volume
with a 40 dBZ echo anywhere in the column; "mature" is the time of the highest 50 dBZ echo top (ties: the largest
composite 40 dBZ area); death is the last volume with 40 dBZ. The end type is "died" (40 dBZ gone), "merged",
"collapsed_new_cell_on_outflow" or "left_range"; for "died" the end-of-life metrics (last-hour trends of max dBZ, tops, area, speed, and how many new
cells formed within 50 km) say whether it weakened in place or was undercut by outflow - check the PNG.
"""
import argparse
import json
import sys
import urllib.request
from datetime import datetime, timedelta
from pathlib import Path

import numpy as np
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).resolve().parent))   # so it also runs under python -I
from fetch_nexrad import RAW, DER, BUCKET, list_volumes  # noqa: E402

R_E = 6371.0
HALF = 80          # km, half-width of the analysis box
MIN_AREA = 4       # km2: smallest composite 40 dBZ cell that counts


def ll_to_xy(lat, lon, lat0, lon0):
    return (np.radians(lon - lon0) * R_E * np.cos(np.radians(0.5 * (lat + lat0))), np.radians(lat - lat0) * R_E)


def xy_to_ll(x, y, lat0, lon0):
    lat = lat0 + np.degrees(y / R_E)
    return lat, lon0 + np.degrees(x / (R_E * np.cos(np.radians(0.5 * (lat + lat0)))))


def volumes(site, t0, t1):
    out, d = [], t0.replace(hour=0, minute=0, second=0)
    while d <= t1:
        out += [v for v in list_volumes(site, d) if t0 <= v[0] <= t1]
        d += timedelta(days=1)
    return sorted(set(out))


def load(key):
    f = RAW / key.split("/")[-1]
    if not f.exists():
        RAW.mkdir(parents=True, exist_ok=True)
        urllib.request.urlretrieve(BUCKET + key, f)
    return f


def grid_volume(f, cx, cy):
    """Grid one volume to a 1 km box centred on (cx, cy) km from the radar.

    Returns composite dBZ, lowest-tilt dBZ, max height (km above radar) of >=18/30/50 dBZ gates, and the radar lat/lon.
    """
    import pyart  # noqa: PLC0415
    r = pyart.io.read_nexrad_archive(str(f), include_fields=["reflectivity", "cross_correlation_ratio"])
    n = 2 * HALF
    comp = np.full((n, n), -99.0); low = np.full((n, n), -99.0)
    tops = {th: np.full((n, n), np.nan) for th in (18, 30, 50)}
    seen_low = False
    for s in range(r.nsweeps):
        sl = r.get_slice(s)
        z = r.fields["reflectivity"]["data"][sl]
        if np.ma.count(z) < 0.01 * z.size:
            continue
        gx, gy, gz = r.get_gate_x_y_z(s)
        ix = np.floor((gx / 1000 - cx + HALF)).astype(int); iy = np.floor((gy / 1000 - cy + HALF)).astype(int)
        zz = z.filled(-99.0)
        cc = r.fields.get("cross_correlation_ratio")
        if cc is not None:
            ccv = cc["data"][sl].filled(0)
            # weather: CC >= 0.8; let very high dBZ through down to 0.7 (giant hail drops CC); clutter is lower
            bad = (ccv < 0.8) & ~((zz >= 55) & (ccv >= 0.7))
            zz = np.where(bad, -99.0, zz)
        m = (ix >= 0) & (ix < n) & (iy >= 0) & (iy < n) & (zz > -30)
        if not m.any():
            continue
        np.maximum.at(comp, (iy[m], ix[m]), zz[m])
        if not seen_low:
            np.maximum.at(low, (iy[m], ix[m]), zz[m]); seen_low = True
        for th, a in tops.items():
            mm = m & (zz >= th)
            if mm.any():
                h = np.full((n, n), -1.0)
                np.maximum.at(h, (iy[mm], ix[mm]), gz[mm] / 1000)
                a[:] = np.where(h > 0, np.fmax(a, h), a)
    return comp, low, tops, float(r.latitude["data"][0]), float(r.longitude["data"][0])


def find_cell(comp, low, tops, px, py, cx, cy, search, core_km):
    """Composite >= 40 dBZ cell nearest (px, py) whose core lies within core_km of it; (dict or None, label image)."""
    lab, nl = ndimage.label(comp >= 40)
    if nl == 0:
        return None, lab
    xs = cx - HALF + 0.5 + np.arange(comp.shape[1]); ys = cy - HALF + 0.5 + np.arange(comp.shape[0])
    X, Y = np.meshgrid(xs, ys)
    best, bd = None, 1e9
    for k in range(1, nl + 1):
        mk = lab == k
        if mk.sum() < MIN_AREA:
            continue
        d = np.hypot(X[mk] - px, Y[mk] - py).min()     # distance to the nearest part of the cell
        if d < bd:
            best, bd = k, d
    if best is None or bd > search:
        return None, lab
    mk = lab == best
    zmax = comp[mk].max()
    # position: the core (>= max-10 dBZ) sub-cell nearest the prediction, dBZ-weighted centroid
    sub, ns = ndimage.label(mk & (comp >= max(40.0, zmax - 10)))
    kbest = min(range(1, ns + 1), key=lambda k: np.hypot(X[sub == k] - px, Y[sub == k] - py).min())
    core = sub == kbest
    w = 10 ** (comp[core] / 10)
    x, y = float((X[core] * w).sum() / w.sum()), float((Y[core] * w).sum() / w.sum())
    if np.hypot(x - px, y - py) > core_km:      # the nearest cell's core is too far away: a neighbour, not ours
        return None, lab
    def top(th):
        v = tops[th][mk]
        return round(float(np.nanmax(v)), 1) if np.isfinite(v).any() else None
    cell = {"x": x, "y": y, "max_dbz": round(float(zmax), 1), "low_max_dbz": round(float(low[mk].max()), 1),
            "area40_low_km2": int((mk & (low >= 40)).sum()), "area40_comp_km2": int(mk.sum()),
            "area50_comp_km2": int((mk & (comp >= 50)).sum()),
            "top18_km": top(18), "top30_km": top(30), "top50_km": top(50), "label": best}
    return cell, lab


def other_cells(lab, comp, cx, cy, exclude):
    xs = cx - HALF + 0.5 + np.arange(comp.shape[1]); ys = cy - HALF + 0.5 + np.arange(comp.shape[0])
    X, Y = np.meshgrid(xs, ys)
    out = []
    for k in range(1, lab.max() + 1):
        mk = lab == k
        if k == exclude or mk.sum() < MIN_AREA:
            continue
        out.append({"x": float(X[mk].mean()), "y": float(Y[mk].mean()), "area": int(mk.sum())})
    return out


def motion(track, i, half_min=12):
    """Storm motion at track[i] from a straight-line fit to positions within +-half_min minutes."""
    t0 = track[i]["t"]
    pts = [(p["t"] - t0).total_seconds() / 3600 for p in track if abs((p["t"] - t0).total_seconds()) <= half_min * 60]
    sel = [p for p in track if abs((p["t"] - t0).total_seconds()) <= half_min * 60]
    if len(sel) < 3:
        return None
    vx = np.polyfit(pts, [p["x"] for p in sel], 1)[0]; vy = np.polyfit(pts, [p["y"] for p in sel], 1)[0]
    spd = float(np.hypot(vx, vy))
    frm = (np.degrees(np.arctan2(vx, vy)) + 180) % 360
    return {"from_deg": round(float(frm)), "kmh": round(spd, 1), "kt": round(spd / 1.852, 1)}


def walk(vols, start_idx, step, px, py, cx, cy, vel, args, log):
    """Track from vols[start_idx] in direction step (+1/-1). Returns list of cell dicts with times, and end reason."""
    track, misses, i, prev = [], 0, start_idx, None
    while 0 <= i < len(vols):
        vt, key = vols[i]
        if prev is not None:
            dt = (vt - prev).total_seconds() / 3600
            px += vel[0] * dt; py += vel[1] * dt
        # keep the analysis box centred near the storm
        if abs(px - cx) > HALF / 2 or abs(py - cy) > HALF / 2:
            cx, cy = round(px), round(py)
        comp, low, tops, rlat, rlon = grid_volume(load(key), cx, cy)
        search = args.search_km + (8 * misses)
        cell, lab = find_cell(comp, low, tops, px, py, cx, cy, search, args.core_km + 5 * misses)
        if cell is not None:
            cell["range_km"] = round(float(np.hypot(cell["x"], cell["y"])), 1)
            cell["rlat"], cell["rlon"] = rlat, rlon
        if cell is not None and track and step > 0:
            # collapse and replacement: the old core had fallen out (no 50 dBZ above 3 km) and a new tall core appears
            # beside it -> a new cell on the old storm's outflow, not the same storm
            lt, ct = track[-1]["top50_km"] or 0, cell["top50_km"] or 0
            if lt < 3 and ct >= 8 and np.hypot(cell["x"] - track[-1]["x"], cell["y"] - track[-1]["y"]) > 3:
                log(f"{vt:%H:%M}  old core collapsed; new {ct} km core {np.hypot(cell['x'] - track[-1]['x'], cell['y'] - track[-1]['y']):.0f} km away: new cell")
                return track, "collapsed_new_cell_on_outflow", cx, cy
        if cell is not None and track:
            last = track[-1]
            others_prev = last.get("_others", [])
            # merger: a comparable cell from the previous volume now lies inside our cell
            def lab_at(o):
                j, i = int(np.floor(o["y"] - cy + HALF)), int(np.floor(o["x"] - cx + HALF))
                return lab[j, i] if 0 <= j < lab.shape[0] and 0 <= i < lab.shape[1] else 0
            inside = [o for o in others_prev if lab_at(o) == cell["label"]]
            big = [o for o in inside if o["area"] >= 0.3 * last["area40_comp_km2"]]
            if big and step > 0:
                cell["merged_with_km2"] = [o["area"] for o in big]
                track.append(dict(cell, t=vt, key=key))
                log(f"{vt:%H:%M}  merged with a cell of {big[0]['area']} km2")
                return track, "merged", cx, cy
            if big and step < 0:
                # going backward, a comparable cell inside ours means ours split off this parent: keep following the
                # parent (its history is the storm's history) and note the split
                cell["split_from_parent_km2"] = cell["area40_comp_km2"]
                log(f"{vt:%H:%M}  (backward) split: our storm and a {big[0]['area']} km2 cell were one parent here")
            elif inside and step > 0:
                cell["absorbed_km2"] = [o["area"] for o in inside]
        if cell is None:
            misses += 1
            log(f"{vt:%H:%M}  no 40 dBZ cell within {search:.0f} km of ({px:.0f},{py:.0f})")
            if misses >= args.gap:
                return track, "died" if step > 0 else "initiated", cx, cy
        else:
            misses = 0
            cell["_others"] = other_cells(lab, comp, cx, cy, cell["label"])
            if track:
                dt = (vt - track[-1]["t"]).total_seconds() / 3600
                if dt != 0:
                    inst = ((cell["x"] - track[-1]["x"]) / dt, (cell["y"] - track[-1]["y"]) / dt)
                    # blend: centroid jumps are noisy; cap the speed at 90 km/h
                    sp = np.hypot(*inst)
                    if sp > 90:
                        inst = (inst[0] * 90 / sp, inst[1] * 90 / sp)
                    vel = (0.6 * vel[0] + 0.4 * inst[0], 0.6 * vel[1] + 0.4 * inst[1])
            px, py = cell["x"], cell["y"]
            track.append(dict(cell, t=vt, key=key))
            log(f"{vt:%H:%M}  {cell['max_dbz']:4.0f} dBZ  top50 {cell['top50_km']}  A40low {cell['area40_low_km2']:4d}"
                f"  A40comp {cell['area40_comp_km2']:4d}  range {cell['range_km']:5.0f} km")
            if cell["range_km"] > args.max_range_km and step > 0:
                return track, "left_range", cx, cy
        prev = vt
        i += step
    return track, "end_of_window", cx, cy


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--case", required=True); ap.add_argument("--site", required=True)
    ap.add_argument("--anchor", required=True, help="UTC YYYY-MM-DDTHH:MM, a time the storm is known")
    ap.add_argument("--lat", type=float, required=True); ap.add_argument("--lon", type=float, required=True)
    ap.add_argument("--hours-before", type=float, default=6); ap.add_argument("--hours-after", type=float, default=6)
    ap.add_argument("--search-km", type=float, default=12, help="max distance from the predicted position to the cell edge")
    ap.add_argument("--core-km", type=float, default=10, help="max distance from the predicted position to the cell's core")
    ap.add_argument("--gap", type=int, default=2, help="volumes in a row without the cell before it counts as gone")
    ap.add_argument("--max-range-km", type=float, default=280)
    ap.add_argument("--motion", default="0,0", help="first-guess motion 'from_deg,kt' for the backward step")
    a = ap.parse_args()

    ta = datetime.strptime(a.anchor, "%Y-%m-%dT%H:%M")
    vols = volumes(a.site, ta - timedelta(hours=a.hours_before), ta + timedelta(hours=a.hours_after))
    ia = min(range(len(vols)), key=lambda k: abs((vols[k][0] - ta).total_seconds()))
    # the radar location from the first file read
    import pyart  # noqa: PLC0415
    r0 = pyart.io.read_nexrad_archive(str(load(vols[ia][1])), include_fields=["reflectivity"])
    rlat, rlon = float(r0.latitude["data"][0]), float(r0.longitude["data"][0])
    px, py = ll_to_xy(a.lat, a.lon, rlat, rlon)
    frm, kt = (float(v) for v in a.motion.split(","))
    sp = kt * 1.852
    vel = (-sp * np.sin(np.radians(frm)), -sp * np.cos(np.radians(frm)))
    print(f"{len(vols)} volumes from {a.site}; anchor {vols[ia][0]:%H:%M}Z at ({px:.0f}, {py:.0f}) km from the radar")
    log = lambda s: print("  " + s, flush=True)  # noqa: E731
    print("forward:")
    fwd, end, _, _ = walk(vols, ia, +1, px, py, round(px), round(py), vel, a, log)
    if not fwd:
        raise SystemExit("storm not found at the anchor time; check --lat/--lon")
    print("backward:")
    v_back = (vel[0], vel[1])
    if len(fwd) >= 3:
        dt = (fwd[2]["t"] - fwd[0]["t"]).total_seconds() / 3600
        v_back = ((fwd[2]["x"] - fwd[0]["x"]) / dt, (fwd[2]["y"] - fwd[0]["y"]) / dt)
    back, start, _, _ = walk(vols, ia - 1, -1, fwd[0]["x"], fwd[0]["y"], round(fwd[0]["x"]), round(fwd[0]["y"]),
                             v_back, a, log)
    track = back[::-1] + fwd
    for i, p in enumerate(track):
        p["motion"] = motion(track, i)

    # summary
    t_init, t_death = track[0]["t"], track[-1]["t"]
    tops = [(p["top50_km"] or 0, p["area40_comp_km2"], i) for i, p in enumerate(track)]
    imat = max(tops)[2]
    mot = [p["motion"] for p in track if p["motion"]]
    def vec_mean(ms):
        if not ms:
            return None
        u = np.mean([-m["kmh"] * np.sin(np.radians(m["from_deg"])) for m in ms])
        v = np.mean([-m["kmh"] * np.cos(np.radians(m["from_deg"])) for m in ms])
        return {"from_deg": round(float((np.degrees(np.arctan2(u, v)) + 180) % 360)), "kmh": round(float(np.hypot(u, v)), 1),
                "kt": round(float(np.hypot(u, v)) / 1.852, 1)}
    net_x, net_y = track[-1]["x"] - track[0]["x"], track[-1]["y"] - track[0]["y"]
    life_h = (t_death - t_init).total_seconds() / 3600
    # end-of-life metrics over the last hour
    last = [p for p in track if (t_death - p["t"]).total_seconds() <= 3600]
    eol = None
    if end in ("died", "collapsed_new_cell_on_outflow") and len(last) >= 3:
        def trend(k):
            v = [p[k] for p in last if p[k] is not None]
            return [v[0], v[-1]] if v else None
        new_cells = 0
        for j in range(1, len(last)):
            prev_cells = last[j - 1].get("_others", [])
            for o in last[j].get("_others", []):
                near = np.hypot(o["x"] - last[j]["x"], o["y"] - last[j]["y"]) <= 50
                fresh = all(np.hypot(o["x"] - q["x"], o["y"] - q["y"]) > 12 for q in prev_cells)
                new_cells += int(near and fresh)
        sp_ = [p["motion"]["kmh"] for p in last if p["motion"]]
        eol = {"max_dbz_first_last": trend("max_dbz"), "top50_km_first_last": trend("top50_km"),
               "area40_low_km2_first_last": trend("area40_low_km2"), "area40_comp_km2_first_last": trend("area40_comp_km2"),
               "speed_kmh_first_last": [sp_[0], sp_[-1]] if sp_ else None,
               "new_cells_within_50km_last_hour": new_cells}
    rlat, rlon = track[0]["rlat"], track[0]["rlon"]
    def ll(p):
        la, lo = xy_to_ll(p["x"], p["y"], rlat, rlon)
        return [round(float(la), 3), round(float(lo), 3)]
    out = {
        "case": a.case, "site": a.site, "radar_latlon": [round(rlat, 3), round(rlon, 3)],
        "method": "composite (all-tilt column max) CC-filtered reflectivity on a 1 km grid; cell = composite >= 40 dBZ "
                  "region nearest the predicted position; position = dBZ-weighted centroid of the core (>= max-10 dBZ); "
                  "tops = highest beam-centre gate at the threshold over the cell, km above radar level; area40_low = "
                  "cell area >= 40 dBZ at the lowest tilt",
        "summary": {
            "first_40dbz_utc": t_init.isoformat(), "start_reason": start,
            "mature_utc": track[imat]["t"].isoformat(), "mature_top50_km": track[imat]["top50_km"],
            "last_40dbz_utc": t_death.isoformat(), "end_reason": end,
            "lifetime_h": round(life_h, 2),
            "max_dbz": max(p["max_dbz"] for p in track),
            "max_top50_km": max((p["top50_km"] or 0) for p in track),
            "max_top18_km": max((p["top18_km"] or 0) for p in track),
            "max_area40_low_km2": max(p["area40_low_km2"] for p in track),
            "max_area40_comp_km2": max(p["area40_comp_km2"] for p in track),
            "mean_motion": vec_mean(mot),
            "net_displacement_km": round(float(np.hypot(net_x, net_y)), 1),
            "net_motion": {"from_deg": round(float((np.degrees(np.arctan2(net_x, net_y)) + 180) % 360)),
                           "kmh": round(float(np.hypot(net_x, net_y)) / max(life_h, 1e-3), 1)},
            "start_latlon": ll(track[0]), "end_latlon": ll(track[-1]),
            "end_of_life": eol,
            "splits_utc": [p["t"].isoformat() for p in track if "split_from_parent_km2" in p],
            "absorbed_small_cells_utc": [p["t"].isoformat() for p in track if "absorbed_km2" in p],
        },
        "volumes": [{"utc": p["t"].isoformat(), "latlon": ll(p), "range_km": p["range_km"], "max_dbz": p["max_dbz"],
                     "low_max_dbz": p["low_max_dbz"], "top18_km": p["top18_km"], "top30_km": p["top30_km"],
                     "top50_km": p["top50_km"], "area40_low_km2": p["area40_low_km2"],
                     "area40_comp_km2": p["area40_comp_km2"], "area50_comp_km2": p["area50_comp_km2"],
                     "motion": p["motion"], **({"absorbed_km2": p["absorbed_km2"]} if "absorbed_km2" in p else {}),
                     **({"merged_with_km2": p["merged_with_km2"]} if "merged_with_km2" in p else {}),
                     **({"split_from_parent": True} if "split_from_parent_km2" in p else {}),
                     "other_cells": [{"latlon": ll(o), "area40_comp_km2": o["area"]} for o in p.get("_others", [])]}
                    for p in track],
    }
    DER.mkdir(parents=True, exist_ok=True)
    fj = DER / f"{a.case}_lifecycle.json"
    fj.write_text(json.dumps(out, indent=1))
    print(json.dumps(out["summary"], indent=1))
    plot(out, track, DER / f"{a.case}_lifecycle.png", a)
    print(f"wrote {fj}")


def plot(out, track, path, a):
    import matplotlib  # noqa: PLC0415
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt  # noqa: PLC0415
    t = [p["t"] for p in track]
    fig, ax = plt.subplots(1, 4, figsize=(20, 4.8))
    # composite at three stages around the storm
    picks = [0, max(range(len(track)), key=lambda i: (track[i]["top50_km"] or 0, track[i]["area40_comp_km2"])), len(track) - 1]
    titles = ["first echo", "mature", "last 40 dBZ"]
    for k, (i, ti) in enumerate(zip(picks, titles)):
        p = track[i]
        comp, *_ = grid_volume(load(p["key"]), round(p["x"]), round(p["y"]))
        ext = [round(p["x"]) - HALF, round(p["x"]) + HALF, round(p["y"]) - HALF, round(p["y"]) + HALF]
        im = ax[k].imshow(np.ma.masked_less(comp, 5), origin="lower", extent=ext, vmin=5, vmax=70, cmap="turbo")
        ax[k].plot([q["x"] for q in track], [q["y"] for q in track], "k-", lw=1)
        ax[k].plot(p["x"], p["y"], "wo", mec="k")
        ax[k].set_title(f"{ti}: {p['t']:%H:%M}Z composite dBZ"); ax[k].set_xlabel("km E of radar"); ax[k].set_ylabel("km N")
    fig.colorbar(im, ax=ax[2], fraction=0.046)
    a3 = ax[3]
    a3.plot(t, [p["max_dbz"] for p in track], "r-", label="max dBZ")
    a3.plot(t, [(p["top50_km"] or 0) * 5 for p in track], "b-", label="50 dBZ top x5 (km)")
    a3.plot(t, [p["area40_low_km2"] / 5 for p in track], "g-", label="40 dBZ area low tilt /5 (km2)")
    a3.legend(fontsize=8); a3.set_title("life cycle"); a3.tick_params(axis="x", rotation=45)
    fig.suptitle(f"{out['case']}  {out['site']}  {track[0]['t']:%Y-%m-%d %H:%M} - {track[-1]['t']:%H:%M}Z  end: {out['summary']['end_reason']}")
    fig.savefig(path, dpi=100, bbox_inches="tight"); plt.close(fig)


if __name__ == "__main__":
    main()
