# Checkpoint 3 — First navigation training run

Run `nav_ppo_v1` (configs/nav_ppo_v1.yaml, seed 1): PPO, MLP 2×256, 33-value observation, curriculum L1→L4.
- L1 solved in ~0.8 M steps (training success ≈ 95 %), curriculum reached L4 at 2.3 M steps; stopped at 10.76 M
  (L4 plateau, reward ≈ 1.45, training success ≈ 83 %) — and because the CPU reached 100 °C.
- Held-out evaluation (100 episodes per level): 98 / 98 / 93 / 80 % success on L1–L4 (`docs/results/nav_v1/`).
- Curves: `docs/results/training/training_Nav.png`.
