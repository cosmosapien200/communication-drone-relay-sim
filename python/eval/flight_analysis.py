"""Flight-controller metrics (SPEC Section 5 deliverables): rise time, overshoot, settling time per axis,
fast-vs-full comparison, gust response and waypoint-tracking error.

usage: python flight_analysis.py results/flight docs/results
"""
import os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(p):
    d = np.genfromtxt(p, delimiter=",", names=True)
    return {k: d[k] for k in d.dtype.names}


def step_metrics(t, cmd, y, t0=1.0):
    m = t >= t0
    t, y, target = t[m] - t0, y[m], cmd[m][-1]
    y0 = y[0]
    span = target - y0
    n = (y - y0) / span
    i10 = np.argmax(n >= 0.1); i90 = np.argmax(n >= 0.9)
    rise = t[i90] - t[i10] if n.max() >= 0.9 else np.nan
    overshoot = max(0.0, (n.max() - 1.0) * 100)
    outside = np.where(np.abs(n - 1.0) > 0.05)[0]
    settle = t[outside[-1] + 1] if len(outside) and outside[-1] + 1 < len(t) else (0.0 if not len(outside) else np.nan)
    sse = abs(1 - n[-50:].mean()) * 100
    return rise, overshoot, settle, sse


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    rows = []
    fig, axs = plt.subplots(1, 4, figsize=(20, 4))
    for k, axis in enumerate(["vx", "vy", "vz", "yaw"]):
        for mode, style in [("Full", "-"), ("Fast", "--")]:
            d = load(os.path.join(src, f"step_{mode}_{axis}.csv"))
            r = step_metrics(d["t"], d["cmd"], d["resp"])
            rows.append((axis, mode) + r + (d["tilt_deg"].max(),))
            axs[k].plot(d["t"], d["resp"], style, label=f"{mode}")
        axs[k].plot(d["t"], d["cmd"], "k:", label="setpoint")
        axs[k].set_title(f"step {axis}"); axs[k].set_xlabel("s"); axs[k].legend()
    fig.tight_layout(); fig.savefig(os.path.join(dst, "flight_steps.png"), dpi=80)

    lines = ["| Axis | Mode | Rise 10-90% (s) | Overshoot (%) | Settling 5% (s) | Steady-state err (%) | Max tilt (deg) |",
             "|---|---|---|---|---|---|---|"]
    for a, m, rise, os_, st, sse, tilt in rows:
        lines.append(f"| {a} | {m} | {rise:.2f} | {os_:.1f} | {st:.2f} | {sse:.1f} | {tilt:.1f} |")

    fig, ax = plt.subplots(1, 2, figsize=(12, 4))
    for mode, style in [("Full", "-"), ("Fast", "--")]:
        g = load(os.path.join(src, f"gust_{mode}.csv"))
        ax[0].plot(g["t"], g["err"], style, label=f"{mode}: max {g['err'].max():.2f} m")
        lines_g = f"Gust (3 m/s peak, 2 s): {mode} max position error {g['err'].max():.2f} m, RMS {np.sqrt((g['err']**2).mean()):.2f} m"
        lines.append(""); lines.append(lines_g)
        s = load(os.path.join(src, f"square_{mode}.csv"))
        ax[1].plot(s["x"], s["z"], style, label=f"{mode}: RMS xtrack {np.sqrt((s['cross_track']**2).mean()):.3f} m")
        lines.append(f"Square 20 m @2 m/s: {mode} cross-track RMS {np.sqrt((s['cross_track']**2).mean()):.3f} m, max {s['cross_track'].max():.3f} m")
    ax[0].plot(g["t"], g["wind"] / 3.0, "k:", label="gust (scaled)"); ax[0].set_title("position hold under gust"); ax[0].legend()
    ax[1].set_title("waypoint tracking (20 m square)"); ax[1].axis("equal"); ax[1].legend()
    fig.tight_layout(); fig.savefig(os.path.join(dst, "flight_gust_square.png"), dpi=80)
    out = "\n".join(lines)
    open(os.path.join(dst, "flight_metrics.md"), "w").write(out + "\n")
    print(out)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
