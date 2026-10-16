using UnityEngine;

namespace CommDrone
{
    /// Log-distance path loss with per-wall material attenuation (SPEC Section 4).
    /// RSSI = P0 - 10 n log10(d/d0) - sum(L_k) - groundPenalty(onGroundB) + X_sigma.
    /// Each obstacle run crossed counts as two walls (entry and exit). Values are simulation parameters
    /// chosen within literature ranges (e.g. ITU-R P.2040 / P.1238-style building-entry and wall losses), not measurements.
    [System.Serializable]
    public class WifiParams
    {
        public float P0 = -40f;          // dBm at d0
        public float d0 = 1f;            // m
        public float n = 2.1f;           // path-loss exponent (urban LOS-dominated streets)
        public float lossConcrete = 12f; // dB per wall
        public float lossBrick = 8f;
        public float lossRubble = 6f;
        public float groundPenalty = 3f; // dB extra when B rests on the ground
        public float sigma = 2f;         // shadowing std (dB), 0 disables
        public float tLink = -80f;       // minimum usable link RSSI
        public float margin = 6f;        // safety margin M
        /// RSSI a spot must show (measured at cruise altitude) to qualify for landing.
        public float QualifyThreshold => tLink + margin + groundPenalty;
    }

    public class WifiModel
    {
        public readonly WifiParams p;
        readonly TownMap map;
        System.Random rng;

        public WifiModel(TownMap map, WifiParams p = null, int seed = 0)
        {
            this.map = map; this.p = p ?? new WifiParams(); rng = new System.Random(seed);
        }

        /// The frozen interface: RSSI in dBm between world positions A and B.
        public float RSSI(Vector3 posA, Vector3 posB, bool onGroundB) => RSSI(posA, posB, onGroundB, p.sigma > 0f);

        public float RSSI(Vector3 posA, Vector3 posB, bool onGroundB, bool noise)
        {
            Vector2 a = map.ToLocal(posA), b = map.ToLocal(posB);
            float d = Mathf.Max(Vector3.Distance(posA, posB), p.d0);
            float rssi = p.P0 - 10f * p.n * Mathf.Log10(d / p.d0) - WallLoss(a, b);
            if (onGroundB) rssi -= p.groundPenalty;
            if (noise) rssi += p.sigma * Gaussian();
            return rssi;
        }

        public float WallLoss(Vector2 a, Vector2 b)
        {
            float loss = 0f;
            map.ForEachObstacleRun(a, b, mat => loss += 2f * MaterialLoss(mat));
            return loss;
        }

        public float MaterialLoss(WallMaterial m) => m switch
        {
            WallMaterial.Concrete => p.lossConcrete,
            WallMaterial.Brick => p.lossBrick,
            WallMaterial.Rubble => p.lossRubble,
            _ => 0f,
        };

        float Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
