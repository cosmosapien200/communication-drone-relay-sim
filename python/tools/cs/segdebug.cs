using UnityEngine;
using UnityEditor;
using CommDrone;
using Unity.InferenceEngine;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var seg = new SegModel(Resources.Load<ModelAsset>("Models/unet_v1"), BackendType.CPU);
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        tex.LoadImage(System.IO.File.ReadAllBytes("../datasets/vision/test/s400001_000_rgb.png"));
        seg.Run(tex);
        result.Log("png: {0}", Stats(seg));
        var map = MapGenerator.Generate(400001, CurriculumParams.ForLevel(2));
        var vis = TownBuilder.Build(map, 1);
        var cam = new GameObject("dbgcam").AddComponent<Camera>();
        cam.enabled = false; cam.fieldOfView = 60;
        cam.transform.SetPositionAndRotation(new Vector3(map.BasePos.x + 3, 5, map.BasePos.y), Quaternion.Euler(90, 0, 0));
        var rt = new RenderTexture(128, 128, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        seg.Run(rt);
        result.Log("rt: {0}", Stats(seg));
        var t2 = new Texture2D(128, 128, TextureFormat.RGB24, false);
        RenderTexture.active = rt; t2.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); t2.Apply(); RenderTexture.active = null;
        var px = t2.GetPixels32(); float m = 0; foreach (var p in px) m += p.r; result.Log("rt mean red {0}", m / px.Length);
        seg.Run(t2);
        result.Log("t2: {0}", Stats(seg));
        seg.Dispose(); vis.Destroy(); Object.DestroyImmediate(cam.gameObject);
    }
    string Stats(SegModel s)
    {
        int[] h = new int[4]; foreach (var a in s.argmax) h[a]++;
        float mn = float.MaxValue, mx = float.MinValue; int nan = 0;
        foreach (var p in s.probs) { if (float.IsNaN(p)) nan++; else { mn = Mathf.Min(mn, p); mx = Mathf.Max(mx, p); } }
        return $"hist {h[0]},{h[1]},{h[2]},{h[3]} min {mn} max {mx} nan {nan}";
    }
}
