# Demo maps — why they failed and what fixed them

Every number below is a **success rate over 10 wind / detector-noise / lighting seeds** (run seeds 2000001…2000019)
with the real U-Net in the loop, not a single run. A single run on these maps is effectively a coin flip near the
margins, which is why the original "verified" seeds still failed live.

## Result

| Map | Before (original code, U-Net v2) | After (all fixes, U-Net v4) | Demo seed |
|---|---|---|---|
| Z-route | 5/10 | **10/10** | 2000001 |
| U-turn | 9/10 | **10/10** | 2000001 |
| Staircase | 9/10 | **10/10** | 2000001 |
| Blocked boulevard | 5/10 | **8/10** | 2000003 |
| Procedural town A (old: L2 seed 2000015) | 5/10 | 5/10 → **replaced** by L2 seed 2000019: **10/10** | 2000001 |
| Procedural town B (old: L3 seed 2000001) | 1/10 | 0/10 → **replaced** by L2 seed 2000025: **10/10** | 2000001 |
| **Total** | **34/60** | **58/60** on the final six maps | |

All six maps succeed on their demo seed in the Demo build, and runs are now deterministic (identical numbers
headless and live).

## Why each map failed (root causes, from failure dumps)

Failures were classified per run: **perception** (a usable landable spot was explored but never believed landable),
**exploration** (a usable spot was in radio range but never explored), **geometry** (no usable spot existed within the
parent's radio range). Before the fixes almost every failure was *perception* (`python/eval/classify_failures.py`).

### Live demo ≠ my headless check (all maps)
- **Cause:** the built-in render pipeline sizes the sun's shadow map from the screen resolution. The windowed demo and
  the headless runner therefore rendered slightly different shadows → slightly different U-Net masks → after a few
  drones the missions diverged (first drone's landing RSSI differed by 0.01 dB, the fifth drone's outcome flipped).
- **Fix:** fixed shadow-map resolution (2048). Demo and headless runs now produce identical results.

### Z-route (5/10 → 10/10)
- **Cause:** drone 2 always stalled ~101 m from the camp. The corner patch it needed (58, 20) collected almost only
  "not landable" votes (ratio 0.00–0.13). Re-rendering the exact logged camera poses showed why: the navigation
  policy controls body yaw freely and flew the last stretch **sideways/backwards** (heading 223–275°), and the
  explore camera was fixed to the body — it never looked at the corner it was flying to.
- **Fix:** the gimbal now points at the planner's current goal (frontier or spot), falling back to the velocity
  direction, with the ±60° pan around that.

### U-turn (9/10 → 10/10, but 5/10 at one intermediate step)
- **Cause:** same family — the corner patch at (60, 100) was missed (ratio 0.03–0.19), partly the camera-direction issue,
  partly white flat concrete in the shadow of three surrounding buildings, which U-Net v2 often called "not landable".
- **Fix:** gimbal fix + U-Net v4 (fine-tuned on views aimed at patches, including low sun / long shadows).

### Staircase (9/10 → 10/10)
- **Cause (live failure):** (1) exploration followed an *optimistic* RSSI prediction into unexplored space — drone 4 flew
  up a 70 m dead end and all the way to the camp where the real link was < −90 dBm; (2) the corner-3 spot measured
  −72.2 dBm vs −71 required (1 dB short on a noisy reading) and was discarded without being re-measured;
  (3) the corner patch got many far-away "not landable" votes.
- **Fix:** unexplored stretches between parent and candidate cost an uncertainty penalty (6 dB) in the RSSI
  prediction; spots up to 2 dB below the threshold are *visited* and re-measured by hovering (the final decision still
  requires ≥ −71 dBm averaged at the spot); geometry-weighted evidence fusion.

### Blocked boulevard (5/10 → 8/10)
- **Cause:** the corner patch west of the collapse (48, 60) was classified "not landable" (ratio 0.01–0.06): white
  concrete boxed in by buildings, seen obliquely.
- **Fix:** gimbal + U-Net v4. **Remaining 2/10** (seeds 2000001, 2000019): still perception at the same corner under
  particular lighting. The demo uses seed 2000003.

### Procedural town A, old (L2, map 2000015: 5/10 → 5/10)
- **Cause:** drone 1's best spot at (36, 80) is a landable surface whose random palette + lighting renders it as dull
  blue-grey, nearly the same as the cracked street; U-Net v4 still calls it "not landable" even at 2 m. A genuinely
  ambiguous appearance, not a pipeline bug; it also sits at the edge of radio range (−72.9 dBm estimated).
- **Fix for the demo:** replaced by the level-2 town 2000019, which succeeds 10/10.

### Procedural town B, old (L3, map 2000001: 1/10 → 0/10)
- **Cause:** 3–4 of the failures are *geometry* (no landable spot inside the parent's ~35 m line-of-sight range at a
  critical corner), the rest perception. Level-3 towns are beyond the current system: of six level-3 towns tested,
  the best succeeds 5/10, four succeed 0/10.
- **Fix for the demo:** replaced by the level-2 town 2000025 (10/10).

## Changes made (and their measured effect)
| Change | Where | Effect |
|---|---|---|
| Gimbal points at the planner's goal | `DroneMission.Sense` | **the largest single gain**: 30/60 → 43/60 on the demo matrix; Z-route 1 → 10/10 |
| U-Net v4: fine-tuned from v2 → v3 (3.6k close-range nadir frames) → v4 (+4.8k 45° patch-aimed views, low sun) on **new procedural maps only** | `python/vision/train_seg.py`, `VisionDataset.GenerateLowAlt/GeneratePatchView` | patch-view landable IoU 0.787 → 0.874 (recall 0.83 → 0.93); test mIoU 0.909 → 0.919; close-range landable IoU 0.566 → 0.617; demo matrix 43 → 43/60 overall but U-turn 7 → 10, boulevard 6 → 8 |
| Geometry-weighted fusion (confidence × incidence × (5 m / range)²), 60 % vote | `VisionSegmenter.Project`, `SharedMap` | with the same planner: L2 30-mission success 33 % → 50 % (intermediate variant); one close look outweighs many distant ones |
| Conservative RSSI prediction through unexplored space (6 dB penalty) | `DroneMission.RssiEstimate` | stops drones wandering out of radio range (Staircase) |
| Tentative spots (≤ 2 dB below threshold) visited and re-measured | `DroneMission.SelectSpot` | marginal corner spots no longer thrown away on one noisy reading |
| Fixed shadow-map resolution | `TownBuilder` | demo ≡ headless (determinism) |

Not every change helped on its own: weighting by range alone and the conservative exploration on their own made
Z-route and U-turn *worse* (they reduced the evidence the drones collected) until the camera-direction bug was fixed.
The intermediate matrices are kept in `results/fix/robust_{old,new,v4,v5,v6}`.

## Honest limits
- Level-3 procedural towns: best 5/10, typical 0/10 — missing landable spots at the right corners (geometry) and
  ambiguous low-contrast surfaces (perception).
- Blocked boulevard still fails on 2/10 lighting seeds.
- The demo maps were selected/seeded after measuring robustness; the general benchmark below is the unbiased number.

## General benchmark (100 held-out level-2 missions, real detector)
See the table appended below (filled from `results/eval/missions_v2/`).
