# Live demo — run sheet

## Before you start (2 min)
1. Close Unity Editor and training jobs (the demo uses CPU for the U-Net and policies).
2. Plug in power, set the laptop not to sleep.
3. Launch:
   ```bash
   ./demo          # windowed 1600×900
   ./demo full     # fullscreen (projector)
   ```
   Fallback without the build: open `unity/Assets/Scenes/Demo.unity` in the Editor → Play.
   Fallback without Unity at all: `docs/demo/demo_level_real_detector.mp4`.

## Controls
| Key | Action |
|---|---|
| 1–6 / click | choose map (restarts) |
| R | restart current map |
| Space | pause / resume (pause to explain) |
| + (=) / − | simulation speed ×1…×16 (starts at ×2) |
| C | camera: chase → overview → top-down |
| V | real U-Net ↔ perfect (ground-truth) vision — restarts the map; press again to return (menu shows "vision: …") |
| H | hide the menu panel |

## Maps (verified 24 Oct, real U-Net, demo seed — all six cover the camp)
| # | Map | Relays | ≈ duration at ×2 | Story |
|---|---|---|---|---|
| 1 | Z-route | 4 | 1.7 min | one relay at every corner — clearest to explain, **start here** |
| 2 | U-turn | 4 | 1.4 min | camp is behind the base's block; chain goes round three sides |
| 3 | Staircase | 4 | 2.1 min | tempting dead-end shortcut; drones don't take it (radio-aware exploration) |
| 4 | Blocked boulevard | 4 | 1.6 min | straight road cut by rubble → detour on the parallel street |
| 5 | Procedural town A | 3 | 1.2 min | generated town never seen in training |
| 6 | Procedural town B | 4 | 2.1 min | second unseen generated town |

Runs are deterministic: same map → same result every time. Over 10 random wind/lighting seeds, maps 1, 2, 3, 5, 6
succeed 10/10 and map 4 8/10 (the demo seed is one of the successes).

Suggested 5-minute flow: **1 (Z-route) full run → 4 (Blocked boulevard) → 5 (unseen town)**, pausing during LAND.

## What to point at on screen
- **Top-left HUD** — drone number + mode (TRANSIT → EXPLORE → NAVIGATE → LAND → UPLOAD), RSSI to parent (must be
  ≥ −71 dBm to land), weakest link, battery, wind/GUST, relays landed.
- **Top-right minimap** — shared map; green = confirmed landable; later drones reuse it (explore 30 s instead of 52 s).
- **Bottom-right** — the drone camera with live U-Net segmentation (green landable / red not / blue obstacle);
  switches to straight-down during landing.
- **Link lines** — green strong, yellow near −80 dBm, red broken. Success = line from last relay to the red camp turns green.

## Talking points (one line each)
- **Problem:** buildings block Wi-Fi; one hovering drone has short battery → a chain of drones that *land* as relays.
- **Hierarchy:** rule-based planner decides *where*; two PPO policies do the *flying*; autopilot-style velocity commands.
- **Navigation RL:** 33 inputs (24 range rays, waypoint, velocity), MLP 2×256, path-progress reward, curriculum L1→L4;
  80 % vs 54 % (potential field) on the hardest towns; without it missions drop 47 % → 9 %.
- **Landing RL:** 10 vision-derived inputs, MLP 2×128, shaping + gust curriculum + real detector noise;
  100 % at 6 m/s gusts vs 44 % for PID.
- **Vision:** U-Net 0.48 M params, synthetic auto-labelled data, landable IoU 0.83, mIoU 0.92, ~4 ms/frame on CPU.
- **Spot rule:** furthest-toward-camp landable spot that still hears the parent at −71 dBm → relays end up at corners.
- **Result:** 45 % of 100 unseen level-2 towns fully covered, +62 m coverage on average; all in simulation.
- **Press V** to switch to perfect vision and show what the planner does without perception errors.

## If something goes wrong live
- A drone takes off again after landing → that is recovery (patch lost or link too weak → next spot), say so.
- Anything odd → press **R** (deterministic restart) or switch map.
- Window too small for the HUD → `./demo full`.
