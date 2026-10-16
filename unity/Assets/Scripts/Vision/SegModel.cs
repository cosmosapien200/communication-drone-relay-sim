using Unity.InferenceEngine;
using UnityEngine;

namespace CommDrone
{
    /// U-Net segmentation model wrapper (ONNX via Unity Inference Engine). Input: 128x128 RGB render;
    /// output: class probabilities [4, 128, 128], row 0 = top of the image (same convention as the PNG dataset).
    public class SegModel : System.IDisposable
    {
        public const int Res = 128;
        readonly Worker worker;
        readonly Tensor<float> input;
        public readonly float[] probs = new float[SegClass.Count * Res * Res];
        public readonly byte[] argmax = new byte[Res * Res];
        public readonly float[] conf = new float[Res * Res];

        public SegModel(ModelAsset asset, BackendType backend = BackendType.GPUCompute)
        {
            worker = new Worker(ModelLoader.Load(asset), backend);
            input = new Tensor<float>(new TensorShape(1, 3, Res, Res));
        }

        public void Run(Texture frame)
        {
            TextureConverter.ToTensor(frame, input, new TextureTransform().SetCoordOrigin(CoordOrigin.TopLeft).SetDimensions(Res, Res, 3));
            worker.Schedule(input);
            using var o = (worker.PeekOutput() as Tensor<float>).ReadbackAndClone();
            var data = o.DownloadToArray();
            System.Array.Copy(data, probs, probs.Length);
            int n = Res * Res;
            for (int i = 0; i < n; i++)
            {
                byte best = 0; float bp = probs[i];
                for (byte c = 1; c < SegClass.Count; c++) { float p = probs[c * n + i]; if (p > bp) { bp = p; best = c; } }
                argmax[i] = best; conf[i] = bp;
            }
        }

        public void Dispose() { worker?.Dispose(); input?.Dispose(); }
    }
}
