using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace CommDrone
{
    /// Headless evaluation on held-out (odd) seeds (SPEC Section 12). Runs N episodes with a policy or baseline
    /// and writes one CSV row per episode.
    /// Args: -eval nav|land -controller policy|pid|astar|potential|straight -model <Resources/Models name>
    ///       -episodes N -seedBase S(odd) -level L -gust G -windMean W -flight fast|full -noiseScale k -arenas A -out file.csv
    public class EvalRunner : MonoBehaviour
    {
        public string mode = "nav", controller = "potential", modelName = "", outPath = "eval.csv", flight = "fast";
        public int episodes = 100, seedBase = 1000001, arenas = 16;
        public float level = 2, gust = 0, windMean = 0, noiseScale = 1;

        readonly List<string> rows = new List<string>();
        readonly Dictionary<Agent, int> current = new Dictionary<Agent, int>();
        int nextEpisode, done;
        bool finished;

        void Awake()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                string v = a[i + 1];
                switch (a[i])
                {
                    case "-eval": mode = v; break;
                    case "-controller": controller = v; break;
                    case "-model": modelName = v; break;
                    case "-out": outPath = v; break;
                    case "-flight": flight = v; break;
                    case "-episodes": episodes = int.Parse(v); break;
                    case "-seedBase": seedBase = int.Parse(v) | 1; break;
                    case "-arenas": arenas = int.Parse(v); break;
                    case "-level": level = F(v); break;
                    case "-gust": gust = F(v); break;
                    case "-windMean": windMean = F(v); break;
                    case "-noiseScale": noiseScale = F(v); break;
                }
            }
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Academy.Instance.AutomaticSteppingEnabled = false;
            var fm = flight == "full" ? FlightMode.Full : FlightMode.Fast;
            Unity.InferenceEngine.ModelAsset model = null;
            if (controller == "policy")
            {
                model = Resources.Load<Unity.InferenceEngine.ModelAsset>("Models/" + modelName);
                if (model == null) { Debug.LogError("model not found: " + modelName); Application.Quit(2); return; }
            }
            var bt = controller == "policy" ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
            var kind = mode == "nav" ? TrainingManager.Kind.Nav : TrainingManager.Kind.Land;
            rows.Add(mode == "nav"
                ? "episode,seed,controller,level,flight,outcome,steps,time_s,path_dist,travelled,path_ratio"
                : "episode,seed,controller,gust,wind_mean,noise_scale,flight,outcome,steps,time_s,landing_error,touchdown_speed,patch_w,patch_h");
            for (int i = 0; i < Mathf.Min(arenas, episodes); i++)
            {
                var ag = TrainingManager.Spawn(kind, i, transform, level, fm, model, bt);
                if (ag is NavAgent na) SetupNav(na);
                else SetupLand((LandAgent)ag);
            }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        static string S(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        int Claim(Agent a) { int e = nextEpisode++; current[a] = e; return e; }

        void SetupNav(NavAgent a)
        {
            a.controllerOverride = controller switch
            {
                "astar" => NavBaselines.AStar,
                "potential" => NavBaselines.PotentialField,
                "straight" => NavBaselines.StraightLine,
                _ => null,
            };
            a.episodeProvider = ag =>
            {
                int e = Claim(ag);
                int seed = seedBase + 2 * e;
                var cp = CurriculumParams.ForLevel(level);
                var rng = new System.Random(seed);
                var map = MapGenerator.Generate(seed, cp, withFeatures: false);
                ag.Wind.Reseed(seed);
                // same sampling rule as training: path distance within the level's band
                for (int attempt = 0; attempt < 1000; attempt++)
                {
                    var start = ClearPoint(map, rng, 1.0f);
                    var pf = new PathField(map, map.CellOf(start));
                    for (int t = 0; t < 200; t++)
                    {
                        var wp = ClearPoint(map, rng, 0.8f);
                        float d = pf.At(wp);
                        if (d >= cp.navMinDist && d <= cp.navMaxDist)
                            return (map, start, (float)rng.NextDouble() * 360f, wp);
                    }
                }
                return (map, map.BasePos, 0f, map.TargetPos);
            };
            a.EpisodeEnded += (ag, outcome) =>
            {
                var na = (NavAgent)ag;
                if (!current.TryGetValue(na, out int e) || e >= episodes) return;
                rows.Add(string.Join(",", e, seedBase + 2 * e, controller, S(level), flight, outcome, na.StepsInEpisode,
                    S(na.StepsInEpisode * Sim.ControlDt), S(na.StartPathDist), S(na.TravelledDist), S(na.TravelledDist / Mathf.Max(0.1f, na.StartPathDist))));
                done++;
            };
        }

        static Vector2 ClearPoint(TownMap m, System.Random rng, float clearance)
        {
            for (int t = 0; t < 10000; t++)
            {
                var p = new Vector2((float)rng.NextDouble() * m.W, (float)rng.NextDouble() * m.H) * m.cellSize;
                if (!m.Collides(p, clearance)) return p;
            }
            return m.BasePos;
        }

        void SetupLand(LandAgent a)
        {
            a.gustOverride = gust;
            a.meanWindOverride = windMean;
            if (controller == "pid") a.controllerOverride = LandBaselines.PidServo;
            a.episodeProvider = ag =>
            {
                int e = Claim(ag);
                int seed = seedBase + 2 * e;
                var rng = new System.Random(seed);
                float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
                var patch = new GroundFeature { type = GroundType.FlatConcrete, center = Vector2.zero, size = new Vector2(R(1.8f, 5f), R(1.8f, 5f)) };
                float ang = R(0, 2 * Mathf.PI), rad = R(0f, 2.0f);
                var start = new Vector3(Mathf.Cos(ang) * rad, R(3f, 6f), Mathf.Sin(ang) * rad);
                ag.Wind.Reseed(seed);
                var np = new VisionNoiseParams();
                np.fnRate = Mathf.Clamp01(np.fnRate * noiseScale); np.fpRate = Mathf.Clamp01(np.fpRate * noiseScale);
                np.locSigma *= noiseScale; np.locBias *= noiseScale; np.dropRate = Mathf.Clamp01(np.dropRate * noiseScale);
                ag.noiseFromEnv = false;
                ag.detector = new VisionNoiseModel(np, seed);
                return (patch, start, R(0, 360));
            };
            a.EpisodeEnded += (ag, outcome) =>
            {
                var la = (LandAgent)ag;
                if (!current.TryGetValue(la, out int e) || e >= episodes) return;
                bool touched = outcome == "success" || outcome == "off_patch" || outcome == "hard_impact";
                rows.Add(string.Join(",", e, seedBase + 2 * e, controller, S(gust), S(windMean), S(noiseScale), flight, outcome, la.StepsInEpisode,
                    S(la.StepsInEpisode * Sim.ControlDt), touched ? S(la.LastLandingError) : "", touched ? S(la.LastTouchdownSpeed) : "",
                    S(la.Patch.size.x), S(la.Patch.size.y)));
                done++;
            };
        }

        void Update()
        {
            if (finished) return;
            for (int k = 0; k < 20 && done < episodes; k++) Academy.Instance.EnvironmentStep();
            if (done >= episodes)
            {
                finished = true;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllText(outPath, string.Join("\n", rows) + "\n");
                Debug.Log($"EvalRunner wrote {done} episodes to {outPath}");
                Application.Quit(0);
            }
        }
    }
}
