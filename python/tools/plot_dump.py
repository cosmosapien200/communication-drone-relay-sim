"""Plot a DroneMission failure dump: explored/obstacles, landable (believed vs true), RSSI estimate, trail."""
import json, sys
import numpy as np
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
d = json.load(open(sys.argv[1])); W, H = d["W"], d["H"]
A = lambda k: np.array(d[k], float).reshape(H, W)
fig, ax = plt.subplots(1, 3, figsize=(20, 7))
img = np.zeros((H, W, 3)); img[A("occ") > 0] = (0.3, 0.3, 0.35); img[(A("explored") > 0) & (A("occ") == 0)] = (0.75, 0.75, 0.7)
img[A("trueland") > 0] = (0.5, 0.9, 0.5); img[A("landable") > 0] = (0, 0.6, 0)
ax[0].imshow(img, origin="lower"); ax[0].set_title(f"{d['why']}: grey explored, light green=true landable, dark green=believed")
r = A("rssi"); r[A("occ") > 0] = np.nan
im = ax[1].imshow(r, origin="lower", cmap="RdYlGn", vmin=-100, vmax=-50); fig.colorbar(im, ax=ax[1]); ax[1].contour(np.nan_to_num(r, nan=-200), levels=[-74, -71], colors=["b", "k"], origin="lower")
ax[1].set_title("RSSI estimate to parent (blue -74 frontier, black -71 spot)")
ax[2].imshow(A("tfield"), origin="lower"); ax[2].set_title("path distance to target (known map)")
t = np.array(d["trail"]) if d["trail"] else np.zeros((0, 2))
for a in ax:
    if len(t): a.plot(t[:, 0], t[:, 1], "m-", lw=1)
    a.plot(*d["parent"], "b^", ms=10); a.plot(*d["drone"], "mo", ms=6); a.plot(*d["target"], "r*", ms=14)
fig.tight_layout(); fig.savefig(sys.argv[2], dpi=60)
