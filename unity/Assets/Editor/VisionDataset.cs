using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CommDrone.EditorTools
{
    /// Synthetic auto-labelled segmentation dataset (SPEC Section 6). Flies a camera to random poses over
    /// generated maps; each RGB frame is paired with a pixel-perfect mask from a flat-class-colour re-render.
    /// Split by map seed: train (even seeds 200000+), val (odd 300001+), test (odd 400001+).
    public static class VisionDataset
    {
        const string Root = "../datasets/vision";
        const int Res = 128;

        public static void Preview() => Generate("preview", 900001, 1, 12, 256);
        public static void GenerateAll()
        {
            Generate("train", 200000, 100, 110, Res);
            Generate("val", 300001, 10, 110, Res);
            Generate("test", 400001, 15, 110, Res);
        }

        /// Fine-tuning data for the detector's weak spot: close-range nadir frames (0.5-2.5 m) over landable
        /// surfaces and look-alikes. New map seeds (train 600000+ even, test 700001+ odd).
        public static void GenerateLowAlt()
        {
            lowAlt = true;
            try { Generate("lowalt_train", 600000, 60, 60, Res); Generate("lowalt_test", 700001, 12, 60, Res); }
            finally { lowAlt = false; }
        }

        static bool lowAlt, patchView;

        /// Fine-tuning data for the explore camera's real use: 45 deg views aimed at a landable patch or look-alike
        /// from 5-20 m (often in shadow at street corners). New map seeds (train 800000+ even, test 900001+ odd).
        public static void GeneratePatchView()
        {
            patchView = true;
            try { Generate("patchview_train", 800000, 80, 60, Res); Generate("patchview_test", 900001, 15, 60, Res); }
            finally { patchView = false; }
        }

        public static void Generate(string split, int seedBase, int maps, int framesPerMap, int res)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string dir = Path.Combine(Root, split);
            Directory.CreateDirectory(dir);
            var camGo = new GameObject("DatasetCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = NadirCamera.FovDeg;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.allowMSAA = false;
            var rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
            var lab = new Texture2D(res, res, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            var meta = new StringBuilder("file,seed,level,mode,x,alt,z,yaw,pitch,class_frac_unknown,class_frac_landable,class_frac_notlandable,class_frac_obstacle\n");
            int seedStep = split == "train" ? 2 : 2;
            var counts = new long[SegClass.Count];
            for (int mi = 0; mi < maps; mi++)
            {
                int seed = seedBase + seedStep * mi;
                var rng = new System.Random(seed * 31 + 7);
                float level = 2 + (mi % 3); // levels 2..4
                var map = MapGenerator.Generate(seed, CurriculumParams.ForLevel(level));
                var vis = TownBuilder.Build(map, seed ^ 0x5bd1e995);
                RenderSettings.skybox = null;
                Color sky = Color.Lerp(new Color(0.55f, 0.65f, 0.8f), new Color(0.75f, 0.72f, 0.65f), (float)rng.NextDouble());
                bool fog = rng.NextDouble() < 0.3;
                for (int f = 0; f < framesPerMap; f++)
                {
                    bool nadir; Vector2 p; float alt, yaw, pitch;
                    if (patchView)
                    {
                        // aim at a feature from 5-20 m along a free line of sight (what the explore gimbal sees)
                        if (map.features.Count == 0) continue;
                        var ft = map.features[rng.Next(map.features.Count)];
                        p = ft.center; bool found = false;
                        for (int t = 0; t < 30 && !found; t++)
                        {
                            float a = R(rng, 0, 2 * Mathf.PI), dist = R(rng, 5f, 20f);
                            var dirv = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                            p = ft.center + dirv * dist;
                            if (!map.Collides(p, 0.6f) && map.Raycast(p, -dirv, dist) >= dist - 0.1f) found = true;
                        }
                        if (!found) continue;
                        var to = ft.center - p;
                        nadir = false;
                        alt = Sim.CruiseAltitude + R(rng, -0.5f, 0.5f);
                        yaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg + R(rng, -25f, 25f);
                        pitch = 45f + R(rng, -5f, 5f);
                    }
                    else
                    {
                        nadir = lowAlt || rng.NextDouble() < 0.45;
                        if (nadir && rng.NextDouble() < (lowAlt ? 0.85 : 0.7) && map.features.Count > 0)
                        {
                            var feat = map.features[rng.Next(map.features.Count)];
                            p = feat.center + new Vector2(R(rng, -1.5f, 1.5f), R(rng, -1.5f, 1.5f)) * Mathf.Max(feat.size.x, feat.size.y) * 0.5f;
                            if (map.Collides(p, 0.3f)) p = feat.center;
                        }
                        else p = RandomFree(map, rng, 0.6f);
                        alt = lowAlt ? R(rng, 0.5f, 2.5f) : nadir ? R(rng, 0.6f, 6f) : Sim.CruiseAltitude + R(rng, -0.6f, 0.6f);
                        yaw = R(rng, 0, 360);
                        pitch = nadir ? 90f + R(rng, -3f, 3f) : 45f + R(rng, -6f, 6f);
                    }
                    cam.transform.position = map.ToWorld(p, alt);
                    cam.transform.rotation = Quaternion.Euler(pitch, yaw, R(rng, -2f, 2f));
                    // RGB render
                    vis.SetLabelMode(false);
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = sky;
                    RenderSettings.fog = fog; RenderSettings.fogColor = sky; RenderSettings.fogMode = FogMode.Linear;
                    RenderSettings.fogStartDistance = 20f; RenderSettings.fogEndDistance = 140f;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, res, res), 0, 0); tex.Apply();
                    // label render (unlit flat colours, no fog, black = unknown)
                    vis.SetLabelMode(true);
                    RenderSettings.fog = false;
                    cam.backgroundColor = Color.black;
                    cam.Render();
                    lab.ReadPixels(new Rect(0, 0, res, res), 0, 0); lab.Apply();
                    RenderTexture.active = null;
                    // decode label to class ids (stored in all channels)
                    var lp = lab.GetPixels32();
                    var frac = new int[SegClass.Count];
                    for (int i = 0; i < lp.Length; i++)
                    {
                        byte c = Decode(lp[i]);
                        frac[c]++;
                        lp[i] = new Color32(c, c, c, 255);
                    }
                    for (int c = 0; c < SegClass.Count; c++) counts[c] += frac[c];
                    lab.SetPixels32(lp); lab.Apply();
                    string name = $"s{seed}_{f:D3}";
                    File.WriteAllBytes(Path.Combine(dir, name + "_rgb.png"), tex.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dir, name + "_lbl.png"), lab.EncodeToPNG());
                    float n = lp.Length;
                    meta.AppendLine(string.Join(",", name, seed, level, nadir ? "nadir" : "explore", S(p.x), S(alt), S(p.y), S(yaw), S(pitch),
                        S(frac[0] / n), S(frac[1] / n), S(frac[2] / n), S(frac[3] / n)));
                }
                vis.Destroy();
            }
            File.WriteAllText(Path.Combine(dir, "meta.csv"), meta.ToString());
            long tot = counts[0] + counts[1] + counts[2] + counts[3];
            Debug.Log($"VisionDataset {split}: {maps * framesPerMap} frames; class fractions unknown {counts[0] / (double)tot:F3} landable {counts[1] / (double)tot:F3} notlandable {counts[2] / (double)tot:F3} obstacle {counts[3] / (double)tot:F3}");
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(lab); Object.DestroyImmediate(camGo);
        }

        static byte Decode(Color32 c)
        {
            // nearest class colour (label renders are unlit, no AA)
            int best = 0, bd = int.MaxValue;
            for (int k = 0; k < SegClass.Count; k++)
            {
                var q = TownVisual.ClassColors[k];
                int d = (c.r - q.r) * (c.r - q.r) + (c.g - q.g) * (c.g - q.g) + (c.b - q.b) * (c.b - q.b);
                if (d < bd) { bd = d; best = k; }
            }
            return (byte)best;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static string S(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static Vector2 RandomFree(TownMap m, System.Random rng, float clearance)
        {
            for (int t = 0; t < 5000; t++)
            {
                var p = new Vector2((float)rng.NextDouble() * m.W, (float)rng.NextDouble() * m.H);
                if (!m.Collides(p, clearance)) return p;
            }
            return m.BasePos;
        }
    }
}
