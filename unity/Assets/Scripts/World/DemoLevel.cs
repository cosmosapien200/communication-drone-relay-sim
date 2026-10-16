using UnityEngine;

namespace CommDrone
{
    /// Hand-built demo town (SPEC Section 14), made from the same primitives as the generator and never used in
    /// training (seed -1). A Z-shaped route — east along street A, north along street B, east along street C to the
    /// camp — with dead-end side streets, a decoy plaza and look-alikes. With the link budget (qualify ≈ 35 m LOS,
    /// link ≈ 100 m LOS) the camp needs about four relays, one of them at each corner.
    public static class DemoLevel
    {
        public static TownMap Build()
        {
            const int N = 120;
            var m = new TownMap(N, N) { seed = -1, curriculum = CurriculumParams.ForLevel(3) };
            var rng = new System.Random(4242);
            // everything starts as buildings: 12 m tiles with varied height and material
            for (int ty = 0; ty < N; ty += 12)
                for (int tx = 0; tx < N; tx += 12)
                {
                    var mat = rng.NextDouble() < 0.6 ? WallMaterial.Concrete : WallMaterial.Brick;
                    Fill(m, tx, ty, tx + 12, ty + 12, mat, 8f + (float)rng.NextDouble() * 12f);
                }
            // streets
            Carve(m, 0, 16, 62, 24);    // A: base street, west -> east
            Carve(m, 54, 16, 62, 72);   // B: south -> north
            Carve(m, 54, 64, 120, 72);  // C: west -> east to the camp
            Carve(m, 0, 24, 6, 58);     // west edge street (dead end)
            Carve(m, 20, 24, 25, 44);   // side street north of A ...
            Carve(m, 25, 40, 44, 55);   // ... leading to a decoy plaza
            Carve(m, 62, 36, 86, 41);   // side street east of B, closed by rubble
            Carve(m, 86, 72, 91, 96);   // dead end north of C
            Carve(m, 100, 40, 108, 64); // dead end south of C
            // alleys inside blocks (gaps between buildings)
            Carve(m, 36, 24, 38, 34); Carve(m, 72, 52, 74, 64); Carve(m, 30, 4, 32, 16);
            // rubble
            Fill(m, 80, 36, 86, 41, WallMaterial.Rubble, 7f);   // closes the east side street
            Fill(m, 10, 16, 14, 18, WallMaterial.Rubble, 6.5f); // narrows street A
            Fill(m, 60, 44, 62, 50, WallMaterial.Rubble, 6.5f); // collapsed facade into street B
            Fill(m, 54, 0, 62, 16, WallMaterial.Rubble, 7f);    // street B closed to the south
            Fill(m, 100, 60, 108, 64, WallMaterial.Rubble, 6.5f);
            m.baseCell = new Vector2Int(2, 20);
            m.targetCell = new Vector2Int(115, 68);
            void P(GroundType t, float x, float y, float w, float h) => m.features.Add(new GroundFeature { type = t, center = new Vector2(x, y), size = new Vector2(w, h) });
            // landable surfaces
            P(GroundType.IntactRoad, 30, 20, 13f, 5f);     // street A strip
            P(GroundType.FlatConcrete, 58, 20, 4.5f, 4.5f); // corner A/B
            P(GroundType.IntactRoad, 57.5f, 53, 4.5f, 10f); // street B strip
            P(GroundType.Plaza, 58, 68, 5.5f, 5.5f);       // corner B/C
            P(GroundType.Plaza, 36, 48, 7f, 6f);           // decoy plaza off the route
            P(GroundType.FlatConcrete, 22.5f, 34, 3f, 3f); // side street
            P(GroundType.IntactRoad, 88.5f, 84, 3.5f, 8f); // dead end north of C
            P(GroundType.FlatConcrete, 3, 45, 3f, 3f);     // west edge street
            // look-alikes
            P(GroundType.Debris, 45, 20, 3.5f, 3.5f); P(GroundType.Puddle, 58, 32, 3.5f, 3f); P(GroundType.Vehicle, 57, 40, 2f, 4.2f);
            P(GroundType.Hole, 75, 68, 3f, 3f); P(GroundType.Slope, 90, 68, 3.5f, 3.5f); P(GroundType.Debris, 100, 67, 3f, 3f);
            P(GroundType.Vehicle, 14, 21, 4.2f, 2f); P(GroundType.Puddle, 70, 38.5f, 3f, 2.5f); P(GroundType.Slope, 58, 61, 4f, 3f);
            MapGenerator.RasterizeGround(m);
            return m;
        }

        static void Carve(TownMap m, int x0, int y0, int x1, int y1)
        {
            for (int j = Mathf.Max(0, y0); j < Mathf.Min(m.H, y1); j++)
                for (int i = Mathf.Max(0, x0); i < Mathf.Min(m.W, x1); i++)
                { int k = m.Idx(i, j); m.occ[k] = 0; m.material[k] = 0; m.height[k] = 0; }
        }

        static void Fill(TownMap m, int x0, int y0, int x1, int y1, WallMaterial mat, float h)
        {
            for (int j = Mathf.Max(0, y0); j < Mathf.Min(m.H, y1); j++)
                for (int i = Mathf.Max(0, x0); i < Mathf.Min(m.W, x1); i++)
                { int k = m.Idx(i, j); m.occ[k] = 1; m.material[k] = (byte)mat; m.height[k] = h; }
        }
    }
}
