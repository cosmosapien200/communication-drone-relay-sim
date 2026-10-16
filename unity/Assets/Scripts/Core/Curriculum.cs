using UnityEngine;

namespace CommDrone
{
    /// Curriculum parameters (SPEC Section 3). Level 1..4 maps to concrete generator settings;
    /// values in between are interpolated so ML-Agents can raise `level` smoothly.
    [System.Serializable]
    public struct CurriculumParams
    {
        public float level;
        public int mapSize;            // cells per side (1 m cells)
        public float rubbleDensity;    // fraction of street segments closed by rubble
        public float collapseProb;     // fraction of buildings collapsed into the street
        public int landablePatches;    // per 100x100 m
        public int lookAlikes;         // per 100x100 m
        public int intactRoads;        // intact-road strips along streets per 100x100 m (landable)
        public bool lineOfSight;       // base and target on one straight street
        public float navMinDist, navMaxDist; // nav training start-to-waypoint path distance (m)
        public float windMean, windGust;     // m/s

        public static CurriculumParams ForLevel(float level)
        {
            level = Mathf.Clamp(level, 1f, 4f);
            int l = Mathf.RoundToInt(level);
            var p = new CurriculumParams { level = level };
            switch (l)
            {
                case 1:
                    p.mapSize = 110; p.rubbleDensity = 0f; p.collapseProb = 0f; p.landablePatches = 40; p.lookAlikes = 30; p.intactRoads = 36;
                    p.lineOfSight = true; p.navMinDist = 4; p.navMaxDist = 15; p.windMean = 0; p.windGust = 0; break;
                case 2:
                    p.mapSize = 96; p.rubbleDensity = 0.08f; p.collapseProb = 0.05f; p.landablePatches = 35; p.lookAlikes = 40; p.intactRoads = 30;
                    p.lineOfSight = false; p.navMinDist = 8; p.navMaxDist = 30; p.windMean = 0.3f; p.windGust = 0.3f; break;
                case 3:
                    p.mapSize = 110; p.rubbleDensity = 0.16f; p.collapseProb = 0.10f; p.landablePatches = 25; p.lookAlikes = 50; p.intactRoads = 24;
                    p.lineOfSight = false; p.navMinDist = 15; p.navMaxDist = 50; p.windMean = 0.6f; p.windGust = 0.8f; break;
                default:
                    p.mapSize = 120; p.rubbleDensity = 0.25f; p.collapseProb = 0.15f; p.landablePatches = 14; p.lookAlikes = 60; p.intactRoads = 16;
                    p.lineOfSight = false; p.navMinDist = 20; p.navMaxDist = 80; p.windMean = 1.0f; p.windGust = 1.5f; break;
            }
            return p;
        }
    }

    /// Global simulation constants shared by all modules.
    public static class Sim
    {
        public const float CruiseAltitude = 5f;
        public const float DroneRadius = 0.3f;
        public const float MaxHorizontalSpeed = 3f;
        public const float MaxVerticalSpeed = 1f;
        public const float MaxYawRate = 90f; // deg/s
        public const float ControlDt = 0.1f; // policy decision period (s)
        public const float PhysicsDt = 0.02f; // flight-controller integration step (s)
        public const float MinLandablePatch = 1.6f; // drone footprint (0.6 m) + clearance on each side
    }
}
