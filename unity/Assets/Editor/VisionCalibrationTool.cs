using System.Globalization;
using System.IO;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CommDrone.EditorTools
{
    /// 1) VerifyModel: runs the ONNX U-Net inside Unity on test PNGs and checks pixel accuracy vs labels
    ///    (catches orientation / preprocessing mismatches with PyTorch).
    /// 2) MeasureLocator: flies nadir poses over landable patches of held-out test maps, runs the real
    ///    LandingSpotLocator and the geometric ground truth, and logs per-frame results for the noise model.
    public static class VisionCalibrationTool
    {
        static string S(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static void VerifyModel() => VerifyModel("unet_v1");

        public static void VerifyModel(string model)
        {
            var seg = new SegModel(Resources.Load<ModelAsset>("Models/" + model));
            var files = Directory.GetFiles("../datasets/vision/test", "*_rgb.png");
            System.Array.Sort(files);
            long ok = 0, okFlip = 0, tot = 0;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var lbl = new Texture2D(2, 2, TextureFormat.RGB24, false);
            for (int f = 0; f < 60; f++)
            {
                tex.LoadImage(File.ReadAllBytes(files[f * 25 % files.Length]));
                lbl.LoadImage(File.ReadAllBytes(files[f * 25 % files.Length].Replace("_rgb", "_lbl")));
                seg.Run(tex);
                var lp = lbl.GetPixels32(); // row 0 = bottom
                int R = SegModel.Res;
                for (int v = 0; v < R; v++)
                    for (int u = 0; u < R; u++)
                    {
                        byte truthTop = lp[(R - 1 - v) * R + u].r;   // label at image row v from the top
                        byte truthFlip = lp[v * R + u].r;
                        byte pred = seg.argmax[v * R + u];
                        if (pred == truthTop) ok++;
                        if (pred == truthFlip) okFlip++;
                        tot++;
                    }
            }
            seg.Dispose();
            Debug.Log($"VerifyModel {model}: pixel acc (top-left convention) {ok / (double)tot:F4}, if flipped {okFlip / (double)tot:F4}");
        }

        static void DumpDebug(Camera cam, SegModel seg, int f)
        {
            Directory.CreateDirectory("../results/vision/locdebug");
            var rt = new RenderTexture(128, 128, 24); cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var t = new Texture2D(128, 128, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); t.Apply(); RenderTexture.active = null;
            File.WriteAllBytes($"../results/vision/locdebug/f{f}_rgb.png", t.EncodeToPNG());
            var px = new Color32[128 * 128];
            Color32[] pal = { new Color32(0, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) };
            for (int v = 0; v < 128; v++) for (int u = 0; u < 128; u++) px[(127 - v) * 128 + u] = pal[seg.argmax[v * 128 + u]];
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes($"../results/vision/locdebug/f{f}_mask.png", t.EncodeToPNG());
        }

        public static void MeasureLocatorV3() => MeasureLocator("unet_v3", "../results/vision/locator_calib_unet_v3.csv", 15, 60);
        public static void VerifyModelV3() => VerifyModel("unet_v3");

        public static void MeasureLocator() => MeasureLocator("unet_v1", "../results/vision/locator_calib_unet_v1.csv", 15, 60);

        public static void MeasureLocator(string model, string outPath, int maps, int framesPerMap)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var seg = new SegModel(Resources.Load<ModelAsset>("Models/" + model));
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.enabled = false; cam.allowMSAA = false; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
            var loc = new LandingSpotLocator(seg, cam);
            var gt = new GeometricSpotSensor();
            var sb = new StringBuilder("seed,alt,gt_det,cnn_det,gt_x,gt_y,cnn_x,cnn_y,gt_area,cnn_area,cnn_conf,gt_clear,cnn_clear\n");
            for (int mi = 0; mi < maps; mi++)
            {
                int seed = 400001 + 2 * mi; // vision test maps (never trained on)
                var rng = new System.Random(seed);
                var map = MapGenerator.Generate(seed, CurriculumParams.ForLevel(2 + mi % 3));
                var vis = TownBuilder.Build(map, seed ^ 0x5bd1e995);
                loc.worldOrigin = map.origin;
                var patches = map.features.FindAll(f => GroundTypes.IsLandable(f.type));
                if (patches.Count == 0) { vis.Destroy(); continue; }
                for (int f = 0; f < framesPerMap; f++)
                {
                    var p = patches[rng.Next(patches.Count)];
                    float alt = 0.6f + (float)rng.NextDouble() * 5.4f;
                    // 80 %: patch near the centre of view (as during landing); 20 %: patch out of view (false-positive check)
                    float spread = rng.NextDouble() < 0.8 ? 0.6f : 2.2f;
                    float he = NadirCamera.HalfExtent(alt);
                    var off = new Vector2((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1) * he * spread;
                    var pos = new Vector3(p.center.x + off.x, alt, p.center.y + off.y);
                    if (map.Collides(new Vector2(pos.x, pos.z), 0.3f)) continue;
                    float yaw = (float)rng.NextDouble() * 360f;
                    var g = gt.Detect(pos, yaw, p);
                    var c = loc.Detect(pos, yaw, p);
                    if (mi == 0 && f < 6) DumpDebug(cam, seg, f);
                    sb.AppendLine(string.Join(",", seed, S(alt), g.detected ? 1 : 0, c.detected ? 1 : 0, S(g.offset.x), S(g.offset.y), S(c.offset.x), S(c.offset.y),
                        S(g.area), S(c.area), S(c.confidence), S(g.clearance), S(c.clearance)));
                }
                vis.Destroy();
            }
            seg.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("MeasureLocator wrote " + outPath);
        }
    }
}

namespace CommDrone.EditorTools
{
    /// Renders what the explore camera sees while approaching a demo-map spot, with the U-Net mask.
    public static class SpotDebug
    {
        public static void ZRouteCorner() => Approach(0, new Vector2(58, 20), new Vector2(-1, 0), "unet_v2", "../results/fix/spotdebug_z");
        public static void ZRouteSeeds()
        {
            foreach (int rs in new[] { 2000001, 2000003, 2000005, 2000007, 2000009, 2000011, 2000013, 2000015, 2000017, 2000019 })
            {
                visualSeed = rs ^ 0x5bd1e995;
                Debug.Log($"SPOTDEBUG runSeed {rs}");
                Approach(0, new Vector2(58, 20), new Vector2(-1, 0), "unet_v2", "../results/fix/spotdebug_seeds/" + rs);
            }
            visualSeed = -1 ^ 0x5bd1e995;
        }
        static int visualSeed = -1 ^ 0x5bd1e995;

        /// Re-render poses logged by a player frame dump (results/fix/focus/poses.csv) in the Editor, same map and visual seed.
        public static void RenderLoggedPoses()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var map = DemoLevels.Build(0);
            var vis = TownBuilder.Build(map, 2000005 ^ 0x5bd1e995);
            var seg = new SegModel(Resources.Load<Unity.InferenceEngine.ModelAsset>("Models/unet_v2"));
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = NadirCamera.FovDeg; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; cam.allowMSAA = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
            var rt = new RenderTexture(128, 128, 24) { antiAliasing = 1 };
            curMap = null;
            foreach (var line in System.IO.File.ReadAllLines("../results/fix/focus/poses.csv"))
            {
                var f = line.Split(','); var ic = System.Globalization.CultureInfo.InvariantCulture;
                cam.transform.SetPositionAndRotation(new Vector3(float.Parse(f[1], ic), float.Parse(f[2], ic), float.Parse(f[3], ic)),
                    Quaternion.Euler(float.Parse(f[4], ic), float.Parse(f[5], ic), float.Parse(f[6], ic)));
                Save(cam, rt, seg, "../results/fix/focus/editor_" + f[0]);
            }
            Debug.Log("ambient mode " + RenderSettings.ambientMode + " ambient " + RenderSettings.ambientLight + " intensity " + RenderSettings.ambientIntensity);
            seg.Dispose(); vis.Destroy();
        }

        /// Generic spot render driven by ../results/fix/spot_req.txt: "level,mapSeed,x,y,fromDirX,fromDirY,model,runSeed,outDir"
        public static void FromFile()
        {
            var f = System.IO.File.ReadAllText("../results/fix/spot_req.txt").Trim().Split(',');
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            int lvl = int.Parse(f[0]), mapSeed = int.Parse(f[1]);
            var spot = new Vector2(float.Parse(f[2], ic), float.Parse(f[3], ic));
            var from = new Vector2(float.Parse(f[4], ic), float.Parse(f[5], ic)).normalized;
            visualSeed = int.Parse(f[7]) ^ 0x5bd1e995;
            procMap = MapGenerator.Generate(mapSeed, CurriculumParams.ForLevel(lvl));
            int pi = procMap.PatchAt(spot);
            foreach (var ft in procMap.features) if (ft.Contains(spot)) Debug.Log($"SPOTDEBUG feature at spot: {ft.type} centre {ft.center} size {ft.size} (landable index {pi})");
            Approach(-1, spot, from, f[6], f[8]);
            procMap = null; visualSeed = -1 ^ 0x5bd1e995;
        }
        static TownMap procMap;

        public static void ZRouteCornerV3() => Approach(0, new Vector2(58, 20), new Vector2(-1, 0), "unet_v3", "../results/fix/spotdebug_z_v3");

        public static void Approach(int demoMap, Vector2 spot, Vector2 fromDir, string model, string dir)
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var map = procMap ?? DemoLevels.Build(demoMap);
            curMap = map; patch = new Rect(spot.x - 2.25f, spot.y - 2.25f, 4.5f, 4.5f);
            var vis = TownBuilder.Build(map, visualSeed);
            var seg = new SegModel(Resources.Load<Unity.InferenceEngine.ModelAsset>("Models/" + model));
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = NadirCamera.FovDeg; cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
            var rt = new RenderTexture(128, 128, 24);
            System.IO.Directory.CreateDirectory(dir);
            float yaw = Mathf.Atan2(-fromDir.x, -fromDir.y) * Mathf.Rad2Deg;
            int k = 0;
            foreach (float d in new[] { 20f, 14f, 9f, 5f, 2f })
            {
                var p = spot + fromDir * d;
                cam.transform.SetPositionAndRotation(map.ToWorld(p, Sim.CruiseAltitude), Quaternion.Euler(45f, yaw, 0f));
                Save(cam, rt, seg, $"{dir}/a{k++}_d{d}");
            }
            foreach (float alt in new[] { 5f, 3f, 1.5f })
            {
                cam.transform.SetPositionAndRotation(map.ToWorld(spot, alt), Quaternion.Euler(90f, yaw, 0f));
                Save(cam, rt, seg, $"{dir}/n{k++}_alt{alt}");
            }
            seg.Dispose(); vis.Destroy();
        }

        public static TownMap curMap; public static Rect patch;
        static void Save(Camera cam, RenderTexture rt, SegModel seg, string path)
        {
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            seg.Run(rt);
            if (curMap != null)
            {
                var obs = VisionSegmenter.Project(curMap, cam.transform.position, cam.transform.rotation, cam.fieldOfView, seg.argmax, seg.conf, 2, 25f);
                int yes = 0, no = 0, maskYes = 0;
                foreach (var o in obs) if (patch.Contains(new Vector2(o.cell.x + 0.5f, o.cell.y + 0.5f))) { if (o.cls == SegClass.Landable) yes++; else no++; }
                foreach (var a in seg.argmax) if (a == SegClass.Landable) maskYes++;
                Debug.Log($"SPOTDEBUG {System.IO.Path.GetFileName(path)}: landable pixels in mask {maskYes}, projected onto patch cells: landable {yes}, not-landable {no}");
            }
            var t = new Texture2D(128, 128, TextureFormat.RGB24, false);
            RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); t.Apply(); RenderTexture.active = null;
            System.IO.File.WriteAllBytes(path + "_rgb.png", t.EncodeToPNG());
            Color32[] pal = { new Color32(0, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) };
            var px = new Color32[128 * 128];
            for (int v = 0; v < 128; v++) for (int u = 0; u < 128; u++) px[(127 - v) * 128 + u] = pal[seg.argmax[v * 128 + u]];
            t.SetPixels32(px); t.Apply();
            System.IO.File.WriteAllBytes(path + "_mask.png", t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
