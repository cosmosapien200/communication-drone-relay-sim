# Full-mission results

Held-out seeds (odd, 2000001+). Mean ± std over missions.

## Vision ablation (level 2)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| vision_cnn | 100 | 34 % (±5) | 58.1 ± 36.2 | 18.0 ± 7.4 | 3.0 ± 1.7 | 3.8 ± 1.6 | 0.28 ± 0.76 | -72.4 ± 2.5 |
| vision_gt | 100 | 43 % (±5) | 63.6 ± 34.0 | 18.5 ± 7.5 | 3.2 ± 1.6 | 3.9 ± 1.5 | 0.25 ± 0.57 | -72.9 ± 1.6 |
| vision_mid | 100 | 7 % (±3) | 27.9 ± 29.7 | 11.5 ± 10.0 | 1.6 ± 1.5 | 2.7 ± 1.5 | 0.20 ± 0.55 | -71.4 ± 3.9 |
| vision_weak | 100 | 0 % (±0) | 0.0 ± 0.0 | 0.0 ± 0.0 | 0.0 ± 0.0 | 1.1 ± 0.4 | 0.09 ± 0.40 | – |

## Detector-quality sweep, measured noise model × k (level 2)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| quality_k0 | 100 | 45 % (±5) | 65.5 ± 33.4 | 18.8 ± 7.3 | 3.2 ± 1.6 | 3.9 ± 1.5 | 0.27 ± 0.63 | -73.0 ± 1.8 |
| quality_k0.5 | 100 | 51 % (±5) | 63.5 ± 36.1 | 18.3 ± 7.9 | 3.1 ± 1.7 | 3.8 ± 1.5 | 0.26 ± 0.58 | -72.8 ± 1.9 |
| quality_k1 | 100 | 47 % (±5) | 64.4 ± 35.8 | 18.4 ± 7.9 | 3.1 ± 1.7 | 3.9 ± 1.6 | 0.28 ± 0.58 | -72.9 ± 1.6 |
| quality_k2 | 100 | 45 % (±5) | 61.0 ± 36.6 | 18.2 ± 8.3 | 3.0 ± 1.8 | 3.7 ± 1.6 | 0.23 ± 0.61 | -72.8 ± 1.8 |
| quality_k4 | 100 | 30 % (±5) | 53.0 ± 34.9 | 15.7 ± 8.5 | 2.8 ± 1.7 | 3.8 ± 1.6 | 0.30 ± 0.59 | -72.3 ± 2.4 |
| quality_k8 | 100 | 0 % (±0) | 0.0 ± 0.0 | 0.0 ± 0.0 | 0.0 ± 0.0 | 1.1 ± 0.3 | 0.06 ± 0.34 | – |

## Spot selection strategy (level 2, noise model)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| strategy_best | 100 | 47 % (±5) | 64.4 ± 35.8 | 18.4 ± 7.9 | 3.1 ± 1.7 | 3.9 ± 1.6 | 0.28 ± 0.58 | -72.9 ± 1.6 |
| strategy_nearest | 100 | 3 % (±2) | 33.5 ± 19.8 | 7.0 ± 3.6 | 4.2 ± 2.2 | 4.8 ± 1.8 | 0.27 ± 0.79 | -66.4 ± 3.9 |
| strategy_random | 100 | 25 % (±4) | 52.3 ± 31.0 | 11.9 ± 5.9 | 3.9 ± 2.1 | 4.5 ± 1.7 | 0.24 ± 0.59 | -71.2 ± 2.5 |

## Difficulty (noise model)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| level_L3 | 100 | 17 % (±4) | 52.8 ± 42.6 | 15.4 ± 9.0 | 2.7 ± 1.9 | 3.7 ± 1.8 | 0.25 ± 0.78 | -71.9 ± 3.3 |
| level_L4 | 100 | 2 % (±1) | 32.8 ± 32.6 | 13.2 ± 9.4 | 1.9 ± 1.7 | 3.1 ± 1.7 | 0.34 ± 0.91 | -71.0 ± 3.2 |
| quality_k1 | 100 | 47 % (±5) | 64.4 ± 35.8 | 18.4 ± 7.9 | 3.1 ± 1.7 | 3.9 ± 1.6 | 0.28 ± 0.58 | -72.9 ± 1.6 |

## RL policies vs scripted stand-ins (level 2, noise model)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| standin_land | 100 | 42 % (±5) | 61.8 ± 36.8 | 18.4 ± 7.8 | 3.0 ± 1.7 | 3.8 ± 1.6 | 0.27 ± 0.60 | -72.7 ± 1.6 |
| standin_nav | 100 | 9 % (±3) | 35.0 ± 32.8 | 14.2 ± 10.7 | 1.7 ± 1.5 | 4.7 ± 1.7 | 2.60 ± 2.25 | -72.5 ± 1.7 |
| quality_k1 | 100 | 47 % (±5) | 64.4 ± 35.8 | 18.4 ± 7.9 | 3.1 ± 1.7 | 3.9 ± 1.6 | 0.28 ± 0.58 | -72.9 ± 1.6 |

## Flight-controller fidelity (level 2, noise model)

| Condition | Missions | Target reached | Range extended (m) | Range per relay (m) | Relays landed | Drones used | Drones lost | Chain quality (dBm) |
|---|---|---|---|---|---|---|---|---|
| flight_full | 100 | 30 % (±5) | 53.9 ± 36.3 | 18.0 ± 8.5 | 2.6 ± 1.6 | 4.3 ± 1.6 | 1.36 ± 1.60 | -72.7 ± 2.0 |
| quality_k1 | 100 | 47 % (±5) | 64.4 ± 35.8 | 18.4 ± 7.9 | 3.1 ± 1.7 | 3.9 ± 1.6 | 0.28 ± 0.58 | -72.9 ± 1.6 |

## Shared map (level 2, noise model): drones 2..k (who can benefit from the map)

| Condition | Drones | Exploration time (s) | Battery used (s) | Target reached | Range extended (m) |
|---|---|---|---|---|---|
| shared_on | 285 | 30.4 ± 24.1 | 67.9 ± 36.1 | 47 % | 64.4 ± 35.8 |
| shared_off | 289 | 52.0 ± 35.6 | 92.1 ± 46.0 | 42 % | 62.6 ± 35.2 |

