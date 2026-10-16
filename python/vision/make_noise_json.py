"""Builds the VisionNoiseModel parameters from measured detector errors (SPEC Section 6, noise model):
 - segmentation confusion P(pred | true) from the U-Net test set,
 - landing-spot locator FN/FP rates, localisation error (sigma, bias), area error from the Unity calibration run.
Writes unity/Assets/Resources/vision_noise.json and docs/results/vision_<name>.md + plots.

usage: python make_noise_json.py results/vision/unet_v1 results/vision/locator_calib_unet_v1.csv [--out-json path] [--name unet_v1]
"""
import argparse, json, os
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

CLASSES = ["unknown", "landable", "not landable", "obstacle"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model_dir"); ap.add_argument("locator_csv")
    ap.add_argument("--out-json", default="unity/Assets/Resources/vision_noise.json")
    ap.add_argument("--name", default="unet_v1")
    ap.add_argument("--docs", default="docs/results")
    a = ap.parse_args()
    m = json.load(open(os.path.join(a.model_dir, "metrics.json")))
    cm = np.array(m["test"]["confusion"], float)
    P = cm / cm.sum(1, keepdims=True)

    d = np.genfromtxt(a.locator_csv, delimiter=",", names=True)
    gt, cn = d["gt_det"] > 0, d["cnn_det"] > 0
    fn = float((gt & ~cn).sum() / max(1, gt.sum()))
    fp = float((~gt & cn).sum() / max(1, (~gt).sum()))
    tp = gt & cn
    ex, ey = d["cnn_x"][tp] - d["gt_x"][tp], d["cnn_y"][tp] - d["gt_y"][tp]
    if tp.sum() == 0:  # detector never localises the patch (e.g. the weakened model)
        ex = ey = np.array([1.0]); tp_empty = True
    else:
        tp_empty = False
    bias = float(np.hypot(ex.mean(), ey.mean()))
    sigma = float(np.sqrt(0.5 * (ex.var() + ey.var())))
    # robust sigma (median absolute deviation) to report alongside
    mad = float(1.4826 * np.median(np.abs(np.concatenate([ex - np.median(ex), ey - np.median(ey)]))))
    area_rel = d["cnn_area"][tp] / np.maximum(d["gt_area"][tp], 1e-3) - 1 if not tp_empty else np.array([1.0])
    area_sigma = float(np.clip(np.std(area_rel), 0, 1))
    alt = d["alt"]
    # localisation error in metres (offset x half-extent)
    he = alt[tp] * np.tan(np.radians(30)) if not tp_empty else np.array([np.nan])
    err_m = np.hypot(ex, ey) * he

    noise = {"confusion": [float(x) for x in P.flatten()], "fnRate": fn, "fpRate": fp,
             "locSigma": min(sigma, 0.5), "locBias": bias, "areaSigma": area_sigma,
             "dropRate": 0.02, "confNoise": float(np.clip(1 - d["cnn_conf"][tp].mean(), 0.05, 0.5)) if not tp_empty else 0.5,
             "source": f"measured: {a.name} test set + locator calibration ({len(d)} frames)"}
    os.makedirs(os.path.dirname(a.out_json), exist_ok=True)
    json.dump(noise, open(a.out_json, "w"), indent=1)

    os.makedirs(a.docs, exist_ok=True)
    t = m["test"]
    lines = [f"# Vision results — {a.name}", "",
             f"Test set: {len(open(os.path.join('datasets/vision/test/meta.csv')).readlines()) - 1} frames from held-out map seeds; "
             f"model params {m['params']}, best val mIoU {m['val_best_miou']:.3f}.", "",
             "| Class | IoU | Precision | Recall |", "|---|---|---|---|"]
    for i, c in enumerate(CLASSES):
        lines.append(f"| {c} | {t['iou'][i]:.3f} | {t['precision'][i]:.3f} | {t['recall'][i]:.3f} |")
    lines += [f"| **mean** | **{t['miou']:.3f}** | | |", "", f"Pixel accuracy {t['pixel_acc']:.4f}.", "",
              "## Landing-spot locator (nadir, Unity in-the-loop, held-out maps)", "",
              f"- Frames: {len(d)} (patch in view: {int(gt.sum())}, out of view: {int((~gt).sum())})",
              f"- False-negative rate (patch visible, not found): **{fn:.3f}**",
              f"- False-positive rate (patch out of view, something reported): **{fp:.3f}**",
              f"- Centre localisation error: bias {bias:.3f}, sigma {sigma:.3f} (robust {mad:.3f}) normalised image units;"
              f" median {np.median(err_m):.3f} m, 90th pct {np.percentile(err_m, 90):.3f} m",
              f"- Relative area error sigma: {area_sigma:.3f}", "",
              "These rates parameterise `VisionNoiseModel` (unity/Assets/Resources/vision_noise.json)."]
    open(os.path.join(a.docs, f"vision_{a.name}.md"), "w").write("\n".join(lines) + "\n")

    fig, ax = plt.subplots(1, 2, figsize=(12, 5))
    im = ax[0].imshow(P, cmap="Blues", vmin=0, vmax=1)
    for i in range(4):
        for j in range(4):
            ax[0].text(j, i, f"{P[i, j]:.2f}", ha="center", va="center", color="w" if P[i, j] > 0.5 else "k")
    ax[0].set_xticks(range(4)); ax[0].set_xticklabels(CLASSES, rotation=20); ax[0].set_yticks(range(4)); ax[0].set_yticklabels(CLASSES)
    ax[0].set_xlabel("predicted"); ax[0].set_ylabel("true"); ax[0].set_title(f"Confusion (row-normalised), mIoU {t['miou']:.3f}")
    h = [e["val_miou"] for e in m["history"]]
    ax[1].plot(h, label="val mIoU"); ax[1].plot([e["val_iou"][1] for e in m["history"]], label="val IoU landable")
    ax[1].set_xlabel("epoch"); ax[1].legend(); ax[1].grid(alpha=0.3); ax[1].set_title("training curve")
    fig.tight_layout(); fig.savefig(os.path.join(a.docs, f"vision_{a.name}.png"), dpi=90)
    print("\n".join(lines)); print(json.dumps({k: v for k, v in noise.items() if k != "confusion"}, indent=1))


if __name__ == "__main__":
    main()
