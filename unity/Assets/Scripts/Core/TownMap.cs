using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Segmentation classes (vision labels and shared-map landable channel).
    public static class SegClass
    {
        public const byte Unknown = 0, Landable = 1, NotLandable = 2, Obstacle = 3;
        public const int Count = 4;
    }

    public enum WallMaterial : byte { None = 0, Concrete = 1, Brick = 2, Rubble = 3 }

    /// Ground surface types. The first group is landable, the rest are look-alikes or obstacles.
    public enum GroundType : byte
    {
        Street = 0,     // cracked asphalt: not landable
        Plaza = 1, IntactRoad = 2, FlatConcrete = 3,           // landable
        Debris = 4, Puddle = 5, Hole = 6, Vehicle = 7, Slope = 8, // look-alikes (not landable)
    }

    public static class GroundTypes
    {
        public static bool IsLandable(GroundType g) => g == GroundType.Plaza || g == GroundType.IntactRoad || g == GroundType.FlatConcrete;
    }

    /// Axis-aligned ground feature (landable patch or look-alike) in map-local metres.
    public struct GroundFeature
    {
        public GroundType type;
        public Vector2 center; // map-local x,z (metres)
        public Vector2 size;   // full extent x,z
        public float rotation; // degrees (visual only for look-alikes; landable patches are axis-aligned)
        public bool Contains(Vector2 p) => Mathf.Abs(p.x - center.x) <= size.x * 0.5f && Mathf.Abs(p.y - center.y) <= size.y * 0.5f;
        /// Distance from p to the nearest patch edge (positive inside).
        public float EdgeClearance(Vector2 p) => Mathf.Min(size.x * 0.5f - Mathf.Abs(p.x - center.x), size.y * 0.5f - Mathf.Abs(p.y - center.y));
    }

    /// Ground-truth town: occupancy grid, wall materials, ground classes, features, base and target.
    /// Coordinates: map-local metres (x, z) with cell (i, j) covering [i, i+1) x [j, j+1) * cellSize.
    /// World position = origin + (x, alt, z).
    public class TownMap
    {
        public int W, H;
        public float cellSize = 1f;
        public Vector3 origin;
        public byte[] occ;        // 1 = obstacle (building or rubble), taller than cruise altitude
        public byte[] material;   // WallMaterial per obstacle cell
        public float[] height;    // obstacle height (m)
        public byte[] ground;     // GroundType per free cell (rasterised from features)
        public List<GroundFeature> features = new List<GroundFeature>();
        public Vector2Int baseCell, targetCell;
        public int seed;
        public CurriculumParams curriculum;

        public TownMap(int w, int h)
        {
            W = w; H = h;
            occ = new byte[w * h]; material = new byte[w * h]; height = new float[w * h]; ground = new byte[w * h];
        }

        public int Idx(int i, int j) => j * W + i;
        public bool InBounds(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
        public bool Blocked(int i, int j) => !InBounds(i, j) || occ[Idx(i, j)] != 0;
        public Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize));
        public Vector2 CellCenter(int i, int j) => new Vector2((i + 0.5f) * cellSize, (j + 0.5f) * cellSize);
        public Vector2 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);
        public Vector2 ToLocal(Vector3 world) => new Vector2(world.x - origin.x, world.z - origin.z);
        public Vector3 ToWorld(Vector2 local, float alt) => origin + new Vector3(local.x, alt, local.y);
        public Vector2 BasePos => CellCenter(baseCell);
        public Vector2 TargetPos => CellCenter(targetCell);

        public bool BlockedAt(Vector2 p) { var c = CellOf(p); return Blocked(c.x, c.y); }

        /// True if a disc of the given radius at p touches an obstacle or the map boundary.
        public bool Collides(Vector2 p, float radius)
        {
            if (p.x < radius || p.y < radius || p.x > W * cellSize - radius || p.y > H * cellSize - radius) return true;
            int i0 = Mathf.FloorToInt((p.x - radius) / cellSize), i1 = Mathf.FloorToInt((p.x + radius) / cellSize);
            int j0 = Mathf.FloorToInt((p.y - radius) / cellSize), j1 = Mathf.FloorToInt((p.y + radius) / cellSize);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    if (!Blocked(i, j)) continue;
                    // closest point of cell to p
                    float cx = Mathf.Clamp(p.x, i * cellSize, (i + 1) * cellSize);
                    float cy = Mathf.Clamp(p.y, j * cellSize, (j + 1) * cellSize);
                    if ((cx - p.x) * (cx - p.x) + (cy - p.y) * (cy - p.y) < radius * radius) return true;
                }
            return false;
        }

        /// Exact grid ray cast (Amanatides-Woo DDA). Returns distance to the first obstacle cell or map edge, capped at maxDist.
        public float Raycast(Vector2 p, Vector2 dir, float maxDist)
        {
            float inv = 1f / cellSize;
            float x = p.x * inv, y = p.y * inv;
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(y);
            if (Blocked(i, j)) return 0f;
            int si = dir.x > 0 ? 1 : -1, sj = dir.y > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dir.x) < 1e-9f ? float.MaxValue : Mathf.Abs(1f / dir.x);
            float tdy = Mathf.Abs(dir.y) < 1e-9f ? float.MaxValue : Mathf.Abs(1f / dir.y);
            float tmx = Mathf.Abs(dir.x) < 1e-9f ? float.MaxValue : ((dir.x > 0 ? (i + 1 - x) : (x - i)) * tdx);
            float tmy = Mathf.Abs(dir.y) < 1e-9f ? float.MaxValue : ((dir.y > 0 ? (j + 1 - y) : (y - j)) * tdy);
            float maxT = maxDist * inv;
            while (true)
            {
                float t;
                if (tmx < tmy) { t = tmx; tmx += tdx; i += si; }
                else { t = tmy; tmy += tdy; j += sj; }
                if (t >= maxT) return maxDist;
                if (Blocked(i, j)) return t * cellSize;
            }
        }

        /// Walks the segment a→b over the grid and calls onRun(material) once per contiguous obstacle run it crosses.
        public void ForEachObstacleRun(Vector2 a, Vector2 b, System.Action<WallMaterial> onRun)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-6f) return;
            Vector2 dir = d / len;
            float inv = 1f / cellSize;
            float x = a.x * inv, y = a.y * inv;
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(y);
            int ei = Mathf.FloorToInt(b.x * inv), ej = Mathf.FloorToInt(b.y * inv);
            int si = dir.x > 0 ? 1 : -1, sj = dir.y > 0 ? 1 : -1;
            float tdx = Mathf.Abs(dir.x) < 1e-9f ? float.MaxValue : Mathf.Abs(1f / dir.x);
            float tdy = Mathf.Abs(dir.y) < 1e-9f ? float.MaxValue : Mathf.Abs(1f / dir.y);
            float tmx = Mathf.Abs(dir.x) < 1e-9f ? float.MaxValue : ((dir.x > 0 ? (i + 1 - x) : (x - i)) * tdx);
            float tmy = Mathf.Abs(dir.y) < 1e-9f ? float.MaxValue : ((dir.y > 0 ? (j + 1 - y) : (y - j)) * tdy);
            bool inside = InBounds(i, j) && occ[Idx(i, j)] != 0;
            if (inside) onRun((WallMaterial)material[Idx(i, j)]);
            float maxT = len * inv;
            int guard = 4 * (W + H);
            while ((i != ei || j != ej) && guard-- > 0)
            {
                float t;
                if (tmx < tmy) { t = tmx; tmx += tdx; i += si; } else { t = tmy; tmy += tdy; j += sj; }
                if (t > maxT) break;
                bool o = InBounds(i, j) && occ[Idx(i, j)] != 0;
                if (o && !inside) onRun((WallMaterial)material[Idx(i, j)]);
                inside = o;
            }
        }

        /// Ground-truth segmentation class of a map-local ground point (no 3D objects considered except obstacles).
        public byte GroundClassAt(Vector2 p)
        {
            var c = CellOf(p);
            if (!InBounds(c.x, c.y)) return SegClass.Unknown;
            if (occ[Idx(c.x, c.y)] != 0) return SegClass.Obstacle;
            for (int k = features.Count - 1; k >= 0; k--)
                if (features[k].Contains(p))
                    return GroundTypes.IsLandable(features[k].type) ? SegClass.Landable : SegClass.NotLandable;
            return SegClass.NotLandable;
        }

        /// Ground-truth landable patch containing p (index into features) or -1.
        public int PatchAt(Vector2 p)
        {
            for (int k = features.Count - 1; k >= 0; k--)
                if (features[k].Contains(p))
                    return GroundTypes.IsLandable(features[k].type) ? k : -1;
            return -1;
        }
    }
}
