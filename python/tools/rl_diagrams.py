"""RL architecture diagram and PPO training flowchart -> docs/report_assets/drones/."""
import os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, Polygon

OUT = "docs/report_assets/drones"
os.makedirs(OUT, exist_ok=True)
NAVY, ORANGE, TEAL, INK, MUTED = "#14213d", "#f27f1b", "#1f8a70", "#222b38", "#5f6b7a"
F = {"env": "#dbe9f6", "net": "#fde2c8", "buf": "#fff3c4", "upd": "#ece0f5", "dep": "#d9f0d3", "io": "#eeeeee"}


def box(ax, x, y, w, h, t, c, fs=10, bold=False, ec="#444"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.12", fc=c, ec=ec, lw=1.1))
    ax.text(x + w / 2, y + h / 2, t, ha="center", va="center", fontsize=fs, weight="bold" if bold else "normal", color=INK)


def group(ax, x, y, w, h, label, c):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.2", fc="none", ec=c, lw=2, ls="--"))
    ax.text(x + 0.15, y + h - 0.12, label, ha="left", va="top", fontsize=12, weight="bold", color=c)


def arrow(ax, x0, y0, x1, y1, t=None, c=INK, rad=0.0, fs=9, dx=0, dy=0.13, ha="center"):
    ax.annotate("", xy=(x1, y1), xytext=(x0, y0),
                arrowprops=dict(arrowstyle="-|>", color=c, lw=1.4, shrinkA=2, shrinkB=2, connectionstyle=f"arc3,rad={rad}"))
    if t:
        ax.text((x0 + x1) / 2 + dx, (y0 + y1) / 2 + dy, t, ha=ha, va="center", fontsize=fs, color=c)


# ====================================================================== RL architecture
fig, ax = plt.subplots(figsize=(16, 9.4)); ax.set_xlim(0, 16); ax.set_ylim(0, 9.5); ax.axis("off")


def line(pts, c=INK, lw=1.5):
    xs, ys = zip(*pts)
    ax.plot(xs[:-1], ys[:-1], color=c, lw=lw)
    ax.annotate("", xy=pts[-1], xytext=pts[-2], arrowprops=dict(arrowstyle="-|>", color=c, lw=lw, shrinkA=0, shrinkB=2))


# --- environment
group(ax, 0.2, 0.9, 4.6, 7.8, "ENVIRONMENT  (Unity 6, C#)", "#2c6fb7")
box(ax, 0.5, 6.5, 4.0, 1.4, "32 parallel arenas per process\nprocedural towns · wind & gusts\nfast flight model (50 Hz)", F["env"], 10)
box(ax, 0.5, 4.75, 1.9, 1.45, "NavAgent\nobs 33: 24 rays,\nwaypoint, vel.,\nheading, last a", F["env"], 9)
box(ax, 2.6, 4.75, 1.9, 1.45, "LandAgent\nobs 10: patch\noffset, area, conf.,\nclearance, alt., vel.", F["env"], 9)
box(ax, 0.5, 3.0, 4.0, 1.45, "Reward function\nnav: path progress, −1 crash, +1 goal\nland: offset/speed, shaping ΔΦ, ±1 outcome", F["env"], 9)
box(ax, 0.5, 1.2, 4.0, 1.5, "Flight controller\nvelocity set-point → motion\n(cascaded PID in full mode)", F["env"], 9)
arrow(ax, 2.5, 2.7, 2.5, 3.0); arrow(ax, 1.45, 4.45, 1.45, 4.75); arrow(ax, 3.55, 4.45, 3.55, 4.75)

# --- trainer
group(ax, 5.4, 0.9, 7.0, 7.8, "PPO TRAINER  (Python, PyTorch)", ORANGE)
box(ax, 5.7, 6.85, 2.0, 1.0, "Obs normaliser\n(running μ, σ²)", F["io"], 9)
box(ax, 8.1, 7.0, 2.0, 0.9, "Actor MLP\n2×256 (nav) / 2×128", F["net"], 9, bold=True)
box(ax, 10.3, 7.0, 1.9, 0.9, "π(a|s) = N(μ, σ)\nsample, clip", F["net"], 9)
box(ax, 8.1, 5.85, 2.0, 0.9, "Critic MLP\nV(s)", F["net"], 9, bold=True)
box(ax, 5.7, 4.35, 3.0, 1.1, "Trajectory buffer\n(s, a, log π, V, r) per agent", F["buf"], 9)
box(ax, 9.1, 4.35, 3.1, 1.1, "GAE (γ, λ = 0.95)\nadvantages Â, returns R", F["buf"], 9)
box(ax, 5.7, 2.45, 6.5, 1.45, "PPO update  ·  K = 3 epochs × minibatches  →  new actor θ, critic φ\nL = −min(r·Â, clip(r, 1±ε)·Â) + 0.5·(V − R)²  − β·H[π]\nAdam, lr 3·10⁻⁴, ε = 0.2, β = 3·10⁻³", F["upd"], 10)
box(ax, 5.7, 1.15, 3.0, 0.95, "Curriculum manager\nlesson ↑ when reward ≥ threshold", F["io"], 9)
box(ax, 9.1, 1.15, 3.1, 0.95, "Checkpoints · TensorBoard\nreward, success, losses", F["io"], 9)
arrow(ax, 7.7, 7.35, 8.1, 7.45); arrow(ax, 7.7, 7.2, 8.1, 6.3); arrow(ax, 10.1, 7.45, 10.3, 7.45)
arrow(ax, 9.1, 5.85, 8.0, 5.45); arrow(ax, 8.7, 4.9, 9.1, 4.9)
arrow(ax, 10.65, 4.35, 10.65, 3.9)

# --- env <-> trainer
arrow(ax, 4.5, 7.45, 5.7, 7.45, "observations s_t", c="#2c6fb7")
line([(11.25, 7.9), (11.25, 9.05), (2.5, 9.05), (2.5, 7.9)], c=ORANGE)
ax.text(6.9, 9.2, "actions a_t  (3 velocity commands, every 0.1 s)", ha="center", fontsize=10.5, color=ORANGE)
arrow(ax, 4.5, 3.75, 5.7, 4.6, c="#2c6fb7")
ax.text(5.0, 3.55, "reward r_t,\ndone", ha="center", fontsize=9, color="#2c6fb7")
line([(5.7, 1.62), (5.05, 1.62), (5.05, 6.85), (4.5, 6.85)], c=MUTED)
ax.text(5.12, 2.25, "env params\n(level, gust,\nnoise)", ha="left", fontsize=8, color=MUTED)

# --- deployment
group(ax, 12.9, 0.9, 2.9, 7.8, "DEPLOYMENT", TEAL)
box(ax, 13.15, 6.3, 2.4, 1.2, "Export ONNX\n(actor + normaliser,\ndeterministic μ)", F["dep"], 9)
box(ax, 13.15, 4.4, 2.4, 1.4, "Unity Inference\nEngine (CPU)\nPolicyRunner", F["dep"], 9)
box(ax, 13.15, 2.2, 2.4, 1.7, "Mission planner\ncalls nav / land\npolicy at 10 Hz\n(live demo)", F["dep"], 9)
line([(12.2, 3.2), (12.65, 3.2), (12.65, 6.9), (13.15, 6.9)], c=TEAL)
arrow(ax, 14.35, 6.3, 14.35, 5.8, c=TEAL); arrow(ax, 14.35, 4.4, 14.35, 3.9, c=TEAL)
ax.text(8.0, 0.35, "On-policy actor–critic: collect experience with the current policy in 32 arenas → estimate advantages → clipped policy update → repeat",
        ha="center", fontsize=11, color=MUTED, style="italic")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_rl_architecture.png", dpi=150); plt.close(fig)


# ====================================================================== PPO training flowchart
fig, ax = plt.subplots(figsize=(16, 9)); ax.set_xlim(0, 16); ax.set_ylim(0, 9); ax.axis("off")


def term(x, y, w, h, t, c=NAVY):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.4", fc=c, ec=c))
    ax.text(x + w / 2, y + h / 2, t, ha="center", va="center", fontsize=11, color="white", weight="bold")


def diamond(cx, cy, w, h, t):
    ax.add_patch(Polygon([(cx, cy + h / 2), (cx + w / 2, cy), (cx, cy - h / 2), (cx - w / 2, cy)], closed=True, fc="#fde2c8", ec="#444", lw=1.1))
    ax.text(cx, cy, t, ha="center", va="center", fontsize=9.5, color=INK)


BW, BH = 3.2, 0.95
# column 1: setup + rollout
term(0.6, 7.75, BW, 0.75, "START")
box(ax, 0.6, 6.45, BW, BH, "Load YAML config\ninit actor, critic, normaliser", F["io"])
box(ax, 0.6, 5.15, BW, BH, "Launch Unity build\n32 arenas · set lesson params", F["env"])
box(ax, 0.6, 3.85, BW, BH, "Observe s_t from every agent\n(normalise)", F["env"])
box(ax, 0.6, 2.55, BW, BH, "Sample a_t ~ π_θ(·|s_t)\nstore log π, V(s_t)", F["net"])
box(ax, 0.6, 1.25, BW, BH, "Step environment\nreceive r_t, s_t+1, done", F["env"])
for y0, y1 in ((7.75, 7.4), (6.45, 6.1), (5.15, 4.8), (3.85, 3.5), (2.55, 2.2)):
    arrow(ax, 2.2, y0, 2.2, y1)
def poly(pts, c=INK, label=None, lx=None, ly=None):
    xs, ys = zip(*pts)
    ax.plot(xs[:-1], ys[:-1], color=c, lw=1.4)
    ax.annotate("", xy=pts[-1], xytext=pts[-2], arrowprops=dict(arrowstyle="-|>", color=c, lw=1.4, shrinkA=0, shrinkB=2))
    if label:
        ax.text(lx, ly, label, fontsize=10, color=c)


# column 2: trajectories
diamond(6.1, 1.72, 2.9, 1.25, "episode done\nor horizon T?")
arrow(ax, 3.8, 1.72, 4.65, 1.72)
poly([(6.1, 1.095), (6.1, 0.55), (0.35, 0.55), (0.35, 4.15), (0.6, 4.15)], label="no", lx=6.25, ly=0.72)
box(ax, 4.5, 3.85, BW, BH, "GAE: δ_t = r_t + γV(s_t+1) − V(s_t)\nÂ_t = Σ (γλ)^k δ_t+k ,  R_t = Â_t + V", F["buf"], 9.5)
arrow(ax, 6.1, 2.35, 6.1, 3.85); ax.text(6.25, 3.0, "yes", fontsize=10, color=MUTED)
box(ax, 4.5, 5.15, BW, BH, "Add to update buffer\n(bootstrap V if truncated)", F["buf"])
arrow(ax, 6.1, 4.8, 6.1, 5.15)
diamond(6.1, 7.15, 2.9, 1.25, "buffer ≥\nbuffer_size?")
arrow(ax, 6.1, 6.1, 6.1, 6.52)
poly([(4.65, 7.15), (4.15, 7.15), (4.15, 4.45), (3.8, 4.45)], label="no", lx=4.2, ly=7.3)
# column 3: update
box(ax, 8.5, 6.7, BW + 0.4, BH, "Normalise advantages\nshuffle into minibatches", F["upd"])
arrow(ax, 7.55, 7.15, 8.5, 7.15); ax.text(7.75, 7.35, "yes", fontsize=10, color=MUTED)
box(ax, 8.5, 5.15, BW + 0.4, 1.15, "PPO loss per minibatch\nclipped surrogate (ε = 0.2)\n+ 0.5·value loss − β·entropy", F["upd"], 9.5)
box(ax, 8.5, 3.85, BW + 0.4, BH, "Adam step; repeat\nK = 3 epochs; clear buffer", F["upd"])
arrow(ax, 10.3, 6.7, 10.3, 6.3); arrow(ax, 10.3, 5.15, 10.3, 4.8)
diamond(10.3, 2.55, 3.0, 1.3, "smoothed reward ≥\nlesson threshold?")
arrow(ax, 10.3, 3.85, 10.3, 3.2)
box(ax, 8.5, 0.95, BW + 0.4, 0.75, "Advance curriculum lesson", F["io"])
arrow(ax, 10.3, 1.9, 10.3, 1.7); ax.text(10.45, 1.75, "yes", fontsize=10, color=MUTED)
# column 4: end
diamond(14.0, 2.55, 2.8, 1.25, "step ≥\nmax_steps?")
arrow(ax, 11.8, 2.55, 12.6, 2.55); ax.text(11.95, 2.7, "no", fontsize=10, color=MUTED)
poly([(12.1, 1.32), (13.2, 1.32), (13.2, 2.15)])
poly([(14.0, 1.925), (14.0, 0.25), (0.15, 0.25), (0.15, 4.3), (0.6, 4.3)], c=ORANGE, label="no → collect more experience", lx=14.1, ly=0.85)
box(ax, 12.4, 4.4, BW, BH, "Save checkpoint\nexport ONNX policy", F["dep"])
arrow(ax, 14.0, 3.175, 14.0, 4.4); ax.text(14.15, 3.7, "yes", fontsize=10, color=MUTED)
term(12.4, 6.2, BW, 0.75, "DEPLOY in mission / demo", TEAL)
arrow(ax, 14.0, 5.35, 14.0, 6.2)
fig.tight_layout(); fig.savefig(f"{OUT}/fig_ppo_flowchart.png", dpi=150); plt.close(fig)
print("ok")
