using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Renderable town built from a TownMap (box primitives + one procedurally painted ground texture).
    /// Label mode swaps every material for a flat unlit class colour so a re-render yields a pixel-perfect mask.
    public class TownVisual
    {
        public GameObject root;
        public Texture2D groundTex, groundLabelTex;
        public readonly List<(Renderer r, Material rgb, Material label)> swaps = new List<(Renderer, Material, Material)>();
        public readonly List<Object> owned = new List<Object>();
        public Light sun;
        public bool labelMode;

        public static readonly Color32[] ClassColors =
        {
            new Color32(0, 0, 0, 255),    // unknown
            new Color32(0, 255, 0, 255),  // landable
            new Color32(255, 0, 0, 255),  // not landable
            new Color32(0, 0, 255, 255),  // obstacle
        };

        public void SetLabelMode(bool on)
        {
            labelMode = on;
            foreach (var s in swaps) if (s.r) s.r.sharedMaterial = on ? s.label : s.rgb;
            if (sun) sun.enabled = !on;
        }

        public void Destroy()
        {
            if (root) Object.DestroyImmediate(root);
            foreach (var o in owned) if (o) Object.DestroyImmediate(o);
            owned.Clear(); swaps.Clear();
        }
    }

    public static class TownBuilder
    {
        public const int PixelsPerMetre = 16;
        static Texture2D noiseTile;

        public static TownVisual Build(TownMap m, int visualSeed, Transform parent = null, bool withSun = true)
        {
            var rng = new System.Random(visualSeed);
            var v = new TownVisual { root = new GameObject($"Town_{m.seed}") };
            if (parent) v.root.transform.SetParent(parent, false);
            v.root.transform.position = m.origin;
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---------- materials (domain-randomised palette)
            var labelMats = new Material[SegClass.Count];
            for (int c = 0; c < SegClass.Count; c++) { labelMats[c] = new Material(Shader.Find("Unlit/Color")) { color = TownVisual.ClassColors[c] }; v.owned.Add(labelMats[c]); }
            Material Lit(Texture2D tex, Color tint, float smooth = 0.1f)
            {
                var mat = new Material(Shader.Find("Standard")) { color = tint, enableInstancing = true };
                if (tex) mat.mainTexture = tex;
                mat.SetFloat("_Glossiness", smooth);
                v.owned.Add(mat);
                return mat;
            }
            var concreteFacade = FacadeTexture(rng, new Color(R(0.5f, 0.7f), R(0.5f, 0.7f), R(0.5f, 0.68f)), v);
            var brickFacade = FacadeTexture(rng, new Color(R(0.45f, 0.6f), R(0.2f, 0.3f), R(0.15f, 0.22f)), v);
            var rubbleTex = SpeckleTexture(rng, new Color(R(0.35f, 0.5f), R(0.32f, 0.45f), R(0.28f, 0.4f)), v);
            var matConcrete = Lit(concreteFacade, Color.white);
            var matBrick = Lit(brickFacade, Color.white);
            var matRubble = Lit(rubbleTex, Color.white);
            var matRamp = Lit(null, new Color(R(0.6f, 0.78f), R(0.6f, 0.78f), R(0.6f, 0.76f)));
            var matDebris = Lit(rubbleTex, new Color(R(0.7f, 1f), R(0.7f, 1f), R(0.7f, 1f)));

            void Box(string name, Vector3 center, Vector3 size, Material rgb, byte cls, Quaternion? rot = null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(v.root.transform, false);
                go.transform.localPosition = center;
                go.transform.localScale = size;
                if (rot.HasValue) go.transform.localRotation = rot.Value;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = rgb;
                v.swaps.Add((r, rgb, labelMats[cls]));
            }

            // ---------- obstacles: greedy rectangle merge of cells with equal material and height
            var used = new bool[m.W * m.H];
            for (int j = 0; j < m.H; j++)
                for (int i = 0; i < m.W; i++)
                {
                    int k = m.Idx(i, j);
                    if (m.occ[k] == 0 || used[k]) continue;
                    byte mat = m.material[k]; float h = m.height[k];
                    int i1 = i;
                    while (i1 + 1 < m.W && Same(i1 + 1, j)) i1++;
                    int j1 = j;
                    while (j1 + 1 < m.H)
                    {
                        bool row = true;
                        for (int x = i; x <= i1 && row; x++) row = Same(x, j1 + 1);
                        if (!row) break;
                        j1++;
                    }
                    for (int y = j; y <= j1; y++) for (int x = i; x <= i1; x++) used[m.Idx(x, y)] = true;
                    float w = (i1 - i + 1) * m.cellSize, d = (j1 - j + 1) * m.cellSize;
                    var c = new Vector3((i + (i1 + 1)) * 0.5f * m.cellSize, h * 0.5f, (j + (j1 + 1)) * 0.5f * m.cellSize);
                    if ((WallMaterial)mat == WallMaterial.Rubble)
                    {
                        Box("Rubble", c, new Vector3(w, h, d), matRubble, SegClass.Obstacle);
                        // irregular top: a few tilted slabs
                        int slabs = Mathf.Clamp((int)(w * d / 6f), 1, 6);
                        for (int s = 0; s < slabs; s++)
                            Box("RubbleSlab", new Vector3(c.x + R(-w, w) * 0.4f, h + R(0f, 0.6f), c.z + R(-d, d) * 0.4f),
                                new Vector3(R(0.8f, 2.5f), R(0.3f, 1.0f), R(0.8f, 2.5f)), matRubble, SegClass.Obstacle,
                                Quaternion.Euler(R(-25, 25), R(0, 360), R(-25, 25)));
                    }
                    else Box("Building", c, new Vector3(w, h, d), (WallMaterial)mat == WallMaterial.Brick ? matBrick : matConcrete, SegClass.Obstacle);

                    bool Same(int x, int y) { int q = m.Idx(x, y); return !used[q] && m.occ[q] != 0 && m.material[q] == mat && Mathf.Approximately(m.height[q], h); }
                }

            // ---------- 3D look-alikes on the ground
            foreach (var f in m.features)
            {
                var c3 = new Vector3(f.center.x, 0f, f.center.y);
                switch (f.type)
                {
                    case GroundType.Vehicle:
                    {
                        var body = Lit(null, Color.HSVToRGB(R(0, 1), R(0.1f, 0.8f), R(0.2f, 0.9f)), 0.5f);
                        bool alongZ = f.size.y > f.size.x;
                        var bs = alongZ ? new Vector3(1.8f, 0.9f, 4.0f) : new Vector3(4.0f, 0.9f, 1.8f);
                        float yawJ = R(-12, 12);
                        Box("Vehicle", c3 + new Vector3(0, 0.75f, 0), bs, body, SegClass.NotLandable, Quaternion.Euler(R(-4, 4), yawJ, R(-6, 6)));
                        var cab = alongZ ? new Vector3(1.6f, 0.6f, 2.0f) : new Vector3(2.0f, 0.6f, 1.6f);
                        Box("VehicleCab", c3 + new Vector3(0, 1.45f, 0), cab, body, SegClass.NotLandable, Quaternion.Euler(0, yawJ, 0));
                        break;
                    }
                    case GroundType.Slope:
                    {
                        float ang = R(8, 20);
                        bool tiltX = rng.NextDouble() < 0.5;
                        float len = tiltX ? f.size.x : f.size.y;
                        Box("Slope", c3 + new Vector3(0, Mathf.Sin(ang * Mathf.Deg2Rad) * len * 0.5f * 0.5f, 0),
                            new Vector3(f.size.x, 0.15f, f.size.y), matRamp, SegClass.NotLandable,
                            tiltX ? Quaternion.Euler(0, 0, ang) : Quaternion.Euler(ang, 0, 0));
                        break;
                    }
                    case GroundType.Debris:
                    {
                        int n = Mathf.Clamp((int)(f.size.x * f.size.y * 0.8f), 2, 14);
                        for (int s = 0; s < n; s++)
                            Box("Debris", c3 + new Vector3(R(-0.45f, 0.45f) * f.size.x, R(0.02f, 0.12f), R(-0.45f, 0.45f) * f.size.y),
                                new Vector3(R(0.1f, 0.5f), R(0.05f, 0.25f), R(0.1f, 0.6f)), matDebris, SegClass.NotLandable,
                                Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)));
                        break;
                    }
                }
            }

            // ---------- ground: painted RGB texture + label texture
            PaintGround(m, rng, v);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.SetParent(v.root.transform, false);
            ground.transform.localPosition = new Vector3(m.W * m.cellSize * 0.5f, 0f, m.H * m.cellSize * 0.5f);
            ground.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ground.transform.localScale = new Vector3(m.W * m.cellSize, m.H * m.cellSize, 1);
            var gMat = Lit(v.groundTex, Color.white, R(0.02f, 0.2f));
            var gLabel = new Material(Shader.Find("Unlit/Texture")) { mainTexture = v.groundLabelTex };
            v.owned.Add(gLabel);
            var gr = ground.GetComponent<Renderer>();
            gr.sharedMaterial = gMat;
            v.swaps.Add((gr, gMat, gLabel));

            // ---------- lighting (randomised direction and intensity)
            if (withSun)
            {
                var sunGo = new GameObject("Sun");
                sunGo.transform.SetParent(v.root.transform, false);
                v.sun = sunGo.AddComponent<Light>();
                v.sun.type = LightType.Directional;
                v.sun.shadows = LightShadows.Soft;
                v.sun.shadowCustomResolution = 2048; // screen-independent shadows: live demo == headless runs
                v.sun.intensity = R(0.6f, 1.4f);
                v.sun.color = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.65f), R(0, 0.6f));
                sunGo.transform.rotation = Quaternion.Euler(R(15, 75), R(0, 360), 0);
                RenderSettings.ambientLight = Color.gray * R(0.6f, 1.2f);
            }
            return v;
        }

        // ------------------------------------------------------------ textures
        static Texture2D Noise()
        {
            if (noiseTile) return noiseTile;
            const int N = 256;
            var rng = new System.Random(12345);
            var grid = new float[N * N];
            noiseTile = new Texture2D(N, N, TextureFormat.R8, false) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var px = new Color32[N * N];
            // fractal value noise
            for (int o = 0, cell = 32; o < 5; o++, cell /= 2)
            {
                int g = N / cell;
                var lat = new float[g * g];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rng.NextDouble();
                float amp = 1f / (1 << o);
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float fx = x / (float)cell, fy = y / (float)cell;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        float a = lat[(y0 % g) * g + x0 % g], b = lat[(y0 % g) * g + (x0 + 1) % g];
                        float c = lat[((y0 + 1) % g) * g + x0 % g], d = lat[((y0 + 1) % g) * g + (x0 + 1) % g];
                        grid[y * N + x] += amp * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    }
            }
            for (int i = 0; i < grid.Length; i++) px[i] = new Color32((byte)Mathf.Clamp(grid[i] / 1.94f * 255f, 0, 255), 0, 0, 255);
            noiseTile.SetPixels32(px); noiseTile.Apply();
            return noiseTile;
        }

        static float[] noiseCache;
        static float N(int x, int y)
        {
            if (noiseCache == null)
            {
                var t = Noise().GetPixels32();
                noiseCache = new float[t.Length];
                for (int i = 0; i < t.Length; i++) noiseCache[i] = t[i].r / 255f;
            }
            return noiseCache[(y & 255) * 256 + (x & 255)];
        }

        static Texture2D FacadeTexture(System.Random rng, Color baseCol, TownVisual v)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool window = (x % 16) is > 4 and < 12 && (y % 16) is > 5 and < 13 && rng.NextDouble() > 0.05;
                    float n = 0.85f + 0.3f * N(x * 3, y * 3);
                    Color c = window ? new Color(0.12f, 0.14f, 0.18f) * (0.7f + 0.6f * (float)rng.NextDouble()) : baseCol * n;
                    px[y * S + x] = c;
                }
            t.SetPixels32(px); t.Apply();
            v.owned.Add(t);
            return t;
        }

        static Texture2D SpeckleTexture(System.Random rng, Color baseCol, TownVisual v)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                float n = 0.6f + 0.8f * N(i % S * 5, i / S * 5) * (float)rng.NextDouble();
                px[i] = baseCol * n;
            }
            t.SetPixels32(px); t.Apply();
            v.owned.Add(t);
            return t;
        }

        /// Paints the ground RGB and label textures at PixelsPerMetre. Labels: landable patches = Landable, all other ground = NotLandable.
        static void PaintGround(TownMap m, System.Random rng, TownVisual v)
        {
            int ppm = PixelsPerMetre;
            int W = Mathf.RoundToInt(m.W * m.cellSize * ppm), H = Mathf.RoundToInt(m.H * m.cellSize * ppm);
            var rgb = new Color32[W * H];
            var lab = new Color32[W * H];
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int ox = rng.Next(256), oy = rng.Next(256);

            // global randomisation: asphalt tone, dust tint
            float asph = R(0.25f, 0.38f);
            Color dust = new Color(R(0.55f, 0.7f), R(0.5f, 0.6f), R(0.4f, 0.5f));
            float dustAmt = R(0f, 0.35f);
            Color Dust(Color c, int x, int y) => Color.Lerp(c, dust, dustAmt * (0.4f + 0.9f * N(x / 7 + 91, y / 7 + 37)));

            // street: cracked, patchy asphalt (not landable)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = N(x / 2 + ox, y / 2 + oy) * 0.6f + N(x * 2 + oy, y * 2 + ox) * 0.4f;
                    float patchy = N(x / 9 + 17, y / 9 + 53);
                    float g = asph * (0.75f + 0.5f * n) + (patchy > 0.62f ? 0.06f : 0f);
                    rgb[y * W + x] = Dust(new Color(g, g, g * 1.03f), x, y);
                    lab[y * W + x] = TownVisual.ClassColors[SegClass.NotLandable];
                }
            // cracks
            int cracks = (int)(m.W * m.H * 0.25f);
            for (int c = 0; c < cracks; c++)
            {
                float x = R(0, W), y = R(0, H), a = R(0, 2 * Mathf.PI);
                int len = rng.Next(10, 70);
                for (int s = 0; s < len; s++)
                {
                    a += R(-0.5f, 0.5f); x += Mathf.Cos(a); y += Mathf.Sin(a);
                    int xi = (int)x, yi = (int)y;
                    if (xi < 0 || yi < 0 || xi >= W || yi >= H) break;
                    rgb[yi * W + xi] = Color.Lerp(rgb[yi * W + xi], Color.black, 0.6f);
                }
            }

            foreach (var f in m.features)
            {
                int x0 = Mathf.Max(0, (int)((f.center.x - f.size.x / 2) * ppm)), x1 = Mathf.Min(W - 1, (int)((f.center.x + f.size.x / 2) * ppm));
                int y0 = Mathf.Max(0, (int)((f.center.y - f.size.y / 2) * ppm)), y1 = Mathf.Min(H - 1, (int)((f.center.y + f.size.y / 2) * ppm));
                bool landable = GroundTypes.IsLandable(f.type);
                Color baseC; float noiseAmp; float joint = 0f; bool tiles = false, lane = false;
                switch (f.type)
                {
                    case GroundType.Plaza: baseC = new Color(R(0.55f, 0.72f), R(0.52f, 0.68f), R(0.45f, 0.6f)); noiseAmp = 0.08f; tiles = true; break;
                    case GroundType.IntactRoad: baseC = Color.white * R(0.18f, 0.27f); noiseAmp = 0.05f; lane = rng.NextDouble() < 0.6; break;
                    case GroundType.FlatConcrete: baseC = Color.white * R(0.58f, 0.75f); noiseAmp = 0.06f; joint = R(2.5f, 3.5f); break;
                    case GroundType.Debris: baseC = rng.NextDouble() < 0.5 ? Color.white * R(0.55f, 0.72f) : Color.white * asph; noiseAmp = 0.1f; break;
                    case GroundType.Slope: baseC = Color.white * R(0.5f, 0.7f); noiseAmp = 0.06f; break;
                    default: baseC = Color.white * asph; noiseAmp = 0.12f; break;
                }
                float ex = (f.size.x / 2) * ppm, ey = (f.size.y / 2) * ppm, cx = f.center.x * ppm, cy = f.center.y * ppm;
                float wob = R(0.15f, 0.3f);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = y * W + x;
                        float n = N(x + ox * 3, y + oy * 3) - 0.5f;
                        Color c = baseC * (1f + noiseAmp * 2f * n);
                        float lx = (x - (cx - ex)) / ppm, ly = (y - (cy - ey)) / ppm;
                        if (tiles && ((x % (ppm / 2)) == 0 || (y % (ppm / 2)) == 0)) c *= 0.82f;
                        if (joint > 0 && ((lx % joint) < 0.06f || (ly % joint) < 0.06f)) c *= 0.75f;
                        if (lane && Mathf.Abs((f.size.x > f.size.y ? ly - f.size.y / 2 : lx - f.size.x / 2)) < 0.07f
                            && ((f.size.x > f.size.y ? lx : ly) % 2f) < 1.1f) c = new Color(0.85f, 0.85f, 0.8f);
                        float ux = (x - cx) / ex, uy = (y - cy) / ey;
                        float rr = ux * ux + uy * uy;
                        float edge = 0.8f + wob * (N(x / 3 + 7, y / 3 + 11) - 0.5f) * 2f;
                        switch (f.type)
                        {
                            case GroundType.Puddle:
                                if (rr < edge) c = Color.Lerp(new Color(0.12f, 0.15f, 0.2f), new Color(0.35f, 0.4f, 0.48f), Mathf.Clamp01(N(x, y) * 1.2f - 0.2f) * 0.6f);
                                else c = rgb[k];
                                break;
                            case GroundType.Hole:
                                if (rr < edge * 0.75f) c = new Color(0.04f, 0.035f, 0.03f) * (1f + n);
                                else if (rr < edge) c = new Color(0.35f, 0.27f, 0.18f) * (0.8f + n);
                                else c = rgb[k];
                                break;
                            case GroundType.Debris:
                                if (N(x * 3 + 5, y * 3 + 9) > 0.62f) c = Color.Lerp(c, (N(x, y) > 0.5f ? new Color(0.3f, 0.22f, 0.15f) : new Color(0.75f, 0.72f, 0.68f)), 0.8f);
                                break;
                            case GroundType.Vehicle:
                                c = (Color)rgb[k] * 0.85f; // oil stains / shadow under the car
                                break;
                        }
                        rgb[k] = Dust(c, x, y);
                        lab[k] = TownVisual.ClassColors[landable ? SegClass.Landable : SegClass.NotLandable];
                    }
            }

            v.groundTex = new Texture2D(W, H, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            v.groundTex.SetPixels32(rgb); v.groundTex.Apply();
            v.groundLabelTex = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            v.groundLabelTex.SetPixels32(lab); v.groundLabelTex.Apply();
            v.owned.Add(v.groundTex); v.owned.Add(v.groundLabelTex);
        }
    }
}
