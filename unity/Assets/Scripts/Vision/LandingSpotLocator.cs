using System.Collections.Generic;
using UnityEngine;

namespace CommDrone
{
    /// Task C (SPEC Section 6): landing-spot localisation from the nadir camera.
    /// Segments the frame, takes the landable connected component at (or nearest to) where the target patch
    /// is expected, and reports its centre offset, apparent area, confidence and edge clearance.
    public class LandingSpotLocator : ISpotDetector
    {
        readonly SegModel model;
        readonly Camera cam;
        readonly RenderTexture rt;
        public float minProb = 0.5f;
        public float searchRadius = 0.35f; // fraction of the image width around the expected centre
        public int minPixels = 12;
        readonly int[] label = new int[SegModel.Res * SegModel.Res];
        readonly int[] queue = new int[SegModel.Res * SegModel.Res];
        public byte[] LastMask => model.argmax;

        public LandingSpotLocator(SegModel model, Camera cam)
        {
            this.model = model; this.cam = cam;
            rt = new RenderTexture(SegModel.Res, SegModel.Res, 24) { antiAliasing = 1 };
        }

        public void ResetEpisode() { }

        public SpotDetection Detect(Vector3 dronePos, float yawDeg, GroundFeature patch)
        {
            // dronePos is map-local (x, alt, z); the camera is placed in world space by the caller via `worldOrigin`.
            cam.transform.SetPositionAndRotation(worldOrigin + dronePos, Quaternion.Euler(90f, yawDeg, 0f));
            cam.fieldOfView = NadirCamera.FovDeg;
            var prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
            model.Run(rt);
            // expected patch centre in pixels from the planner's map position (dead reckoning)
            var expected = new GeometricSpotSensor().Detect(dronePos, yawDeg, patch).offset;
            return Locate(model.argmax, model.probs, expected, dronePos.y);
        }

        public Vector3 worldOrigin;

        public SpotDetection Locate(byte[] cls, float[] probs, Vector2 expectedOffset, float alt)
        {
            int R = SegModel.Res, n = R * R;
            System.Array.Clear(label, 0, n);
            float ex = (expectedOffset.x + 1f) * 0.5f * R, ey = (1f - expectedOffset.y) * 0.5f * R; // pixel coords (v from top)
            int bestLabel = -1, comp = 0; float bestDist = float.MaxValue;
            var comps = new List<(int count, float sx, float sy, float sp)>();
            for (int s = 0; s < n; s++)
            {
                if (label[s] != 0 || cls[s] != SegClass.Landable || probs[SegClass.Landable * n + s] < minProb) continue;
                comp++;
                int head = 0, tail = 0; queue[tail++] = s; label[s] = comp;
                int count = 0; float sx = 0, sy = 0, sp = 0, dmin = float.MaxValue;
                while (head < tail)
                {
                    int k = queue[head++]; int x = k % R, y = k / R;
                    count++; sx += x; sy += y; sp += probs[SegClass.Landable * n + k];
                    float d = (x - ex) * (x - ex) + (y - ey) * (y - ey);
                    if (d < dmin) dmin = d;
                    Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
                }
                comps.Add((count, sx, sy, sp));
                if (count >= minPixels && dmin < bestDist) { bestDist = dmin; bestLabel = comp; }

                void Push(int x, int y)
                {
                    if (x < 0 || y < 0 || x >= R || y >= R) return;
                    int q = y * R + x;
                    if (label[q] != 0 || cls[q] != SegClass.Landable || probs[SegClass.Landable * n + q] < minProb) return;
                    label[q] = comp; queue[tail++] = q;
                }
            }
            if (bestLabel < 0 || Mathf.Sqrt(bestDist) > searchRadius * R) return default;
            var c = comps[bestLabel - 1];
            float cx = c.sx / c.count, cy = c.sy / c.count;
            // clearance: distance from the component centroid to the nearest pixel outside the component (or image edge)
            int ci = Mathf.Clamp((int)cy, 0, R - 1) * R + Mathf.Clamp((int)cx, 0, R - 1);
            float clearPx = label[ci] == bestLabel ? ClearanceAt((int)cx, (int)cy, bestLabel) : 0f;
            float gsd = 2f * NadirCamera.HalfExtent(alt) / R; // metres per pixel
            return new SpotDetection
            {
                offset = new Vector2(cx / R * 2f - 1f + 1f / R, 1f - cy / R * 2f - 1f / R),
                area = c.count / (float)n,
                confidence = c.sp / c.count,
                clearance = clearPx * gsd,
            };
        }

        float ClearanceAt(int cx, int cy, int lbl)
        {
            int R = SegModel.Res;
            for (int r = 1; r < R; r++)
            {
                // scan the square ring of radius r
                for (int t = -r; t <= r; t++)
                {
                    if (Outside(cx + t, cy - r) || Outside(cx + t, cy + r) || Outside(cx - r, cy + t) || Outside(cx + r, cy + t)) return r;
                }
            }
            return R;
            bool Outside(int x, int y) => x < 0 || y < 0 || x >= R || y >= R ? false : label[y * R + x] != lbl;
        }
    }
}
