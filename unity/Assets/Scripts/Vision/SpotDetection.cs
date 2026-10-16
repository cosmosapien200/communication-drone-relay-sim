using UnityEngine;

namespace CommDrone
{
    /// Output of the landing-spot locator (SPEC Section 6, Task C). Shared by the real locator and the noise model.
    public struct SpotDetection
    {
        public Vector2 offset;    // patch centre offset from image centre, normalised to [-1, 1] (x right, y forward)
        public float area;        // fraction of the image covered by the patch
        public float confidence;  // 0 when the patch is not detected
        public float clearance;   // metres from patch centre to the nearest non-landable edge
        public bool detected => confidence > 0f;
    }

    /// Anything that can report the target patch from the nadir camera (real CNN locator or ground truth + noise).
    public interface ISpotDetector
    {
        SpotDetection Detect(Vector3 dronePos, float yawDeg, GroundFeature patch);
        void ResetEpisode();
    }

    public static class NadirCamera
    {
        public const float FovDeg = 60f;
        public const int Resolution = 128;
        public static float HalfExtent(float alt) => Mathf.Max(alt, 0.05f) * Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad);
    }

    /// Exact geometric detection of a patch in the nadir image (ground truth).
    public class GeometricSpotSensor : ISpotDetector
    {
        public void ResetEpisode() { }

        public SpotDetection Detect(Vector3 dronePos, float yawDeg, GroundFeature patch)
        {
            float alt = Mathf.Max(dronePos.y, 0.05f);
            float he = NadirCamera.HalfExtent(alt);
            Vector2 d = patch.center - new Vector2(dronePos.x, dronePos.z);
            // world -> body (x right, y forward)
            float a = yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            var body = new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
            var off = body / he;
            // visible fraction: patch rect ∩ footprint (approximated axis-aligned in world)
            float ix = Overlap(patch.center.x - patch.size.x / 2, patch.center.x + patch.size.x / 2, dronePos.x - he, dronePos.x + he);
            float iy = Overlap(patch.center.y - patch.size.y / 2, patch.center.y + patch.size.y / 2, dronePos.z - he, dronePos.z + he);
            float visible = ix * iy;
            float patchArea = patch.size.x * patch.size.y;
            var det = new SpotDetection
            {
                offset = off,
                area = Mathf.Clamp01(visible / (4f * he * he)),
                clearance = Mathf.Min(patch.size.x, patch.size.y) * 0.5f,
            };
            bool centreInView = Mathf.Abs(off.x) <= 1f && Mathf.Abs(off.y) <= 1f;
            det.confidence = (centreInView || visible / patchArea > 0.4f) && visible > 0f ? 1f : 0f;
            return det;
        }

        static float Overlap(float a0, float a1, float b0, float b1) => Mathf.Max(0f, Mathf.Min(a1, b1) - Mathf.Max(a0, b0));
    }

    [System.Serializable]
    public class VisionNoiseParams
    {
        public float fnRate = 0.08f;     // per-frame probability a visible patch is missed
        public float fpRate = 0.02f;     // per-frame probability of a spurious detection
        public float locSigma = 0.05f;   // localisation noise (normalised image units)
        public float locBias = 0.03f;    // max per-episode localisation bias (normalised)
        public float areaSigma = 0.1f;   // relative area noise
        public float dropRate = 0.03f;   // dropped frames (no output at all)
        public float confNoise = 0.15f;  // confidence jitter for true detections
    }

    /// VisionNoiseModel (SPEC Section 6/11): ground truth corrupted with the detector's measured error rates.
    public class VisionNoiseModel : ISpotDetector
    {
        public VisionNoiseParams p;
        readonly ISpotDetector truth;
        readonly System.Random rng;
        Vector2 bias;

        public VisionNoiseModel(VisionNoiseParams p, int seed, ISpotDetector truth = null)
        {
            this.p = p; rng = new System.Random(seed); this.truth = truth ?? new GeometricSpotSensor();
        }

        public void ResetEpisode()
        {
            bias = new Vector2(U() * 2 - 1, U() * 2 - 1) * p.locBias;
            truth.ResetEpisode();
        }

        public SpotDetection Detect(Vector3 dronePos, float yawDeg, GroundFeature patch)
        {
            var d = truth.Detect(dronePos, yawDeg, patch);
            if (U() < p.dropRate) return default;
            if (d.detected)
            {
                if (U() < p.fnRate) return default;
                d.offset += bias + new Vector2(N(), N()) * p.locSigma;
                d.area = Mathf.Clamp01(d.area * (1f + p.areaSigma * N()));
                d.confidence = Mathf.Clamp(1f - Mathf.Abs(N()) * p.confNoise, 0.05f, 1f);
                d.clearance = Mathf.Max(0f, d.clearance * (1f + 0.1f * N()));
            }
            else if (U() < p.fpRate)
            {
                d.offset = new Vector2(U() * 2 - 1, U() * 2 - 1);
                d.area = U() * 0.3f;
                d.confidence = 0.2f + 0.5f * U();
                d.clearance = U() * 2f;
            }
            return d;
        }

        float U() => (float)rng.NextDouble();
        float N()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
