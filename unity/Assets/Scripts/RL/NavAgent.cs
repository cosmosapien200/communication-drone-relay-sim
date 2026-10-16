using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace CommDrone
{
    /// Navigation policy agent (SPEC Section 8). Flies to a waypoint using only onboard range sensors.
    /// The arena is the grid map only (no scene objects), so training runs headless at high speed.
    public class NavAgent : Agent
    {
        public const int NumRays = 24;
        public const float RayRange = 10f;
        public const int ObsSize = 2 + 2 + 2 + NumRays + 3;

        [Tooltip("Fixed curriculum level (<=0: read ML-Agents environment parameter 'level').")]
        public float fixedLevel = 0f;
        public FlightMode flightMode = FlightMode.Fast;
        public int seedOffset;
        public float successRadius = 1.0f;
        public float safetyDistance = 1.0f;

        // reward terms
        public float timeCost = 0.0005f, proximityCost = 0.02f, collisionPenalty = 1f, successBonus = 1f;

        public TownMap Map { get; private set; }
        public FlightController FC { get; private set; }
        public WindModel Wind { get; private set; }
        public PathField Field { get; private set; }
        public Vector2 Waypoint { get; private set; }
        public float StartPathDist { get; private set; }
        public float TravelledDist { get; private set; }
        public int StepsInEpisode { get; private set; }
        public int MaxEpisodeSteps { get; private set; }
        public string LastOutcome { get; private set; } = "";
        /// When set, the next episode uses this map / start / waypoint instead of sampling (evaluation and mission use).
        public System.Func<NavAgent, (TownMap map, Vector2 start, float yaw, Vector2 waypoint)> episodeProvider;
        /// Optional external action source (baselines). Returns forward, sideways, yawRate in [-1, 1].
        public System.Func<NavAgent, Vector3> controllerOverride;
        public event System.Action<NavAgent, string> EpisodeEnded;

        float prevPathDist;
        Vector3 prevAction;
        System.Random rng;
        readonly float[] rays = new float[NumRays];
        int mapUses;

        public override void Initialize()
        {
            rng = new System.Random(seedOffset * 7919 + System.Environment.TickCount);
            FC = new FlightController(flightMode);
            Wind = new WindModel(rng.Next());
        }

        float Level => fixedLevel > 0 ? fixedLevel : Academy.Instance.EnvironmentParameters.GetWithDefault("level", 1f);

        public override void OnEpisodeBegin()
        {
            FC.mode = flightMode;
            Vector2 start, wp; float yaw;
            var cp = CurriculumParams.ForLevel(Level);
            if (episodeProvider != null)
            {
                var e = episodeProvider(this);
                Map = e.map; start = e.start; yaw = e.yaw; wp = e.waypoint;
                Field = new PathField(Map, Map.CellOf(wp));
            }
            else
            {
                // reuse a map for a few episodes to amortise generation; new map otherwise
                if (Map == null || mapUses >= 4 || Mathf.RoundToInt(Map.curriculum.level) != Mathf.RoundToInt(cp.level))
                {
                    Map = MapGenerator.Generate(rng.Next(), cp, withFeatures: false);
                    mapUses = 0;
                }
                mapUses++;
                SampleStartGoal(cp, out start, out wp);
                yaw = (float)rng.NextDouble() * 360f;
            }
            Waypoint = wp;
            FC.Reset(new Vector3(start.x, Sim.CruiseAltitude, start.y), yaw);
            if (cp.windMean > 0 || cp.windGust > 0) Wind.Randomize(cp.windMean, cp.windGust); else Wind.SetCalm();
            prevPathDist = StartPathDist = Field.At(start);
            MaxEpisodeSteps = 50 + Mathf.CeilToInt(2.5f * StartPathDist / (Sim.MaxHorizontalSpeed * Sim.ControlDt));
            StepsInEpisode = 0; TravelledDist = 0; prevAction = Vector3.zero;
            transform.localPosition = new Vector3(start.x, Sim.CruiseAltitude, start.y);
        }

        void SampleStartGoal(CurriculumParams cp, out Vector2 start, out Vector2 wp)
        {
            for (int attempt = 0; ; attempt++)
            {
                start = RandomClearPoint(1.0f);
                var fromStart = new PathField(Map, Map.CellOf(start));
                // candidate waypoints within the curriculum distance band
                for (int t = 0; t < 200; t++)
                {
                    var c = RandomClearPoint(0.8f);
                    float d = fromStart.At(c);
                    if (d >= cp.navMinDist && d <= cp.navMaxDist)
                    {
                        wp = c;
                        Field = new PathField(Map, Map.CellOf(wp));
                        return;
                    }
                }
                if (attempt > 20) { Map = MapGenerator.Generate(rng.Next(), cp, false); attempt = 0; }
            }
        }

        Vector2 RandomClearPoint(float clearance)
        {
            for (int t = 0; t < 10000; t++)
            {
                var p = new Vector2((float)rng.NextDouble() * Map.W, (float)rng.NextDouble() * Map.H) * Map.cellSize;
                if (!Map.Collides(p, clearance)) return p;
            }
            return Map.BasePos;
        }

        public Vector2 Pos2 => new Vector2(FC.position.x, FC.position.z);

        public float[] CastRays()
        {
            var p = Pos2;
            for (int k = 0; k < NumRays; k++)
            {
                float a = (FC.yaw + k * 360f / NumRays) * Mathf.Deg2Rad;
                // Unity yaw: forward = (sin a, cos a) in (x, z)
                rays[k] = Map.Raycast(p, new Vector2(Mathf.Sin(a), Mathf.Cos(a)), RayRange);
            }
            return rays;
        }

        /// Waypoint in body frame: (distance m, bearing deg in [-180, 180], positive to the right).
        public Vector2 WaypointBody()
        {
            var d = Waypoint - Pos2;
            float bearing = Mathf.DeltaAngle(FC.yaw, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg);
            return new Vector2(d.magnitude, bearing);
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            var wb = WaypointBody();
            sensor.AddObservation(Mathf.Log(1f + wb.x) / Mathf.Log(101f));
            sensor.AddObservation(wb.y / 180f);
            var bv = FC.BodyVelocity;
            sensor.AddObservation(bv.z / Sim.MaxHorizontalSpeed);
            sensor.AddObservation(bv.x / Sim.MaxHorizontalSpeed);
            sensor.AddObservation(Mathf.Sin(FC.yaw * Mathf.Deg2Rad));
            sensor.AddObservation(Mathf.Cos(FC.yaw * Mathf.Deg2Rad));
            CastRays();
            for (int k = 0; k < NumRays; k++) sensor.AddObservation(rays[k] / RayRange);
            sensor.AddObservation(prevAction);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            Vector3 a;
            if (controllerOverride != null) a = controllerOverride(this);
            else
            {
                var ca = actions.ContinuousActions;
                a = new Vector3(Mathf.Clamp(ca[0], -1f, 1f), Mathf.Clamp(ca[1], -1f, 1f), Mathf.Clamp(ca[2], -1f, 1f));
            }
            FC.SetVelocity(a.x * Sim.MaxHorizontalSpeed, a.y * Sim.MaxHorizontalSpeed, 0f, a.z * Sim.MaxYawRate);
            var before = Pos2;
            int sub = Mathf.RoundToInt(Sim.ControlDt / Sim.PhysicsDt);
            bool collided = false;
            for (int s = 0; s < sub; s++)
            {
                FC.Step(Sim.PhysicsDt, Wind.Step(FC.time));
                FC.position.y = Sim.CruiseAltitude; // altitude hold at cruise (2.5D flight)
                FC.velocity.y = 0f;
                if (Map.Collides(Pos2, Sim.DroneRadius)) { collided = true; break; }
            }
            TravelledDist += (Pos2 - before).magnitude;
            transform.localPosition = new Vector3(FC.position.x, Sim.CruiseAltitude, FC.position.z);
            transform.localRotation = Quaternion.Euler(0, FC.yaw, 0);
            StepsInEpisode++;

            float pd = Field.At(Pos2);
            if (float.IsInfinity(pd)) pd = prevPathDist;
            // progress normalised by the start path distance: total progress reward is ~1 at every curriculum level
            float r = (prevPathDist - pd) / Mathf.Max(StartPathDist, 5f) - timeCost;
            prevPathDist = pd;
            float minRay = RayRange;
            CastRays();
            for (int k = 0; k < NumRays; k++) minRay = Mathf.Min(minRay, rays[k]);
            if (minRay < safetyDistance) r -= proximityCost * (1f - minRay / safetyDistance);
            // smoothness: small penalty on action change
            r -= 0.002f * (a - prevAction).sqrMagnitude;
            prevAction = a;
            AddReward(r);

            if (collided) { AddReward(-collisionPenalty); Finish("collision", false); }
            else if ((Waypoint - Pos2).magnitude < successRadius) { AddReward(successBonus); Finish("success", false); }
            else if (StepsInEpisode >= MaxEpisodeSteps) Finish("timeout", true);
        }

        void Finish(string outcome, bool interrupted)
        {
            LastOutcome = outcome;
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Nav/Success", outcome == "success" ? 1f : 0f);
            stats.Add("Nav/Collision", outcome == "collision" ? 1f : 0f);
            stats.Add("Nav/Timeout", outcome == "timeout" ? 1f : 0f);
            stats.Add("Nav/Level", Level);
            if (outcome == "success") stats.Add("Nav/PathRatio", TravelledDist / Mathf.Max(1f, StartPathDist));
            EpisodeEnded?.Invoke(this, outcome);
            if (interrupted) EpisodeInterrupted(); else EndEpisode();
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var a = NavBaselines.PotentialField(this);
            var ca = actionsOut.ContinuousActions;
            ca[0] = a.x; ca[1] = a.y; ca[2] = a.z;
        }
    }
}
