using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEngine;

namespace CommDrone
{
    /// Runs full multi-drone missions: drones launch one at a time from the base, each following DroneMission
    /// with a shared map, until the target camp is covered or drones run out (SPEC Sections 7, 12, 14).
    /// Args: -missions N -seedBase S -level L -detector gt|noisy|cnn -segModel name -noiseScale k
    ///       -strategy best|random|nearest -shared 1|0 -flight fast|full -gust g -windMean w -navModel n -landModel n
    ///       -scriptedNav 0|1 -scriptedLand 0|1 -demo 0|1 -out file.csv
    public class MissionRunner : MonoBehaviour
    {
        public int missions = 100, seedBase = 2000001, maxDrones = 6;
        public float level = 3, noiseScale = 1f;
        public string detector = "noisy", segModel = "unet_v1", navModel = "nav_v1", landModel = "land_v2", outPath = "missions.csv";
        public MissionConfig cfg = new MissionConfig();
        public bool demo;
        public int stepsPerFrame = 50;
        public bool handBuiltDemoLevel;

        // runtime state (public for the HUD / demo recorder)
        public TownMap truth;
        public SharedMap shared;
        public WifiModel wifi;
        public DroneMission current;
        public readonly List<DroneMission> finished = new List<DroneMission>();
        public int missionIndex, droneIndex;
        public string status = "";
        public bool covered;
        public TownVisual visual;

        PolicyRunner nav, land;
        SegModel seg;
        Camera visionCam;
        ICellDetector cellDet;
        ISpotDetector spotDet;
        readonly StringBuilder csv = new StringBuilder();
        readonly StringBuilder droneCsv = new StringBuilder();
        bool done;
        float basePathDist;
        System.Random rng;

        void Awake()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                string v = a[i + 1];
                switch (a[i])
                {
                    case "-missions": missions = int.Parse(v); break;
                    case "-seedBase": seedBase = int.Parse(v) | 1; break;
                    case "-level": level = F(v); break;
                    case "-detector": detector = v; break;
                    case "-segModel": segModel = v; break;
                    case "-navModel": navModel = v; break;
                    case "-landModel": landModel = v; break;
                    case "-noiseScale": noiseScale = F(v); break;
                    case "-strategy": cfg.strategy = v == "random" ? SpotStrategy.Random : v == "nearest" ? SpotStrategy.NearestParent : SpotStrategy.BestToTarget; break;
                    case "-shared": cfg.useSharedMap = v != "0"; break;
                    case "-flight": cfg.flight = v == "full" ? FlightMode.Full : FlightMode.Fast; break;
                    case "-gust": cfg.gust = F(v); break;
                    case "-windMean": cfg.windMean = F(v); break;
                    case "-scriptedNav": cfg.scriptedNav = v == "1"; break;
                    case "-scriptedLand": cfg.scriptedLand = v == "1"; break;
                    case "-maxDrones": maxDrones = int.Parse(v); break;
                    case "-demo": demo = v == "1"; break;
                    case "-demoLevel": handBuiltDemoLevel = v == "1"; break;
                    case "-out": outPath = v; break;
                    case "-dumpDir": DroneMission.DumpDir = v; break;
                }
            }
            cfg.maxDrones = maxDrones;
            Application.targetFrameRate = demo ? 30 : -1;
            QualitySettings.vSyncCount = 0;
            var navAsset = Resources.Load<ModelAsset>("Models/" + navModel);
            var landAsset = Resources.Load<ModelAsset>("Models/" + landModel);
            if (navAsset) nav = new PolicyRunner(navAsset, NavAgent.ObsSize); else Debug.LogWarning("nav model missing, scripted nav");
            if (landAsset) land = new PolicyRunner(landAsset, LandAgent.ObsSize); else Debug.LogWarning("land model missing, scripted landing");
            if (detector.StartsWith("cnn"))
            {
                var segAsset = Resources.Load<ModelAsset>("Models/" + segModel);
                seg = new SegModel(segAsset);
                visionCam = new GameObject("VisionCamera").AddComponent<Camera>();
                visionCam.enabled = false; visionCam.allowMSAA = false;
                visionCam.nearClipPlane = 0.05f; visionCam.farClipPlane = 200f;
                visionCam.clearFlags = CameraClearFlags.SolidColor; visionCam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
                cellDet = new VisionSegmenter(seg, visionCam);
                spotDet = new LandingSpotLocator(seg, visionCam);
            }
            csv.AppendLine("mission,seed,level,detector,noise_scale,strategy,shared,flight,success,fail_reason,drones_used,drones_lost,range_extended_m,base_path_m,final_path_m,chain_quality_dbm,target_rssi_dbm,landing_attempts,total_time_s");
            droneCsv.AppendLine("mission,seed,drone,outcome,fail_reason,explore_time_s,battery_used_s,flight_time_s,landing_attempts,landed_rssi,shared");
            StartMission();
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string S(float f) => float.IsNaN(f) ? "" : f.ToString("0.##", CultureInfo.InvariantCulture);

        int Seed => seedBase + 2 * missionIndex;

        void StartMission()
        {
            visual?.Destroy();
            rng = new System.Random(Seed);
            truth = handBuiltDemoLevel ? DemoLevel.Build() : MapGenerator.Generate(Seed, CurriculumParams.ForLevel(level));
            wifi = new WifiModel(truth, new WifiParams(), Seed);
            shared = new SharedMap(truth);
            shared.nodes.Add(truth.ToWorld(truth.BasePos, 3f)); // base-station antenna mast
            finished.Clear(); droneIndex = 0; covered = false;
            basePathDist = new PathField(truth, truth.targetCell).At(truth.BasePos);
            if (seg != null || demo)
            {
                visual = TownBuilder.Build(truth, Seed ^ 0x5bd1e995);
                if (spotDet is LandingSpotLocator loc) loc.worldOrigin = truth.origin;
            }
            if (!detector.StartsWith("cnn"))
            {
                var np = VisionCalibration.LoadNoise();
                if (detector == "gt")
                {
                    cellDet = new GroundTruthCellDetector();
                    spotDet = new GeometricSpotSensor();
                }
                else
                {
                    cellDet = new NoisyCellDetector(VisionCalibration.ScaledConfusion(np.confusion, noiseScale), Seed);
                    spotDet = new VisionNoiseModel(VisionCalibration.Scaled(np.spot, noiseScale), Seed + 1);
                }
            }
            covered = Covered();
            LaunchNext();
        }

        bool Covered()
        {
            var last = shared.nodes[shared.nodes.Count - 1];
            return wifi.RSSI(last, truth.ToWorld(truth.TargetPos, 1.5f), true, false) >= wifi.p.tLink;
        }

        void LaunchNext()
        {
            if (covered || droneIndex >= cfg.maxDrones) { EndMission(); return; }
            droneIndex++;
            var parent = shared.nodes[shared.nodes.Count - 1];
            current = new DroneMission(droneIndex, truth, shared, parent, wifi, cfg, nav, land, cellDet, spotDet, Seed * 13 + droneIndex);
            status = $"mission {missionIndex + 1}/{missions} drone {droneIndex}";
        }

        void Update()
        {
            if (done) return;
            int n = demo ? Mathf.Max(1, demoSpeed) : stepsPerFrame;
            for (int k = 0; k < n && !done; k++) Tick();
        }

        public int demoSpeed = 2;

        void Tick()
        {
            if (current == null) return;
            current.Step();
            if (!current.Finished) return;
            var d = current;
            finished.Add(d);
            if (d.mode == MissionMode.Done)
            {
                if (cfg.useSharedMap) shared.Merge(d.map, d.downloaded);
                else shared.nodes.Add(d.landedWorld);
                if (!shared.nodes.Contains(d.landedWorld)) shared.nodes.Add(d.landedWorld);
                covered = Covered();
                LaunchNext();
            }
            else if (d.failReason == "no_qualifying_spot") EndMission();
            else LaunchNext(); // drone lost: the next drone retries from the same parent
        }

        void EndMission()
        {
            var tf = new PathField(truth, truth.targetCell);
            float finalPath = tf.At(truth.ToLocal(shared.nodes[shared.nodes.Count - 1]));
            float chain = float.PositiveInfinity;
            for (int i = 1; i < shared.nodes.Count; i++)
                chain = Mathf.Min(chain, wifi.RSSI(shared.nodes[i - 1], shared.nodes[i], true, false));
            float targetRssi = wifi.RSSI(shared.nodes[shared.nodes.Count - 1], truth.ToWorld(truth.TargetPos, 1.5f), true, false);
            int lost = 0, attempts = 0; float total = 0;
            foreach (var d in finished) { if (d.crashed) lost++; attempts += d.landingAttempts; total += d.time; }
            string reason = covered ? "" : finished.Count > 0 ? finished[finished.Count - 1].failReason : "none";
            if (!covered && droneIndex >= cfg.maxDrones && reason == "") reason = "out_of_drones";
            csv.AppendLine(string.Join(",", missionIndex, Seed, S(level), detector, S(noiseScale), cfg.strategy, cfg.useSharedMap ? 1 : 0, cfg.flight,
                covered ? 1 : 0, reason, finished.Count, lost, S(basePathDist - finalPath), S(basePathDist), S(finalPath),
                S(float.IsInfinity(chain) ? float.NaN : chain), S(targetRssi), attempts, S(total)));
            foreach (var d in finished)
                droneCsv.AppendLine(string.Join(",", missionIndex, Seed, d.index, d.mode, d.failReason, S(d.exploreTime), S(cfg.batteryCapacity - d.battery),
                    S(d.time), d.landingAttempts, S(d.mode == MissionMode.Done ? d.landedRssi : float.NaN), cfg.useSharedMap ? 1 : 0));
            Debug.Log($"Mission {missionIndex} seed {Seed}: covered={covered} drones={finished.Count} reason={reason} range+={basePathDist - finalPath:F1} m chain={chain:F1} dBm");
            if (demo) { done = true; status = covered ? "TARGET COVERED" : "mission failed: " + reason; Flush(); return; }
            missionIndex++;
            if (missionIndex >= missions) { done = true; Flush(); Application.Quit(0); return; }
            StartMission();
        }

        void Flush()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, csv.ToString());
            File.WriteAllText(outPath.Replace(".csv", "_drones.csv"), droneCsv.ToString());
        }

        void OnDestroy() { nav?.Dispose(); land?.Dispose(); seg?.Dispose(); }
    }
}
