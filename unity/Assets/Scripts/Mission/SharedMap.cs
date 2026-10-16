using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Shared map held at the base station (SPEC Section 7): explored, obstacle, landable evidence and
    /// measured RSSI to the current parent, plus the relay node list. Drones download a copy before launch
    /// and upload (merge) it after landing.
    public class SharedMap
    {
        public readonly int W, H;
        public readonly float cellSize;
        public readonly Vector3 origin;
        public bool[] explored, obstacle;
        public float[] landYes, landNo;      // accumulated confidence-weighted landable / not-landable evidence
        public float[] rssiSum; public int[] rssiCount; // measured RSSI to the current parent (cleared per mission)
        public List<Vector3> nodes = new List<Vector3>(); // world positions: base station, then landed relays
        public Vector2 targetPos;            // map-local camp position (known a priori)

        public const float LandableMinEvidence = 3f, LandableRatio = 0.7f;

        public SharedMap(TownMap truth)
        {
            W = truth.W; H = truth.H; cellSize = truth.cellSize; origin = truth.origin;
            int n = W * H;
            explored = new bool[n]; obstacle = new bool[n];
            landYes = new float[n]; landNo = new float[n];
            rssiSum = new float[n]; rssiCount = new int[n];
            targetPos = truth.TargetPos;
        }

        public SharedMap Clone()
        {
            var c = (SharedMap)MemberwiseClone();
            c.explored = (bool[])explored.Clone(); c.obstacle = (bool[])obstacle.Clone();
            c.landYes = (float[])landYes.Clone(); c.landNo = (float[])landNo.Clone();
            c.rssiSum = (float[])rssiSum.Clone(); c.rssiCount = (int[])rssiCount.Clone();
            c.nodes = new List<Vector3>(nodes); // MemberwiseClone shares the list reference
            return c;
        }

        /// A copy with only the node list (the "no shared map" ablation).
        public SharedMap NodesOnly()
        {
            var c = Clone();
            System.Array.Clear(c.explored, 0, c.explored.Length); System.Array.Clear(c.obstacle, 0, c.obstacle.Length);
            System.Array.Clear(c.landYes, 0, c.landYes.Length); System.Array.Clear(c.landNo, 0, c.landNo.Length);
            System.Array.Clear(c.rssiSum, 0, c.rssiSum.Length); System.Array.Clear(c.rssiCount, 0, c.rssiCount.Length);
            return c;
        }

        /// Upload: merge a drone's map into this one (evidence adds, explored/obstacle union).
        public void Merge(SharedMap o, SharedMap baseline)
        {
            for (int k = 0; k < explored.Length; k++)
            {
                explored[k] |= o.explored[k]; obstacle[k] |= o.obstacle[k];
                // only add the evidence the drone gathered itself (its copy started from `baseline`)
                landYes[k] += o.landYes[k] - (baseline?.landYes[k] ?? 0f);
                landNo[k] += o.landNo[k] - (baseline?.landNo[k] ?? 0f);
            }
            foreach (var nd in o.nodes) if (!nodes.Contains(nd)) nodes.Add(nd);
        }

        public int Idx(int i, int j) => j * W + i;
        public bool InBounds(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
        public Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
        public Vector2 CellCenter(int i, int j) => new Vector2((i + 0.5f) * cellSize, (j + 0.5f) * cellSize);

        public bool IsLandable(int k) => landYes[k] >= LandableMinEvidence && landYes[k] / Mathf.Max(1e-3f, landYes[k] + landNo[k]) >= LandableRatio;
        public float LandableConfidence(int k) => landYes[k] / Mathf.Max(1e-3f, landYes[k] + landNo[k]);

        public void ClearRssi() { System.Array.Clear(rssiSum, 0, rssiSum.Length); System.Array.Clear(rssiCount, 0, rssiCount.Length); }

        public void AddRssi(Vector2 p, float rssi)
        {
            var c = CellOf(p);
            if (!InBounds(c.x, c.y)) return;
            int k = Idx(c.x, c.y);
            rssiSum[k] += rssi; rssiCount[k]++;
        }

        public bool MeasuredRssi(int i, int j, out float rssi)
        {
            rssi = 0;
            // nearest measurement within 1 cell
            float best = float.MaxValue; bool found = false;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int x = i + di, y = j + dj;
                    if (!InBounds(x, y)) continue;
                    int k = Idx(x, y);
                    if (rssiCount[k] == 0) continue;
                    float d = di * di + dj * dj;
                    if (d < best) { best = d; rssi = rssiSum[k] / rssiCount[k]; found = true; }
                }
            return found;
        }

        /// Mark cells along a range-sensor ray as explored free, and the hit cell as obstacle.
        public void AddRay(Vector2 from, Vector2 dir, float hitDist, float maxRange)
        {
            float step = cellSize * 0.5f;
            for (float t = 0; t < hitDist; t += step)
            {
                var c = CellOf(from + dir * t);
                if (!InBounds(c.x, c.y)) return;
                explored[Idx(c.x, c.y)] = true;
            }
            if (hitDist < maxRange - 1e-3f)
            {
                var c = CellOf(from + dir * (hitDist + 0.05f));
                if (InBounds(c.x, c.y)) { int k = Idx(c.x, c.y); obstacle[k] = true; explored[k] = true; }
            }
        }

        public void AddVision(List<CellObservation> obs)
        {
            foreach (var o in obs)
            {
                int k = Idx(o.cell.x, o.cell.y);
                explored[k] = true;
                if (o.cls == SegClass.Landable) landYes[k] += o.confidence;
                else if (o.cls == SegClass.NotLandable) landNo[k] += o.confidence;
            }
        }

        /// A TownMap view of current knowledge (known obstacles as concrete walls) for path fields and RSSI prediction.
        public TownMap KnownMap(TownMap truth, bool unknownBlocked)
        {
            var m = new TownMap(W, H) { origin = origin, cellSize = cellSize, baseCell = truth.baseCell, targetCell = truth.targetCell, seed = truth.seed };
            for (int k = 0; k < W * H; k++)
            {
                bool blocked = obstacle[k] || (unknownBlocked && !explored[k]);
                if (blocked) { m.occ[k] = 1; m.material[k] = (byte)(obstacle[k] ? WallMaterial.Concrete : WallMaterial.None); m.height[k] = 10f; }
            }
            return m;
        }
    }
}
