using Unity.InferenceEngine;
using UnityEngine;

namespace CommDrone
{
    /// Runs an ML-Agents-exported ONNX policy directly (deterministic actions). Used by NavPolicy/LandPolicy in
    /// missions so the planner can drive the policies without ML-Agents episode bookkeeping.
    public class PolicyRunner : System.IDisposable
    {
        readonly Worker worker;
        readonly Tensor<float> input;
        readonly int obsSize;
        readonly float[] result = new float[3];

        public PolicyRunner(ModelAsset asset, int obsSize)
        {
            this.obsSize = obsSize;
            worker = new Worker(ModelLoader.Load(asset), BackendType.CPU);
            input = new Tensor<float>(new TensorShape(1, obsSize));
        }

        /// Returns the 3 continuous actions clipped to [-1, 1].
        public Vector3 Act(float[] obs)
        {
            for (int i = 0; i < obsSize; i++) input[0, i] = obs[i];
            worker.Schedule(input);
            using var o = (worker.PeekOutput("deterministic_continuous_actions") as Tensor<float>).ReadbackAndClone();
            for (int i = 0; i < 3; i++) result[i] = Mathf.Clamp(o[0, i], -1f, 1f);
            return new Vector3(result[0], result[1], result[2]);
        }

        public void Dispose() { worker?.Dispose(); input?.Dispose(); }
    }
}
