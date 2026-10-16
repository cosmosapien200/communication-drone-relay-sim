using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Shortest-path distance field to a goal cell over the occupancy grid (SPEC Section 11: PathField).
    /// 8-connected Dijkstra with octile step costs and no corner cutting; distances in metres.
    public class PathField
    {
        public readonly float[] dist;
        public readonly Vector2Int goal;
        readonly TownMap map;
        public const float Unreachable = float.PositiveInfinity;

        static readonly int[] DI = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DJ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// blocked: optional override (e.g. a shared-map "known obstacle or unexplored" grid). Defaults to map occupancy.
        public PathField(TownMap map, Vector2Int goal, System.Func<int, int, bool> blocked = null)
        {
            this.map = map; this.goal = goal;
            if (blocked == null) blocked = map.Blocked;
            else { var user = blocked; blocked = (i, j) => !map.InBounds(i, j) || user(i, j); }
            int n = map.W * map.H;
            dist = new float[n];
            for (int k = 0; k < n; k++) dist[k] = Unreachable;
            if (blocked(goal.x, goal.y)) return;
            var heap = new MinHeap(n);
            int g = map.Idx(goal.x, goal.y);
            dist[g] = 0; heap.Push(g, 0);
            float cs = map.cellSize, diag = cs * 1.41421356f;
            while (heap.Count > 0)
            {
                heap.Pop(out int k, out float dk);
                if (dk > dist[k]) continue;
                int i = k % map.W, j = k / map.W;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + DI[d], nj = j + DJ[d];
                    if (blocked(ni, nj)) continue;
                    if (d >= 4 && (blocked(i + DI[d], j) || blocked(i, j + DJ[d]))) continue; // no corner cutting
                    float nd = dk + (d < 4 ? cs : diag);
                    int nk = map.Idx(ni, nj);
                    if (nd < dist[nk]) { dist[nk] = nd; heap.Push(nk, nd); }
                }
            }
        }

        public float AtCell(int i, int j) => map.InBounds(i, j) ? dist[map.Idx(i, j)] : Unreachable;

        /// Continuous distance at a map-local point: min over the 3x3 neighbourhood of (cell distance + straight distance to that cell centre).
        public float At(Vector2 p)
        {
            var c = map.CellOf(p);
            float best = Unreachable;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    float d = AtCell(c.x + di, c.y + dj);
                    if (float.IsInfinity(d)) continue;
                    float v = d + (map.CellCenter(c.x + di, c.y + dj) - p).magnitude;
                    if (v < best) best = v;
                }
            return best;
        }

        /// Steepest-descent direction (unit vector) toward the goal from p, used by the A* baseline follower.
        public Vector2 Descent(Vector2 p)
        {
            var c = map.CellOf(p);
            float best = AtCell(c.x, c.y); Vector2Int bc = c;
            for (int d = 0; d < 8; d++)
            {
                float v = AtCell(c.x + DI[d], c.y + DJ[d]);
                if (d >= 4 && (map.Blocked(c.x + DI[d], c.y) || map.Blocked(c.x, c.y + DJ[d]))) continue;
                if (v < best) { best = v; bc = new Vector2Int(c.x + DI[d], c.y + DJ[d]); }
            }
            var dir = map.CellCenter(bc) - p;
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.zero;
        }

        /// Cell path from start to goal following the field (empty if unreachable).
        public List<Vector2Int> PathFrom(Vector2Int start)
        {
            var path = new List<Vector2Int>();
            if (float.IsInfinity(AtCell(start.x, start.y))) return path;
            var c = start;
            path.Add(c);
            int guard = map.W * map.H;
            while (c != goal && guard-- > 0)
            {
                float best = AtCell(c.x, c.y); var bc = c;
                for (int d = 0; d < 8; d++)
                {
                    int ni = c.x + DI[d], nj = c.y + DJ[d];
                    if (d >= 4 && (map.Blocked(c.x + DI[d], c.y) || map.Blocked(c.x, c.y + DJ[d]))) continue;
                    float v = AtCell(ni, nj);
                    if (v < best) { best = v; bc = new Vector2Int(ni, nj); }
                }
                if (bc == c) break;
                c = bc; path.Add(c);
            }
            return path;
        }

        class MinHeap
        {
            int[] keys; float[] pri; public int Count;
            public MinHeap(int cap) { keys = new int[cap * 2 + 16]; pri = new float[cap * 2 + 16]; }
            public void Push(int k, float p)
            {
                if (Count == keys.Length) { System.Array.Resize(ref keys, Count * 2); System.Array.Resize(ref pri, Count * 2); }
                int i = Count++;
                while (i > 0) { int par = (i - 1) >> 1; if (pri[par] <= p) break; keys[i] = keys[par]; pri[i] = pri[par]; i = par; }
                keys[i] = k; pri[i] = p;
            }
            public void Pop(out int k, out float p)
            {
                k = keys[0]; p = pri[0];
                int lk = keys[--Count]; float lp = pri[Count];
                int i = 0;
                while (true)
                {
                    int c = 2 * i + 1; if (c >= Count) break;
                    if (c + 1 < Count && pri[c + 1] < pri[c]) c++;
                    if (pri[c] >= lp) break;
                    keys[i] = keys[c]; pri[i] = pri[c]; i = c;
                }
                keys[i] = lk; pri[i] = lp;
            }
        }
    }
}
