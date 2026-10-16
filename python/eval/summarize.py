"""Aggregate EvalRunner CSVs into Markdown tables (mean ± std over episodes) and plots.

usage:
  python summarize.py nav  results/eval/nav_nav_v1  docs/results/nav_v1
  python summarize.py land results/eval/land_xxx    docs/results/land_xxx
"""
import glob, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import csv


def read(p):
    return list(csv.DictReader(open(p)))


def ms(x):
    x = np.asarray(x, float)
    if len(x) == 0:
        return "–"
    return f"{x.mean():.2f} ± {x.std():.2f}"


def rate(rows, outcome):
    v = np.array([r["outcome"] == outcome for r in rows], float)
    return f"{100 * v.mean():.0f} % (±{100 * v.std() / np.sqrt(len(v)):.0f})", v.mean()


def nav(src, dst):
    files = sorted(glob.glob(os.path.join(src, "*.csv")))
    lines = ["| Controller | Level | Flight | N | Success | Collision | Timeout | Path ÷ optimal (succ.) | Time to waypoint s (succ.) |",
             "|---|---|---|---|---|---|---|---|---|"]
    plotd = {}
    for f in files:
        rows = read(f)
        if not rows:
            continue
        c, L, fl = rows[0]["controller"], rows[0]["level"], rows[0]["flight"]
        succ = [r for r in rows if r["outcome"] == "success"]
        s_txt, s = rate(rows, "success")
        lines.append(f"| {c} | {L} | {fl} | {len(rows)} | {s_txt} | {rate(rows, 'collision')[0]} | {rate(rows, 'timeout')[0]} | "
                     f"{ms([float(r['path_ratio']) for r in succ])} | {ms([float(r['time_s']) for r in succ])} |")
        if fl == "fast":
            plotd.setdefault(c, {})[int(float(L))] = s
    os.makedirs(dst, exist_ok=True)
    open(os.path.join(dst, "nav_table.md"), "w").write("\n".join(lines) + "\n")
    fig, ax = plt.subplots(figsize=(6, 4))
    for c, d in plotd.items():
        ks = sorted(d)
        ax.plot(ks, [100 * d[k] for k in ks], "o-", label=c)
    ax.set_xlabel("curriculum level (held-out maps)"); ax.set_ylabel("success rate (%)"); ax.set_xticks([1, 2, 3, 4])
    ax.set_ylim(0, 105); ax.grid(alpha=0.3); ax.legend(); ax.set_title("Navigation: policy vs baselines")
    fig.tight_layout(); fig.savefig(os.path.join(dst, "nav_success.png"), dpi=90)
    print("\n".join(lines))


def land(src, dst):
    files = sorted(glob.glob(os.path.join(src, "*.csv")))
    lines = ["| Controller | Gust (m/s) | Mean wind | Noise × | Flight | N | Success | Off patch | Hard impact | Lost/timeout | Landing error m (touchdowns) | Touchdown speed m/s |",
             "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    plotd = {}
    for f in files:
        rows = read(f)
        if not rows:
            continue
        r0 = rows[0]
        tag = os.path.basename(f).split("_")[0]
        label = r0["controller"] if r0["controller"] == "pid" else ("policy_" + tag if tag in ("v2", "v3") else "policy")
        td = [r for r in rows if r["landing_error"]]
        lost = np.array([r["outcome"] in ("lost", "timeout", "fly_away") for r in rows], float)
        s_txt, s = rate(rows, "success")
        lines.append(f"| {label} | {r0['gust']} | {r0['wind_mean']} | {r0['noise_scale']} | {r0['flight']} | {len(rows)} | {s_txt} | "
                     f"{rate(rows, 'off_patch')[0]} | {rate(rows, 'hard_impact')[0]} | {100 * lost.mean():.0f} % | "
                     f"{ms([float(r['landing_error']) for r in td])} | {ms([float(r['touchdown_speed']) for r in td])} |")
        if r0["flight"] == "fast" and float(r0["noise_scale"]) == 1.0:
            plotd.setdefault(label, {})[float(r0["gust"])] = (s, np.mean([float(r["landing_error"]) for r in td]) if td else np.nan)
    os.makedirs(dst, exist_ok=True)
    open(os.path.join(dst, "land_table.md"), "w").write("\n".join(lines) + "\n")
    fig, ax = plt.subplots(1, 2, figsize=(11, 4))
    for c, d in plotd.items():
        ks = sorted(d)
        ax[0].plot(ks, [100 * d[k][0] for k in ks], "o-", label=c)
        ax[1].plot(ks, [d[k][1] for k in ks], "o-", label=c)
    ax[0].set_xlabel("gust amplitude (m/s)"); ax[0].set_ylabel("success rate (%)"); ax[0].set_ylim(0, 105); ax[0].legend(); ax[0].grid(alpha=0.3)
    ax[1].set_xlabel("gust amplitude (m/s)"); ax[1].set_ylabel("landing error (m)"); ax[1].legend(); ax[1].grid(alpha=0.3)
    fig.suptitle("Landing: RL policy vs PID visual servoing"); fig.tight_layout(); fig.savefig(os.path.join(dst, "land_gusts.png"), dpi=90)
    print("\n".join(lines))


if __name__ == "__main__":
    {"nav": nav, "land": land}[sys.argv[1]](sys.argv[2], sys.argv[3])
