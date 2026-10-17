# communication-drone-relay-sim

Drones deploy one at a time from a base station and land on camera-detected spots in a collapsed town, forming a
WiFi relay chain toward a disaster camp the base cannot reach directly. Simulation only.

Demo video: [`docs/demo/demo_level_real_detector.mp4`](docs/demo/demo_level_real_detector.mp4) — 4 relays, real detector in the loop, camp covered.

| | |
|---|---|
| **Simulator** | Unity 6 (6000.6.4f1), built-in render pipeline; Unity ML-Agents 4.1 (C#) + `mlagents` 1.1.0 (Python 3.10, PyTorch 2.5) |
| **Vision** | U-Net semantic segmentation (PyTorch → ONNX → Unity Inference Engine) |
| **RL** | PPO (ML-Agents), two separate policies: navigation and landing |
| **Drone** | Simulated flight controller (velocity setpoints), fast first-order model for training, full 6-DOF quadrotor with cascaded PID for evaluation |
| **Mission** | Scripted planner: frontier exploration, spot selection, shared map, sequential multi-drone chaining |

**New here? Start with [`docs/EXPLAINER.md`](docs/EXPLAINER.md)** — how everything works, end to end.

Full spec: [`SPEC.md`](SPEC.md) · report: [`docs/report.md`](docs/report.md) · log: [`PROGRESS.md`](PROGRESS.md)

---

## Simulator / world
- **Procedural collapsed town** (`MapGenerator.cs`, seeded): irregular street grid, box buildings 8–20 m (taller than the 5 m
  cruise altitude → flight is 2.5D through streets), collapsed buildings, rubble barriers (dead ends, detours), landable
  surfaces (plaza, intact road, flat concrete) and look-alikes (debris, puddles, holes, wrecked vehicles, slopes).
  4 curriculum levels (map size 96–120 m, rubble density, landable scarcity, wind).
- **Grid-first**: occupancy/material/ground grids; ray sensors, collisions and WiFi wall crossings are exact grid DDA, so RL
  arenas need no GameObjects (~4,500 env steps/s headless). `TownBuilder.cs` turns the same map into a rendered 3D town.
- **WiFi model**: log-distance path loss `RSSI = P0 − 10·n·log10(d) − Σ wall losses − ground penalty + N(0, σ)`;
  P0 −40 dBm, n 2.0, 2 walls per building crossed (concrete 10 / brick 7 / rubble 5 dB), ground penalty 3 dB, σ 2 dB;
  T_link −80 dBm, margin 6 dB → a landing spot qualifies at ≥ −71 dBm (~35 m line-of-sight hop).
- **Wind**: constant mean + random raised-cosine gusts per episode.
- **Splits**: training maps use even seeds, evaluation maps odd seeds (never seen in training).

## Drone & flight controller
- Interface: `SetVelocity(vx, vy, vz, yawRate)` in body frame (3 m/s horizontal, 1 m/s vertical, 90°/s).
- **Full mode** (evaluation/demo): rigid-body quadrotor (1.2 kg, 4 rotors, 30 ms motor lag, drag), cascaded PID
  velocity → attitude → body rate → mixer, drag feed-forward, anti-windup. vx step: rise 0.54 s, 1 % overshoot, settle 0.83 s.
- **Fast mode** (RL training): first-order velocity lag + accel limits, gusts as high-pass wind disturbance; lags and gust
  gain fitted to the full mode (settling 0.84 vs 0.83 s; 3 m/s gust hold error 0.38 vs 0.38 m).

## Reinforcement learning (PPO, ML-Agents)
Both policies output velocity setpoints only; never rotor commands. Trained on CPU, 1 Unity env × 32 arenas.

### Navigation policy — fly to a waypoint through unseen streets (no map access)
- **Network**: MLP 2 × 256, observation normalisation, no LSTM.
- **Observations (33)**: waypoint distance + bearing (2), body velocity (2), heading sin/cos (2), 24-ray horizontal range ring
  10 m (24), previous action (3).
- **Actions (3)**: forward velocity, sideways velocity, yaw rate ∈ [−1, 1].
- **Reward**: path-distance progress on a precomputed shortest-path field, normalised so a full trip ≈ +1;
  −0.0005/step time cost; proximity penalty up to −0.02/step below 1 m clearance; −0.002·|Δaction|²; **−1** collision (ends);
  **+1** waypoint reached (< 1 m).
- **Curriculum**: levels 1→4 (waypoint distance 4–15 m → 20–80 m, rubble, wind), advanced on reward threshold.
- **Training**: `nav_ppo_v1`, 10.75 M steps (~35 min); PPO lr 3e-4, batch 2048, buffer 32 768, γ 0.995, λ 0.95, ε 0.2, 3 epochs.

### Landing policy — descend onto the chosen patch under gusts and imperfect detections
- **Network**: MLP 2 × 128.
- **Observations (10)**: patch centre offset in image (2), apparent area, detection confidence, edge clearance,
  altitude, body velocity (3), frames since last detection.
- **Actions (3)**: vx, vy, descent rate.
- **Reward**: −0.01·horizontal offset (m) − 0.05·excess descent speed near the ground − 0.002/step − 0.002·|Δaction|²
  + potential shaping Φ = −0.1·(altitude + 2·offset); terminal **+1 + centring bonus (0–1)** on soft touchdown inside the patch;
  **−1** off-patch, hard impact (> 0.6 m/s), patch lost > 3 s, fly-away or timeout.
- **Curriculum**: gust 0 → 1.5 → 3 → 4.5 m/s.
- **Training**: v2 (4.3 M steps, placeholder detector noise) → **v3 fine-tuned 3.7 M steps with the detector noise measured
  on held-out maps** (FN/FP rates, localisation σ and bias, dropped frames). (v1 learned to hover — timeout penalty was smaller than failure.)

## Vision
- **Model**: U-Net (4 levels, base width 16, **0.48 M params**), 128 × 128 RGB → 4 classes
  (unknown, landable, not landable, obstacle). Loss: class-weighted cross-entropy + 0.5·Dice; AdamW, OneCycle, 40 epochs on an RTX 3060.
- **Data**: fully synthetic, auto-labelled — 13,750 frames from 125 generated towns (11,000 train / 1,100 val / 1,650 test,
  **split by map seed**), camera at 45° (exploration) and nadir (landing, 0.6–6 m). Labels are pixel-perfect: the scene is
  re-rendered with flat unlit per-class colours. Domain randomisation: textures/palette, dust, sun direction & intensity,
  shadows, fog; GPU augmentation (colour jitter, blur, noise, flips).
- **Results (held-out test maps)**: mIoU **0.909**, landable IoU 0.81 (P 0.88 / R 0.91). Weakened detectors for the ablation:
  20 % of maps → mIoU 0.66; 5 % → 0.48.
- **In the loop**: runs inside Unity via Inference Engine (CPU backend).
  - *Segmentation → ground projection*: each pixel is ray-cast onto the ground with the known camera pose; cells become
    "landable" after repeated consistent evidence (≥ 3, ≥ 70 % agreement) and only with room for footprint + clearance.
  - *Landing-spot locator* (nadir): landable connected component nearest the expected patch → offset, area, confidence,
    edge clearance. Measured: FN 1.6 %, median centre error 0.44 m.
  - *Noise model*: ground truth corrupted with the measured confusion matrix and locator error rates (same output type), used
    for RL training and the detector-quality sweep.

## Mission planner (scripted)
Per drone: **Download** shared map → **Transit** to parent → **Explore** (frontier with RSSI to parent ≥ −74 dBm and
shortest path distance to the camp; gimbal camera pans ±60°) → **Navigate** to the best spot (landable, RSSI ≥ −71 dBm,
≥ 5 m progress, shortest path to camp) → **Land** (landing policy + locator) → **Upload** map and confirm link (else next spot).
Chain complete when the camp gets ≥ −80 dBm from the last relay.

## Results (held-out maps, 100 episodes / missions per condition)
| Experiment | Result |
|---|---|
| Navigation success L1–L4 | PPO 98 / 98 / 93 / 80 % · potential field 93 / 92 / 78 / 54 % · straight line 84 / 64 / 39 / 18 % · A* (full map) 100 / 100 / 99 / 94 % |
| Landing success, gust 0 → 6 m/s | PPO 100 % (1–5 cm error) · PID visual servoing 97 → 44 % |
| Full mission, level 2, real U-Net | camp covered 34 %, +58 m toward the camp, 3 relays (ground-truth vision: 43 %, +64 m) |
| Detector quality sweep | chain reach holds to ~2× measured detector error, then collapses (0 m at 8×) |
| Shared map | exploration per later drone 52 s → 30 s |
| Spot rule | best-toward-target 47 % vs random 25 % vs nearest-to-parent 3 % |

Details and limitations: [`docs/report.md`](docs/report.md).

## Run the live demo
Requires Unity 6000.6.4f1 with Linux build support (or the built player).
- **Editor**: open `unity/`, open `Assets/Scenes/Demo.unity`, press **Play**.
- **Player** (after building `CommDrone.EditorTools.Builder.BuildDemo`): `Builds/Demo/Demo.x86_64`

Six maps — Z-route, U-turn, Staircase, Blocked boulevard (hand-built) and two held-out procedural towns.
Keys: **1–6** map · **R** restart · **Space** pause · **+/−** speed · **C** camera (chase / overview / top-down) ·
**V** real U-Net ↔ ground-truth vision · **H** hide panel. HUD: mode, RSSI to parent, weakest link, battery, detection
confidence, step reward, wind; shared-map minimap; camera view with live segmentation overlay.

## Reproduce
```bash
uv sync                                                            # Python env (mlagents 1.1.0, torch cu121, ultralytics...)
python/rl/train.sh configs/nav_ppo_v1.yaml nav_ppo_v1 NavTraining 1 32 1      # navigation PPO
python/rl/train.sh configs/land_ppo_v3.yaml land_ppo_v3 LandTraining 1 32 1 --initialize-from land_ppo_v2
.venv/bin/python python/vision/train_seg.py --out results/vision/unet_v2       # segmentation (dataset from VisionDataset.GenerateAll)
python/eval/eval_nav.sh nav_v1; python/eval/eval_land.sh; python/eval/eval_missions.sh 100
```
Players are built from editor scripts (`unity/Assets/Editor/Builder.cs`); `./um` drives the Unity Editor through the official Unity MCP relay.

## Layout
```
unity/Assets/Scripts/  Core (map, WiFi, path field) · Flight · RL (agents, eval) · Vision · Mission · World (builder, demo levels)
unity/Assets/Editor/   builders, dataset generator, calibration, tests (all content built from code)
python/                rl/ vision/ eval/ tools/
configs/               one PPO config per run      docs/  report, results, checkpoints, demo video
```
