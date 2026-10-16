"""Landable-surface segmentation (SPEC Section 6, Task A): small U-Net, 4 classes
(0 unknown, 1 landable, 2 not landable, 3 obstacle), trained on the synthetic dataset.

usage: python train_seg.py --data datasets/vision --out results/vision/unet_v1 [--epochs 40] [--width 16]
Writes: best.pt, unet.onnx (input 'input' [1,3,128,128] float 0..1, output 'probs' [1,4,128,128]), metrics.json, curves.
"""
import argparse, csv, json, os, time
import numpy as np
import torch
import torch.nn as nn
import torch.nn.functional as F
from PIL import Image

CLASSES = ["unknown", "landable", "not_landable", "obstacle"]


def load_split(root, split):
    rows = list(csv.DictReader(open(os.path.join(root, split, "meta.csv"))))
    X = np.zeros((len(rows), 128, 128, 3), np.uint8)
    Y = np.zeros((len(rows), 128, 128), np.uint8)
    for i, r in enumerate(rows):
        X[i] = np.array(Image.open(os.path.join(root, split, r["file"] + "_rgb.png")).convert("RGB"))
        Y[i] = np.array(Image.open(os.path.join(root, split, r["file"] + "_lbl.png")))[..., 0]
    return X, Y, rows  # PNGs are stored upright (row 0 = top)


def block(i, o):
    return nn.Sequential(nn.Conv2d(i, o, 3, padding=1, bias=False), nn.BatchNorm2d(o), nn.ReLU(inplace=True),
                         nn.Conv2d(o, o, 3, padding=1, bias=False), nn.BatchNorm2d(o), nn.ReLU(inplace=True))


class UNet(nn.Module):
    def __init__(self, w=16, n_cls=4):
        super().__init__()
        self.e1, self.e2, self.e3, self.e4 = block(3, w), block(w, 2 * w), block(2 * w, 4 * w), block(4 * w, 8 * w)
        self.up3, self.d3 = nn.ConvTranspose2d(8 * w, 4 * w, 2, 2), block(8 * w, 4 * w)
        self.up2, self.d2 = nn.ConvTranspose2d(4 * w, 2 * w, 2, 2), block(4 * w, 2 * w)
        self.up1, self.d1 = nn.ConvTranspose2d(2 * w, w, 2, 2), block(2 * w, w)
        self.head = nn.Conv2d(w, n_cls, 1)

    def forward(self, x):
        e1 = self.e1(x)
        e2 = self.e2(F.max_pool2d(e1, 2))
        e3 = self.e3(F.max_pool2d(e2, 2))
        e4 = self.e4(F.max_pool2d(e3, 2))
        d3 = self.d3(torch.cat([self.up3(e4), e3], 1))
        d2 = self.d2(torch.cat([self.up2(d3), e2], 1))
        d1 = self.d1(torch.cat([self.up1(d2), e1], 1))
        return self.head(d1)


class Exportable(nn.Module):
    """ONNX wrapper: softmax probabilities."""
    def __init__(self, m):
        super().__init__(); self.m = m

    def forward(self, x):
        return torch.softmax(self.m(x), dim=1)


def augment(x, strength=1.0):
    """x: float [B,3,H,W] 0..1 on GPU. Photometric jitter, blur, noise, horizontal flip (labels flipped by caller)."""
    B = x.shape[0]
    dev = x.device
    b = 1 + (torch.rand(B, 1, 1, 1, device=dev) - 0.5) * 0.5 * strength
    c = 1 + (torch.rand(B, 1, 1, 1, device=dev) - 0.5) * 0.5 * strength
    tint = 1 + (torch.rand(B, 3, 1, 1, device=dev) - 0.5) * 0.15 * strength
    m = x.mean((1, 2, 3), keepdim=True)
    x = ((x - m) * c + m) * b * tint
    gray = x.mean(1, keepdim=True)
    s = 1 + (torch.rand(B, 1, 1, 1, device=dev) - 0.5) * 0.6 * strength
    x = gray + (x - gray) * s
    # random blur on half the batch
    k = torch.tensor([1., 2., 1.], device=dev); k = (k[:, None] * k[None, :]) / 16
    blurred = F.conv2d(F.pad(x, (1, 1, 1, 1), mode="replicate"), k.expand(3, 1, 3, 3), groups=3)
    sel = (torch.rand(B, 1, 1, 1, device=dev) < 0.5).float()
    x = sel * blurred + (1 - sel) * x
    x = x + torch.randn_like(x) * (torch.rand(B, 1, 1, 1, device=dev) * 0.04 * strength)
    return x.clamp(0, 1)


def confusion(pred, y, n=4):
    return torch.bincount((y * n + pred).flatten(), minlength=n * n).reshape(n, n)


def metrics_from_cm(cm):
    cm = cm.double()
    tp = cm.diag(); fp = cm.sum(0) - tp; fn = cm.sum(1) - tp
    iou = tp / (tp + fp + fn).clamp(min=1)
    prec = tp / (tp + fp).clamp(min=1); rec = tp / (tp + fn).clamp(min=1)
    return {"iou": iou.tolist(), "precision": prec.tolist(), "recall": rec.tolist(),
            "miou": iou.mean().item(), "pixel_acc": (tp.sum() / cm.sum()).item(), "confusion": cm.long().tolist()}


@torch.no_grad()
def evaluate(model, X, Y, dev, bs=256):
    model.eval()
    cm = torch.zeros(4, 4, dtype=torch.long, device=dev)
    for i in range(0, len(X), bs):
        x = torch.from_numpy(X[i:i + bs]).to(dev).permute(0, 3, 1, 2).float() / 255
        y = torch.from_numpy(Y[i:i + bs]).to(dev).long()
        cm += confusion(model(x).argmax(1), y)
    return metrics_from_cm(cm.cpu())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", default="datasets/vision")
    ap.add_argument("--out", default="results/vision/unet_v1")
    ap.add_argument("--epochs", type=int, default=40)
    ap.add_argument("--width", type=int, default=16)
    ap.add_argument("--bs", type=int, default=64)
    ap.add_argument("--lr", type=float, default=3e-3)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--train-frac", type=float, default=1.0, help="fraction of training maps (weakened-detector ablation)")
    a = ap.parse_args()
    torch.manual_seed(a.seed); np.random.seed(a.seed)
    torch.set_num_threads(2)
    os.makedirs(a.out, exist_ok=True)
    dev = torch.device("cuda")
    t0 = time.time()
    Xtr, Ytr, rtr = load_split(a.data, "train")
    if a.train_frac < 1.0:
        seeds = sorted({r["seed"] for r in rtr})
        keep = set(seeds[: max(1, int(len(seeds) * a.train_frac))])
        idx = [i for i, r in enumerate(rtr) if r["seed"] in keep]
        Xtr, Ytr = Xtr[idx], Ytr[idx]
    Xva, Yva, _ = load_split(a.data, "val")
    Xte, Yte, _ = load_split(a.data, "test")
    print(f"loaded train {len(Xtr)} val {len(Xva)} test {len(Xte)} in {time.time() - t0:.1f}s", flush=True)

    freq = np.bincount(Ytr.flatten(), minlength=4).astype(np.float64); freq /= freq.sum()
    w = torch.tensor((1 / np.sqrt(freq + 1e-6)) / (1 / np.sqrt(freq + 1e-6)).mean(), dtype=torch.float32, device=dev)
    print("class freq", np.round(freq, 4), "weights", w.cpu().numpy().round(3), flush=True)

    model = UNet(a.width).to(dev)
    print("params", sum(p.numel() for p in model.parameters()), flush=True)
    opt = torch.optim.AdamW(model.parameters(), lr=a.lr, weight_decay=1e-4)
    steps = a.epochs * (len(Xtr) // a.bs)
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=a.lr, total_steps=steps, pct_start=0.15)
    Xg = torch.from_numpy(Xtr).to(dev)  # keep the uint8 training set on the GPU (~0.5 GB)
    Yg = torch.from_numpy(Ytr).to(dev)
    best, hist = -1, []
    for ep in range(a.epochs):
        model.train()
        perm = torch.randperm(len(Xg), device=dev)
        tl = 0.0
        for i in range(0, len(perm) - a.bs + 1, a.bs):
            idx = perm[i:i + a.bs]
            x = Xg[idx].permute(0, 3, 1, 2).float() / 255
            y = Yg[idx].long()
            flip = torch.rand(len(idx), device=dev) < 0.5
            x[flip] = x[flip].flip(-1); y[flip] = y[flip].flip(-1)
            x = augment(x)
            logits = model(x)
            ce = F.cross_entropy(logits, y, weight=w)
            p = logits.softmax(1)
            oh = F.one_hot(y, 4).permute(0, 3, 1, 2).float()
            dice = 1 - ((2 * (p * oh).sum((0, 2, 3)) + 1) / (p.sum((0, 2, 3)) + oh.sum((0, 2, 3)) + 1)).mean()
            loss = ce + 0.5 * dice
            opt.zero_grad(set_to_none=True); loss.backward(); opt.step(); sched.step()
            tl += loss.item()
        mv = evaluate(model, Xva, Yva, dev)
        hist.append({"epoch": ep, "loss": tl / (len(perm) // a.bs), "val_miou": mv["miou"], "val_iou": mv["iou"]})
        print(f"ep {ep:02d} loss {hist[-1]['loss']:.4f} val mIoU {mv['miou']:.4f} IoU {np.round(mv['iou'], 3)}", flush=True)
        if mv["miou"] > best:
            best = mv["miou"]; torch.save(model.state_dict(), os.path.join(a.out, "best.pt"))
    model.load_state_dict(torch.load(os.path.join(a.out, "best.pt")))
    mt = evaluate(model, Xte, Yte, dev)
    print("TEST", json.dumps({k: (np.round(v, 4).tolist() if isinstance(v, list) and k != "confusion" else v) for k, v in mt.items()}), flush=True)
    json.dump({"test": mt, "val_best_miou": best, "history": hist, "classes": CLASSES, "args": vars(a),
               "train_images": len(Xtr), "params": sum(p.numel() for p in model.parameters())},
              open(os.path.join(a.out, "metrics.json"), "w"), indent=1)
    model.eval().cpu()
    torch.onnx.export(Exportable(model), torch.zeros(1, 3, 128, 128), os.path.join(a.out, "unet.onnx"),
                      input_names=["input"], output_names=["probs"], opset_version=15)
    print("exported", os.path.join(a.out, "unet.onnx"), flush=True)


if __name__ == "__main__":
    main()
