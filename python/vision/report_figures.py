"""Machine-vision figures and tables for the final report -> docs/report_assets/.

usage: python python/vision/report_figures.py
"""
import json, os, sys, time
import numpy as np
import torch
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from train_seg import UNet, load_split, evaluate, confusion, metrics_from_cm  # noqa: E402

OUT = "docs/report_assets"
os.makedirs(OUT, exist_ok=True)
DEV = torch.device("cuda" if torch.cuda.is_available() else "cpu")
CLASSES = ["Unknown", "Landable", "Not landable", "Obstacle"]
PAL = np.array([[0, 0, 0], [40, 200, 70], [215, 60, 50], [50, 90, 230]], np.uint8)
MODELS = {"U-Net v1": "unet_v1", "U-Net v2": "unet_v2", "U-Net v4 (final)": "unet_v4", "Mid (20 % data)": "unet_mid", "Weak (5 % data)": "unet_weak"}
plt.rcParams.update({"font.size": 10, "figure.dpi": 150})


def load_model(name):
    m = UNet(16).to(DEV)
    m.load_state_dict(torch.load(f"results/vision/{name}/best.pt", map_location=DEV, weights_only=True))
    return m.eval()


@torch.no_grad()
def predict(m, X, bs=128):
    out = []
    for i in range(0, len(X), bs):
        x = torch.from_numpy(X[i:i + bs]).to(DEV).permute(0, 3, 1, 2).float() / 255
        out.append(m(x).softmax(1).cpu().numpy())
    return np.concatenate(out)


def metrics(pred, Y):
    cm = confusion(torch.from_numpy(pred.argmax(1)).long(), torch.from_numpy(Y).long())
    return metrics_from_cm(cm)


tables = {}
X, Y, rows = load_split("datasets/vision", "test")
modes = np.array([r["mode"] for r in rows]); alt = np.array([float(r["alt"]) for r in rows])
XL, YL, _ = load_split("datasets/vision", "lowalt_test")
XP, YP, _ = load_split("datasets/vision", "patchview_test")

# ---- 1. per-model, per-class metrics on the main test set + per-condition breakdown
per_model, per_cond = {}, {}
preds = {}
for label, name in MODELS.items():
    m = load_model(name)
    p = predict(m, X); preds[name] = p
    per_model[label] = metrics(p, Y)
    if name in ("unet_v2", "unet_v4"):
        cond = {}
        for cname, sel in [("45° explore view", modes == "explore"), ("Nadir view (all)", modes == "nadir"),
                           ("Nadir, altitude < 2 m", (modes == "nadir") & (alt < 2)), ("Nadir, altitude > 4 m", (modes == "nadir") & (alt > 4))]:
            cond[cname] = metrics(p[sel], Y[sel])
        cond["Close-range nadir set (0.5–2.5 m)"] = metrics(predict(m, XL), YL)
        cond["Patch-aimed 45° set (5–20 m)"] = metrics(predict(m, XP), YP)
        per_cond[label] = cond
tables["per_model"] = {k: {kk: v[kk] for kk in ("iou", "precision", "recall", "miou", "pixel_acc")} for k, v in per_model.items()}
tables["per_condition"] = {k: {c: {"landable_iou": v["iou"][1], "landable_p": v["precision"][1], "landable_r": v["recall"][1], "miou": v["miou"]} for c, v in d.items()} for k, d in per_cond.items()}

# ---- 2. confusion matrix (final model)
cm = np.array(per_model["U-Net v4 (final)"]["confusion"], float)
P = cm / cm.sum(1, keepdims=True)
fig, ax = plt.subplots(figsize=(4.6, 4))
ax.imshow(P, cmap="Blues", vmin=0, vmax=1)
for i in range(4):
    for j in range(4):
        ax.text(j, i, f"{P[i, j]:.3f}", ha="center", va="center", color="w" if P[i, j] > 0.5 else "k", fontsize=9)
ax.set_xticks(range(4)); ax.set_xticklabels(CLASSES, rotation=25, ha="right"); ax.set_yticks(range(4)); ax.set_yticklabels(CLASSES)
ax.set_xlabel("Predicted class"); ax.set_ylabel("True class"); ax.set_title("Row-normalised confusion matrix (test set)")
fig.tight_layout(); fig.savefig(f"{OUT}/fig_confusion_v4.png"); plt.close(fig)

# ---- 3. training curves (v2 from scratch + v4 fine-tune)
fig, ax = plt.subplots(1, 2, figsize=(9, 3.2))
for name, a, title in (("unet_v2", ax[0], "U-Net v2 — training from scratch (40 epochs)"), ("unet_v4", ax[1], "U-Net v4 — fine-tuning from v3 (15 epochs)")):
    h = json.load(open(f"results/vision/{name}/metrics.json"))["history"]
    a.plot([e["epoch"] + 1 for e in h], [e["val_miou"] for e in h], "o-", ms=3, label="val mIoU")
    a.plot([e["epoch"] + 1 for e in h], [e["val_iou"][1] for e in h], "s-", ms=3, label="val IoU (landable)")
    a2 = a.twinx(); a2.plot([e["epoch"] + 1 for e in h], [e["loss"] for e in h], "--", color="gray", label="train loss"); a2.set_ylabel("loss", color="gray")
    a.set_xlabel("epoch"); a.set_ylim(0.3, 1.0); a.grid(alpha=0.3); a.set_title(title, fontsize=9); a.legend(loc="lower right", fontsize=8)
fig.tight_layout(); fig.savefig(f"{OUT}/fig_training_curves.png"); plt.close(fig)

# ---- 4. qualitative predictions (RGB | ground truth | prediction | error) + failure cases
p4 = preds["unet_v4"]; pred_lbl = p4.argmax(1)
land_iou = []
for i in range(len(Y)):
    t, q = Y[i] == 1, pred_lbl[i] == 1
    u = (t | q).sum()
    land_iou.append((t & q).sum() / u if u > 200 else np.nan)
land_iou = np.array(land_iou)
rng = np.random.default_rng(3)
good = [i for i in rng.permutation(np.where((land_iou > 0.85) & (modes == "explore"))[0])[:3]] + \
       [i for i in rng.permutation(np.where((land_iou > 0.85) & (modes == "nadir"))[0])[:2]]
bad = list(np.argsort(np.nan_to_num(land_iou, nan=9))[:3])


def grid(idx, path, title):
    fig, axs = plt.subplots(len(idx), 4, figsize=(7.2, 1.85 * len(idx)))
    for r, i in enumerate(idx):
        err = (pred_lbl[i] != Y[i])
        ims = [X[i], PAL[Y[i]], PAL[pred_lbl[i]], np.where(err[..., None], [255, 40, 40], [235, 235, 235]).astype(np.uint8)]
        for c, im in enumerate(ims):
            axs[r, c].imshow(im); axs[r, c].set_xticks([]); axs[r, c].set_yticks([])
            if r == 0: axs[r, c].set_title(["Input (128×128)", "Ground truth", "U-Net v4 prediction", "Errors"][c], fontsize=9)
        axs[r, 0].set_ylabel(f"{modes[i]}\n{alt[i]:.1f} m", fontsize=8)
    fig.suptitle(title, fontsize=10); fig.tight_layout(); fig.savefig(path); plt.close(fig)


grid(good, f"{OUT}/fig_qualitative.png", "Held-out test maps: green = landable, red = not landable, blue = obstacle, black = unknown")
grid(bad, f"{OUT}/fig_failures.png", "Hardest test frames (lowest landable IoU)")

# ---- 5. dataset samples per view type + class balance
def sample(split, k, seed):
    xs, ys, rr = load_split("datasets/vision", split)
    idx = np.random.default_rng(seed).choice(len(xs), k, replace=False)
    return [(xs[i], ys[i]) for i in idx]


sets = [("Main (45° explore)", [(X[i], Y[i]) for i in np.where(modes == "explore")[0][[5, 400, 800]]]),
        ("Main (nadir)", [(X[i], Y[i]) for i in np.where(modes == "nadir")[0][[10, 300, 600]]]),
        ("Close-range nadir", sample("lowalt_test", 3, 1)), ("Patch-aimed 45°", sample("patchview_test", 3, 2))]
fig, axs = plt.subplots(2, 12, figsize=(14, 2.7))
for s, (name, items) in enumerate(sets):
    for k, (x, y) in enumerate(items):
        axs[0, s * 3 + k].imshow(x); axs[1, s * 3 + k].imshow(PAL[y])
        if k == 1: axs[0, s * 3 + k].set_title(name, fontsize=9)
for a in axs.flat: a.set_xticks([]); a.set_yticks([])
axs[0, 0].set_ylabel("RGB", fontsize=9); axs[1, 0].set_ylabel("label", fontsize=9)
fig.tight_layout(); fig.savefig(f"{OUT}/fig_dataset_samples.png"); plt.close(fig)

balance = {}
for split in ("train", "val", "test", "lowalt_train", "lowalt_test", "patchview_train", "patchview_test"):
    _, ys, rr = load_split("datasets/vision", split)
    balance[split] = {"frames": len(ys), "maps": len({r["seed"] for r in rr}),
                      "fractions": (np.bincount(ys.flatten(), minlength=4) / ys.size).round(4).tolist()}
tables["dataset"] = balance

# ---- 6. data efficiency (training maps vs IoU)
fig, ax = plt.subplots(figsize=(4.6, 3.2))
pts = [(5, "Weak (5 % data)"), (20, "Mid (20 % data)"), (100, "U-Net v2")]
ax.plot([p for p, _ in pts], [per_model[l]["miou"] for _, l in pts], "o-", label="mIoU")
ax.plot([p for p, _ in pts], [per_model[l]["iou"][1] for _, l in pts], "s-", label="IoU landable")
ax.set_xscale("log"); ax.set_xticks([5, 20, 100]); ax.set_xticklabels(["5 maps", "20 maps", "100 maps"])
ax.set_ylim(0, 1); ax.grid(alpha=0.3); ax.legend(); ax.set_xlabel("training maps (550 / 2,200 / 11,000 frames)"); ax.set_title("Data efficiency (held-out test set)", fontsize=10)
fig.tight_layout(); fig.savefig(f"{OUT}/fig_data_efficiency.png"); plt.close(fig)

# ---- 7. inference cost
import onnxruntime as ort
x1 = np.random.rand(1, 3, 128, 128).astype(np.float32)
timing = {}
for th in (1, 4):
    so = ort.SessionOptions(); so.intra_op_num_threads = th
    s = ort.InferenceSession("results/vision/unet_v4/unet.onnx", so, providers=["CPUExecutionProvider"])
    for _ in range(10): s.run(None, {"input": x1})
    t = time.perf_counter(); n = 100
    for _ in range(n): s.run(None, {"input": x1})
    timing[f"onnxruntime CPU, {th} thread(s)"] = (time.perf_counter() - t) / n * 1000
if DEV.type == "cuda":
    m = load_model("unet_v4"); xt = torch.from_numpy(x1).to(DEV)
    with torch.no_grad():
        for _ in range(20): m(xt)
        torch.cuda.synchronize(); t = time.perf_counter()
        for _ in range(200): m(xt)
        torch.cuda.synchronize()
    timing["PyTorch CUDA (RTX 3060), batch 1"] = (time.perf_counter() - t) / 200 * 1000
from thop import profile  # noqa
macs, params = profile(load_model("unet_v4").cpu(), inputs=(torch.from_numpy(x1),), verbose=False)
tables["inference"] = {"ms": timing, "GMACs": macs / 1e9, "params": params, "onnx_MB": os.path.getsize("results/vision/unet_v4/unet.onnx") / 1e6}

json.dump(tables, open(f"{OUT}/vision_tables.json", "w"), indent=1)
print(json.dumps(tables["per_condition"], indent=1)); print(json.dumps(tables["inference"], indent=1))
for k, v in tables["per_model"].items(): print(k, "mIoU %.3f landable %.3f" % (v["miou"], v["iou"][1]))
