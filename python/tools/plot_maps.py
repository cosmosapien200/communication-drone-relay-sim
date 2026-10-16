"""Render debug maps exported by DebugExports.ExportMaps: layout, ground classes, path field, RSSI heatmap.

usage: python plot_maps.py results/debug_maps docs/screenshots
"""
import glob, json, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.colors import ListedColormap

T_LINK, MARGIN, GROUND = -80.0, 6.0, 3.0

GROUND_COLORS = {0: (0.55, 0.55, 0.55), 1: (0.3, 0.8, 0.3), 2: (0.4, 0.9, 0.5), 3: (0.2, 0.7, 0.4),
                 4: (0.6, 0.45, 0.3), 5: (0.3, 0.45, 0.8), 6: (0.15, 0.1, 0.1), 7: (0.85, 0.3, 0.2), 8: (0.8, 0.7, 0.3)}
MAT_COLORS = {1: (0.25, 0.25, 0.3), 2: (0.55, 0.25, 0.2), 3: (0.45, 0.4, 0.3)}


def load(p):
    d = json.load(open(p))
    W, H = d["W"], d["H"]
    g = lambda k, dt=float: np.array([np.nan if v is None else v for v in d[k]], dtype=dt).reshape(H, W)
    return d, W, H, g


def layout_img(d, W, H, g):
    occ, mat, ground = g("occ"), g("material"), g("ground")
    img = np.zeros((H, W, 3))
    for j in range(H):
        for i in range(W):
            img[j, i] = MAT_COLORS.get(int(mat[j, i]), (0, 0, 0)) if occ[j, i] else GROUND_COLORS[int(ground[j, i])]
    return img


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    files = sorted(glob.glob(os.path.join(src, "map_L*_s*.json")))
    # one figure per map; plus a grid overview of first seed per level
    overview, axs = plt.subplots(2, 4, figsize=(20, 10))
    for p in files:
        d, W, H, g = load(p)
        name = os.path.basename(p)[:-5]
        img = layout_img(d, W, H, g)
        rssi, pf = g("rssi"), g("pathfield")
        path = np.array(d["path"]) if d["path"] else np.zeros((0, 2))
        fig, ax = plt.subplots(1, 3, figsize=(18, 6))
        ax[0].imshow(img, origin="lower"); ax[0].set_title(f"{name}: layout (green=landable, grey=street)")
        ax[1].imshow(pf, origin="lower", cmap="viridis"); ax[1].set_title("path field to target (m)")
        im = ax[2].imshow(rssi, origin="lower", cmap="RdYlGn", vmin=-110, vmax=-40)
        ax[2].contour(np.nan_to_num(rssi, nan=-200), levels=[T_LINK, T_LINK + MARGIN + GROUND], colors=["k", "b"], linewidths=0.8, origin="lower")
        ax[2].set_title("RSSI from base @cruise (black=T_link, blue=qualify)")
        fig.colorbar(im, ax=ax[2], fraction=0.046, label="dBm")
        for a in ax:
            if len(path): a.plot(path[:, 0], path[:, 1], "w-", lw=1.2)
            a.plot(*d["base"], "b^", ms=10); a.plot(*d["target"], "r*", ms=14)
        fig.tight_layout(); fig.savefig(os.path.join(dst, f"{name}.png"), dpi=70); plt.close(fig)
        lvl = int(d["level"])
        if name.endswith("s%d" % (1000 * lvl)):
            axs[0, lvl - 1].imshow(img, origin="lower"); axs[0, lvl - 1].set_title(f"Level {lvl} layout")
            axs[1, lvl - 1].imshow(rssi, origin="lower", cmap="RdYlGn", vmin=-110, vmax=-40); axs[1, lvl - 1].set_title(f"Level {lvl} RSSI from base")
            for a in (axs[0, lvl - 1], axs[1, lvl - 1]):
                if len(path): a.plot(path[:, 0], path[:, 1], "w-", lw=1)
                a.plot(*d["base"], "b^", ms=8); a.plot(*d["target"], "r*", ms=12)
    overview.tight_layout(); overview.savefig(os.path.join(dst, "maps_overview.png"), dpi=60)
    print("wrote", len(files), "maps to", dst)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
