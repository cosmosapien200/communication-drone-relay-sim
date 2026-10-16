using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CommDrone.EditorTools
{
    /// WP1/WP2 debug view: dumps generated maps, base->target path and the WiFi RSSI field to JSON
    /// (python/tools/plot_maps.py renders them), and times the generator and path field.
    public static class DebugExports
    {
        public static void ExportMaps() => ExportMaps("../results/debug_maps");

        public static void ExportMaps(string dir)
        {
            Directory.CreateDirectory(dir);
            var sw = new Stopwatch();
            var log = new StringBuilder();
            for (int level = 1; level <= 4; level++)
                for (int s = 0; s < 3; s++)
                {
                    int seed = 1000 * level + s;
                    sw.Restart();
                    var m = MapGenerator.Generate(seed, CurriculumParams.ForLevel(level));
                    double tGen = sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    var pf = new PathField(m, m.targetCell);
                    double tPf = sw.Elapsed.TotalMilliseconds;
                    var wifi = new WifiModel(m, new WifiParams { sigma = 0 });
                    sw.Restart();
                    var rssi = new float[m.W * m.H];
                    Vector3 basePos = m.ToWorld(m.BasePos, Sim.CruiseAltitude);
                    for (int j = 0; j < m.H; j++)
                        for (int i = 0; i < m.W; i++)
                            rssi[m.Idx(i, j)] = m.occ[m.Idx(i, j)] != 0 ? float.NaN : wifi.RSSI(basePos, m.ToWorld(m.CellCenter(i, j), Sim.CruiseAltitude), false, false);
                    double tRssi = sw.Elapsed.TotalMilliseconds;
                    var path = pf.PathFrom(m.baseCell);
                    float direct = wifi.RSSI(basePos, m.ToWorld(m.TargetPos, Sim.CruiseAltitude), false, false);
                    int land = 0; foreach (var f in m.features) if (GroundTypes.IsLandable(f.type)) land++;
                    log.AppendLine($"L{level} seed {seed}: {m.W}x{m.H} gen {tGen:F1} ms, pathfield {tPf:F1} ms, rssi field {tRssi:F1} ms, path {pf.AtCell(m.baseCell.x, m.baseCell.y):F1} m, straight {(m.TargetPos - m.BasePos).magnitude:F1} m, base->target RSSI {direct:F1} dBm, landable {land}, look-alikes {m.features.Count - land}");
                    File.WriteAllText(Path.Combine(dir, $"map_L{level}_s{seed}.json"), ToJson(m, rssi, path, pf));
                }
            File.WriteAllText(Path.Combine(dir, "summary.txt"), log.ToString());
            Debug.Log(log.ToString());
        }

        static string ToJson(TownMap m, float[] rssi, System.Collections.Generic.List<Vector2Int> path, PathField pf)
        {
            var sb = new StringBuilder();
            sb.Append("{\"W\":").Append(m.W).Append(",\"H\":").Append(m.H).Append(",\"seed\":").Append(m.seed);
            sb.Append(",\"level\":").Append(m.curriculum.level);
            sb.Append(",\"base\":[").Append(m.baseCell.x).Append(',').Append(m.baseCell.y).Append(']');
            sb.Append(",\"target\":[").Append(m.targetCell.x).Append(',').Append(m.targetCell.y).Append(']');
            sb.Append(",\"occ\":["); for (int k = 0; k < m.occ.Length; k++) { if (k > 0) sb.Append(','); sb.Append(m.occ[k]); } sb.Append(']');
            sb.Append(",\"material\":["); for (int k = 0; k < m.occ.Length; k++) { if (k > 0) sb.Append(','); sb.Append(m.material[k]); } sb.Append(']');
            sb.Append(",\"ground\":["); for (int k = 0; k < m.occ.Length; k++) { if (k > 0) sb.Append(','); sb.Append(m.ground[k]); } sb.Append(']');
            sb.Append(",\"rssi\":["); for (int k = 0; k < rssi.Length; k++) { if (k > 0) sb.Append(','); sb.Append(float.IsNaN(rssi[k]) ? "null" : rssi[k].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); } sb.Append(']');
            sb.Append(",\"pathfield\":["); for (int k = 0; k < rssi.Length; k++) { if (k > 0) sb.Append(','); sb.Append(float.IsInfinity(pf.dist[k]) ? "null" : pf.dist[k].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)); } sb.Append(']');
            sb.Append(",\"path\":["); for (int k = 0; k < path.Count; k++) { if (k > 0) sb.Append(','); sb.Append('[').Append(path[k].x).Append(',').Append(path[k].y).Append(']'); } sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }
    }
}

namespace CommDrone.EditorTools
{
    /// Report figures: rendered overview of a generated town (RGB and label mode).
    public static class ReportShots
    {
        public static void TownOverview()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            foreach (var lvl in new[] { 2, 4 })
            {
                var map = MapGenerator.Generate(5000 + lvl * 2 + 1, CurriculumParams.ForLevel(lvl));
                var vis = TownBuilder.Build(map, 77);
                var cam = new GameObject("cam").AddComponent<Camera>();
                cam.fieldOfView = 50; cam.farClipPlane = 800; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
                cam.transform.position = map.ToWorld(new Vector2(map.W * 0.5f, -map.H * 0.35f), map.W * 0.95f);
                cam.transform.LookAt(map.ToWorld(new Vector2(map.W * 0.5f, map.H * 0.5f), 0));
                ScreenshotUtil.RenderCamera(cam, 1280, 800, $"../docs/screenshots/town_L{lvl}_rgb.png");
                vis.SetLabelMode(true); cam.backgroundColor = Color.black;
                ScreenshotUtil.RenderCamera(cam, 1280, 800, $"../docs/screenshots/town_L{lvl}_labels.png");
                Object.DestroyImmediate(cam.gameObject); vis.Destroy();
            }
            var demo = DemoLevel.Build();
            var dv = TownBuilder.Build(demo, 99);
            var c2 = new GameObject("cam").AddComponent<Camera>();
            c2.orthographic = true; c2.orthographicSize = 62; c2.farClipPlane = 300; c2.clearFlags = CameraClearFlags.SolidColor; c2.backgroundColor = Color.black;
            c2.transform.position = demo.ToWorld(new Vector2(60, 60), 150); c2.transform.rotation = Quaternion.Euler(90, 0, 0);
            ScreenshotUtil.RenderCamera(c2, 1000, 1000, "../docs/screenshots/demo_level_topdown.png");
            Object.DestroyImmediate(c2.gameObject); dv.Destroy();
        }
    }
}
