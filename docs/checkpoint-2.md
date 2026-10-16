# Checkpoint 2 — Interfaces frozen, WiFi heatmap, measured throughput

## Section 11 interfaces → code
| Module | Implementation | Signature |
|---|---|---|
| MapGenerator | `Scripts/Core/MapGenerator.cs` (+ `World/TownBuilder.cs` for scene objects) | `TownMap Generate(int seed, CurriculumParams cp)`; `TownVisual TownBuilder.Build(TownMap, int visualSeed)` |
| WifiModel | `Scripts/Core/WifiModel.cs` | `float RSSI(Vector3 posA, Vector3 posB, bool onGroundB)` |
| PathField | `Scripts/Core/PathField.cs` | `new PathField(TownMap, Vector2Int goal)`; `.At(Vector2)`, `.AtCell(i,j)` (metres) |
| FlightController | `Scripts/Flight/FlightController.cs` | `SetVelocity(vx, vy, vz, yawRate)`; `Step(dt, Vector3 wind)`; fast/full modes |
| VisionSegmenter | `Scripts/Vision/VisionSegmenter.cs` | `ICellDetector.Observe(map, camPos, camRot, fov) → List<CellObservation>` (renders the frame for the pose) |
| LandingSpotLocator | `Scripts/Vision/LandingSpotLocator.cs` | `ISpotDetector.Detect(dronePos, yaw, patch) → SpotDetection` |
| VisionNoiseModel | `VisionNoiseModel` (spot) and `NoisyCellDetector` (cells) | same return types as the real modules — switching is one argument (`-detector`) |
| NavPolicy / LandPolicy | ONNX via `Mission/PolicyRunner.cs`; observations from `NavAgent.WriteObs` / `LandAgent.WriteObs` | obs vector → velocity setpoint |
| MissionPlanner | `Mission/DroneMission.cs` (`SelectFrontier`, `SelectSpot`, `NextHop`, mode switches) | shared map, parent, battery → waypoints, spot, modes |
| SharedMap | `Mission/SharedMap.cs` | `Clone()` (download), `Merge()` (upload), node list |

## WiFi model
P0 = −40 dBm @ 1 m, n = 2.0 (street canyon), 2 walls per obstacle crossed (concrete 10 / brick 7 / rubble 5 dB),
ground penalty 3 dB, σ = 2 dB, T_link = −80 dBm, M = 6 dB → spot qualifies at ≥ −71 dBm (≈ 35 m LOS hop), link ≈ 100 m LOS.
Heatmaps: `docs/screenshots/maps/maps_overview.png` and per-map figures.
(Values were tuned twice after mission debugging; the history is in PROGRESS.md.)

## Throughput
Navigation training: ~4,500 env steps/s with 1 Unity env × 32 grid-only arenas (Python trainer is the bottleneck,
~4 cores). The spec's risk threshold is 1k steps/s.
