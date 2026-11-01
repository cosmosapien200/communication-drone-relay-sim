"""RL subject DA1: base-paper presentation (PPO, Schulman et al. 2017) -> submissions/DA1/.

usage: .venv/bin/python python/tools/build_ppt_da1.py
"""
import os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

_here = os.path.dirname(os.path.abspath(__file__))
exec(open(os.path.join(_here, "build_ppt.py")).read().split("# ---------------------------------------------------------------- 1 title")[0])  # shared slide helpers
OUT = os.environ.get("DA1_OUT", A("submissions/DA1/DA1_PPO_Base_Paper_Presentation.pptx"))
STUDENT = os.environ.get("DA1_STUDENT", "LOKESHWARAN P  23BAI1098")
FIG = A("docs/report_assets/drones/fig_ppo_clip.png")

# the clipped objective as a function of the probability ratio r, for a single sample
r = np.linspace(0.4, 1.6, 300); eps = 0.2
fig, ax = plt.subplots(1, 2, figsize=(9, 3.3), sharey=False)
for a, A_, name in ((ax[0], 1.0, "Advantage > 0 (good action)"), (ax[1], -1.0, "Advantage < 0 (bad action)")):
    a.plot(r, r * A_, "--", color="#a6a6a6", lw=2, label="unclipped  r·A")
    a.plot(r, np.minimum(r * A_, np.clip(r, 1 - eps, 1 + eps) * A_), color="#f27f1b", lw=3, label="L_CLIP")
    a.axvline(1, color="#14213d", lw=1, ls=":"); a.set_title(name, fontsize=11); a.set_xlabel("probability ratio r = π_new / π_old"); a.grid(alpha=0.3); a.legend(fontsize=9)
fig.tight_layout(); fig.savefig(FIG, dpi=170); plt.close(fig)

n = 0
# 1 title
s = new(dark=True)
cover(s, "docs/report_assets/video/t100.png", 7.0, 0, W - 7.0, H)
text(s, 0.7, 1.1, 6.3, 2.2, "Proximal Policy Optimization Algorithms", size=38, color=WHITE, font=HEAD, bold=True)
text(s, 0.7, 3.2, 6.3, 0.9, "Base paper for the Communication Drone project", size=18, color=RGBColor(0xC9, 0xD3, 0xE3), italic=True)
text(s, 0.7, 4.3, 6.3, 1.0, "J. Schulman, F. Wolski, P. Dhariwal, A. Radford, O. Klimov (OpenAI), arXiv:1707.06347, 2017", size=14, color=WHITE)
text(s, 0.7, 5.5, 6.3, 1.0, [[("Theory DA1 — Reinforcement Learning", {"bold": True})], STUDENT], size=14, color=WHITE)
s.notes_slide.notes_text_frame.text = "We present the PPO paper, then show why it is the base method for our relay-drone project."

# 2 why this paper
s = new()
title(s, "Why this paper")
bullets(s, 0.6, 1.5, 6.1, 5, [
    ("Our task: ", "autonomous drones that fly unknown streets and land under gusts, forming a Wi-Fi relay chain after a disaster."),
    ("Both skills are continuous control: ", "the policies output velocity commands."),
    ("The paper's promise: ", "the stability of trust-region methods with the simplicity of first-order optimisation."),
    ("It scales with simulators: ", "many parallel actors collect data, which suits our 32 training arenas."),
], size=17)
picture(s, "docs/report_assets/video/t100.png", 7.0, 1.6, 5.8, 3.4)
text(s, 7.0, 5.1, 5.8, 0.5, "A relay drone landing in our simulator, flown by a PPO policy.", size=12, color=MUTED)

# 3 background
s = new()
title(s, "The problem PPO solves")
for i, (h_, t_, c) in enumerate([
    ("Vanilla policy gradient", "L = E[log π(a|s) · A]. Only one safe gradient step per batch; repeated steps cause destructively large policy updates.", NAVY),
    ("TRPO", "Limits each update with a KL trust region. Reliable, but needs conjugate gradients and is complicated to implement.", TEAL),
    ("Goal of PPO", "Several epochs of minibatch updates per batch of data, using only a first-order optimiser such as Adam.", ORANGE)]):
    x = 0.6 + i * 4.1
    card(s, x, 1.6, 3.9, 3.9)
    text(s, x + 0.2, 1.75, 3.5, 4.5, [[(h_, {"bold": True, "color": c, "size": 22, "font": HEAD})], t_], size=17)
s.notes_slide.notes_text_frame.text = "The surrogate objective in TRPO is maximised under a KL constraint; PPO replaces the constraint with a clipped objective."

# 4 core idea
s = new()
title(s, "Core idea: clip the probability ratio")
picture(s, "docs/report_assets/drones/fig_ppo_clip.png", 0.6, 1.4, 12.1, 3.9)
text(s, 0.6, 5.4, 12.1, 0.6, "L_CLIP(θ) = E_t [ min( r_t(θ) A_t ,  clip(r_t(θ), 1 − ε, 1 + ε) A_t ) ]", size=22, color=NAVY, font=HEAD, bold=True, align=PP_ALIGN.CENTER)
text(s, 0.6, 6.05, 12.1, 0.7, "Taking the minimum gives a pessimistic bound: there is no gain from pushing r outside [1 − ε, 1 + ε].", size=16, color=MUTED, align=PP_ALIGN.CENTER)

# 5 full objective
s = new()
title(s, "Full objective and advantage estimate")
card(s, 0.6, 1.5, 12.1, 1.5)
text(s, 0.8, 1.55, 11.7, 1.4, [[("L_t(θ) = E_t [ L_CLIP − c₁ · (V_θ(s_t) − V_target)² + c₂ · S[π_θ](s_t) ]", {"bold": True, "size": 22, "font": HEAD, "color": NAVY})],
                               "clipped policy term  −  value-function error  +  entropy bonus for exploration"], size=15, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
card(s, 0.6, 3.2, 12.1, 1.5)
text(s, 0.8, 3.25, 11.7, 1.4, [[("Â_t = δ_t + (γλ)δ_t+1 + … + (γλ)^(T−t+1) δ_T−1 ,   δ_t = r_t + γV(s_t+1) − V(s_t)", {"bold": True, "size": 20, "font": HEAD, "color": NAVY})],
                               "truncated generalised advantage estimation over T steps"], size=15, color=MUTED, anchor=MSO_ANCHOR.MIDDLE)
bullets(s, 0.6, 4.95, 12.1, 2, ["Policy and value function can share parameters, hence the combined loss.",
                                "The paper also tests an adaptive KL penalty as an alternative; the clipped version performed better."], size=16)

# 6 algorithm
s = new()
title(s, "The algorithm")
steps = ["for each iteration:", "    for each of N parallel actors:", "        run π_old for T timesteps", "        compute advantages Â_1 … Â_T",
         "    optimise L for K epochs", "    with minibatch Adam (M ≤ NT)", "    θ_old ← θ"]
card(s, 0.6, 1.5, 6.0, 4.3)
text(s, 0.8, 1.65, 5.6, 4.8, steps, size=18, font="Courier New", color=NAVY)
stat(s, 7.1, 1.5, 2.6, "N", "parallel actors: ours 32 arenas")
stat(s, 10.0, 1.5, 2.6, "T", "horizon: ours 256 (nav), 128 (land)")
stat(s, 7.1, 3.3, 2.6, "K", "epochs per batch: ours 3")
stat(s, 10.0, 3.3, 2.6, "ε = 0.2", "the paper's best clip value, used by us")
text(s, 7.1, 5.2, 5.6, 1.2, "Simple enough to implement in full: our DA2 code does this in one Python file.", size=16, color=INK, italic=True)

# 7 results
s = new()
title(s, "What the paper showed")
stat(s, 0.7, 1.5, 3.7, "0.82", "average normalised score with ε = 0.2 on 7 MuJoCo tasks: best setting tested", vsize=54)
stat(s, 0.7, 3.5, 3.7, "−0.39", "same benchmark with no clipping or penalty", vsize=54, vcolor=NAVY)
bullets(s, 4.8, 1.6, 8.0, 5, [
    ("Continuous control: ", "beat TRPO, CEM, vanilla PG and A2C on almost all 7 MuJoCo tasks at 1 M steps."),
    ("3-D humanoid: ", "learned running, steering and getting up, showing that it scales to hard high-dimensional control."),
    ("Atari (49 games): ", "fastest learning (won 30 games on training-average reward); ACER won more on final scores, 28 to 19."),
    ("Practical: ", "the paper notes PPO matches ACER's sample complexity while being much simpler."),
], size=17)

# 8 algorithm selection
s = new()
title(s, "Why PPO for our drones")
rows = [("Algorithm", "Actions", "Why / why not for this project"), ("DQN", "Discrete only", "Velocity commands are continuous"),
        ("SAC", "Continuous", "Sample-efficient, but off-policy: more complexity when the simulator already gives cheap data"),
        ("PPO", "Continuous", "Stable, few settings, scales with parallel arenas; proven on quadrotors and in drone racing")]
tb = s.shapes.add_table(4, 3, Inches(0.6), Inches(1.6), Inches(12.1), Inches(3.4)).table
tb.columns[0].width = Inches(1.9); tb.columns[1].width = Inches(2.2); tb.columns[2].width = Inches(8.0)
for i, row in enumerate(rows):
    for j, v in enumerate(row):
        c = tb.cell(i, j); c.text = v; c.fill.solid(); c.fill.fore_color.rgb = NAVY if i == 0 else (RGBColor(0xFD, 0xE2, 0xC8) if i == 3 else TINT)
        for r_ in c.text_frame.paragraphs[0].runs:
            r_.font.size = Pt(16); r_.font.name = BODY; r_.font.bold = i == 0 or i == 3; r_.font.color.rgb = WHITE if i == 0 else INK
text(s, 0.6, 5.3, 12.1, 1, "Our environment produces more than 10,000 steps per second, so simplicity and stability matter more than sample efficiency.", size=17, color=MUTED, italic=True)

# 9 architecture in the project
s = new()
title(s, "How PPO fits our system")
picture(s, "docs/report_assets/drones/fig_rl_architecture.png", 0.4, 1.2, 12.5, 6.0)

# 10 gaps
s = new()
title(s, "What the paper leaves open, and our plan")
card(s, 0.6, 1.5, 5.9, 4.3)
text(s, 0.85, 1.6, 5.4, 0.5, "Gaps for a real mission", size=20, color=NAVY, font=HEAD, bold=True)
bullets(s, 0.85, 2.2, 5.5, 4.4, ["Assumes the reward is given: reward design is left to us",
                                 "Benchmarks have perfect state, while drones have noisy detectors",
                                 "One task, not a multi-drone mission with radio constraints",
                                 "Hard tasks can fail from a cold start"], size=16)
card(s, 6.85, 1.5, 5.9, 4.3)
text(s, 7.1, 1.6, 5.4, 0.5, "Assignment plan", size=20, color=ORANGE, font=HEAD, bold=True)
bullets(s, 7.1, 2.2, 5.5, 4.4, [("DA2: ", "implement PPO from the paper in PyTorch and train it on our drone tasks"),
                                ("DA3: ", "the proposed novelties: path-progress reward, shaping, curriculum, noise calibrated from the real detector, radio-aware mission planner")], size=16)

# 11 refs
s = new(dark=True)
text(s, 0.8, 0.8, 11.7, 0.9, "References", size=36, color=WHITE, font=HEAD, bold=True)
text(s, 0.8, 1.9, 11.7, 4.5, ["[1] J. Schulman et al., Proximal Policy Optimization Algorithms, arXiv:1707.06347, 2017. (base paper)",
    "[2] J. Schulman et al., Trust Region Policy Optimization, ICML 2015.",
    "[3] J. Schulman et al., High-Dimensional Continuous Control Using Generalized Advantage Estimation, ICLR 2016.",
    "[4] J. Hwangbo et al., Control of a Quadrotor with Reinforcement Learning, IEEE RA-L, 2017.",
    "[5] E. Kaufmann et al., Champion-level drone racing using deep reinforcement learning, Nature, 2023."], size=16, color=RGBColor(0xC9, 0xD3, 0xE3))

prs.save(OUT)
print("saved", OUT, n)
