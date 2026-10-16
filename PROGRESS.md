# Progress log

Updated at every major milestone. Newest first.

## 2026-10-17 01:57 — WP1–WP3 done, first RL runs, vision dataset
- **WP1 map generator** (`Scripts/Core/MapGenerator.cs`): seeded street grid, irregular blocks/alleys, collapsed
  buildings, rubble barriers keeping base→target connected, landable patches + 5 look-alike types. 1–20 ms/map.
  Debug views: `docs/screenshots/maps/`. **PathField**: 8-connected Dijkstra, ~1 ms on 120×120.
- **WP2 WiFi model** (`WifiModel.cs`): P0=−40 dBm, n=2.1, 2 walls per building crossed (concrete 12 / brick 8 / rubble 6 dB),
  ground penalty 3 dB, σ=2 dB, T_link=−80, M=6 → qualify ≥ −71 dBm (~30 m LOS hop). Heatmaps in `docs/screenshots/maps/`.
- **WP3 flight controller**: fast (first-order lag) + full (6-DOF quadrotor, 4 rotors, velocity→attitude→rate cascade,
  mixer, motor lag). Tuned: full vx rise 0.54 s / 1 % overshoot / settle 0.83 s; fast mode matched (settle 0.84 s);
  gust (3 m/s) hold error full 0.36 m vs fast 0.38 m. `docs/results/flight_metrics.md`, `flight_steps.png`.
- **Throughput (checkpoint-2 number)**: ~4,500 env steps/s nav training with 1 env × 32 arenas (trainer-bound).
- **N1 nav PPO** (`nav_ppo_v1`): curriculum L1→L4 reached by 6.6M steps; stopped at 10.76M (plateau ~1.46 reward on L4,
  and CPU hit 100 °C). ONNX exported.
- **Baselines (L4, 100 held-out eps)**: A*+reactive 93 % success, potential field 55 %, straight line 18 %.
- **Landing**: v1 learned to hover (timeout penalty < failure penalty). v2 adds potential shaping and −1 timeout →
  reward +2.0 and already in the hardest gust lesson at <1M steps.
- **Vision dataset**: 13.75k frames (train 11k / val 1.1k / test 1.65k, split by map seed), pixel-perfect labels by
  flat-colour re-render. U-Net (0.48M params) training on GPU.
- **Thermals**: CPU package reached 100 °C with nav+landing+vision in parallel. `python/tools/resmon.sh` now logs every
  30 s and pauses training (SIGSTOP 45 s) at ≥ 98 °C. Run at most ~3 busy cores at a time on this cooler.

## 2026-10-17 — Bootstrap (Section 15.3) — DONE (checkpoint 1)
- [x] Spec read; copied to `SPEC.md`
- [x] git repo, `.gitignore`, layout (`unity/`, `python/`, `configs/`, `docs/`, `results/`)
- [x] `CLAUDE.md` and `.claude/agents/{implementer,evaluator}.md` from spec 15.4/15.5
- [x] uv env: Python 3.10.12, mlagents 1.1.0, torch 2.5.1+cu121 (`cuda.is_available() == True`, RTX 3060), ultralytics 8.4.178
- [x] Unity 6000.6.4f1 project created in batch mode (built-in render pipeline), packages `com.unity.ml-agents@4.1.0`, `com.unity.ai.assistant@2.20.0-pre.2`; 0 compile errors
- [x] Unity MCP relay registered with Claude Code (`unity-mcp`, official relay)
- [x] MCP verified (no approval gate was needed): tool list, console logs, scene hierarchy, C# execution via `python/tools/unity_mcp.py`
- [x] Smoke test: floor+cube scene saved, `docs/screenshots/smoke.png` rendered and checked

### Prerequisite notes
- Filesystem snapshot: root is btrfs but snapshot could not be verified (no sudo) — human to confirm.
- Session is Wayland (XWayland `DISPLAY=:1`), spec asks for X11; Editor launched under XWayland.
- `mlagents` needs `setuptools<70` for `pkg_resources` (added to pyproject).
