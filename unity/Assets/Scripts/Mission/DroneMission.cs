using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    public enum MissionMode { Download, Transit, Explore, Navigate, Land, Upload, Done, Failed }
    public enum SpotStrategy { BestToTarget, Random, NearestParent }

    [System.Serializable]
    public class MissionConfig
    {
        public int maxDrones = 6;
        public float exploreBudget = 300f;   // s
        public float batteryCapacity = 900f; // s of flight
        public float exploreStopBattery = 0.5f; // stop exploring below this fraction (reserve to navigate + land)
        public bool useSharedMap = true;
        public SpotStrategy strategy = SpotStrategy.BestToTarget;
        public FlightMode flight = FlightMode.Fast;
        public float gust = 1.5f, windMean = 0.5f;
        public float hopLength = 12f;
        public int visionEvery = 3;          // decisions between camera frames
        public bool scriptedNav = false;     // stand-in: A* follower instead of the nav policy
        public bool scriptedLand = false;    // stand-in: PID visual servoing instead of the landing policy
    }

    /// Mission for one drone k (SPEC Section 7): Download -> Transit -> Explore -> Navigate -> Land -> Upload.
    /// Land failure loops back to Navigate with the next spot.
    public class DroneMission
    {
        public readonly int index;
        public MissionMode mode = MissionMode.Download;
        public string failReason = "";
        public readonly FlightController fc;
        public readonly WindModel wind;
        public SharedMap map;           // drone's local copy
        public SharedMap downloaded;    // what it started from (for merging only new evidence)
        readonly TownMap truth;
        readonly WifiModel wifi;
        readonly MissionConfig cfg;
        readonly Vector3 parentWorld;
        readonly PolicyRunner navPolicy, landPolicy;
        readonly ICellDetector cellDet;
        readonly ISpotDetector spotDet;
        readonly System.Random rng;

        // telemetry
        public float time, battery, exploreTime, rssiToParent, lastReward, detConfidence;
        public float CameraYaw;
        public int landingAttempts, step;
        public Vector2 waypoint;            // current hop
        public Vector2 goal;                // current frontier / spot
        public Vector2Int spotCell = new Vector2Int(-1, -1);
        public Vector3 landedWorld;
        public float landedRssi;
        public bool crashed;
        public readonly List<Vector2> trail = new List<Vector2>();

        // internals
        TownMap knownFree, knownExplored;
        PathField targetField, hopField;
        Vector2Int hopFieldGoal = new Vector2Int(-1, -1);
        int replanCountdown;
        Vector3 prevNavAction, prevLandAction;
        readonly float[] navObs = new float[NavAgent.ObsSize], landObs = new float[LandAgent.ObsSize], rays = new float[NavAgent.NumRays];
        readonly HashSet<Vector2Int> rejected = new HashSet<Vector2Int>();
        SpotDetection held; int framesSinceDet; int landSteps; int hoverSteps; float rssiAcc; int rssiN;
        GroundFeature landPatch;
        bool takingOff;

        public Vector2 Pos => new Vector2(fc.position.x, fc.position.z);
        public Vector3 World => truth.ToWorld(Pos, fc.position.y);
        public System.Action<DroneMission, Vector3, Quaternion> OnCamera; // hook for rendering/vision overlays

        public DroneMission(int index, TownMap truth, SharedMap shared, Vector3 parentWorld, WifiModel wifi, MissionConfig cfg,
                            PolicyRunner nav, PolicyRunner land, ICellDetector cellDet, ISpotDetector spotDet, int seed)
        {
            this.index = index; this.truth = truth; this.wifi = wifi; this.cfg = cfg; this.parentWorld = parentWorld;
            navPolicy = nav; landPolicy = land; this.cellDet = cellDet; this.spotDet = spotDet;
            rng = new System.Random(seed);
            fc = new FlightController(cfg.flight);
            wind = new WindModel(seed * 7 + 3);
            if (cfg.gust > 0 || cfg.windMean > 0) wind.Randomize(cfg.windMean, cfg.gust); else wind.SetCalm();
            // Download
            downloaded = cfg.useSharedMap ? shared.Clone() : shared.NodesOnly();
            map = downloaded.Clone();
            map.ClearRssi();
            battery = cfg.batteryCapacity;
            var start = truth.BasePos + new Vector2(0.8f, 0f);
            fc.Reset(new Vector3(start.x, Sim.CruiseAltitude, start.y), 90f);
            RefreshKnown();
            Vector2 parentLocal = truth.ToLocal(parentWorld);
            mode = (parentLocal - start).magnitude > 4f ? MissionMode.Transit : MissionMode.Explore;
            goal = parentLocal;
        }

        public bool Finished => mode == MissionMode.Done || mode == MissionMode.Failed;

        // ------------------------------------------------------------------ main step (one 0.1 s decision)
        public void Step()
        {
            if (Finished) return;
            step++; time += Sim.ControlDt;
            if (!fc.onGround) battery -= Sim.ControlDt;
            if (battery <= 0f && !fc.onGround) { Crash("battery"); return; }
            Sense();
            switch (mode)
            {
                case MissionMode.Transit: StepTransit(); break;
                case MissionMode.Explore: StepExplore(); break;
                case MissionMode.Navigate: StepNavigate(); break;
                case MissionMode.Land: StepLand(); break;
                case MissionMode.Upload: StepUpload(); break;
            }
            if (step % 5 == 0) trail.Add(Pos);
        }

        void Sense()
        {
            if (fc.position.y < Sim.CruiseAltitude - 0.5f) return; // range ring and 45° camera only at cruise
            NavAgent.CastRays(truth, Pos, fc.yaw, rays);
            for (int r = 0; r < NavAgent.NumRays; r++)
            {
                float a = (fc.yaw + r * 360f / NavAgent.NumRays) * Mathf.Deg2Rad;
                map.AddRay(Pos, new Vector2(Mathf.Sin(a), Mathf.Cos(a)), rays[r], NavAgent.RayRange);
            }
            rssiToParent = wifi.RSSI(parentWorld, World, false);
            map.AddRssi(Pos, rssiToParent);
            bool visionOn = mode == MissionMode.Explore || mode == MissionMode.Navigate;
            if (visionOn && step % cfg.visionEvery == 0)
            {
                // gimbal pans +-60 deg (period 4 s) while exploring so side streets are seen
                float pan = mode == MissionMode.Explore ? 60f * Mathf.Sin(2f * Mathf.PI * time / 4f) : 0f;
                // the gimbal looks where the planner is going (goal), else along the velocity; the nav policy's body yaw
                // is free (it often flies sideways or backwards), so a body-fixed camera missed what lay ahead
                Vector2 look = goal - Pos;
                var vel = new Vector2(fc.velocity.x, fc.velocity.z);
                if (look.magnitude < 2f) look = vel.magnitude > 0.3f ? vel : new Vector2(Mathf.Sin(fc.yaw * Mathf.Deg2Rad), Mathf.Cos(fc.yaw * Mathf.Deg2Rad));
                CameraYaw = Mathf.Atan2(look.x, look.y) * Mathf.Rad2Deg + pan;
                var camRot = Quaternion.Euler(45f, CameraYaw, 0f);
                OnCamera?.Invoke(this, World, camRot);
                map.AddVision(cellDet.Observe(truth, World, camRot, NadirCamera.FovDeg));
            }
        }

        // ------------------------------------------------------------------ modes
        void StepTransit()
        {
            if ((Pos - goal).magnitude < 3f) { mode = MissionMode.Explore; replanCountdown = 0; return; }
            if (--replanCountdown <= 0) { RefreshKnown(); waypoint = NextHop(goal, knownExplored, knownFree); replanCountdown = 20; }
            if (Fly(waypoint)) replanCountdown = 0;
        }

        void StepExplore()
        {
            exploreTime += Sim.ControlDt;
            if (--replanCountdown <= 0 || (Pos - waypoint).magnitude < 1.2f)
            {
                RefreshKnown();
                bool budgetLeft = exploreTime < cfg.exploreBudget && battery > cfg.exploreStopBattery * cfg.batteryCapacity;
                if (!budgetLeft || !SelectFrontier(out goal)) { BeginSpotSelection(); return; }
                waypoint = NextHop(goal, knownFree, knownFree);
                replanCountdown = 20;
            }
            Fly(waypoint);
        }

        void BeginSpotSelection()
        {
            RefreshKnown();
            if (!SelectSpot(out spotCell)) { Fail("no_qualifying_spot"); return; }
            goal = map.CellCenter(spotCell.x, spotCell.y);
            mode = MissionMode.Navigate; replanCountdown = 0; hoverSteps = 0; rssiAcc = 0; rssiN = 0;
        }

        void StepNavigate()
        {
            if (takingOff) { TakeOff(); return; }
            float d = (Pos - goal).magnitude;
            if (d < 0.8f)
            {
                // hover and confirm the link quality measured at the spot (airborne)
                Hover();
                rssiAcc += rssiToParent; rssiN++;
                if (++hoverSteps >= 8)
                {
                    float measured = rssiAcc / rssiN;
                    if (measured < wifi.p.QualifyThreshold) { RejectSpot(); return; }
                    StartLanding();
                }
                return;
            }
            if (--replanCountdown <= 0) { RefreshKnown(); waypoint = NextHop(goal, knownFree, knownFree); replanCountdown = 20; }
            if ((Pos - goal).magnitude < 4f) waypoint = goal;
            Fly(waypoint);
        }

        void StartLanding()
        {
            mode = MissionMode.Land; landingAttempts++; landSteps = 0; framesSinceDet = 0; prevLandAction = Vector3.zero;
            int pi = truth.PatchAt(goal);
            if (pi >= 0)
            {
                // landing target: the true patch clipped to a 4 m window around the chosen spot
                var f = truth.features[pi];
                float x0 = Mathf.Max(f.center.x - f.size.x / 2, goal.x - 2f), x1 = Mathf.Min(f.center.x + f.size.x / 2, goal.x + 2f);
                float y0 = Mathf.Max(f.center.y - f.size.y / 2, goal.y - 2f), y1 = Mathf.Min(f.center.y + f.size.y / 2, goal.y + 2f);
                landPatch = new GroundFeature { type = f.type, center = new Vector2((x0 + x1) / 2, (y0 + y1) / 2), size = new Vector2(x1 - x0, y1 - y0) };
            }
            else
            {
                // the planner believed this was landable but it is not: estimated patch from the shared map component
                landPatch = new GroundFeature { type = GroundType.Street, center = goal, size = new Vector2(0.01f, 0.01f) };
            }
            spotDet.ResetEpisode();
            held = spotDet.Detect(fc.position, fc.yaw, landPatch);
        }

        void StepLand()
        {
            var d = spotDet.Detect(fc.position, fc.yaw, landPatch);
            detConfidence = d.confidence;
            if (d.detected) { held = d; framesSinceDet = 0; } else { framesSinceDet++; held.confidence = 0f; }
            Vector3 a;
            if (cfg.scriptedLand || landPolicy == null) a = PidLand();
            else { LandAgent.WriteObs(landObs, held, fc, framesSinceDet); a = landPolicy.Act(landObs); }
            prevLandAction = a;
            fc.SetVelocity(a.x * LandAgent.MaxHSpeed, a.y * LandAgent.MaxHSpeed, a.z * Sim.MaxVerticalSpeed, 0f);
            bool touched = false; float vzBefore = 0f;
            for (int s = 0; s < 5 && !touched; s++)
            {
                vzBefore = -fc.velocity.y; bool was = fc.onGround;
                fc.Step(Sim.PhysicsDt, wind.Step(time + s * Sim.PhysicsDt));
                if (fc.onGround && !was) touched = true;
            }
            landSteps++;
            if (touched)
            {
                float impact = Mathf.Max(fc.lastImpactSpeed, vzBefore);
                int pi = truth.PatchAt(Pos);
                bool inside = pi >= 0 && truth.features[pi].EdgeClearance(Pos) >= Sim.DroneRadius;
                if (impact > LandAgent.SafeTouchdown) { Crash("hard_impact"); return; }
                if (!inside) { RejectSpot(takeOff: true); return; }
                mode = MissionMode.Upload; return;
            }
            if (framesSinceDet > LandAgent.LostLimit || landSteps > 400 || fc.position.y > 9f) RejectSpot(takeOff: true);
        }

        void StepUpload()
        {
            landedWorld = truth.ToWorld(Pos, 0.3f);
            landedRssi = wifi.RSSI(parentWorld, landedWorld, true);
            if (landedRssi < wifi.p.tLink) { RejectSpot(takeOff: true); return; }
            map.nodes.Add(landedWorld);
            mode = MissionMode.Done;
            if (DumpDir != null) Dump("landed");
        }

        void RejectSpot(bool takeOff = false)
        {
            for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) rejected.Add(spotCell + new Vector2Int(di, dj));
            takingOff = takeOff || fc.position.y < Sim.CruiseAltitude - 0.3f;
            RefreshKnown();
            if (!SelectSpot(out spotCell)) { Fail("no_qualifying_spot"); return; }
            goal = map.CellCenter(spotCell.x, spotCell.y);
            mode = MissionMode.Navigate; replanCountdown = 0; hoverSteps = 0; rssiAcc = 0; rssiN = 0;
        }

        void TakeOff()
        {
            fc.SetVelocity(0, 0, Sim.MaxVerticalSpeed, 0);
            for (int s = 0; s < 5; s++) fc.Step(Sim.PhysicsDt, wind.Step(time));
            if (fc.position.y >= Sim.CruiseAltitude - 0.05f) { takingOff = false; fc.position.y = Sim.CruiseAltitude; }
        }

        void Hover()
        {
            fc.SetVelocity(0, 0, (Sim.CruiseAltitude - fc.position.y), 0);
            StepFlight(true);
        }

        // ------------------------------------------------------------------ flight with the navigation policy
        /// One decision toward a waypoint. Returns true when within 1 m of it.
        bool Fly(Vector2 wp)
        {
            Vector3 a;
            if (cfg.scriptedNav || navPolicy == null) a = ScriptedNav(wp);
            else { NavAgent.WriteObs(navObs, truth, fc, wp, prevNavAction, rays); a = navPolicy.Act(navObs); }
            float before = hopField != null ? hopField.At(Pos) : 0f;
            fc.SetVelocity(a.x * Sim.MaxHorizontalSpeed, a.y * Sim.MaxHorizontalSpeed, (Sim.CruiseAltitude - fc.position.y) * 1.5f, a.z * Sim.MaxYawRate);
            prevNavAction = a;
            if (!StepFlight(false)) return false;
            if (hopField != null) lastReward = (before - hopField.At(Pos)) / 10f - 0.0005f;
            return (Pos - wp).magnitude < 1f;
        }

        /// Integrates 0.1 s of flight; returns false on collision (drone lost).
        bool StepFlight(bool hover)
        {
            for (int s = 0; s < 5; s++)
            {
                fc.Step(Sim.PhysicsDt, wind.Step(time + s * Sim.PhysicsDt));
                if (fc.mode == FlightMode.Fast) { fc.position.y = Mathf.Max(fc.position.y, 0f); }
                if (truth.Collides(Pos, Sim.DroneRadius)) { Crash("collision"); return false; }
            }
            return true;
        }

        Vector3 ScriptedNav(Vector2 wp)
        {
            var d = wp - Pos;
            float bearing = Mathf.DeltaAngle(fc.yaw, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing)) * Mathf.Min(1f, d.magnitude / 2f);
            // repulsion
            NavAgent.CastRays(truth, Pos, fc.yaw, rays);
            for (int k = 0; k < NavAgent.NumRays; k++)
            {
                if (rays[k] >= 1.5f) continue;
                float ang = k * 2f * Mathf.PI / NavAgent.NumRays;
                dir -= new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * (1f / Mathf.Max(rays[k], 0.2f) - 1f / 1.5f) * 0.5f;
            }
            if (dir.magnitude > 1f) dir.Normalize();
            return new Vector3(dir.y, dir.x, Mathf.Clamp(bearing * Mathf.Rad2Deg / 45f, -1f, 1f));
        }

        Vector3 PidLand()
        {
            if (!held.detected) return new Vector3(0, 0, 0.3f);
            float alt = fc.position.y;
            Vector2 err = held.offset * NadirCamera.HalfExtent(alt);
            Vector2 v = Vector2.ClampMagnitude(LandBaselines.kp * err, LandAgent.MaxHSpeed) / LandAgent.MaxHSpeed;
            float vz = err.magnitude < 0.25f + 0.08f * alt ? (alt > 1.2f ? -0.5f : -0.3f) : 0f;
            return new Vector3(v.y, v.x, vz);
        }

        void Crash(string why) { crashed = true; Fail(why); }
        void Fail(string why) { failReason = why; mode = MissionMode.Failed; if (DumpDir != null) Dump(why); }

        public static string DumpDir;
        void Dump(string why)
        {
            var sb = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("{\"why\":\"").Append(why).Append("\",\"W\":").Append(map.W).Append(",\"H\":").Append(map.H);
            var p = truth.ToLocal(parentWorld);
            sb.Append(",\"parent\":[").Append(p.x.ToString(inv)).Append(',').Append(p.y.ToString(inv)).Append(']');
            sb.Append(",\"drone\":[").Append(Pos.x.ToString(inv)).Append(',').Append(Pos.y.ToString(inv)).Append(']');
            sb.Append(",\"target\":[").Append(map.targetPos.x.ToString(inv)).Append(',').Append(map.targetPos.y.ToString(inv)).Append(']');
            void Arr(string name, System.Func<int, string> f) { sb.Append(",\"").Append(name).Append("\":["); for (int k = 0; k < map.W * map.H; k++) { if (k > 0) sb.Append(','); sb.Append(f(k)); } sb.Append(']'); }
            Arr("occ", k => truth.occ[k].ToString());
            Arr("explored", k => map.explored[k] ? "1" : "0");
            Arr("obstacle", k => map.obstacle[k] ? "1" : "0");
            Arr("landable", k => map.IsLandable(k) ? "1" : "0");
            Arr("landyes", k => map.landYes[k].ToString("0.#", inv));
            Arr("landno", k => map.landNo[k].ToString("0.#", inv));
            Arr("trueland", k => truth.GroundClassAt(map.CellCenter(k % map.W, k / map.W)) == SegClass.Landable ? "1" : "0");
            Arr("rssi", k => RssiEstimate(k % map.W, k / map.W).ToString("0.#", inv));
            Arr("tfield", k => float.IsInfinity(targetField.dist[k]) ? "-1" : targetField.dist[k].ToString("0.#", inv));
            sb.Append(",\"trail\":["); for (int t = 0; t < trail.Count; t++) { if (t > 0) sb.Append(','); sb.Append('[').Append(trail[t].x.ToString("0.#", inv)).Append(',').Append(trail[t].y.ToString("0.#", inv)).Append(']'); }
            sb.Append("]}");
            System.IO.Directory.CreateDirectory(DumpDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(DumpDir, $"dump_{truth.seed}_d{index}.json"), sb.ToString());
        }

        // ------------------------------------------------------------------ planner rules
        void RefreshKnown()
        {
            knownFree = map.KnownMap(truth, unknownBlocked: false);
            knownExplored = map.KnownMap(truth, unknownBlocked: true);
            targetField = new PathField(knownFree, truth.CellOf(map.targetPos));
        }

        /// RSSI to the parent at a cell: measured if the drone has been there, else predicted on the known map (−2 dB).
        float RssiEstimate(int i, int j)
        {
            if (map.MeasuredRssi(i, j, out float m)) return m;
            var pos = knownFree.ToWorld(map.CellCenter(i, j), Sim.CruiseAltitude);
            float d = Mathf.Max(Vector3.Distance(parentWorld, pos), wifi.p.d0);
            float loss = 0f;
            // known obstacles cost two concrete walls; unexplored stretches cost an uncertainty penalty
            // (v1 treated unexplored space as open air, which lured drones out of radio range)
            knownExplored.ForEachObstacleRun(truth.ToLocal(parentWorld), map.CellCenter(i, j),
                mat => loss += mat == WallMaterial.Concrete ? 2f * wifi.p.lossConcrete : UnknownPenalty);
            return wifi.p.P0 - 10f * wifi.p.n * Mathf.Log10(d / wifi.p.d0) - loss - 2f;
        }

        /// Target-biased frontier selection: among frontier cells whose RSSI to the parent is at least T_link + M,
        /// the one with the shortest path distance to the target.
        bool SelectFrontier(out Vector2 best)
        {
            best = default;
            float thr = wifi.p.tLink + wifi.p.margin, bestD = float.MaxValue;
            var reach = new PathField(knownFree, map.CellOf(Pos));
            for (int j = 1; j < map.H - 1; j++)
                for (int i = 1; i < map.W - 1; i++)
                {
                    int k = map.Idx(i, j);
                    if (!map.explored[k] || map.obstacle[k]) continue;
                    if (map.explored[k + 1] && map.explored[k - 1] && map.explored[k + map.W] && map.explored[k - map.W]) continue;
                    float dt = targetField.AtCell(i, j);
                    if (float.IsInfinity(dt) || dt >= bestD) continue;
                    if (float.IsInfinity(reach.AtCell(i, j))) continue;
                    if (truth.Collides(map.CellCenter(i, j), 0.5f) && knownFree.Collides(map.CellCenter(i, j), 0.5f)) continue;
                    if (RssiEstimate(i, j) < thr) continue;
                    bestD = dt; best = map.CellCenter(i, j);
                }
            return bestD < float.MaxValue;
        }

        /// Spot rule: among landable spots with RSSI to parent >= T_link + M + ground penalty, the shortest path
        /// distance to the target (ties: higher RSSI). Alternative strategies for the Section 12 comparison.
        public const float UnknownPenalty = 6f;  // dB per unexplored stretch on the line to the parent
        public const float TentativeMargin = 2f; // spots up to 2 dB below the threshold are visited and re-measured
        public const float MinProgress = 5f; // m of path distance toward the target a new relay must gain over its parent
        float parentTargetDist => targetField.At(truth.ToLocal(parentWorld));

        public bool SelectSpot(out Vector2Int spot)
        {
            spot = new Vector2Int(-1, -1);
            var cands = new List<(Vector2Int c, float dist, float rssi)>();
            var reach = new PathField(knownFree, map.CellOf(Pos));
            float pDist = parentTargetDist;
            for (int j = 1; j < map.H - 1; j++)
                for (int i = 1; i < map.W - 1; i++)
                {
                    int k = map.Idx(i, j);
                    if (!map.IsLandable(k) || map.obstacle[k]) continue;
                    // footprint + clearance: at least 3 of the 4 neighbours landable as well
                    int nb = (map.IsLandable(k + 1) ? 1 : 0) + (map.IsLandable(k - 1) ? 1 : 0) + (map.IsLandable(k + map.W) ? 1 : 0) + (map.IsLandable(k - map.W) ? 1 : 0);
                    if (nb < 3) continue;
                    var c = new Vector2Int(i, j);
                    if (rejected.Contains(c) || float.IsInfinity(reach.AtCell(i, j))) continue;
                    float dist = targetField.AtCell(i, j);
                    if (dist > pDist - MinProgress) continue; // a relay must extend the chain toward the target
                    float rssi = RssiEstimate(i, j);
                    if (rssi < wifi.p.QualifyThreshold - TentativeMargin) continue; // final check: hover-averaged RSSI at the spot
                    cands.Add((c, dist, rssi));
                }
            if (cands.Count == 0) return false;
            switch (cfg.strategy)
            {
                case SpotStrategy.Random: spot = cands[rng.Next(cands.Count)].c; break;
                case SpotStrategy.NearestParent:
                    var pl = truth.ToLocal(parentWorld);
                    cands.Sort((a, b) => (map.CellCenter(a.c.x, a.c.y) - pl).sqrMagnitude.CompareTo((map.CellCenter(b.c.x, b.c.y) - pl).sqrMagnitude));
                    spot = cands[0].c; break;
                default:
                    cands.Sort((a, b) => Mathf.Abs(a.dist - b.dist) < 0.5f ? b.rssi.CompareTo(a.rssi) : a.dist.CompareTo(b.dist));
                    spot = cands[0].c; break;
            }
            return true;
        }

        /// Next hop toward `target`: the furthest point within hopLength along the shortest path on `pathMap`
        /// (falls back to `fallback` if unreachable there).
        Vector2 NextHop(Vector2 target, TownMap pathMap, TownMap fallback)
        {
            var tc = map.CellOf(target);
            hopField = new PathField(pathMap, tc);
            hopFieldGoal = tc;
            var path = hopField.PathFrom(map.CellOf(Pos));
            if (path.Count == 0 && fallback != pathMap)
            {
                hopField = new PathField(fallback, tc);
                path = hopField.PathFrom(map.CellOf(Pos));
            }
            if (path.Count == 0) return target;
            float acc = 0f; Vector2 prev = Pos, hop = map.CellCenter(path[path.Count - 1].x, path[path.Count - 1].y);
            for (int k = 1; k < path.Count; k++)
            {
                var c = map.CellCenter(path[k].x, path[k].y);
                acc += (c - prev).magnitude; prev = c;
                if (acc >= cfg.hopLength) { hop = c; break; }
            }
            if ((hop - target).magnitude < 1.5f) hop = target;
            return hop;
        }
    }
}
