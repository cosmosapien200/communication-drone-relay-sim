using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace CommDrone
{
    /// Landing policy agent (SPEC Section 9). Descends onto the chosen patch using the spot locator's output,
    /// under wind gusts and imperfect detections.
    public class LandAgent : Agent
    {
        public const int ObsSize = 2 + 1 + 1 + 1 + 1 + 3 + 1;
        public const float MaxHSpeed = 1.5f;      // landing-mode horizontal limit (m/s)
        public const float SafeTouchdown = 0.6f;  // max vertical speed at touchdown (m/s)
        public const int LostLimit = 30;          // decisions (3 s) without detection -> fail

        public float fixedLevel = 0f; // unused for curriculum but kept for symmetry with NavAgent
        public FlightMode flightMode = FlightMode.Fast;
        public int seedOffset;
        /// Gust amplitude (m/s); <0 reads ML-Agents environment parameter "gust".
        public float gustOverride = -1f;
        public float meanWindOverride = -1f;

        public FlightController FC { get; private set; }
        public WindModel Wind { get; private set; }
        public GroundFeature Patch { get; private set; }
        public ISpotDetector detector;
        /// Training reads noise rates from ML-Agents environment parameters; evaluation fixes them.
        public bool noiseFromEnv = true;
        public SpotDetection LastDet { get; private set; }
        public int FramesSinceDetection { get; private set; }
        public int StepsInEpisode { get; private set; }
        public string LastOutcome { get; private set; } = "";
        public float LastLandingError { get; private set; }
        public float LastTouchdownSpeed { get; private set; }
        /// Optional external controller (PID baseline): returns vx fwd, vy right, vz in [-1, 1].
        public System.Func<LandAgent, Vector3> controllerOverride;
        /// Optional fixed scenario (patch, start position, yaw) for evaluation/mission use.
        public System.Func<LandAgent, (GroundFeature patch, Vector3 start, float yaw)> episodeProvider;
        public event System.Action<LandAgent, string> EpisodeEnded;
        public int maxSteps = 400;

        System.Random rng;
        Vector3 prevAction;
        SpotDetection held;

        public override void Initialize()
        {
            rng = new System.Random(seedOffset * 104729 + System.Environment.TickCount);
            FC = new FlightController(flightMode);
            Wind = new WindModel(rng.Next());
            detector ??= new VisionNoiseModel(ReadNoiseParams(), rng.Next());
        }

        static float Env(string k, float d) => Academy.Instance.EnvironmentParameters.GetWithDefault(k, d);

        public static VisionNoiseParams ReadNoiseParams()
        {
            var p = new VisionNoiseParams();
            p.fnRate = Env("noise_fn", p.fnRate); p.fpRate = Env("noise_fp", p.fpRate);
            p.locSigma = Env("noise_sigma", p.locSigma); p.locBias = Env("noise_bias", p.locBias);
            p.dropRate = Env("noise_drop", p.dropRate);
            return p;
        }

        public override void OnEpisodeBegin()
        {
            FC.mode = flightMode;
            Vector3 start; float yaw;
            if (episodeProvider != null)
            {
                var e = episodeProvider(this);
                Patch = e.patch; start = e.start; yaw = e.yaw;
            }
            else
            {
                var size = new Vector2(R(1.8f, 5f), R(1.8f, 5f));
                Patch = new GroundFeature { type = GroundType.FlatConcrete, center = Vector2.zero, size = size };
                float ang = R(0, 2 * Mathf.PI), rad = R(0f, 2.0f);
                start = new Vector3(Mathf.Cos(ang) * rad, R(3f, 6f), Mathf.Sin(ang) * rad);
                yaw = R(0, 360);
            }
            if (noiseFromEnv && detector is VisionNoiseModel vnm) vnm.p = ReadNoiseParams();
            detector.ResetEpisode();
            FC.Reset(start, yaw);
            float gust = gustOverride >= 0 ? gustOverride : Env("gust", 0f);
            float mean = meanWindOverride >= 0 ? meanWindOverride : Env("wind_mean", 0f);
            if (gust > 0 || mean > 0) Wind.Randomize(mean, gust); else Wind.SetCalm();
            FC.velocity = new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f));
            StepsInEpisode = 0; FramesSinceDetection = 0; prevAction = Vector3.zero;
            held = detector.Detect(FC.position, FC.yaw, Patch);
            LastDet = held;
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        void Sense()
        {
            var d = detector.Detect(FC.position, FC.yaw, Patch);
            LastDet = d;
            if (d.detected) { held = d; FramesSinceDetection = 0; }
            else { FramesSinceDetection++; held.confidence = 0f; }
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            sensor.AddObservation(Mathf.Clamp(held.offset.x, -2f, 2f));
            sensor.AddObservation(Mathf.Clamp(held.offset.y, -2f, 2f));
            sensor.AddObservation(held.area);
            sensor.AddObservation(held.confidence);
            sensor.AddObservation(Mathf.Clamp01(held.clearance / 3f));
            sensor.AddObservation(Mathf.Clamp01(FC.position.y / 8f));
            var bv = FC.BodyVelocity;
            sensor.AddObservation(bv.z / MaxHSpeed);
            sensor.AddObservation(bv.x / MaxHSpeed);
            sensor.AddObservation(bv.y / Sim.MaxVerticalSpeed);
            sensor.AddObservation(Mathf.Clamp01(FramesSinceDetection / 10f));
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
            FC.SetVelocity(a.x * MaxHSpeed, a.y * MaxHSpeed, a.z * Sim.MaxVerticalSpeed, 0f);
            int sub = Mathf.RoundToInt(Sim.ControlDt / Sim.PhysicsDt);
            bool touched = false; float vzBefore = 0f;
            for (int s = 0; s < sub && !touched; s++)
            {
                vzBefore = -FC.velocity.y;
                bool wasGround = FC.onGround;
                FC.Step(Sim.PhysicsDt, Wind.Step(FC.time));
                if (FC.onGround && !wasGround) touched = true;
            }
            StepsInEpisode++;
            Sense();

            var pos2 = new Vector2(FC.position.x, FC.position.z);
            float offset = (pos2 - Patch.center).magnitude;
            float alt = FC.position.y;
            float allowed = 0.3f + 0.35f * alt;
            float vDown = Mathf.Max(0f, -FC.velocity.y);
            float r = -0.01f * offset - 0.05f * Mathf.Max(0f, vDown - allowed) - 0.002f - 0.002f * (a - prevAction).sqrMagnitude;
            prevAction = a;
            AddReward(r);

            if (touched)
            {
                float impact = Mathf.Max(FC.lastImpactSpeed, vzBefore);
                LastTouchdownSpeed = impact;
                LastLandingError = offset;
                bool inside = Patch.EdgeClearance(pos2) >= Sim.DroneRadius;
                if (inside && impact <= SafeTouchdown)
                {
                    float halfMin = Mathf.Min(Patch.size.x, Patch.size.y) * 0.5f;
                    AddReward(1f + (1f - Mathf.Clamp01(offset / halfMin)));
                    Finish("success");
                }
                else { AddReward(-1f); Finish(inside ? "hard_impact" : "off_patch"); }
            }
            else if (FramesSinceDetection > LostLimit) { AddReward(-1f); Finish("lost"); }
            else if (alt > 9f || offset > 8f) { AddReward(-1f); Finish("fly_away"); }
            else if (StepsInEpisode >= maxSteps) { AddReward(-0.5f); Finish("timeout"); }
        }

        void Finish(string outcome)
        {
            LastOutcome = outcome;
            var stats = Academy.Instance.StatsRecorder;
            stats.Add("Land/Success", outcome == "success" ? 1f : 0f);
            stats.Add("Land/OffPatch", outcome == "off_patch" ? 1f : 0f);
            stats.Add("Land/HardImpact", outcome == "hard_impact" ? 1f : 0f);
            stats.Add("Land/Lost", outcome == "lost" || outcome == "fly_away" ? 1f : 0f);
            if (outcome == "success" || outcome == "off_patch" || outcome == "hard_impact")
            {
                stats.Add("Land/Error", LastLandingError);
                stats.Add("Land/TouchdownSpeed", LastTouchdownSpeed);
            }
            EpisodeEnded?.Invoke(this, outcome);
            if (outcome == "timeout") EpisodeInterrupted(); else EndEpisode();
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var a = LandBaselines.PidServo(this);
            var ca = actionsOut.ContinuousActions;
            ca[0] = a.x; ca[1] = a.y; ca[2] = a.z;
        }
    }

    public static class LandBaselines
    {
        /// PID visual servoing: centre the patch, then descend at a fixed rate (SPEC Section 9 baseline).
        public static float kp = 0.9f, ki = 0.05f, kd = 0.25f;
        static readonly System.Collections.Generic.Dictionary<LandAgent, (Vector2 integ, Vector2 prevErr)> state = new();

        public static Vector3 PidServo(LandAgent a)
        {
            var d = a.LastDet;
            float alt = a.FC.position.y;
            if (!state.TryGetValue(a, out var s) || a.StepsInEpisode <= 1) s = (Vector2.zero, Vector2.zero);
            if (!d.detected)
            {
                state[a] = s;
                return new Vector3(0f, 0f, 0.3f); // patch lost: hold position and climb slowly to re-acquire
            }
            // offset in metres (x right, y forward)
            Vector2 err = d.offset * NadirCamera.HalfExtent(alt);
            s.integ = Vector2.ClampMagnitude(s.integ + err * Sim.ControlDt, 2f);
            Vector2 deriv = (err - s.prevErr) / Sim.ControlDt;
            s.prevErr = err;
            state[a] = s;
            Vector2 v = kp * err + ki * s.integ + kd * deriv;
            v = Vector2.ClampMagnitude(v, LandAgent.MaxHSpeed) / LandAgent.MaxHSpeed;
            float centredTol = 0.25f + 0.08f * alt;
            float vz = err.magnitude < centredTol ? (alt > 1.2f ? -0.5f : -0.3f) : 0f;
            return new Vector3(v.y, v.x, vz);
        }
    }
}
