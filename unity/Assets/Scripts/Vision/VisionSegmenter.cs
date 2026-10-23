using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// One ground-cell observation from a camera frame (SPEC Section 11: VisionSegmenter output).
    public struct CellObservation
    {
        public Vector2Int cell;
        public byte cls;          // SegClass seen at that cell
        public float confidence;  // classifier confidence (0..1)
    }

    /// Anything that turns a camera pose (and possibly a rendered frame) into per-cell observations.
    /// Implementations: VisionSegmenter (CNN), GroundTruthCellDetector, NoisyCellDetector (VisionNoiseModel for cells).
    public interface ICellDetector
    {
        List<CellObservation> Observe(TownMap map, Vector3 camPosWorld, Quaternion camRot, float fovDeg);
    }

    /// Shared ray geometry: pixel (u from left, v from top) -> world ray for a pinhole camera.
    public static class CameraGeometry
    {
        public static Ray PixelRay(Vector3 pos, Quaternion rot, float fovDeg, float u, float v, int res)
        {
            float t = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
            float x = ((u + 0.5f) / res * 2f - 1f) * t;
            float y = (1f - (v + 0.5f) / res * 2f) * t;
            return new Ray(pos, rot * new Vector3(x, y, 1f).normalized);
        }

        /// Ground hit of a pixel ray in map-local coordinates; false if the ray misses the ground within range
        /// or the line of sight is blocked by an obstacle column (obstacles are taller than the camera).
        public static bool GroundHit(TownMap map, Ray r, float maxRange, out Vector2 local)
        {
            local = default;
            if (r.direction.y > -1e-3f) return false;
            float s = -r.origin.y / r.direction.y + map.origin.y / r.direction.y;
            Vector3 hit = r.origin + r.direction * s;
            local = map.ToLocal(hit);
            var from = map.ToLocal(r.origin);
            float d = (local - from).magnitude;
            if (d > maxRange) return false;
            if (d > 1e-3f && map.Raycast(from, (local - from) / d, d) < d - 0.05f) return false;
            return true;
        }
    }

    /// Task A + B: CNN segmentation of a rendered frame, projected onto the ground plane with the known camera pose.
    public class VisionSegmenter : ICellDetector
    {
        public readonly SegModel model;
        public readonly Camera cam;
        public readonly RenderTexture rt;
        public int stride = 2;          // pixel subsampling for projection
        public float maxRange = 25f;    // ignore far-away ground (low resolution)

        public static string FrameDump; int dumped, frameCounter;
        public static Vector2 FrameFocus; public static float FrameFocusRadius;
        void Dump()
        {
            System.IO.Directory.CreateDirectory(FrameDump);
            int R = SegModel.Res;
            var t = new Texture2D(R, R, TextureFormat.RGB24, false);
            var prev = RenderTexture.active; RenderTexture.active = rt; t.ReadPixels(new Rect(0, 0, R, R), 0, 0); t.Apply(); RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(FrameDump, $"v{dumped}_rgb.png"), t.EncodeToPNG());
            var tp = cam.transform; var e = tp.rotation.eulerAngles; var ic = System.Globalization.CultureInfo.InvariantCulture;
            System.IO.File.AppendAllText(System.IO.Path.Combine(FrameDump, "poses.csv"), string.Format(ic, "v{0},{1},{2},{3},{4},{5},{6}\n", dumped, tp.position.x, tp.position.y, tp.position.z, e.x, e.y, e.z));
            Color32[] pal = { new Color32(0, 0, 0, 255), new Color32(0, 255, 0, 255), new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) };
            var px = new Color32[R * R];
            for (int v = 0; v < R; v++) for (int u = 0; u < R; u++) px[(R - 1 - v) * R + u] = pal[model.argmax[v * R + u]];
            t.SetPixels32(px); t.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(FrameDump, $"v{dumped}_mask.png"), t.EncodeToPNG());
            Object.Destroy(t); dumped++;
        }

        public VisionSegmenter(SegModel model, Camera cam)
        {
            this.model = model; this.cam = cam;
            rt = new RenderTexture(SegModel.Res, SegModel.Res, 24) { antiAliasing = 1 };
        }

        public List<CellObservation> Observe(TownMap map, Vector3 camPosWorld, Quaternion camRot, float fovDeg)
        {
            cam.transform.SetPositionAndRotation(camPosWorld, camRot);
            cam.fieldOfView = fovDeg;
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            model.Run(rt);
            if (FrameDump != null)
            {
                bool near = FrameFocusRadius > 0 && (map.ToLocal(camPosWorld) - FrameFocus).magnitude < FrameFocusRadius;
                if (FrameFocusRadius > 0 ? (near && dumped < 40) : (dumped < 12 && (frameCounter++ % 10) == 0)) Dump();
            }
            return Project(map, camPosWorld, camRot, fovDeg, model.argmax, model.conf, stride, maxRange);
        }

        public static List<CellObservation> Project(TownMap map, Vector3 pos, Quaternion rot, float fov, byte[] cls, float[] conf, int stride, float maxRange)
        {
            var res = new List<CellObservation>();
            int R = SegModel.Res;
            for (int v = 0; v < R; v += stride)
                for (int u = 0; u < R; u += stride)
                {
                    int i = v * R + u;
                    byte c = cls[i];
                    if (c == SegClass.Unknown || c == SegClass.Obstacle) continue; // only ground classes go to the landable channel
                    var ray = CameraGeometry.PixelRay(pos, rot, fov, u, v, R);
                    if (!CameraGeometry.GroundHit(map, ray, maxRange, out var p)) continue;
                    var cell = map.CellOf(p);
                    if (!map.InBounds(cell.x, cell.y)) continue;
                    // v2 fusion: steep, close pixels are reliable; far grazing pixels are blurred and bleed across
                    // patch edges, so they get much less weight (weight = confidence x incidence x (5 m / range)^2)
                    float range = (p - map.ToLocal(pos)).magnitude;
                    float q = 5f / Mathf.Max(5f, range);
                    float w = conf[i] * Mathf.Clamp01(-ray.direction.y) * q * q; // inverse-square: one close look outweighs many distant ones
                    if (w < 0.005f) continue;
                    res.Add(new CellObservation { cell = cell, cls = c, confidence = w });
                }
            return res;
        }
    }

    /// Ground-truth stand-in with the same output as VisionSegmenter (no rendering needed).
    public class GroundTruthCellDetector : ICellDetector
    {
        public int stride = 4; public float maxRange = 25f;
        protected readonly byte[] cls = new byte[SegModel.Res * SegModel.Res];
        protected readonly float[] conf = new float[SegModel.Res * SegModel.Res];

        public virtual List<CellObservation> Observe(TownMap map, Vector3 pos, Quaternion rot, float fov)
        {
            int R = SegModel.Res;
            for (int v = 0; v < R; v += stride)
                for (int u = 0; u < R; u += stride)
                {
                    int i = v * R + u;
                    if (CameraGeometry.GroundHit(map, CameraGeometry.PixelRay(pos, rot, fov, u, v, R), maxRange, out var p))
                    { cls[i] = map.GroundClassAt(p); conf[i] = 1f; }
                    else { cls[i] = SegClass.Obstacle; conf[i] = 1f; }
                    Corrupt(i);
                }
            return VisionSegmenter.Project(map, pos, rot, fov, cls, conf, stride, maxRange);
        }

        protected virtual void Corrupt(int i) { }
    }

    /// VisionNoiseModel for the segmentation channel: ground truth corrupted with the detector's measured
    /// per-class confusion rates (rows = true class, cols = predicted).
    public class NoisyCellDetector : GroundTruthCellDetector
    {
        public float[,] confusion; // row-normalised P(pred | true)
        readonly System.Random rng;
        public NoisyCellDetector(float[,] confusion, int seed) { this.confusion = confusion; rng = new System.Random(seed); }

        protected override void Corrupt(int i)
        {
            int t = cls[i];
            double u = rng.NextDouble(), acc = 0;
            for (int p = 0; p < SegClass.Count; p++)
            {
                acc += confusion[t, p];
                if (u <= acc) { cls[i] = (byte)p; conf[i] = p == t ? 0.9f : 0.6f; return; }
            }
        }
    }
}
