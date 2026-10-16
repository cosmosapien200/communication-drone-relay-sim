"""Summarise full-mission experiments (results/eval/missions/*.csv) into tables and the headline plot.

usage: python summarize_missions.py results/eval/missions docs/results/missions
"""
import csv, glob, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def load(path):
    rows = list(csv.DictReader(open(path)))
    dpath = path.replace(".csv", "_drones.csv")
    drones = list(csv.DictReader(open(dpath))) if os.path.exists(dpath) else []
    return rows, drones


def f(rows, k):
    return np.array([float(r[k]) for r in rows if r[k] not in ("", "NaN", "Infinity", "-Infinity")], float)


def ms(x, fmt="{:.1f}"):
    return "–" if len(x) == 0 else (fmt + " ± " + fmt).format(x.mean(), x.std())


def stats(rows, drones):
    succ = np.array([int(r["success"]) for r in rows], float)
    landed = {}
    for d in drones:
        landed.setdefault(d["mission"], 0)
        if d["outcome"] == "Done":
            landed[d["mission"]] += 1
    relays = np.array([landed.get(r["mission"], 0) for r in rows], float)
    rng = f(rows, "range_extended_m")
    per_drone = np.array([float(r["range_extended_m"]) / max(1, landed.get(r["mission"], 0)) for r in rows], float)
    used = f(rows, "drones_used"); lost = f(rows, "drones_lost")
    expl = np.array([float(d["explore_time_s"]) for d in drones], float)
    batt = np.array([float(d["battery_used_s"]) for d in drones], float)
    return dict(n=len(rows), succ=succ, relays=relays, rng=rng, per_drone=per_drone, used=used, lost=lost,
                chain=f(rows, "chain_quality_dbm"), expl=expl, batt=batt,
                expl_later=np.array([float(d["explore_time_s"]) for d in drones if int(d["drone"]) > 1], float),
                batt_later=np.array([float(d["battery_used_s"]) for d in drones if int(d["drone"]) > 1], float))


def row(name, s):
    return (f"| {name} | {s['n']} | {100 * s['succ'].mean():.0f} % (±{100 * s['succ'].std() / np.sqrt(max(1, s['n'])):.0f}) | "
            f"{ms(s['rng'])} | {ms(s['per_drone'])} | {ms(s['relays'])} | {ms(s['used'])} | {ms(s['lost'], '{:.2f}')} | {ms(s['chain'])} |")


HEAD = ["| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |",
        "|---|---|---|---|---|---|---|---|---|"]


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    S = {}
    for p in sorted(glob.glob(os.path.join(src, "*.csv"))):
        if p.endswith("_drones.csv"):
            continue
        rows, drones = load(p)
        if rows:
            S[os.path.basename(p)[:-4]] = stats(rows, drones)
    out = ["# Full-mission results", "", "Held-out seeds (odd, 2000001+). Mean ± std over missions.", ""]
    groups = [("Vision ablation (level 2)", ["vision_gt", "vision_cnn", "vision_mid", "vision_weak"]),
              ("Detector-quality sweep, measured noise model × k (level 2)", [k for k in S if k.startswith("quality_k")]),
              ("Spot selection strategy (level 2, noise model)", ["strategy_best", "strategy_random", "strategy_nearest"]),
              ("Difficulty (noise model)", ["quality_k1", "level_L3", "level_L4"]),
              ("RL policies vs scripted stand-ins (level 2, noise model)", ["quality_k1", "standin_nav", "standin_land"]),
              ("Flight-controller fidelity (level 2, noise model)", ["quality_k1", "flight_full"])]
    for title, keys in groups:
        keys = [k for k in keys if k in S]
        if not keys:
            continue
        out += [f"## {title}", ""] + HEAD + [row(k, S[k]) for k in sorted(keys, key=lambda k: (k.startswith("quality") and float(k[9:]) or 0, k) if k.startswith("quality") else (0, k))] + [""]
    if "shared_on" in S and "shared_off" in S:
        out += ["## Shared map (level 2, noise model): drones 2..k (who can benefit from the map)", "",
                "| Condition | Drones | Exploration time (s) | Battery used (s) | Target reached | Range extended (m) |", "|---|---|---|---|---|---|"]
        for k in ("shared_on", "shared_off"):
            s = S[k]
            out.append(f"| {k} | {len(s['expl_later'])} | {ms(s['expl_later'])} | {ms(s['batt_later'])} | {100 * s['succ'].mean():.0f} % | {ms(s['rng'])} |")
        out.append("")
    open(os.path.join(dst, "missions.md"), "w").write("\n".join(out) + "\n")
    print("\n".join(out))

    # headline plot: range extended per relay and total vs detector quality
    q = sorted([(float(k[9:]), S[k]) for k in S if k.startswith("quality_k")])
    if q:
        fig, ax = plt.subplots(1, 2, figsize=(12, 4.5))
        ks = [x[0] for x in q]
        for i, (key, lab) in enumerate([("rng", "range extended toward target (m)"), ("per_drone", "range extended per relay (m)")]):
            m = [x[1][key].mean() for x in q]; e = [x[1][key].std() / np.sqrt(len(x[1][key])) for x in q]
            ax[i].errorbar(ks, m, yerr=e, fmt="o-", capsize=3, label="noise model × k")
            for name, mk, col in (("vision_gt", "s", "g"), ("vision_cnn", "D", "b"), ("vision_weak", "v", "r")):
                if name in S:
                    ax[i].errorbar([{"vision_gt": 0, "vision_cnn": 1, "vision_weak": 1.0}[name] + {"vision_gt": 0.05, "vision_cnn": 0.1, "vision_weak": 0.15}[name]],
                                   [S[name][key].mean()], yerr=[S[name][key].std() / np.sqrt(S[name]["n"])], fmt=mk, color=col, capsize=3,
                                   label={"vision_gt": "ground truth (rendered)", "vision_cnn": "real U-Net in the loop", "vision_weak": "weakened U-Net in the loop"}[name])
            ax[i].set_xscale("symlog", linthresh=0.5); ax[i].set_xlabel("detector error scale k (0 = perfect, 1 = measured U-Net)")
            ax[i].set_ylabel(lab); ax[i].grid(alpha=0.3)
        ax[0].legend(fontsize=8)
        fig.suptitle("Headline: how far the relay chain reaches toward the camp vs detector quality (level 2, held-out maps)")
        fig.tight_layout(); fig.savefig(os.path.join(dst, "headline_range_vs_detector.png"), dpi=90)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
