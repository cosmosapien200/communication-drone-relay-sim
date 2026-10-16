using UnityEngine;

namespace CommDrone
{
    /// Measured detector error rates (written by python/vision/make_noise_json.py into Resources/vision_noise.json).
    [System.Serializable]
    public class VisionNoiseFile
    {
        public float[] confusion = new float[16]; // row-major P(pred | true), 4x4
        public float fnRate = 0.08f, fpRate = 0.02f, locSigma = 0.05f, locBias = 0.03f, areaSigma = 0.1f, dropRate = 0.03f, confNoise = 0.15f;
        public string source = "placeholder";
    }

    public class VisionNoise
    {
        public float[,] confusion = new float[4, 4];
        public VisionNoiseParams spot = new VisionNoiseParams();
        public string source;
    }

    public static class VisionCalibration
    {
        public static VisionNoise LoadNoise()
        {
            var ta = Resources.Load<TextAsset>("vision_noise");
            var f = ta ? JsonUtility.FromJson<VisionNoiseFile>(ta.text) : new VisionNoiseFile();
            var n = new VisionNoise { source = f.source };
            bool empty = true;
            for (int i = 0; i < 16; i++) if (f.confusion[i] > 0) empty = false;
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) n.confusion[r, c] = empty ? (r == c ? 0.9f : 0.1f / 3f) : f.confusion[r * 4 + c];
            n.spot = new VisionNoiseParams { fnRate = f.fnRate, fpRate = f.fpRate, locSigma = f.locSigma, locBias = f.locBias, areaSigma = f.areaSigma, dropRate = f.dropRate, confNoise = f.confNoise };
            return n;
        }

        /// Detector-quality knob: k=0 perfect, k=1 measured, k>1 weakened (off-diagonal error mass scaled by k).
        public static float[,] ScaledConfusion(float[,] c, float k)
        {
            var o = new float[4, 4];
            for (int r = 0; r < 4; r++)
            {
                float off = 0;
                for (int q = 0; q < 4; q++) if (q != r) { o[r, q] = c[r, q] * k; off += o[r, q]; }
                if (off > 0.95f) { for (int q = 0; q < 4; q++) if (q != r) o[r, q] *= 0.95f / off; off = 0.95f; }
                o[r, r] = 1f - off;
            }
            return o;
        }

        public static VisionNoiseParams Scaled(VisionNoiseParams p, float k) => new VisionNoiseParams
        {
            fnRate = Mathf.Clamp01(p.fnRate * k), fpRate = Mathf.Clamp01(p.fpRate * k), locSigma = p.locSigma * k,
            locBias = p.locBias * k, areaSigma = p.areaSigma * k, dropRate = Mathf.Clamp01(p.dropRate * k), confNoise = Mathf.Clamp01(p.confNoise * k),
        };
    }
}
