using UnityEngine;

namespace CommDrone
{
    /// Hand-built demo town (SPEC Section 14) made from the same primitives as the generator: base station on the
    /// west edge, disaster camp in the north-east, the direct line blocked by a dense block of buildings and
    /// rubble barriers that force a zig-zag route. Never used in training (seed -1).
    public static class DemoLevel
    {
        public static TownMap Build()
        {
            const int N = 120;
            var m = new TownMap(N, N) { seed = -1, curriculum = CurriculumParams.ForLevel(4) };
            var rng = new System.Random(4242);
            // block grid: streets 5 m wide every 20 m
            for (int bx = 0; bx < 5; bx++)
                for (int by = 0; by < 5; by++)
                {
                    int x0 = 6 + bx * 23, y0 = 6 + by * 23;
                    if ((bx == 1 && by == 1) || (bx == 3 && by == 3)) continue; // two plazas
                    bool split = (bx + by) % 2 == 0;
                    var mat = (bx * 5 + by) % 3 == 0 ? WallMaterial.Brick : WallMaterial.Concrete;
                    if (split) { Fill(m, x0, y0, x0 + 8, y0 + 18, mat, 9 + bx * 2); Fill(m, x0 + 11, y0, x0 + 18, y0 + 18, mat, 12 + by * 2); }
                    else Fill(m, x0, y0, x0 + 18, y0 + 18, mat, 10 + (bx + by) * 1.5f);
                }
            // rubble barriers closing the obvious straight streets
            Fill(m, 24, 44, 29, 47, WallMaterial.Rubble, 7);   // east-west street at y~47 blocked
            Fill(m, 70, 24, 75, 29, WallMaterial.Rubble, 7);
            Fill(m, 93, 70, 98, 75, WallMaterial.Rubble, 7);
            Fill(m, 47, 93, 52, 98, WallMaterial.Rubble, 7);
            Fill(m, 1, 70, 6, 72, WallMaterial.Rubble, 7);
            // a collapsed building spilling into the street
            Fill(m, 52, 52, 64, 67, WallMaterial.Rubble, 7);
            Fill(m, 64, 58, 69, 62, WallMaterial.Rubble, 6.5f);
            m.baseCell = new Vector2Int(2, 26);
            m.targetCell = new Vector2Int(110, 92);
            // landable patches along plausible routes, plus look-alikes
            void P(GroundType t, float x, float y, float w, float h) => m.features.Add(new GroundFeature { type = t, center = new Vector2(x, y), size = new Vector2(w, h) });
            P(GroundType.Plaza, 36, 36, 6, 5); P(GroundType.FlatConcrete, 31, 34.5f, 2.5f, 2.5f);
            P(GroundType.IntactRoad, 50, 27, 3.5f, 2.2f); P(GroundType.FlatConcrete, 74, 31, 3, 3);
            P(GroundType.Plaza, 82, 82, 6, 6); P(GroundType.IntactRoad, 97, 54, 2.2f, 3.5f);
            P(GroundType.FlatConcrete, 101, 78, 3, 3); P(GroundType.IntactRoad, 59, 74, 3.5f, 2.2f);
            P(GroundType.FlatConcrete, 15, 30, 2.6f, 2.6f); P(GroundType.Plaza, 30, 75, 3, 3);
            P(GroundType.Debris, 44, 31, 3, 3); P(GroundType.Puddle, 62, 28, 3, 2.5f); P(GroundType.Vehicle, 77, 50, 2, 4.2f);
            P(GroundType.Hole, 90, 87, 2.5f, 2.5f); P(GroundType.Slope, 100, 60, 3, 3); P(GroundType.Debris, 79, 76, 2.5f, 2.5f);
            P(GroundType.Puddle, 20, 51, 3, 2); P(GroundType.Vehicle, 56, 50, 4.2f, 2f); P(GroundType.Hole, 104, 98, 2, 2);
            MapGenerator.RasterizeGround(m);
            return m;
        }

        static void Fill(TownMap m, int x0, int y0, int x1, int y1, WallMaterial mat, float h)
        {
            for (int j = Mathf.Max(0, y0); j < Mathf.Min(m.H, y1); j++)
                for (int i = Mathf.Max(0, x0); i < Mathf.Min(m.W, x1); i++)
                { int k = m.Idx(i, j); m.occ[k] = 1; m.material[k] = (byte)mat; m.height[k] = h; }
        }
    }
}
