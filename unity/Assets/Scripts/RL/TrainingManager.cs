using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace CommDrone
{
    /// Spawns headless training arenas and steps the Academy manually (no fixed-update throttling).
    /// Command line: -arenas N  -stepsPerUpdate K  -level L (fixed level, optional)
    public class TrainingManager : MonoBehaviour
    {
        public enum Kind { Nav, Land }
        public Kind kind = Kind.Nav;
        public int arenas = 16;
        public int stepsPerUpdate = 20;
        public float fixedLevel = 0f;
        public readonly List<Agent> agents = new List<Agent>();

        void Awake()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-arenas") int.TryParse(args[i + 1], out arenas);
                if (args[i] == "-stepsPerUpdate") int.TryParse(args[i + 1], out stepsPerUpdate);
                if (args[i] == "-level") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fixedLevel);
            }
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Academy.Instance.AutomaticSteppingEnabled = false;
            for (int i = 0; i < arenas; i++) agents.Add(Spawn(kind, i, transform, fixedLevel));
        }

        public static Agent Spawn(Kind kind, int index, Transform parent, float fixedLevel = 0f, FlightMode mode = FlightMode.Fast)
        {
            var go = new GameObject($"{kind}Agent_{index}");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var bp = go.AddComponent<BehaviorParameters>();
            bp.BehaviorName = kind == Kind.Nav ? "Nav" : "Land";
            bp.BrainParameters.VectorObservationSize = kind == Kind.Nav ? NavAgent.ObsSize : LandAgent.ObsSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(3);
            Agent agent;
            if (kind == Kind.Nav)
            {
                var a = go.AddComponent<NavAgent>();
                a.seedOffset = index; a.fixedLevel = fixedLevel; a.flightMode = mode;
                agent = a;
            }
            else
            {
                var a = go.AddComponent<LandAgent>();
                a.seedOffset = index; a.fixedLevel = fixedLevel; a.flightMode = mode;
                agent = a;
            }
            var dr = go.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 1;
            dr.TakeActionsBetweenDecisions = false;
            go.SetActive(true);
            return agent;
        }

        void Update()
        {
            for (int k = 0; k < stepsPerUpdate; k++) Academy.Instance.EnvironmentStep();
        }
    }
}
