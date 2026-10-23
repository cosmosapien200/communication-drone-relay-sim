"""Classify each 'no_qualifying_spot' failure from DroneMission dumps:
  perception  - a usable true landable spot (interior cell, RSSI-to-parent estimate >= qualify-2dB, >= 5 m progress)
                was explored but not believed landable
  exploration - such a spot exists within RSSI range but was never explored
  geometry    - no such spot exists at all within the parent's RSSI range (placement / map design)
Also reports how far (path distance to camp) the failing drone's parent got.

usage: python classify_failures.py results/fix/robust_new [results/fix/robust_old ...]
"""
import collections, glob, json, re, sys
import numpy as np

NAMES = ['Z-route', 'U-turn', 'Staircase', 'Blocked boulevard', 'Procedural A', 'Procedural B']


def interior(m):
    n = np.zeros_like(m, int)
    n[1:-1, 1:-1] = m[1:-1, 2:].astype(int) + m[1:-1, :-2] + m[2:, 1:-1] + m[:-2, 1:-1]
    return m & (n >= 3)


for root in sys.argv[1:]:
    print(root)
    per = collections.defaultdict(collections.Counter)
    parent_d = collections.defaultdict(list)
    for f in glob.glob(f"{root}/dump_m*_s*/*.json"):
        j = json.load(open(f))
        if j["why"] != "no_qualifying_spot":
            continue
        m = int(re.search(r"dump_m(\d)_", f).group(1))
        W, H = j["W"], j["H"]
        A = lambda k: np.array(j[k], float).reshape(H, W)
        tl, bl, ex, rs, tf, occ = A("trueland") > 0, A("landable") > 0, A("explored") > 0, A("rssi"), A("tfield"), A("occ") > 0
        p = j["parent"]; pd = tf[int(p[1]), int(p[0])]
        ok = interior(tl) & ~occ & (rs >= -73) & (tf >= 0) & (tf <= pd - 5)
        cat = "perception" if (ok & ex).any() else ("exploration" if ok.any() else "geometry")
        per[m][cat] += 1
        parent_d[m].append(pd)
    for m in range(6):
        if per[m]:
            print(f"  {NAMES[m]:18s} {dict(per[m])}  parent path-to-camp at failure: {np.mean(parent_d[m]):.0f} m")
