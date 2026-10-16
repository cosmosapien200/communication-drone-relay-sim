using UnityEngine;

namespace CommDrone
{
    /// Navigation baselines (SPEC Section 8). Each returns (forward, sideways, yawRate) in [-1, 1].
    public static class NavBaselines
    {
        /// Lower bound: fly straight at the waypoint, ignore obstacles.
        public static Vector3 StraightLine(NavAgent a) => SteerTo(a, a.Waypoint, 1f);

        /// Reactive potential field using the same ray sensors as the policy.
        public static Vector3 PotentialField(NavAgent a)
        {
            var wb = a.WaypointBody();
            float br = wb.y * Mathf.Deg2Rad;
            // body frame: x = right, y = forward
            Vector2 attract = new Vector2(Mathf.Sin(br), Mathf.Cos(br)) * Mathf.Min(1f, wb.x / 3f);
            Vector2 repulse = Vector2.zero;
            var rays = a.CastRays();
            const float influence = 3f;
            for (int k = 0; k < NavAgent.NumRays; k++)
            {
                if (rays[k] >= influence) continue;
                float ang = k * 2f * Mathf.PI / NavAgent.NumRays;
                var dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                float w = (1f / Mathf.Max(rays[k], 0.2f) - 1f / influence);
                repulse -= dir * w * 0.6f;
            }
            Vector2 v = attract + repulse;
            if (v.magnitude > 1f) v.Normalize();
            float yawCmd = Mathf.Clamp(wb.y / 45f, -1f, 1f);
            return new Vector3(v.y, v.x, yawCmd);
        }

        /// Upper bound: full-map shortest path planned on an obstacle-inflated grid, followed with a lookahead carrot.
        static readonly System.Collections.Generic.Dictionary<NavAgent, (TownMap map, Vector2 wp, PathField field)> plans = new();

        public static Vector3 AStar(NavAgent a)
        {
            if (!plans.TryGetValue(a, out var plan) || plan.map != a.Map || plan.wp != a.Waypoint)
            {
                var m = a.Map;
                // inflate: cells within 1 cell of an obstacle are avoided when an inflated path exists
                System.Func<int, int, bool> inflated = (i, j) =>
                {
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if (m.Blocked(i + di, j + dj)) return true;
                    return false;
                };
                var goal = m.CellOf(a.Waypoint);
                var f = new PathField(m, goal, (i, j) => m.Blocked(i, j) || (inflated(i, j) && !(Mathf.Abs(i - goal.x) <= 1 && Mathf.Abs(j - goal.y) <= 1)));
                if (float.IsInfinity(f.AtCell(m.CellOf(a.Pos2).x, m.CellOf(a.Pos2).y))) f = a.Field;
                plan = (m, a.Waypoint, f);
                plans[a] = plan;
            }
            var field = plan.field;
            var path = field.PathFrom(a.Map.CellOf(a.Pos2));
            Vector2 carrot = a.Waypoint;
            if (path.Count > 2)
            {
                carrot = a.Map.CellCenter(path[1]);
                // furthest path cell within ~4 cells reachable by a straight, collision-free sweep
                for (int k = Mathf.Min(path.Count - 1, 4); k >= 1; k--)
                {
                    var c = a.Map.CellCenter(path[k]);
                    if (SweepClear(a.Map, a.Pos2, c, Sim.DroneRadius + 0.15f)) { carrot = c; break; }
                }
            }
            float speed = (a.Waypoint - a.Pos2).magnitude < 3f ? 0.5f : 0.8f;
            var cmd = SteerTo(a, carrot, speed);
            // reactive safety layer (gust rejection near walls): same ray repulsion as the potential field
            var rep = Repulsion(a, 1.5f, 0.5f);
            var v = new Vector2(cmd.y, cmd.x) + rep;
            if (v.magnitude > 1f) v.Normalize();
            return new Vector3(v.y, v.x, cmd.z);
        }

        /// Body-frame (x right, y forward) repulsion from ray hits closer than `influence` metres.
        public static Vector2 Repulsion(NavAgent a, float influence, float gain)
        {
            Vector2 repulse = Vector2.zero;
            var rays = a.CastRays();
            for (int k = 0; k < NavAgent.NumRays; k++)
            {
                if (rays[k] >= influence) continue;
                float ang = k * 2f * Mathf.PI / NavAgent.NumRays;
                repulse -= new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * (1f / Mathf.Max(rays[k], 0.2f) - 1f / influence) * gain;
            }
            return repulse;
        }

        static bool SweepClear(TownMap m, Vector2 from, Vector2 to, float radius)
        {
            float len = (to - from).magnitude;
            int n = Mathf.CeilToInt(len / 0.25f);
            for (int i = 1; i <= n; i++) if (m.Collides(Vector2.Lerp(from, to, i / (float)n), radius)) return false;
            return true;
        }

        /// Translate toward a point in the body frame while turning to face it.
        public static Vector3 SteerTo(NavAgent a, Vector2 target, float speed)
        {
            var d = target - a.Pos2;
            float bearing = Mathf.DeltaAngle(a.FC.yaw, Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            var dirBody = new Vector2(Mathf.Sin(bearing), Mathf.Cos(bearing)) * speed;
            return new Vector3(dirBody.y, dirBody.x, Mathf.Clamp(bearing * Mathf.Rad2Deg / 45f, -1f, 1f));
        }
    }
}
