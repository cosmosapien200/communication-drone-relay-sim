# Communication Drone — Project Spec

Oct 17, 2026 · @Preethan SK

## 1. Overview

A team of drones deploys one at a time from a base station, each landing on a camera-detected spot to form a relay chain that extends network coverage toward a disaster camp. Everything is simulation-only, built in Unity with ML-Agents. An implementing agent should start with Section 15.

**Problem.** In a collapsed town, the base station cannot reach the target camp directly: buildings and rubble block the signal. A hovering relay drone drains its battery in minutes. A landed (perched) relay draws only radio power, so it can stay up far longer.

**Goal.** Extend the effective network range toward the target by landing relay drones at spots that maximise progress toward the target while keeping each link above a minimum signal strength.

**Subject mapping.**

| Subject | Component | Graded output |
| --- | --- | --- |
| Reinforcement Learning | Navigation policy and landing policy (PPO) | Reward curves, baseline comparisons on held-out maps |
| Drones | Simulated flight controller (cascaded PID), velocity-command interface, landing | Step responses, tracking error, landing accuracy |
| Machine Vision | Landable-surface segmentation, ground projection, landing-spot localisation | IoU per class, effect of detector quality on chain performance |

**In scope:** procedural town maps, RSSI model, vision module, two RL policies, scripted mission planner, sequential multi-drone chaining with a shared map.

**Out of scope:** real hardware, rotor-level RL control, multi-agent RL, SLAM, full 3D free flight (flight is 2.5D: fixed cruise altitude plus vertical landing).

## 2. System architecture

A scripted mission planner sits on top and decides where to go; two separately trained RL policies do the flying; vision and the WiFi model feed both the planner and the policies.

&#91;embedded content: system architecture · 9 modules\]

RL outputs only velocity setpoints. The flight controller turns them into motion, and the simulated town returns camera frames, raycasts and pose.

**Which modules run in each mode:**

| Mode | Flying | Vision | Camera angle |
| --- | --- | --- | --- |
| Transit | Navigation policy | Off | 45° |
| Explore | Navigation policy, waypoints from the planner | Segmentation → shared map | 45° |
| Navigate to spot | Navigation policy | Segmentation (confirms the spot) | 45° |
| Land | Landing policy | Landing-spot locator | Nadir |
| Upload | Landed | Off | — |

## 3. World design

The world is a procedurally generated collapsed town: low-poly box buildings on an irregular street grid, with rubble blocking some streets so the route to the target is never a straight line.

**Layout generator** (seeded, one new map per episode):

1. Generate a 2D grid (for example 100 × 100 m at 1 m cells) and split it into city blocks with randomised street widths.
2. Extrude buildings as boxes with random footprints and heights, all taller than the cruise altitude so the drone must fly through streets.
3. Place collapsed buildings and rubble piles that close off streets, creating dead ends and detours.
4. Place the base station at one edge and the target camp at the far side, usually without line of sight between them.
5. Scatter landable patches (plazas, intact road, flat concrete) and non-landable look-alikes (debris-strewn ground, puddles, cracks and holes, wrecked vehicles, slopes).
6. Export the ground-truth occupancy grid, landable mask and wall materials for the RSSI model, training and dataset labels.

**Visual realism for vision (not for RL):** randomised ground textures (asphalt, concrete, dirt), lighting direction and intensity, shadows, dust overlay and camera noise. RL training runs headless and never renders.

**Performance rules:** box primitives and simple meshes only, GPU instancing for repeated props, static batching, no real-time global illumination, downward camera at 128 × 128.

**Curriculum parameters** (ML-Agents environment parameters, raised as the agent improves):

| Level | Rubble density | Landing spots | Base-to-target distance | Relays needed |
| --- | --- | --- | --- | --- |
| 1 | None | Many | Short, line of sight | 1 |
| 2 | Low | Many | Medium, one turn | 1–2 |
| 3 | Medium | Moderate | Medium, several turns | 2–3 |
| 4 | High, dead ends | Scarce | Long | 3–5 |

**Map sets:** training seeds, a held-out evaluation seed set never seen in training, and one hand-built demo level made from the same primitives.

## 4. Communication model

RSSI between any two nodes is log-distance path loss minus the attenuation of every wall the straight line between them crosses. The chain's quality is its weakest link.

**Link model** for nodes i and j at distance d:

```latex
\mathrm{RSSI}_{ij} = P_0 - 10\,n\,\log_{10}\!\left(\frac{d}{d_0}\right) - \sum_{k \in \mathrm{walls}} L_k + X_\sigma
```

- P0: received power at reference distance d0 (1 m). Tunable, for example −40 dBm.
- n: path-loss exponent. Tunable, start at 2.5–3 for an urban/indoor mix.
- Lk: per-material wall loss from a raycast between the two nodes (concrete, brick, rubble each get their own value). These are simulation parameters, cited to literature ranges, not measurements.
- Xσ: optional zero-mean Gaussian noise (shadowing), so RSSI readings are not perfectly repeatable.
- **Ground penalty:** a node resting on the ground gets an extra fixed loss compared with the same spot at cruise altitude.

**Thresholds:**

- T\_link: minimum usable RSSI for a relay link (for example −80 dBm).
- Margin M: safety margin (for example 6 dB). A landing spot qualifies only if the RSSI measured there is at least T\_link + M + ground penalty.

**Chain quality** = the minimum RSSI over all links from base to the last drone. Coverage of the target = whether the target camp's RSSI to the last drone is at least T\_link.

**Implementation:** a standalone `WifiModel` component with a single query, `RSSI(posA, posB, onGroundB)`, used by the planner, the training environment and the demo overlay alike. Validate it on its own with a heatmap before any RL work.

**Deliberately ignored:** multipath fading, interference, antenna patterns, throughput and latency. RSSI is a proxy for link quality, and the report should say so.

## 5. Flight controller simulation

RL never commands motors. Both policies send velocity setpoints to a simulated flight controller, the same interface a real autonomy stack uses with PX4 or ArduPilot.

**Interface:** `SetVelocity(vx, vy, vz, yawRate)` in the drone's body frame, clipped to configured limits (for example 3 m/s horizontal, 1 m/s vertical).

**Two fidelity modes, same interface:**

| Mode | Used for | Model |
| --- | --- | --- |
| Fast | RL training | Velocity tracking with first-order lag, acceleration limits, wind as an added velocity disturbance |
| Full | Drones-subject results, final evaluation, demo | Rigidbody quadrotor: four rotor thrusts, cascaded PID (velocity loop → attitude loop → rotor mixing), wind as an external force |

Tune the fast mode's lag and acceleration limits to match the full mode's measured step response, so a policy trained on fast mode transfers to full mode.

**Wind model:** a constant mean wind plus random gusts (amplitude and duration randomised per episode). Gusts are what make the landing policy meaningful.

**Drones-subject deliverables:** PID tuning procedure, step-response plots (rise time, overshoot, settling time) per axis, waypoint-tracking error, and behaviour under gusts.

## 6. Vision module

The camera feeds two decisions: which spots count as landable (written into the shared map) and where exactly to touch down (fed to the landing policy).

**Camera:** gimballed, angled about 45° down during exploration for a wider footprint, straight down (nadir) in landing mode. Resolution 128 × 128 (raise only if accuracy demands it).

**Task A — landable-surface segmentation**

- Classes: landable, not landable (debris, puddle, hole, vehicle, slope), obstacle/building, unknown.
- Model: YOLOv8-seg (nano) or a small U-Net, trained on the RTX 3060.
- Output per frame: a class mask with per-pixel confidence.

**Task B — ground projection**

Using the known camera pose (altitude from the rangefinder, gimbal angle, intrinsics), project the mask onto the ground plane and update the landable channel of the shared map. A cell is marked landable only after several consistent detections, and only if the landable patch is at least the drone's footprint plus a clearance.

**Task C — landing-spot localisation (landing mode)**

From the nadir camera, find the chosen patch and output: offset of its centre from the image centre (normalised x, y), its apparent area, its confidence, and the minimum distance from its centre to a non-landable edge. These become the landing policy's observations.

**Dataset (synthetic, auto-labelled):** a script flies the camera to random poses over generated maps and saves each RGB frame with a pixel-perfect mask, produced by re-rendering the scene with flat per-class colours. Target 10–20k images, split by map seed into train, validation and test, so test maps are never seen in training. Apply domain randomisation (textures, lighting, dust, blur, noise).

**Noise model for RL training:** measure the detector's per-class false-negative and false-positive rates and its localisation error on the test set. Training uses ground truth corrupted with those exact rates, so the policies learn to cope with the real detector's mistakes without rendering cameras.

**Vision deliverables:** IoU and precision/recall per class, confusion matrix, projection accuracy (detected versus true patch position in metres), and the detector-quality ablation in Section 12.

## 7. Mission planner

A scripted planner decides where each drone explores and where it lands; RL only executes the flying. Multi-drone behaviour comes from running the same single-drone mission repeatedly with a shared map, not from multi-agent RL.

**Shared map** (a 2D grid held at the base station):

- Channels: explored, obstacle, landable (with confidence), measured RSSI to the current parent.
- Node list: position of the base station and every landed drone, in chain order.
- Each landed drone uploads its map through the chain; each new drone downloads it before launch.

**Mission for drone k:**

1. **Download** the shared map. The parent is drone k−1 (or the base station for k = 1).
2. **Transit** to the parent's area along known explored cells.
3. **Explore** with target-biased frontier selection: among frontier cells (explored next to unexplored) where measured RSSI to the parent is at least T\_link + M, pick the one with the shortest path distance to the target. Send it as a waypoint to the navigation policy. Repeat as the map grows.
4. **Stop exploring** when no qualifying frontier remains, the battery reserve is reached, or a time budget runs out.
5. **Select the spot** (rule below) and navigate to it.
6. **Land** with the landing policy and vision.
7. **Upload** its map and confirm the link. If the landed RSSI falls below T\_link, take off and try the next-best spot.

**Spot selection rule:** among landable spots where measured RSSI to the parent ≥ T\_link + M + ground penalty, choose the one with the **shortest path distance to the target** (BFS over the occupancy grid, not straight-line distance). Ties go to the higher RSSI.

**Termination:** the chain is complete when the target camp is within T\_link of the last drone. The run fails if drones run out or no qualifying spot exists.

**State machine:** Download → Transit → Explore → Navigate → Land → Upload → Done (Land failure loops back to Navigate with the next spot).

## 8. RL navigation policy

The navigation policy flies the drone to a waypoint through streets it has never seen, using only onboard range sensors. It never sees the map, which is what justifies RL over a planner.

**Algorithm:** PPO (Unity ML-Agents), continuous actions.

**Observations (vector, about 30 values):**

| Input | Size | Notes |
| --- | --- | --- |
| Waypoint in body frame | 2 | Normalised distance and bearing |
| Velocity in body frame | 2 |  |
| Heading (sin, cos) | 2 |  |
| Raycast distances | 16–24 | Horizontal ring, max range for example 10 m, normalised |
| Previous action | 3 | Encourages smooth commands |

**Actions:** forward velocity, sideways velocity, yaw rate, each in \[−1, 1\], scaled to flight limits.

**Reward per step:**

- Plus the reduction in **path distance** to the waypoint since the last step (precomputed BFS distance field per episode, so going around walls is rewarded, not pressing into them).
- Minus a small time cost.
- Minus a penalty for proximity below a safety distance; a large penalty and episode end on collision.
- Plus a terminal bonus on reaching the waypoint within a tolerance.

**Episode:** random map from the curriculum, random start and waypoint with a reachable path, time limit proportional to path length. Wind enabled in later curriculum levels.

**Network:** MLP, 2 × 256, no LSTM (the waypoint and raycasts are enough; memory is not needed for point-to-point navigation).

**Baselines:** A\* with the full map (upper bound), a reactive potential-field controller (same sensors as the policy), straight-line flight (lower bound).

## 9. RL landing policy

The landing policy descends onto the chosen patch using the vision module's output, under wind gusts and imperfect detections. Without disturbances a PID controller would do the job, so disturbances are part of the spec, not an extra.

**Algorithm:** PPO, continuous actions, trained separately from navigation.

**Observations (from vision and sensors, not raw pixels):**

| Input | Size | Notes |
| --- | --- | --- |
| Patch centre offset in image | 2 | Normalised x, y |
| Patch apparent area | 1 | Proxy for altitude and alignment |
| Detection confidence | 1 | 0 when the patch is lost |
| Clearance to nearest non-landable edge | 1 | From the mask |
| Altitude (rangefinder) | 1 |  |
| Velocity | 3 | Body frame |
| Frames since last detection | 1 | Handles dropped frames |

**Actions:** vx, vy, descent rate, each in \[−1, 1\].

**Reward:**

- Per step: minus horizontal offset, minus excess speed near the ground, minus a small time cost.
- Terminal success: touchdown inside the patch with vertical speed below a limit, bonus scaled by centring.
- Terminal failure: touchdown off the patch, hard impact, or patch lost for too long.

**Training disturbances:** gusts (randomised strength and timing), detection noise from the measured noise model, dropped frames, small localisation bias.

**Baseline:** PID visual servoing (centre the patch, then descend at a fixed rate). Report success rate and landing error for both, in calm air and under increasing gust strength.

## 10. Training plan and compute budget

All training runs on one machine: i5-13400F (10 cores, 16 threads), 32 GB DDR4, RTX 3060 12 GB. PPO with small networks is CPU-bound, so the CPU runs RL and the GPU trains vision.

**Speed rules:**

- RL builds run headless (`--no-graphics`) with no cameras rendered.
- Place 8–16 arenas in one scene; run 4–8 executables with `--num-envs`, leaving 2–4 threads free.
- Use fast flight mode and the vision noise model during training.
- Measure real steps per second in week 1 and re-plan from that number.

**Training phases:**

| Phase | What trains | Landability / vision source | Rendering | Rough budget |
| --- | --- | --- | --- | --- |
| V | Segmentation model | Synthetic dataset | Dataset generation only | A few GPU hours |
| N1 | Navigation, curriculum levels 1–4 | Not needed | None | Overnight (≈10–30M steps) |
| L1 | Landing, calm then gusts | Ground truth + measured noise | None | A few hours |
| L2 | Landing fine-tune | Real camera + detector in the loop | 128 × 128 nadir camera | Short (≈1–3M steps) |
| E | Full mission evaluation | Real detector | Yes | Evaluation only |

Step counts and times are estimates to be replaced by week-1 measurements.

**Run discipline:** one config file per run, versioned with git; log every run's seed, curriculum and results; keep TensorBoard logs; never change the reward and the curriculum in the same run. Navigation and landing train in parallel because they share no weights.

## 11. Interfaces between modules

These contracts are frozen in week 1. Any module can be rewritten behind its interface without touching the others.

| Module | Input | Output |
| --- | --- | --- |
| `MapGenerator` | Seed, curriculum parameters | Scene objects, occupancy grid, landable mask, wall materials, base and target positions |
| `WifiModel` | Position A, position B, B on ground (bool) | RSSI in dBm |
| `PathField` | Occupancy grid, goal cell | BFS distance per cell (metres) |
| `FlightController` | Velocity setpoint (vx, vy, vz, yawRate), wind state | Updated drone pose and velocity |
| `VisionSegmenter` | RGB frame, camera pose | Per-cell landable updates with confidence |
| `LandingSpotLocator` | Nadir frame, target patch | Offset (x, y), area, confidence, edge clearance |
| `VisionNoiseModel` | Ground-truth cells or patch, measured error rates | Corrupted detections in the same format as the real modules |
| `NavPolicy` | Navigation observation vector | Velocity setpoint |
| `LandPolicy` | Landing observation vector | Velocity setpoint |
| `MissionPlanner` | Shared map, parent node, battery | Waypoints, chosen spot, mode switches |
| `SharedMap` | Uploads from landed drones | Merged grid and node list |

**Rule:** `VisionNoiseModel` and the real vision modules return the same data type, so switching from training to evaluation is a one-line config change.

## 12. Evaluation, baselines and metrics

Every result is reported on held-out map seeds the agents never trained on, averaged over at least 100 episodes per condition, with standard deviation.

| Experiment | Compared against | Metrics |
| --- | --- | --- |
| Navigation | A\* (full map), potential field, straight line | Success rate, collision rate, path length ÷ optimal path length, time to waypoint |
| Landing | PID visual servoing, at 0 / low / medium / high gusts | Success rate, landing error (m), touchdown speed |
| Vision | — | IoU and precision/recall per class, projection error (m) |
| Vision ablation | Ground truth vs real detector vs weakened detector | Chain length reached, range extended toward target, mission success rate |
| Shared map | Next drone with vs without the shared map | Exploration time and battery used per drone |
| Full mission | Random spot choice, nearest-to-parent choice | Range extended (m of path distance), chain quality (weakest-link dBm), drones used, target reached (%) |
| Flight controller | — | Rise time, overshoot, settling time, tracking error |

**Headline result:** range extended toward the target per drone, and how it drops as detector quality drops. That one plot ties RL, vision and the communication goal together.

**Training evidence:** TensorBoard curves of cumulative reward, episode length and success rate per curriculum level.

## 13. Work packages, timeline and risks

The plan assumes about six weeks. A crude end-to-end mission with placeholder parts must run by the end of week 3; weeks 4–6 improve each part behind its interface.

| # | Work package | Depends on | Weeks |
| --- | --- | --- | --- |
| WP1 | Map generator, path field, debug view | — | 1–2 |
| WP2 | WiFi model + heatmap validation | WP1 | 1–2 |
| WP3 | Flight controller (fast and full modes), wind, PID tuning | — | 1–2 |
| WP4 | Vision: dataset, segmentation, projection, spot locator, noise model | WP1 | 2–4 |
| WP5 | Navigation policy training and baselines | WP1, WP3 | 2–4 |
| WP6 | Landing policy training and PID baseline | WP3, WP4 noise model | 3–5 |
| WP7 | Mission planner, shared map, multi-drone chaining | WP2, WP5 (placeholder until ready) | 3–5 |
| WP8 | Evaluation, ablations, demo level, report | All | 5–6 |

**Milestones:**

- End of week 1: interfaces frozen; measured steps per second; WiFi heatmap working.
- End of week 3: one drone completes Explore → Navigate → Land on a level-2 map, even with scripted stand-ins.
- End of week 5: 3–5 drone chain on held-out maps with the real detector.
- Week 6: results, demo video, report.

**Risks and fallbacks:**

| Risk | Early sign | Fallback |
| --- | --- | --- |
| Navigation policy does not converge on hard maps | Success plateaus below 70% at level 3 | Shorter waypoint hops from the planner; more curriculum steps |
| Landing RL no better than PID | Equal results under gusts | Raise gust strength and detection noise; report the comparison honestly |
| Vision looks trivial | IoU above 0.95 on first try | Harder look-alikes, stronger randomisation |
| Integration slips | No end-to-end run by week 3 | Freeze features; cut learned exploration and full-mode flight to demo-only |
| Training too slow | Under about 1k steps/s | Fewer raycasts, coarser physics step, more parallel arenas |

**Stretch goals (only if ahead of schedule):** learned exploration policy compared with frontier exploration; coverage of several user points at the target camp; MA-POCA multi-agent comparison.

## 14. Demo plan and deliverables

The demo is one hand-built town level the agents never trained on, where 3–5 drones form a chain from the base station to the target camp.

**Demo video (2–3 minutes):**

1. Overview of the level: base station, target camp, blocked streets, no direct link.
2. Drone 1 explores: camera view with segmentation overlay, landable cells appearing on the shared map, RSSI-to-parent readout.
3. Drone 1 selects a spot and lands: nadir camera view, landing-spot box, wind gust visible in telemetry.
4. Drones 2–5 launch in turn, each starting from the shared map.
5. Final state: link lines between nodes coloured by RSSI, weakest-link value, target camp marked as covered.

**On-screen display:** mode (state machine), RSSI to parent, chain weakest link, battery, detection confidence, current reward.

**Deliverables:**

- [ ] Unity project with map generator, all modules and trained models (ONNX)
- [ ] Training configs and TensorBoard logs
- [ ] Vision dataset generator and trained segmentation model
- [ ] Evaluation scripts and results tables (Section 12)
- [ ] Demo video
- [ ] Report: one chapter per subject (RL, Drones, Machine Vision) plus system integration and results

## 15. Agent execution setup

This project is built by Claude Code running unattended: an Opus lead that plans, integrates and debugs, delegating bounded modules to Sonnet subagents. The lead's first job is to set up that structure and the Unity MCP connection, then work through Section 13 in order, stopping at each checkpoint.

### 15.1 Platform

Arch Linux (already verified to run Unity on this machine), X11 session for the Unity Editor. Hardware as in Section 10.

- No `pacman -Syu` or other system-wide upgrades for the duration of the project.
- All Python dependencies live in a project-local uv environment; never the system Python.
- Fallback: if the Unity Editor breaks and is not fixed within an hour, stop and report; the human may switch the project to Windows.

### 15.2 Human prerequisites (done before handoff)

The agent cannot do these; it should verify each one and stop if any is missing.

- [ ] Unity Hub and a Unity 6 LTS Editor installed; license activated and signed in
- [ ] NVIDIA driver working (`nvidia-smi` succeeds)
- [ ] git, uv and Node.js installed
- [ ] Claude Code installed and logged in
- [ ] Filesystem snapshot (btrfs or Timeshift) of the working system taken
- [ ] MCP choice made: Unity's official MCP (needs an active Unity AI trial or subscription and a Unity Cloud-connected project) or a free community server (IvanMurzak Unity-MCP)
- [ ] Project folder created and owned by a non-admin user; the agent gets no sudo
- [ ] Session started with the lead on Opus: `claude --model opus`, in auto permission mode (not bypass), from the project folder

### 15.3 Bootstrap steps for the lead agent

1. **Read this entire spec** before doing anything. Save a copy as `SPEC.md` in the repository root.
2. **Initialise the repository:** `git init`, a `.gitignore` for Unity and Python (Library/, Temp/, Builds/, .venv/, results/), and the folder layout: `unity/` (Unity project), `python/` (training, vision, evaluation), `configs/`, `docs/`.
3. **Write `CLAUDE.md`** with the content in 15.4.
4. **Create the subagent files** in `.claude/agents/` with the content in 15.5. Restart the session if Claude Code reports that a newly created agents directory was not picked up.
5. **Create the Python environment** with uv, pinned to the Python version the current ML-Agents release requires (check its documentation; do not assume). Install mlagents, a CUDA build of PyTorch, ultralytics, tensorboard, numpy, matplotlib. Verify that `torch.cuda.is_available()` is true.
6. **Create the Unity project** from the command line in batch mode under `unity/`, then add the ML-Agents package at the version matching the Python package.
7. **Install and connect the Unity MCP server:**
   - Official: add the `com.unity.ai.assistant` package, then register the relay with Claude Code: `claude mcp add unity-mcp -- ~/.unity/relay/relay_linux --mcp`. The first connection appears as *Pending* under Edit → Project Settings → AI → Unity MCP; this needs the human to approve once, so stop and report at this point.
   - Community: follow the IvanMurzak Unity-MCP README for the Unity package and the Claude Code registration.
   - Verify: the lead can list the scene hierarchy and read the console through MCP.
8. **Smoke test:** an editor script in batch mode creates a scene with a floor and a cube, saves it, and renders a screenshot to `docs/screenshots/`. The lead reads the screenshot to confirm it.
9. **Commit** (`bootstrap complete`) and report the checkpoint, then start WP1 and WP3.

### 15.4 CLAUDE.md content

```markdown
# Communication Drone — agent instructions

SPEC.md is the source of truth. Re-read the relevant section before starting each work package.

## Roles
- You (the main session, Opus) are the lead: plan, integrate, debug, review, design rewards, interpret training results, and make architecture decisions.
- Delegate bounded implementation tasks (one module against a fixed interface from SPEC.md Section 11) to the `implementer` subagent.
- Delegate running evaluations, reading TensorBoard logs and producing plots to the `evaluator` subagent.
- Never delegate RL debugging, reward design or interface changes.

## Hard rules
1. Build all scenes, prefabs and maps from C# code (editor scripts or runtime generators). Use Unity MCP only for inspection, console errors, play-mode tests and screenshots, never as the way to construct content.
2. Interfaces in SPEC.md Section 11 are frozen. Changing one requires stopping and reporting.
3. Verify every milestone visually: render screenshots and read them before calling anything done.
4. Never change the reward function and the curriculum in the same training run. One config file per run, committed with its seed and results.
5. No system-wide installs, no sudo, no pacman. Project-local uv environment only.
6. Commit to git after every completed task with a clear message.
7. Measure before planning: record real training throughput (steps/s) and use it to re-plan step budgets.
8. If stuck on the same error for more than about 45 minutes of attempts, stop, write the attempts and hypotheses to docs/blockers.md, and report.

## Checkpoints: stop and report
At each one, write docs/checkpoint-N.md (what was done, numbers, screenshots, open issues), commit, and stop for human review:
1. Bootstrap complete (Section 15.3)
2. Interfaces frozen; WiFi heatmap working; measured steps per second
3. First navigation training run on curriculum level 1 (reward curve + success rate)
4. Vision dataset generated and segmentation model trained (IoU per class)
5. First end-to-end single-drone mission on a level-2 map
6. Multi-drone chain on held-out maps with the real detector
7. Evaluation results, demo video, report draft
```

### 15.5 Subagent files

`.claude/agents/implementer.md`:

```markdown
---
name: implementer
description: Implements one bounded module against a fixed interface from SPEC.md. Use for C# components, editor scripts, Python scripts, data generators.
model: sonnet
---

You implement exactly one module as described in the task message.
- Follow the interface in SPEC.md Section 11 exactly; do not change it or touch other modules.
- Build Unity content from code, never by manual scene edits.
- Write a test or verification script, run it, and report what passed, what failed, and any assumption you made.
- If you believe the interface or the spec is wrong, stop and say so instead of working around it.
```

`.claude/agents/evaluator.md`:

```markdown
---
name: evaluator
description: Runs evaluation episodes, reads TensorBoard logs and training results, and produces plots and metric tables. Use after training runs and for Section 12 experiments.
model: sonnet
---

You run evaluations and report numbers; you do not modify training code, rewards or configs.
- Use held-out map seeds only, at least 100 episodes per condition, and report mean and standard deviation.
- Save plots to docs/results/ and tables as Markdown.
- Report the numbers plainly, including results that look bad.
```

### 15.6 Permissions and safety

- The agent runs as a non-admin user with access only to the project folder.
- Auto permission mode, so routine commands proceed without prompts while risky ones are still checked. Never bypass mode.
- The Unity Editor must stay open for MCP to work; if it crashes or hangs, the agent restarts it once and stops to report if that fails.
- Long training runs are launched in the background with logs written to `results/`, so the agent keeps working while they run.

Sources: [Claude Code subagents](https://code.claude.com/docs/en/sub-agents.md) · [Unity MCP: get started](https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.0/manual/unity-mcp-get-started.html) · [Unity AI MCP open beta](https://unity.com/blog/unity-ai-mcp-how-to-get-started) · [IvanMurzak Unity-MCP](https://github.com/IvanMurzak/Unity-MCP)
