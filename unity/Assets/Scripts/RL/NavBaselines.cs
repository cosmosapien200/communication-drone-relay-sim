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

        /// Upper bound: full-map shortest path (Dijkstra/A* field) followed with a lookahead carrot.
        public static Vector3 AStar(NavAgent a)
        {
            var path = a.Field.PathFrom(a.Map.CellOf(a.Pos2));
            Vector2 carrot = a.Waypoint;
            if (path.Count > 3)
            {
                // furthest path cell within 3 m that is in line of sight
                for (int k = Mathf.Min(path.Count - 1, 4); k >= 1; k--)
                {
                    var c = a.Map.CellCenter(path[k]);
                    var d = c - a.Pos2;
                    if (a.Map.Raycast(a.Pos2, d.normalized, d.magnitude + 0.01f) >= d.magnitude) { carrot = c; break; }
                }
            }
            return SteerTo(a, carrot, (a.Waypoint - a.Pos2).magnitude < 3f ? 0.5f : 1f);
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
