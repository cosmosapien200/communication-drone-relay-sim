using UnityEngine;

namespace CommDrone
{
    /// Catalog of live-demo maps: hand-built levels (same primitives as the generator, never trained on) and
    /// curated procedural towns, each with a wind/noise seed verified to complete the chain with the real detector.
    public static class DemoCatalog
    {
        public struct Entry
        {
            public string name, description;
            public int handBuilt;   // >= 0: DemoLevels.Build(handBuilt); -1: procedural
            public float level;     // procedural level
            public int mapSeed;     // procedural map seed (odd = held out)
            public int runSeed;     // wind / noise seed for the mission
        }

        // seeds verified to complete the chain with the real U-Net (results/demo_seeds/)
        public static readonly Entry[] Entries =
        {
            new Entry { name = "Z-route", description = "East, north, east: one relay at each corner (≈4 relays).", handBuilt = 0, runSeed = 2000001 },
            new Entry { name = "U-turn", description = "Camp is behind the base's block: the chain must go round three sides.", handBuilt = 1, runSeed = 2000001 },
            new Entry { name = "Staircase", description = "Short alternating streets climbing to a camp in the far corner.", handBuilt = 2, runSeed = 2000001 },
            new Entry { name = "Blocked boulevard", description = "The straight boulevard is cut by rubble; detour via a parallel street.", handBuilt = 3, runSeed = 2000001 },
            new Entry { name = "Procedural town A", description = "Generated level-2 town (held-out seed 2000015): 5 relays.", handBuilt = -1, level = 2, mapSeed = 2000015, runSeed = 2000015 },
            new Entry { name = "Procedural town B", description = "Generated level-3 town (held-out seed 2000001): 5 relays.", handBuilt = -1, level = 3, mapSeed = 2000001, runSeed = 2000001 },
        };

        public static TownMap Build(Entry e) => e.handBuilt >= 0 ? DemoLevels.Build(e.handBuilt) : MapGenerator.Generate(e.mapSeed, CurriculumParams.ForLevel(e.level));
    }

    public static class DemoLevels
    {
        public const int Count = 4;

        public static TownMap Build(int id) => id switch
        {
            1 => UTurn(),
            2 => Staircase(),
            3 => BlockedBoulevard(),
            _ => DemoLevel.Build(),
        };

        static TownMap Blank(int seed, out System.Random rng)
        {
            const int N = 120;
            var m = new TownMap(N, N) { seed = seed, curriculum = CurriculumParams.ForLevel(3) };
            rng = new System.Random(seed * -7919);
            for (int ty = 0; ty < N; ty += 12)
                for (int tx = 0; tx < N; tx += 12)
                    Fill(m, tx, ty, tx + 12, ty + 12, rng.NextDouble() < 0.6 ? WallMaterial.Concrete : WallMaterial.Brick, 8f + (float)rng.NextDouble() * 12f);
            return m;
        }

        /// Base top-left, camp left-middle behind a block: east along A, south along B, west along C.
        static TownMap UTurn()
        {
            var m = Blank(-2, out var rng);
            Carve(m, 0, 96, 66, 104);   // A
            Carve(m, 56, 48, 64, 104);  // B
            Carve(m, 4, 48, 64, 56);    // C
            Carve(m, 0, 56, 6, 96);     // west edge street — closed by rubble below (tempting shortcut)
            Fill(m, 0, 66, 6, 70, WallMaterial.Rubble, 7f);
            Carve(m, 64, 76, 92, 82);   // dead end east of B
            Carve(m, 26, 104, 31, 118); // dead end north of A
            Carve(m, 24, 30, 30, 48);   // side street south of C
            Fill(m, 54, 48, 56, 52, WallMaterial.Rubble, 6.5f);
            m.baseCell = new Vector2Int(2, 100);
            m.targetCell = new Vector2Int(8, 52);
            P(m, GroundType.IntactRoad, 33, 100, 12f, 5f);
            P(m, GroundType.FlatConcrete, 60, 100, 4.5f, 4.5f);
            P(m, GroundType.IntactRoad, 60, 74, 4.5f, 10f);
            P(m, GroundType.Plaza, 60, 52, 5.5f, 5.5f);
            P(m, GroundType.IntactRoad, 34, 52, 10f, 4.5f);
            P(m, GroundType.FlatConcrete, 3, 84, 3f, 3f);
            P(m, GroundType.IntactRoad, 80, 79, 7f, 3.5f);
            P(m, GroundType.Debris, 45, 100, 3.5f, 3.5f); P(m, GroundType.Puddle, 60, 88, 3.5f, 3f); P(m, GroundType.Vehicle, 59, 62, 2f, 4.2f);
            P(m, GroundType.Hole, 20, 52, 3f, 3f); P(m, GroundType.Slope, 28.5f, 110, 3f, 3f); P(m, GroundType.Debris, 46, 52, 3f, 3f);
            MapGenerator.RasterizeGround(m);
            return m;
        }

        /// Base bottom-left, camp top-right: alternating ~30 m streets east / north with a corner relay each.
        static TownMap Staircase()
        {
            var m = Blank(-3, out var rng);
            Carve(m, 0, 11, 40, 19);    // seg 1 east
            Carve(m, 32, 11, 40, 50);   // seg 2 north
            Carve(m, 32, 42, 72, 50);   // seg 3 east
            Carve(m, 64, 42, 72, 80);   // seg 4 north
            Carve(m, 64, 72, 120, 80);  // seg 5 east to the camp
            Carve(m, 36, 50, 41, 112);  // long dead end north of seg 2 (looks like a shortcut)
            Fill(m, 36, 96, 41, 100, WallMaterial.Rubble, 7f);
            Carve(m, 40, 22, 70, 27);   // dead end east of seg 2
            Carve(m, 72, 56, 100, 61);  // dead end east of seg 4
            m.baseCell = new Vector2Int(3, 15);
            m.targetCell = new Vector2Int(112, 76);
            P(m, GroundType.FlatConcrete, 36, 15, 4.5f, 4.5f);
            P(m, GroundType.Plaza, 36, 46, 5f, 5f);
            P(m, GroundType.FlatConcrete, 68, 46, 4.5f, 4.5f);
            P(m, GroundType.IntactRoad, 68, 70, 4.5f, 8f);
            P(m, GroundType.Plaza, 68.5f, 76, 5f, 5f);
            P(m, GroundType.IntactRoad, 38.5f, 80, 3.5f, 10f);
            P(m, GroundType.FlatConcrete, 60, 24.5f, 3f, 3f);
            P(m, GroundType.IntactRoad, 18, 15, 8f, 5f);
            P(m, GroundType.Debris, 26, 15, 3f, 3f); P(m, GroundType.Puddle, 36, 30, 3f, 3f); P(m, GroundType.Vehicle, 52, 46, 4.2f, 2f);
            P(m, GroundType.Hole, 68, 58, 3f, 3f); P(m, GroundType.Slope, 90, 76, 3.5f, 3.5f); P(m, GroundType.Debris, 100, 75, 3f, 3f);
            MapGenerator.RasterizeGround(m);
            return m;
        }

        /// A straight boulevard west -> east, cut by a collapsed building; detour north via a parallel street.
        static TownMap BlockedBoulevard()
        {
            var m = Blank(-4, out var rng);
            Carve(m, 0, 54, 120, 66);   // boulevard (12 m wide)
            Fill(m, 58, 54, 66, 66, WallMaterial.Rubble, 7.5f); // collapse cuts it
            Carve(m, 44, 66, 52, 92);   // connector up (west of the collapse)
            Carve(m, 44, 84, 84, 92);   // parallel street
            Carve(m, 76, 66, 84, 92);   // connector down (east of the collapse)
            Carve(m, 20, 30, 26, 54);   // dead end south
            Carve(m, 90, 30, 96, 54);   // dead end south (east part)
            Carve(m, 52, 92, 57, 116);  // dead end north
            m.baseCell = new Vector2Int(14, 60);
            m.targetCell = new Vector2Int(114, 60);
            P(m, GroundType.IntactRoad, 32, 60, 10f, 8f);
            P(m, GroundType.FlatConcrete, 48, 60, 5f, 5f);
            P(m, GroundType.Plaza, 48, 88, 5f, 5f);
            P(m, GroundType.IntactRoad, 66, 88, 8f, 5f);
            P(m, GroundType.FlatConcrete, 80, 88, 5f, 5f);
            P(m, GroundType.Plaza, 80, 60, 6f, 6f);
            P(m, GroundType.IntactRoad, 98, 60, 8f, 6f);
            P(m, GroundType.FlatConcrete, 23, 40, 3f, 3f);
            P(m, GroundType.Debris, 54, 59, 3f, 3f); P(m, GroundType.Vehicle, 40, 63, 4.2f, 2f); P(m, GroundType.Puddle, 48, 76, 3f, 3f);
            P(m, GroundType.Hole, 70, 60, 3f, 3f); P(m, GroundType.Slope, 80, 76, 3f, 3f); P(m, GroundType.Vehicle, 92, 57, 4.2f, 2f);
            MapGenerator.RasterizeGround(m);
            return m;
        }

        static void P(TownMap m, GroundType t, float x, float y, float w, float h) =>
            m.features.Add(new GroundFeature { type = t, center = new Vector2(x, y), size = new Vector2(w, h) });

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
