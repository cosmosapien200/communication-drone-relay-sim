using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Seeded procedural collapsed-town generator (SPEC Section 3). Pure data: produces a TownMap;
    /// TownBuilder turns it into scene objects when rendering is needed.
    public static class MapGenerator
    {
        struct Rect { public int x0, y0, x1, y1; public Rect(int a, int b, int c, int d) { x0 = a; y0 = b; x1 = c; y1 = d; } public int W => x1 - x0; public int H => y1 - y0; }

        public static TownMap Generate(int seed, CurriculumParams cp, bool withFeatures = true)
        {
            var rng = new System.Random(seed);
            int n = Mathf.Max(32, cp.mapSize);
            var m = new TownMap(n, n) { seed = seed, curriculum = cp };

            // 1. Street grid: 1D block intervals along x and y, edge streets on all sides.
            var xs = Blocks1D(rng, n);
            var ys = Blocks1D(rng, n);
            var buildings = new List<Rect>();

            // 2. Blocks -> plazas or buildings (with irregular insets and alleys).
            foreach (var bx in xs)
                foreach (var by in ys)
                {
                    var block = new Rect(bx.x + rng.Next(0, 2), by.x + rng.Next(0, 2), bx.y - rng.Next(0, 2), by.y - rng.Next(0, 2));
                    if (rng.NextDouble() < 0.12) continue; // open plaza
                    foreach (var r in SplitBlock(rng, block)) buildings.Add(r);
                }
            foreach (var b in buildings)
            {
                var mat = rng.NextDouble() < 0.6 ? WallMaterial.Concrete : WallMaterial.Brick;
                float h = 8f + (float)rng.NextDouble() * 12f;
                FillRect(m, b, mat, h);
            }

            // 3. Base at the left edge, target at the right edge.
            PlaceBaseTarget(m, rng, ys, cp.lineOfSight);

            // 4. Collapsed buildings spilling rubble into streets.
            foreach (var b in buildings)
            {
                if (rng.NextDouble() >= cp.collapseProb) continue;
                for (int j = b.y0; j < b.y1; j++) for (int i = b.x0; i < b.x1; i++) { int k = m.Idx(i, j); m.material[k] = (byte)WallMaterial.Rubble; m.height[k] = 6f + (float)rng.NextDouble() * 2f; }
                int side = rng.Next(4), depth = rng.Next(1, 5);
                int len = side < 2 ? b.H : b.W;
                int s0 = rng.Next(0, Mathf.Max(1, len / 2)), s1 = Mathf.Min(len, s0 + rng.Next(len / 2, len + 1));
                var spill = new List<Vector2Int>();
                for (int s = s0; s < s1; s++)
                    for (int d = 1; d <= depth; d++)
                    {
                        Vector2Int c = side switch
                        {
                            0 => new Vector2Int(b.x0 - d, b.y0 + s),
                            1 => new Vector2Int(b.x1 - 1 + d, b.y0 + s),
                            2 => new Vector2Int(b.x0 + s, b.y0 - d),
                            _ => new Vector2Int(b.x0 + s, b.y1 - 1 + d),
                        };
                        spill.Add(c);
                    }
                TryPlaceRubble(m, spill, rng);
            }

            // 5. Rubble barriers closing streets (dead ends and detours), keeping base->target connected.
            int barriers = Mathf.RoundToInt(cp.rubbleDensity * xs.Count * ys.Count * 2);
            for (int attempt = 0, placed = 0; placed < barriers && attempt < barriers * 20; attempt++)
            {
                int i = rng.Next(1, n - 1), j = rng.Next(1, n - 1);
                if (m.Blocked(i, j)) continue;
                var c = new Vector2Int(i, j);
                if ((c - m.baseCell).sqrMagnitude < 36 || (c - m.targetCell).sqrMagnitude < 36) continue;
                int runX = Run(m, i, j, 1, 0), runY = Run(m, i, j, 0, 1);
                bool streetAlongX = runX > runY; // barrier goes across the street, i.e. along the shorter run
                int across = streetAlongX ? runY : runX;
                if (across > 9 || across < 2) continue; // not a plain street (plaza or intersection)
                var cells = new List<Vector2Int>();
                int thick = rng.Next(2, 4);
                for (int t = 0; t < thick; t++)
                    for (int s = -12; s <= 12; s++)
                    {
                        var q = streetAlongX ? new Vector2Int(i + t, j + s) : new Vector2Int(i + s, j + t);
                        if (!m.Blocked(q.x, q.y) && ContiguousAcross(m, c, q, streetAlongX)) cells.Add(q);
                    }
                if (TryPlaceRubble(m, cells, rng)) placed++;
            }

            // 6. Free cells unreachable from base become rubble (closed courtyards); keeps every free cell navigable.
            var reach = Reachable(m, m.baseCell);
            for (int k = 0; k < n * n; k++)
                if (m.occ[k] == 0 && !reach[k]) { m.occ[k] = 1; m.material[k] = (byte)WallMaterial.Rubble; m.height[k] = 6f; }

            // 7. Ground features: landable patches and non-landable look-alikes.
            if (withFeatures) PlaceFeatures(m, rng, cp);
            RasterizeGround(m);
            return m;
        }

        static List<Vector2Int> Blocks1D(System.Random rng, int n)
        {
            var res = new List<Vector2Int>();
            int x = rng.Next(4, 7);
            while (true)
            {
                int bw = rng.Next(8, 19);
                if (x + bw > n - 4) break;
                res.Add(new Vector2Int(x, x + bw));
                x += bw + rng.Next(3, 8);
            }
            return res;
        }

        static IEnumerable<Rect> SplitBlock(System.Random rng, Rect b)
        {
            if (b.W < 4 || b.H < 4) { yield return b; yield break; }
            bool alongX = b.W >= b.H;
            int len = alongX ? b.W : b.H;
            if (len >= 10 && rng.NextDouble() < 0.45)
            {
                int cut = rng.Next(len / 3, 2 * len / 3), gap = rng.Next(2, 4);
                if (alongX) { yield return new Rect(b.x0, b.y0, b.x0 + cut, b.y1); yield return new Rect(b.x0 + cut + gap, b.y0, b.x1, b.y1); }
                else { yield return new Rect(b.x0, b.y0, b.x1, b.y0 + cut); yield return new Rect(b.x0, b.y0 + cut + gap, b.x1, b.y1); }
            }
            else yield return b;
        }

        static void FillRect(TownMap m, Rect r, WallMaterial mat, float h)
        {
            for (int j = Mathf.Max(0, r.y0); j < Mathf.Min(m.H, r.y1); j++)
                for (int i = Mathf.Max(0, r.x0); i < Mathf.Min(m.W, r.x1); i++)
                { int k = m.Idx(i, j); m.occ[k] = 1; m.material[k] = (byte)mat; m.height[k] = h; }
        }

        static int Run(TownMap m, int i, int j, int di, int dj)
        {
            int a = 0, b = 0;
            while (!m.Blocked(i - (a + 1) * di, j - (a + 1) * dj) && a < 40) a++;
            while (!m.Blocked(i + (b + 1) * di, j + (b + 1) * dj) && b < 40) b++;
            return a + b + 1;
        }

        static bool ContiguousAcross(TownMap m, Vector2Int c, Vector2Int q, bool streetAlongX)
        {
            // q must be reachable from c's row/column across the street without crossing obstacles
            if (streetAlongX)
            {
                int step = q.y > c.y ? 1 : -1;
                for (int y = c.y; y != q.y; y += step) if (m.Blocked(q.x, y)) return false;
            }
            else
            {
                int step = q.x > c.x ? 1 : -1;
                for (int x = c.x; x != q.x; x += step) if (m.Blocked(x, q.y)) return false;
            }
            return true;
        }

        static bool TryPlaceRubble(TownMap m, List<Vector2Int> cells, System.Random rng)
        {
            var placed = new List<int>();
            foreach (var c in cells)
            {
                if (!m.InBounds(c.x, c.y)) continue;
                if (Mathf.Abs(c.x - m.baseCell.x) + Mathf.Abs(c.y - m.baseCell.y) < 4) continue;
                if (Mathf.Abs(c.x - m.targetCell.x) + Mathf.Abs(c.y - m.targetCell.y) < 4) continue;
                int k = m.Idx(c.x, c.y);
                if (m.occ[k] != 0) continue;
                m.occ[k] = 1; m.material[k] = (byte)WallMaterial.Rubble; m.height[k] = 6f + (float)rng.NextDouble() * 2f;
                placed.Add(k);
            }
            if (placed.Count == 0) return false;
            if (Reachable(m, m.baseCell)[m.Idx(m.targetCell.x, m.targetCell.y)]) return true;
            foreach (var k in placed) { m.occ[k] = 0; m.material[k] = 0; m.height[k] = 0; }
            return false;
        }

        static void PlaceBaseTarget(TownMap m, System.Random rng, List<Vector2Int> ys, bool los)
        {
            int n = m.W;
            // horizontal streets: gaps between y-blocks (including edges)
            var rows = new List<int>();
            for (int j = 1; j < n - 1; j++)
            {
                bool free = true;
                for (int i = 0; i < n && free; i++) free = !m.Blocked(i, j);
                if (free && !m.Blocked(0, j - 1) && !m.Blocked(0, j + 1)) rows.Add(j);
            }
            if (los && rows.Count > 0)
            {
                int j = rows[rng.Next(rows.Count)];
                m.baseCell = new Vector2Int(1, j);
                m.targetCell = new Vector2Int(n - 2, j);
                return;
            }
            // usually without line of sight: prefer a pair whose segment crosses buildings
            Vector2Int best = default, bestT = default; int bestWalls = -1;
            for (int t = 0; t < 30; t++)
            {
                var b = new Vector2Int(1, rng.Next(2, n - 2));
                var g = new Vector2Int(n - 2, rng.Next(2, n - 2));
                if (m.Blocked(b.x, b.y) || m.Blocked(g.x, g.y)) continue;
                int walls = 0;
                m.ForEachObstacleRun(m.CellCenter(b), m.CellCenter(g), _ => walls++);
                if (walls > bestWalls) { bestWalls = walls; best = b; bestT = g; }
            }
            if (bestWalls < 0) { best = new Vector2Int(1, 1); bestT = new Vector2Int(n - 2, n - 2); }
            m.baseCell = best; m.targetCell = bestT;
        }

        public static bool[] Reachable(TownMap m, Vector2Int start)
        {
            var seen = new bool[m.W * m.H];
            if (m.Blocked(start.x, start.y)) return seen;
            var q = new Queue<int>();
            q.Enqueue(m.Idx(start.x, start.y)); seen[m.Idx(start.x, start.y)] = true;
            while (q.Count > 0)
            {
                int k = q.Dequeue(), i = k % m.W, j = k / m.W;
                Visit(i + 1, j); Visit(i - 1, j); Visit(i, j + 1); Visit(i, j - 1);
            }
            return seen;

            void Visit(int i, int j)
            {
                if (m.Blocked(i, j)) return;
                int k = m.Idx(i, j);
                if (seen[k]) return;
                seen[k] = true; q.Enqueue(k);
            }
        }

        static void PlaceFeatures(TownMap m, System.Random rng, CurriculumParams cp)
        {
            float areaScale = m.W * m.H / 10000f;
            int nLand = Mathf.RoundToInt(cp.landablePatches * areaScale);
            int nFake = Mathf.RoundToInt(cp.lookAlikes * areaScale);
            var landTypes = new[] { GroundType.Plaza, GroundType.IntactRoad, GroundType.FlatConcrete };
            var fakeTypes = new[] { GroundType.Debris, GroundType.Puddle, GroundType.Hole, GroundType.Vehicle, GroundType.Slope };
            // intact-road strips: street-aligned landable stretches (the street surface survived)
            int nRoads = Mathf.RoundToInt(cp.intactRoads * areaScale);
            for (int placed = 0, attempt = 0; placed < nRoads && attempt < nRoads * 60; attempt++)
            {
                int i = rng.Next(2, m.W - 2), j = rng.Next(2, m.H - 2);
                if (m.Blocked(i, j)) continue;
                int rx = Run(m, i, j, 1, 0), ry = Run(m, i, j, 0, 1);
                bool alongX = rx > ry;
                int across = alongX ? ry : rx;
                if (across < 3 || across > 9) continue; // a plain street, not a plaza
                // centre the strip across the street
                int lo = 0; while (!m.Blocked(alongX ? i : i - lo - 1, alongX ? j - lo - 1 : j) && lo < 10) lo++;
                float centreAcross = (alongX ? j : i) - lo + across * 0.5f;
                float len = Lerp(6f, 15f), wid = Mathf.Max(1.8f, across - 1.2f);
                var c = alongX ? new Vector2(i + 0.5f, centreAcross) : new Vector2(centreAcross, j + 0.5f);
                var f = new GroundFeature { type = GroundType.IntactRoad, center = c, size = alongX ? new Vector2(len, wid) : new Vector2(wid, len) };
                if ((c - m.BasePos).magnitude < 4f || (c - m.TargetPos).magnitude < 4f || !Fits(m, f)) continue;
                m.features.Add(f); placed++;
            }
            // half of the landable patches sit at street intersections (crossroads, wide junctions), the rest anywhere
            var junctions = new List<Vector2>();
            for (int j = 2; j < m.H - 2; j += 2)
                for (int i = 2; i < m.W - 2; i += 2)
                    if (!m.Blocked(i, j) && Run(m, i, j, 1, 0) >= 12 && Run(m, i, j, 0, 1) >= 12) junctions.Add(m.CellCenter(i, j));
            PlaceAt(nLand / 2, landTypes, 1.8f, 5f, junctions);
            Place(nLand - nLand / 2, landTypes, 1.8f, 5f);
            Place(nFake, fakeTypes, 1.5f, 4.5f);

            void PlaceAt(int count, GroundType[] types, float smin, float smax, List<Vector2> sites)
            {
                if (sites.Count == 0) { Place(count, types, smin, smax); return; }
                for (int placed = 0, attempt = 0; placed < count && attempt < count * 40; attempt++)
                {
                    var t = types[rng.Next(types.Length)];
                    var size = new Vector2(Lerp(smin, smax), Lerp(smin, smax));
                    var c = sites[rng.Next(sites.Count)] + new Vector2(Lerp(-2f, 2f), Lerp(-2f, 2f));
                    if ((c - m.BasePos).magnitude < 4f || (c - m.TargetPos).magnitude < 4f) continue;
                    var f = new GroundFeature { type = t, center = c, size = size, rotation = 0 };
                    if (!Fits(m, f)) continue;
                    m.features.Add(f); placed++;
                }
            }

            void Place(int count, GroundType[] types, float smin, float smax)
            {
                for (int placed = 0, attempt = 0; placed < count && attempt < count * 40; attempt++)
                {
                    var t = types[rng.Next(types.Length)];
                    var size = new Vector2(Lerp(smin, smax), Lerp(smin, smax));
                    if (t == GroundType.Vehicle) size = rng.NextDouble() < 0.5 ? new Vector2(2f, 4.2f) : new Vector2(4.2f, 2f);
                    var c = new Vector2(Lerp(1, m.W - 1), Lerp(1, m.H - 1));
                    if ((c - m.BasePos).magnitude < 4f || (c - m.TargetPos).magnitude < 4f) continue;
                    var f = new GroundFeature { type = t, center = c, size = size, rotation = 0 };
                    if (!Fits(m, f)) continue;
                    m.features.Add(f); placed++;
                }
            }
            float Lerp(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        }

        static bool Fits(TownMap m, GroundFeature f)
        {
            float hx = f.size.x * 0.5f + 0.3f, hy = f.size.y * 0.5f + 0.3f;
            for (float y = f.center.y - hy; y <= f.center.y + hy + 1e-3f; y += 0.5f)
                for (float x = f.center.x - hx; x <= f.center.x + hx + 1e-3f; x += 0.5f)
                    if (m.BlockedAt(new Vector2(x, y))) return false;
            foreach (var o in m.features)
                if (Mathf.Abs(o.center.x - f.center.x) < (o.size.x + f.size.x) * 0.5f + 0.6f &&
                    Mathf.Abs(o.center.y - f.center.y) < (o.size.y + f.size.y) * 0.5f + 0.6f) return false;
            return true;
        }

        public static void RasterizeGround(TownMap m)
        {
            for (int j = 0; j < m.H; j++)
                for (int i = 0; i < m.W; i++)
                {
                    var p = m.CellCenter(i, j);
                    byte g = (byte)GroundType.Street;
                    for (int k = m.features.Count - 1; k >= 0; k--)
                        if (m.features[k].Contains(p)) { g = (byte)m.features[k].type; break; }
                    m.ground[m.Idx(i, j)] = g;
                }
        }
    }
}
