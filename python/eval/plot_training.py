"""Training evidence (SPEC Section 12): cumulative reward, episode length, success rate and curriculum lesson
from ML-Agents TensorBoard logs.

usage: python plot_training.py docs/results/training results/runs/nav_ppo_v1/Nav results/runs/nav_ppo_v2/Nav ...
"""
import os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

TAGS = [("Environment/Cumulative Reward", "cumulative reward"), ("Environment/Episode Length", "episode length (steps)"),
        (None, "success rate"), (None, "curriculum lesson")]


def series(ea, tag):
    if tag not in ea.Tags()["scalars"]:
        return None, None
    v = ea.Scalars(tag)
    return np.array([x.step for x in v]), np.array([x.value for x in v])


def main(dst, runs):
    os.makedirs(dst, exist_ok=True)
    fig, axs = plt.subplots(1, 4, figsize=(22, 4.2))
    summary = []
    for run in runs:
        ea = EventAccumulator(run, size_guidance={"scalars": 0}); ea.Reload()
        name = run.rstrip("/").split("/")[-2]
        behaviour = run.rstrip("/").split("/")[-1]
        succ_tag = f"{behaviour}/Success"
        lesson_tags = [t for t in ea.Tags()["scalars"] if t.startswith("Environment/Lesson Number")]
        for i, (tag, lab) in enumerate(TAGS):
            t = tag if tag else (succ_tag if i == 2 else (lesson_tags[0] if lesson_tags else None))
            if t is None:
                continue
            x, y = series(ea, t)
            if x is None:
                continue
            axs[i].plot(x / 1e6, y, label=name)
            axs[i].set_title(lab); axs[i].set_xlabel("steps (M)"); axs[i].grid(alpha=0.3)
        x, y = series(ea, succ_tag)
        r = series(ea, "Environment/Cumulative Reward")[1]
        summary.append(f"| {name} | {x[-1] / 1e6:.2f} M | {r[-1]:.3f} | {100 * y[-1]:.1f} % |" if x is not None else f"| {name} | | | |")
    axs[0].legend()
    fig.tight_layout(); fig.savefig(os.path.join(dst, "training_" + "_".join(r.rstrip('/').split('/')[-1] for r in runs[:1]) + ".png"), dpi=85)
    out = ["| Run | Steps | Final reward | Final success (training episodes) |", "|---|---|---|---|"] + summary
    open(os.path.join(dst, "training_" + runs[0].rstrip('/').split('/')[-1] + ".md"), "w").write("\n".join(out) + "\n")
    print("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
