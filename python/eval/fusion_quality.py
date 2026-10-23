"""Perception-fusion quality from DroneMission dumps: over explored free cells, how well the shared map's
'landable' belief matches ground truth (precision / recall / IoU), plus usable-spot recall.

usage: python fusion_quality.py results/.../dumpdir [more dirs...]
"""
import glob, json, sys
import numpy as np


def interior(m):
    n = np.zeros_like(m, int)
    n[1:-1, 1:-1] = m[1:-1, 2:].astype(int) + m[1:-1, :-2] + m[2:, 1:-1] + m[:-2, 1:-1]
    return m & (n >= 3)


for d in sys.argv[1:]:
    tp = fp = fn = 0; spot_true = spot_found = 0; n = 0
    for f in glob.glob(f"{d}/*.json"):
        j = json.load(open(f)); W, H = j["W"], j["H"]
        A = lambda k: np.array(j[k], float).reshape(H, W) > 0
        ex, occ, tl, bl = A("explored"), A("occ"), A("trueland"), A("landable")
        m = ex & ~occ
        tp += (bl & tl & m).sum(); fp += (bl & ~tl & m).sum(); fn += (~bl & tl & m).sum()
        ti = interior(tl) & m
        spot_true += ti.sum(); spot_found += (ti & interior(bl)).sum(); n += 1
    print(f"{d}: dumps {n}  precision {tp / max(1, tp + fp):.3f}  recall {tp / max(1, tp + fn):.3f}  IoU {tp / max(1, tp + fp + fn):.3f}  "
          f"usable-spot recall {spot_found / max(1, spot_true):.3f}")
