# Communication Drone — Report (draft)

Simulation-only relay-chain drones in Unity 6 (6000.6.4f1) + ML-Agents 4.1 / mlagents 1.1.0 (PPO), PyTorch U-Net,
official Unity MCP. All numbers are on **held-out seeds** never used in training (training map seeds are even,
evaluation seeds odd; vision train/val/test are split by map seed). 100 episodes/missions per condition, mean ± std.

Demo video: `docs/demo/demo_level_real_detector.mp4` (1:52, hand-built level, real U-Net in the loop, 4 relays, camp covered).

---

## 1. System
Scripted mission planner on top; two PPO policies fly (velocity setpoints only); vision and a WiFi model feed both.

| Module (SPEC §11) | Code |
|---|---|
| MapGenerator / TownBuilder | `unity/Assets/Scripts/Core/MapGenerator.cs`, `World/TownBuilder.cs`, `World/DemoLevel.cs` |
| WifiModel, PathField | `Core/WifiModel.cs`, `Core/PathField.cs` |
| FlightController (fast + full) | `Flight/FlightController.cs` |
| VisionSegmenter, LandingSpotLocator, VisionNoiseModel | `Vision/*.cs` |
| NavPolicy / LandPolicy (training agents, ONNX runner) | `RL/NavAgent.cs`, `RL/LandAgent.cs`, `Mission/PolicyRunner.cs` |
| MissionPlanner, SharedMap, runner, demo view | `Mission/DroneMission.cs`, `SharedMap.cs`, `MissionRunner.cs`, `MissionView.cs` |

World: procedural collapsed town (street grid, buildings taller than the 5 m cruise altitude, collapsed buildings and
rubble barriers, landable plazas / intact road / flat concrete, look-alikes: debris, puddles, holes, wrecked vehicles, slopes).
Headless RL arenas use only the grid (no GameObjects): ~4,500 env steps/s.

## 2. Drones subject — flight controller
Full mode: rigid-body quadrotor (1.2 kg, 4 rotors, motor lag 30 ms, drag), cascaded PID velocity → attitude → rate →
mixer, drag feed-forward, conditional-integration anti-windup. Fast mode (RL training): first-order lag + accel limits,
gusts as high-pass wind disturbance; lags and gust gain tuned to match the full mode.

| Axis | Mode | Rise 10–90 % (s) | Overshoot | Settling 5 % (s) | Max tilt |
|---|---|---|---|---|---|
| vx/vy | Full | 0.54 | 1.0 % | 0.83 | 26.8° |
| vx/vy | Fast | 0.61 | 0 % | 0.84 | – |
| vz | Full / Fast | 0.41 / 0.45 | 1.8 % / 0 | 0.55 / 0.62 | – |
| yaw rate | Full / Fast | 0.42 / 0.41 | 0 / 0 | 0.62 / 0.56 | – |

Gust 3 m/s (2 s) position hold: max error 0.38 m (full) vs 0.38 m (fast). 20 m square at 2 m/s: cross-track RMS 6.6 cm.
Plots: `docs/results/flight_steps.png`, `flight_gust_square.png`. Tuning procedure: step each axis, fix
integrator windup (10 % overshoot → 1 %) and drag lag (feed-forward), then fit fast-mode τ to the full-mode settling time.

## 3. Reinforcement learning
### Navigation (PPO, MLP 2×256, 33 obs: waypoint, body velocity, heading, 24 range rays, previous action)
Reward: path-distance progress (BFS field, normalised by start distance), time cost, proximity penalty, −1 collision, +1 reach.
Curriculum L1→L4 reached at 2.3 M steps (`docs/results/training/training_Nav.png`).

| Level | PPO (nav_v1) | A* full map (upper bound) | Potential field (same sensors) | Straight line |
|---|---|---|---|---|
| L1 | 98 % | 100 % | 93 % | 84 % |
| L2 | 98 % | 100 % | 92 % | 64 % |
| L3 | 93 % | 99 % | 78 % | 39 % |
| L4 | 80 % | 94 % | 54 % | 18 % |

Transfer to the full quadrotor: L2 95 %, L4 74 %. Path ÷ optimal ≈ 1.1–1.2 for the policy. A fine-tune on the final fast-mode
parameters (nav_v2) did **not** improve held-out results (L4 80 %, full-mode L4 65 %) — nav_v1 is deployed.
Full tables: `docs/results/nav_v1/nav_table.md`, `nav_v2/`.

### Landing (PPO, MLP 2×128, 10 obs from the spot locator + rangefinder + velocity)
v1 learned to hover (timeout penalty < failure penalty); v2 added potential-based shaping and a −1 timeout; v3 was
fine-tuned with the **measured** detector noise. Gust curriculum 0 → 1.5 → 3 → 4.5 m/s.

| Gust (m/s) | RL (land_v3) | PID visual servoing |
|---|---|---|
| 0 | 100 %, error 0.01 m | 97 %, 0.02 m |
| 1.5 | 100 %, 0.02 m | 91 %, 0.04 m |
| 3 | 100 %, 0.02 m | 76 %, 0.07 m |
| 4.5 | 100 %, 0.03 m | 64 %, 0.11 m |
| 6 (beyond training) | 100 %, 0.05 m | 44 %, 0.14 m |

2× detector noise @3 m/s: RL 100 %, PID 0 % (chases false positives). Full quadrotor @3 m/s: RL 96 % (3 % hard impacts —
the policy touches down at ≈0.5 m/s, close to the 0.6 m/s limit), PID 33 %. PID failures are almost all "never centred, timed out".

## 4. Machine vision
Dataset: 13,750 synthetic frames (128×128, 45° explore and nadir), pixel-perfect labels from a flat-colour re-render,
split by map seed; domain randomisation of palette, dust, lighting, shadows, fog, plus GPU-side blur/noise/colour jitter.

| Model | Training data | mIoU | Landable IoU | Landable P / R |
|---|---|---|---|---|
| U-Net v2 (deployed) | 100 maps | **0.909** | 0.810 | 0.88 / 0.91 |
| U-Net mid | 20 maps, 25 ep | 0.656 | 0.422 | 0.62 / 0.57 |
| U-Net weak | 5 maps, 15 ep | 0.476 | 0.190 | 0.36 / 0.29 |

Runs inside Unity via Inference Engine (CPU backend; the GPU-compute backend returned constant output in the Linux editor).
Landing-spot locator measured in Unity on held-out maps (v2): FN 1.6 %, FP 40 % when the patch is out of view,
median centre error 0.44 m. These measured rates parameterise the noise model used for RL training and the quality sweep.
Confusion matrices: `docs/results/vision_unet_v2.png`.

## 5. Full mission (level 2 unless noted, 100 held-out missions, ≤ 6 drones)
| Condition | Camp covered | Range extended (m) | Range / relay (m) | Relays | Chain quality |
|---|---|---|---|---|---|
| Ground-truth vision | 43 % | 63.6 ± 34.0 | 18.5 | 3.2 | −72.9 dBm |
| **Real U-Net in the loop** | **34 %** | **58.1 ± 36.2** | 18.0 | 3.0 | −72.4 dBm |
| Mid U-Net in the loop | 7 % | 27.9 ± 29.7 | 11.5 | 1.6 | −71.4 dBm |
| Weak U-Net in the loop | 0 % | 0 | 0 | 0 | – |
| Spot rule: random | 25 % | 52.3 | 11.9 | 3.9 | −71.2 |
| Spot rule: nearest to parent | 3 % | 33.5 | 7.0 | 4.2 | −66.4 |
| Scripted nav stand-in | 9 % | 35.0 | 14.2 | 1.7 (2.6 drones lost) | – |
| Scripted (PID) landing | 42 % | 61.8 | 18.4 | 3.0 | – |
| Full-quadrotor flight | 30 % | 53.9 | 18.0 | 2.6 (1.4 lost) | – |
| Level 3 / level 4 | 17 % / 2 % | 52.8 / 32.8 | 15.4 / 13.2 | 2.7 / 1.9 | – |

**Headline** (`docs/results/missions/headline_range_vs_detector.png`): range extended toward the camp stays ≈ 61–65 m
up to 2× the measured detector error, drops to 53 m at 4× and to 0 at 8×; the real CNN in the loop (58 m) sits slightly
below its noise-model twin (64 m) — i.i.d. pixel noise is kinder than the real detector's spatially correlated mistakes.

**Shared map** (drones 2..k): exploration time 30 ± 24 s vs 52 ± 36 s without, battery 68 vs 92 s per drone.

**Spot rule** matters most: "best toward target" covers the camp 47 % vs random 25 % and nearest-to-parent 3 %.

## 6. Honest limitations
- The WiFi model is a log-distance + wall-count proxy (no multipath, diffraction, interference, throughput). Its
  parameters were tuned (n 2.3 → 2.0, wall loss 12 → 10 dB) after the first mission runs; the final values are within
  commonly cited ranges but the history is in PROGRESS.md.
- The world was changed to make missions solvable: street-aligned intact-road strips were added after failure dumps
  showed no landable spot inside the base's line-of-sight corridor; a 5 m progress constraint was added to the spot rule.
- Mission success is modest (34 % with the real detector on L2; 2 % on L4); most failures are "no qualifying spot"
  under the spec's RSSI-gated exploration, i.e. the chain cannot turn a corner without a landable spot at it.
- Noise model: i.i.d. per-pixel confusion; the real detector's errors are spatially correlated (visible as the 6 m gap).
- Landing v3 was trained with v1-detector noise; the deployed detector is v2 (similar magnitudes).
- Demo level: 2 of 6 wind/noise seeds complete the 4-relay chain with the real detector.
- No real hardware; RL uses the fast flight mode; full-mode transfer costs 6–15 points of success.
- Thermal: the CPU reached 100 °C with three parallel jobs; a thermal guard pauses training at ≥ 98 °C.
