"""Block diagrams and composites for the final report -> docs/report_assets/."""
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch
from PIL import Image

OUT = "docs/report_assets"
C = {"sim": "#dbe9f6", "vis": "#d9f0d3", "rl": "#fde2c8", "plan": "#ece0f5", "data": "#fff3c4", "io": "#eeeeee"}


def box(ax, x, y, w, h, text, color, fs=8.5):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc=color, ec="#444", lw=0.9))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=fs, wrap=True)


def arrow(ax, x0, y0, x1, y1, text=None, style="-|>", color="#333"):
    ax.annotate("", xy=(x1, y1), xytext=(x0, y0), arrowprops=dict(arrowstyle=style, color=color, lw=1.1, shrinkA=2, shrinkB=2))
    if text:
        ax.text((x0 + x1) / 2, (y0 + y1) / 2 + 0.12, text, ha="center", fontsize=7, color="#333")


# ---------- vision pipeline
fig, ax = plt.subplots(figsize=(11, 5.6)); ax.set_xlim(0, 11); ax.set_ylim(0, 5.6); ax.axis("off")
ax.text(0.1, 5.35, "Offline: synthetic data and training", fontsize=10, weight="bold")
box(ax, 0.1, 4.0, 1.9, 1.0, "Procedural town\ngenerator\n(seeded maps)", C["sim"])
box(ax, 2.3, 4.0, 2.0, 1.0, "TownBuilder render\n+ domain randomisation\n(palette, sun, dust, fog)", C["sim"])
box(ax, 4.6, 4.0, 2.0, 1.0, "RGB 128×128 +\npixel-perfect label\n(flat-colour re-render)", C["data"])
box(ax, 6.9, 4.0, 1.9, 1.0, "U-Net training\n(CE + Dice, GPU aug.)\nPyTorch", C["vis"])
box(ax, 9.1, 4.0, 1.8, 1.0, "ONNX export →\nUnity Inference\nEngine", C["vis"])
for x0, x1 in ((2.0, 2.3), (4.3, 4.6), (6.6, 6.9), (8.8, 9.1)): arrow(ax, x0, 4.5, x1, 4.5)
ax.text(0.1, 3.55, "Online: perception in the mission loop", fontsize=10, weight="bold")
box(ax, 0.1, 1.9, 1.9, 1.3, "Gimbal camera\n45° explore (aimed at\nplanner goal ±60° pan)\nor nadir (landing)", C["io"])
box(ax, 2.3, 1.9, 1.9, 1.3, "U-Net (ONNX, Unity\nInference Engine)\n4-class probabilities", C["vis"])
box(ax, 4.6, 2.55, 2.1, 1.0, "Ground projection\npixel ray ∩ ground plane\n(known pose, occlusion test)", C["vis"])
box(ax, 7.0, 2.55, 1.9, 1.0, "Evidence fusion\nconf × incidence ×\n(5 m / range)²", C["vis"])
box(ax, 9.2, 2.55, 1.7, 1.0, "Shared map\nlandable channel\n(≥ 60 % vote)\n→ spot selection", C["plan"])
box(ax, 4.6, 1.0, 2.1, 1.1, "Landing-spot locator\nconnected component near\nexpected patch, centroid,\ndistance transform", C["vis"])
box(ax, 7.0, 1.0, 1.9, 1.1, "Offset, area,\nconfidence,\nedge clearance", C["data"])
box(ax, 9.2, 1.0, 1.7, 1.1, "Landing policy\n(PPO) observations", C["rl"])
arrow(ax, 2.0, 2.55, 2.3, 2.55); arrow(ax, 4.2, 2.8, 4.6, 3.05); arrow(ax, 4.2, 2.3, 4.6, 1.55)
arrow(ax, 6.7, 3.05, 7.0, 3.05); arrow(ax, 8.9, 3.05, 9.2, 3.05); arrow(ax, 6.7, 1.55, 7.0, 1.55); arrow(ax, 8.9, 1.55, 9.2, 1.55)
box(ax, 0.1, 0.05, 3.9, 1.3, "Calibration on held-out maps (Unity in the loop):\nconfusion matrix + locator FN / FP / localisation error\n→ Vision noise model (same output types)\n→ used for RL training and detector-quality sweep", C["data"], fs=8)
arrow(ax, 4.0, 1.35, 4.6, 1.35, style="<|-", color="#777")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_vision_pipeline.png", dpi=170); plt.close(fig)

# ---------- system architecture
fig, ax = plt.subplots(figsize=(11, 4.6)); ax.set_xlim(0, 11); ax.set_ylim(0, 4.6); ax.axis("off")
box(ax, 0.1, 1.5, 1.9, 1.3, "Unity 6 simulator\ncollapsed town,\ncamera, range rays,\nWiFi RSSI, wind", C["sim"])
box(ax, 2.4, 1.5, 2.1, 1.3, "MACHINE VISION\nU-Net segmentation\nground projection\nlanding-spot locator", C["vis"], fs=9)
box(ax, 4.9, 2.55, 1.9, 1.0, "Shared map\nexplored / obstacle /\nlandable / RSSI", C["plan"])
box(ax, 4.9, 1.2, 1.9, 1.0, "Mission planner\nfrontier exploration,\nspot selection", C["plan"])
box(ax, 7.2, 2.55, 1.7, 1.0, "Navigation\npolicy (PPO)", C["rl"])
box(ax, 7.2, 0.2, 1.7, 1.0, "Landing\npolicy (PPO)", C["rl"])
box(ax, 9.3, 1.5, 1.6, 1.3, "Flight\ncontroller\n(cascaded PID)", C["sim"])
arrow(ax, 2.0, 2.15, 2.4, 2.15, "frames")
arrow(ax, 4.5, 2.5, 4.9, 2.95); ax.text(4.35, 2.95, "landable\ncells", fontsize=7, ha="right"); arrow(ax, 5.85, 2.55, 5.85, 2.2)
ax.plot([3.45, 3.45], [1.5, 0.7], color="#333", lw=1.1); arrow(ax, 3.45, 0.7, 7.2, 0.7)
ax.text(5.3, 0.82, "spot offset / area / confidence", ha="center", fontsize=7)
arrow(ax, 6.8, 1.9, 7.2, 2.8, "waypoints"); arrow(ax, 6.8, 1.4, 7.2, 1.0, "land")
arrow(ax, 8.9, 3.05, 9.3, 2.5); arrow(ax, 8.9, 0.7, 9.3, 1.7)
ax.text(10.1, 1.25, "velocity\nset-points", ha="center", va="top", fontsize=7, color="#555")
ax.annotate("", xy=(1.05, 2.8), xytext=(10.1, 2.8), arrowprops=dict(arrowstyle="-|>", color="#888", lw=1.1, connectionstyle="arc3,rad=0.36"))
ax.text(5.6, 4.35, "closed loop: new pose and sensor readings every 0.1 s; drones launch one at a time until the camp receives ≥ −80 dBm", ha="center", fontsize=8, color="#555")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_system_architecture.png", dpi=170); plt.close(fig)

# ---------- demo composite (2x2)
ims = [Image.open(f"{OUT}/video/t{t}.png").resize((960, 540)) for t in (5, 35, 100, 111)]
c = Image.new("RGB", (1920, 1080), "white")
for k, im in enumerate(ims): c.paste(im, ((k % 2) * 960, (k // 2) * 540))
c.save(f"{OUT}/fig_demo_frames.png")

# ---------- label re-render pair
a = Image.open("docs/screenshots/town_L4_rgb.png"); b = Image.open("docs/screenshots/town_L4_labels.png")
w, h = a.size; c = Image.new("RGB", (w * 2 + 20, h), "white"); c.paste(a, (0, 0)); c.paste(b, (w + 20, 0))
c.crop((150, 60, c.width - 150, h - 30)).save(f"{OUT}/fig_label_rerender.png")
print("ok")
