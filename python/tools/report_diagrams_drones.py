"""Drone / RL diagrams and charts for the Autonomous Drones report -> docs/report_assets/drones/."""
import os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch

OUT = "docs/report_assets/drones"
os.makedirs(OUT, exist_ok=True)
C = {"sim": "#dbe9f6", "vis": "#d9f0d3", "rl": "#fde2c8", "plan": "#ece0f5", "data": "#fff3c4", "io": "#eeeeee"}


def box(ax, x, y, w, h, text, color, fs=8.5, bold=False):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc=color, ec="#444", lw=0.9))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=fs, weight="bold" if bold else "normal")


def arrow(ax, x0, y0, x1, y1, text=None, color="#333", dy=0.12, rad=0.0, fs=7):
    ax.annotate("", xy=(x1, y1), xytext=(x0, y0),
                arrowprops=dict(arrowstyle="-|>", color=color, lw=1.1, shrinkA=2, shrinkB=2, connectionstyle=f"arc3,rad={rad}"))
    if text:
        ax.text((x0 + x1) / 2, (y0 + y1) / 2 + dy, text, ha="center", fontsize=fs, color="#333")


# ---------- RL agent-environment loop (both policies)
fig, ax = plt.subplots(figsize=(11, 5.0)); ax.set_xlim(0, 11); ax.set_ylim(0, 5.0); ax.axis("off")
ax.text(0.1, 4.75, "Navigation policy (cruise at 5 m, through unknown streets)", fontsize=10, weight="bold")
box(ax, 0.1, 2.9, 2.6, 1.6, "Observation (33)\nwaypoint dist. + bearing (2)\nbody velocity (2), heading (2)\n24-ray range ring ≤ 10 m (24)\nprevious action (3)", C["io"], fs=8)
box(ax, 3.1, 3.2, 1.9, 1.0, "PPO actor\nMLP 2 × 256\nGaussian policy,\nclipped to [−1, 1]", C["rl"])
box(ax, 5.4, 3.2, 2.0, 1.0, "Action (3)\nv_fwd, v_side ≤ 3 m/s\nyaw rate ≤ 90°/s", C["io"])
box(ax, 7.8, 3.2, 1.6, 1.0, "Flight\ncontroller", C["sim"])
box(ax, 9.7, 2.9, 1.2, 1.6, "Unity\ntown +\nwind", C["sim"])
arrow(ax, 2.7, 3.7, 3.1, 3.7); arrow(ax, 5.0, 3.7, 5.4, 3.7); arrow(ax, 7.4, 3.7, 7.8, 3.7); arrow(ax, 9.4, 3.7, 9.7, 3.7)
ax.text(5.5, 2.75, "reward: path-progress (Σ≈+1) − 5·10⁻⁴/step − wall-proximity − jerk;  −1 collision, +1 waypoint reached",
        ha="center", fontsize=7.8, color="#7a3b00")
ax.text(0.1, 2.25, "Landing policy (descent from 3–6 m onto the chosen patch)", fontsize=10, weight="bold")
box(ax, 0.1, 0.45, 2.6, 1.6, "Observation (10)\npatch offset x, y (2), area (1)\nconfidence (1), clearance (1)\naltitude (1), velocity (3)\nframes since detection (1)", C["vis"], fs=8)
box(ax, 3.1, 0.75, 1.9, 1.0, "PPO actor\nMLP 2 × 128", C["rl"])
box(ax, 5.4, 0.75, 2.0, 1.0, "Action (3)\nv_x, v_y ≤ 1.5 m/s\nv_z ≤ 1 m/s", C["io"])
box(ax, 7.8, 0.75, 1.6, 1.0, "Flight\ncontroller", C["sim"])
box(ax, 9.7, 0.45, 1.2, 1.6, "Gusts\n0–6 m/s\n+ detector\nnoise", C["sim"])
arrow(ax, 2.7, 1.25, 3.1, 1.25); arrow(ax, 5.0, 1.25, 5.4, 1.25); arrow(ax, 7.4, 1.25, 7.8, 1.25); arrow(ax, 9.4, 1.25, 9.7, 1.25)
ax.text(5.5, 0.2, "reward: −0.01·offset − 0.05·excess descent speed − 0.002/step + ΔΦ, Φ = −0.1(h + 2·offset);  +1 + centring bonus on soft touchdown, −1 otherwise",
        ha="center", fontsize=7.8, color="#7a3b00")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_rl_loop.png", dpi=170); plt.close(fig)

# ---------- cascaded flight controller (full mode)
fig, ax = plt.subplots(figsize=(11, 3.4)); ax.set_xlim(0, 11); ax.set_ylim(0, 3.4); ax.axis("off")
box(ax, 0.05, 1.3, 1.35, 1.0, "Velocity\nset-point\n(policy, 10 Hz)", C["rl"], fs=8)
box(ax, 1.75, 1.3, 1.55, 1.0, "Velocity PID\nKp 2.4, Ki 0.9\n(→ desired accel.)", C["sim"], fs=8)
box(ax, 3.65, 1.3, 1.45, 1.0, "Thrust vector\n→ tilt set-point\n(≤ 30°)", C["sim"], fs=8)
box(ax, 5.45, 1.3, 1.35, 1.0, "Attitude P\nKp 7", C["sim"], fs=8)
box(ax, 7.15, 1.3, 1.25, 1.0, "Rate P\nKp 18", C["sim"], fs=8)
box(ax, 8.75, 1.3, 1.0, 1.0, "Mixer\n(4 × 4\ninverse)", C["sim"], fs=8)
box(ax, 10.0, 1.1, 0.95, 1.4, "Rigid\nbody\n1.2 kg\n4 rotors\nτ_m 30 ms", C["io"], fs=7.5)
for x0, x1 in ((1.4, 1.75), (3.3, 3.65), (5.1, 5.45), (6.8, 7.15), (8.4, 8.75), (9.75, 10.0)): arrow(ax, x0, 1.8, x1, 1.8)
ax.annotate("", xy=(2.5, 1.3), xytext=(10.45, 1.1), arrowprops=dict(arrowstyle="-|>", color="#777", lw=1, connectionstyle="arc3,rad=-0.12"))
ax.text(6.3, 0.55, "velocity, attitude and body-rate feedback (500 Hz integration, 2 ms)", ha="center", fontsize=8, color="#555")
ax.annotate("", xy=(10.45, 2.5), xytext=(10.45, 3.2), arrowprops=dict(arrowstyle="-|>", color="#1f77b4", lw=1.2))
ax.text(10.3, 3.2, "wind + gusts", ha="right", fontsize=8, color="#1f77b4")
ax.text(5.5, 3.05, "Full mode (evaluation, demo).  Fast mode (training): first-order velocity lag τ = 0.26 s, accel. limits 4 / 3 m/s², gust gain 0.33 — matched to full mode",
        ha="center", fontsize=8, color="#333")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_cascaded_pid.png", dpi=170); plt.close(fig)

# ---------- mission state machine
fig, ax = plt.subplots(figsize=(11, 3.8)); ax.set_xlim(0, 11); ax.set_ylim(0, 3.8); ax.axis("off")
S = {"DOWNLOAD": (0.1, 1.6), "TRANSIT": (1.9, 1.6), "EXPLORE": (3.7, 1.6), "NAVIGATE": (5.5, 1.6), "LAND": (7.3, 1.6), "UPLOAD": (9.1, 1.6)}
sub = {"DOWNLOAD": "copy shared map", "TRANSIT": "fly to parent relay\n(nav policy)", "EXPLORE": "frontiers with\nRSSI ≥ −74 dBm", "NAVIGATE": "to best spot, hover,\nre-measure RSSI",
       "LAND": "landing policy\nnadir camera", "UPLOAD": "landed RSSI check,\nupload map"}
for k, (x, y) in S.items():
    box(ax, x, y, 1.55, 1.0, f"{k}\n{sub[k]}", C["plan"] if k in ("DOWNLOAD", "UPLOAD") else C["rl"] if k in ("TRANSIT", "NAVIGATE", "LAND") else C["vis"], fs=7.5)
for a, b in (("DOWNLOAD", "TRANSIT"), ("TRANSIT", "EXPLORE"), ("EXPLORE", "NAVIGATE"), ("NAVIGATE", "LAND"), ("LAND", "UPLOAD")):
    arrow(ax, S[a][0] + 1.55, 2.1, S[b][0], 2.1)
ax.annotate("", xy=(5.9, 1.6), xytext=(7.6, 1.6), arrowprops=dict(arrowstyle="-|>", color="#b22", lw=1, connectionstyle="arc3,rad=-0.5"))
ax.text(7.55, 0.95, "patch lost / off patch:\ntake off, next spot", ha="center", fontsize=7, color="#b22")
ax.annotate("", xy=(6.3, 1.6), xytext=(9.5, 1.6), arrowprops=dict(arrowstyle="-|>", color="#b22", lw=1, connectionstyle="arc3,rad=-0.55"))
ax.text(8.6, 0.3, "landed link too weak: take off, next spot", ha="center", fontsize=7, color="#b22")
ax.annotate("", xy=(6.0, 2.6), xytext=(6.6, 2.6), arrowprops=dict(arrowstyle="-|>", color="#b22", lw=1, connectionstyle="arc3,rad=1.6"))
ax.text(6.3, 3.35, "RSSI < −71 dBm: next spot", ha="center", fontsize=7, color="#b22")
ax.text(10.65, 2.75, "camp ≥ −80 dBm → DONE\nelse launch next drone", ha="center", fontsize=7.5, color="#185")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_mission_fsm.png", dpi=170); plt.close(fig)

# ---------- mission ablations (100 held-out level-2 missions each, calibrated noise model)
rows = [("Full system\n(RL nav + RL land)", 47, 64.4), ("Scripted landing\n(PID servoing)", 42, 61.8), ("Scripted navigation\n(potential field)", 9, 35.0),
        ("Full quadrotor\nflight model", 30, 53.9), ("Spot rule:\nrandom", 25, 52.3), ("Spot rule:\nnearest", 3, 33.5)]
fig, ax = plt.subplots(1, 2, figsize=(10, 3.3), sharey=True)
cols = ["#2c7fb8", "#fdae61", "#d7191c", "#7b3294", "#a6a6a6", "#636363"]
ys = list(range(len(rows)))[::-1]
labels = [r[0].replace("\n", " ") for r in rows]
for a, k, t, xl in ((ax[0], 1, "Mission success", "camp covered (%)"), (ax[1], 2, "Range extended", "range extended toward camp (m)")):
    a.barh(ys, [r[k] for r in rows], color=cols); a.set_title(t, fontsize=10); a.set_xlabel(xl); a.grid(axis="x", alpha=0.3)
    for y, r in zip(ys, rows): a.text(r[k] + 0.8, y, f"{r[k]:.0f}" if k == 1 else f"{r[k]:.1f}", va="center", fontsize=8)
    a.set_xlim(0, max(r[k] for r in rows) * 1.15)
ax[0].set_yticks(ys); ax[0].set_yticklabels(labels, fontsize=8)
fig.tight_layout(); fig.savefig(f"{OUT}/fig_mission_ablation.png", dpi=160); plt.close(fig)
print("ok")
