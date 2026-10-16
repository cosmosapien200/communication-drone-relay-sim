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
