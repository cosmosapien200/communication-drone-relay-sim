using UnityEngine;

namespace CommDrone
{
    public enum FlightMode { Fast, Full }

    /// Constant mean wind plus randomised gusts (raised-cosine pulses), SPEC Section 5.
    public class WindModel
    {
        public Vector3 mean;
        public float gustAmp, gustDur, gustGap;
        Vector3 gustDir; float gustStart = -1f, gustPeak; float nextGust;
        System.Random rng;
        public Vector3 Current { get; private set; }
        public bool InGust { get; private set; }

        public WindModel(int seed) { rng = new System.Random(seed); }

        /// meanMax: max mean speed (m/s); gustMax: max gust amplitude (m/s).
        public void Randomize(float meanMax, float gustMax)
        {
            float a = (float)(rng.NextDouble() * 2 * System.Math.PI);
            mean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)rng.NextDouble() * meanMax;
            gustAmp = gustMax;
            gustDur = 0.8f + (float)rng.NextDouble() * 2.2f;
            gustGap = 1.5f + (float)rng.NextDouble() * 4f;
            nextGust = (float)rng.NextDouble() * gustGap;
            gustStart = -1f; Current = mean; InGust = false;
        }

        public void SetCalm() { mean = Vector3.zero; gustAmp = 0; Current = Vector3.zero; InGust = false; gustStart = -1f; }

        public Vector3 Step(float t)
        {
            Vector3 g = Vector3.zero;
            if (gustAmp > 0f)
            {
                if (gustStart < 0f && t >= nextGust)
                {
                    gustStart = t;
                    float a = (float)(rng.NextDouble() * 2 * System.Math.PI);
                    gustDir = new Vector3(Mathf.Cos(a), (float)(rng.NextDouble() - 0.5) * 0.3f, Mathf.Sin(a)).normalized;
                    gustPeak = gustAmp * (0.4f + 0.6f * (float)rng.NextDouble());
                    gustDur = 0.8f + (float)rng.NextDouble() * 2.2f;
                }
                if (gustStart >= 0f)
                {
                    float s = (t - gustStart) / gustDur;
                    if (s >= 1f) { gustStart = -1f; nextGust = t + gustGap * (0.5f + (float)rng.NextDouble()); }
                    else g = gustDir * gustPeak * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * s));
                }
            }
            InGust = gustStart >= 0f;
            Current = mean + g;
            return Current;
        }
    }

    /// Simulated flight controller (SPEC Section 5). Same velocity-setpoint interface in both modes.
    /// State is in map-local metres: position (x, altitude, z), yaw in degrees (Unity convention: about +y).
    public class FlightController
    {
        public FlightMode mode;
        public Vector3 position;     // x, altitude, z
        public Vector3 velocity;     // world frame, ground-relative
        public float yaw;            // deg
        public float yawRate;        // deg/s (actual)
        public Quaternion attitude = Quaternion.identity; // full mode (fast mode: yaw only)
        public Vector3 omega;        // body rates rad/s (full mode)
        public bool onGround;
        public float lastImpactSpeed; // vertical speed at last touchdown (m/s, positive down)
        public float time;

        // setpoint (body frame): x=right, y=up, z=forward
        Vector3 cmdBody; float cmdYawRate;

        // ---- fast-mode parameters (tuned to the full mode's measured step response; see docs/flight.md)
        public float tauH = 0.30f, tauV = 0.25f, tauYaw = 0.15f;   // first-order lag (s)
        public float accelLimitH = 4.0f, accelLimitV = 3.0f;         // m/s^2
        public float tauWindReject = 1.2f;                           // velocity-loop integrator rejection time (s)
        Vector3 windLP;

        // ---- full-mode quadrotor parameters
        public float mass = 1.2f, arm = 0.2f, maxRotorThrust = 7.0f, motorTau = 0.03f, km = 0.016f, dragCoef = 0.35f;
        public Vector3 inertia = new Vector3(0.012f, 0.020f, 0.012f); // body x (pitch), y (yaw), z (roll)
        public float kpVelH = 2.4f, kiVelH = 0.9f, kdVelH = 0.05f, kpVelV = 4.0f, kiVelV = 1.5f;
        public float kpAtt = 7.0f, kpRate = 18.0f, kpYawRate = 4.0f;
        public float maxTilt = 30f, maxAccelH = 5f;
        public float fullDt = 0.002f;
        Vector3 velInt, prevVelErr; float yawSet;
        readonly float[] rotorThrust = new float[4];
        float[,] mixInv;
        Vector3[] rotorPos;
        static readonly float[] spin = { 1, -1, 1, -1 }; // FR, FL, BL, BR

        const float G = 9.81f;

        public FlightController(FlightMode mode = FlightMode.Fast) { this.mode = mode; BuildMixer(); }

        public void Reset(Vector3 pos, float yawDeg)
        {
            position = pos; velocity = Vector3.zero; yaw = yawDeg; yawRate = 0; time = 0;
            attitude = Quaternion.Euler(0, yawDeg, 0); omega = Vector3.zero; onGround = pos.y <= 0.001f;
            cmdBody = Vector3.zero; cmdYawRate = 0; windLP = Vector3.zero; vCtrl = Vector3.zero; velInt = Vector3.zero; prevVelErr = Vector3.zero;
            yawSet = yawDeg; lastImpactSpeed = 0;
            float hover = mass * G / 4f;
            for (int i = 0; i < 4; i++) rotorThrust[i] = onGround ? 0 : hover;
        }

        /// SPEC interface: velocity setpoint in body frame (vx forward, vy right, vz up, yawRate deg/s), clipped to limits.
        public void SetVelocity(float vx, float vy, float vz, float yawRateCmd)
        {
            var h = new Vector2(vy, vx);
            if (h.magnitude > Sim.MaxHorizontalSpeed) h = h.normalized * Sim.MaxHorizontalSpeed;
            cmdBody = new Vector3(h.x, Mathf.Clamp(vz, -Sim.MaxVerticalSpeed, Sim.MaxVerticalSpeed), h.y);
            cmdYawRate = Mathf.Clamp(yawRateCmd, -Sim.MaxYawRate, Sim.MaxYawRate);
        }

        public Vector3 CommandWorld => Quaternion.Euler(0, yaw, 0) * cmdBody;
        /// Velocity in the body (yaw) frame: x right, y up, z forward.
        public Vector3 BodyVelocity => Quaternion.Euler(0, -yaw, 0) * velocity;
        public float[] RotorThrusts => rotorThrust;

        /// Advance by dt seconds with the given wind (world frame, m/s).
        public void Step(float dt, Vector3 wind)
        {
            if (mode == FlightMode.Fast) StepFast(dt, wind);
            else
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(dt / fullDt));
                float h = dt / n;
                for (int i = 0; i < n; i++) StepFull(h, wind);
            }
        }

        // ---------------- fast mode: first-order velocity tracking + accel limits + wind disturbance
        public float windGainFast = 0.6f; // fraction of the unrejected (high-pass) wind that moves the drone
        Vector3 vCtrl;                     // controller-tracked velocity component

        void StepFast(float dt, Vector3 wind)
        {
            time += dt;
            yawRate += (cmdYawRate - yawRate) * Mathf.Clamp01(dt / tauYaw);
            yaw = Mathf.Repeat(yaw + yawRate * dt, 360f);
            Vector3 cmd = Quaternion.Euler(0, yaw, 0) * cmdBody;
            if (onGround && cmd.y <= 0f) { vCtrl = Vector3.zero; velocity = Vector3.zero; windLP = wind; return; }
            Vector3 dv = cmd - vCtrl;
            Vector2 dh = new Vector2(dv.x, dv.z) * Mathf.Clamp01(dt / tauH);
            float maxH = accelLimitH * dt;
            if (dh.magnitude > maxH) dh = dh.normalized * maxH;
            float dvv = Mathf.Clamp(dv.y * Mathf.Clamp01(dt / tauV), -accelLimitV * dt, accelLimitV * dt);
            vCtrl += new Vector3(dh.x, dvv, dh.y);
            // the velocity loop's integrator rejects slow wind; the high-pass residual (gusts) pushes the drone
            windLP += (wind - windLP) * Mathf.Clamp01(dt / tauWindReject);
            Vector3 resid = wind - windLP; resid.y *= 0.3f;
            velocity = vCtrl + windGainFast * resid;
            Integrate(dt);
            if (onGround) vCtrl = Vector3.zero;
        }

        void Integrate(float dt)
        {
            position += velocity * dt;
            if (position.y <= 0f)
            {
                if (!onGround) lastImpactSpeed = Mathf.Max(0, -velocity.y);
                position.y = 0f; onGround = true;
                velocity = Vector3.zero;
            }
            else if (position.y > 0.02f) onGround = false;
        }

        // ---------------- full mode: rigid-body quadrotor, cascaded PID, rotor mixing
        void BuildMixer()
        {
            float a = arm / Mathf.Sqrt(2f);
            rotorPos = new[] { new Vector3(a, 0, a), new Vector3(-a, 0, a), new Vector3(-a, 0, -a), new Vector3(a, 0, -a) };
            // rows: collective, tau_x, tau_y (yaw), tau_z
            var M = new float[4, 4];
            for (int i = 0; i < 4; i++)
            {
                M[0, i] = 1f;
                M[1, i] = -rotorPos[i].z;
                M[2, i] = spin[i] * km;
                M[3, i] = rotorPos[i].x;
            }
            mixInv = Invert4(M);
        }

        void StepFull(float dt, Vector3 wind)
        {
            time += dt;
            // --- velocity loop (world frame) -> desired acceleration
            yawSet = Mathf.Repeat(yawSet + cmdYawRate * dt, 360f);
            Vector3 cmdW = Quaternion.Euler(0, yawSet, 0) * cmdBody;
            Vector3 err = cmdW - velocity;
            bool grounded = onGround && cmdW.y <= 0f;
            if (!grounded) velInt += err * dt;
            velInt = Vector3.ClampMagnitude(velInt, 4f);
            Vector3 dErr = (err - prevVelErr) / dt; prevVelErr = err;
            Vector3 aDes = new Vector3(kpVelH * err.x + kiVelH * velInt.x + kdVelH * dErr.x,
                                       kpVelV * err.y + kiVelV * velInt.y,
                                       kpVelH * err.z + kiVelH * velInt.z + kdVelH * dErr.z);
            var aH = new Vector2(aDes.x, aDes.z);
            if (aH.magnitude > maxAccelH) aH = aH.normalized * maxAccelH;
            aDes = new Vector3(aH.x, Mathf.Clamp(aDes.y, -6f, 8f), aH.y);
            // --- desired thrust direction (tilt-limited) and collective
            Vector3 fDes = mass * (aDes + new Vector3(0, G, 0));
            Vector3 upDes = fDes.normalized;
            float tilt = Vector3.Angle(Vector3.up, upDes);
            if (tilt > maxTilt) upDes = Vector3.Slerp(Vector3.up, upDes, maxTilt / tilt).normalized;
            Vector3 up = attitude * Vector3.up;
            float collective = Mathf.Max(0f, Vector3.Dot(fDes, up));
            if (grounded) collective = 0f;
            Vector3 fwdYaw = Quaternion.Euler(0, yawSet, 0) * Vector3.forward;
            Vector3 fwdDes = Vector3.ProjectOnPlane(fwdYaw, upDes).normalized;
            Quaternion qDes = Quaternion.LookRotation(fwdDes, upDes);
            // --- attitude loop -> desired body rates
            Quaternion qErr = Quaternion.Inverse(attitude) * qDes;
            qErr.ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            Vector3 rateDes = float.IsNaN(axis.x) ? Vector3.zero : axis * (ang * Mathf.Deg2Rad * kpAtt);
            rateDes.y = rateDes.y * (kpYawRate / kpAtt) + 0f;
            rateDes = Vector3.ClampMagnitude(rateDes, 6f);
            // --- rate loop -> torques
            Vector3 Iw = Vector3.Scale(inertia, omega);
            Vector3 tau = Vector3.Scale(inertia, (rateDes - omega) * kpRate) + Vector3.Cross(omega, Iw);
            // --- mixing -> rotor thrust commands, motor lag, saturation
            float[] w = { collective, tau.x, tau.y, tau.z };
            for (int i = 0; i < 4; i++)
            {
                float t = 0; for (int k = 0; k < 4; k++) t += mixInv[i, k] * w[k];
                t = Mathf.Clamp(t, 0f, maxRotorThrust);
                rotorThrust[i] += (t - rotorThrust[i]) * Mathf.Clamp01(dt / motorTau);
            }
            // --- rigid-body dynamics
            float T = 0; Vector3 tauB = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                T += rotorThrust[i];
                tauB += Vector3.Cross(rotorPos[i], new Vector3(0, rotorThrust[i], 0));
                tauB.y += spin[i] * km * rotorThrust[i];
            }
            Vector3 F = attitude * new Vector3(0, T, 0) + new Vector3(0, -mass * G, 0) + dragCoef * (wind - velocity);
            if (onGround && F.y <= 0f) { F = Vector3.zero; }
            velocity += F / mass * dt;
            Vector3 omegaDot = new Vector3((tauB.x - Vector3.Cross(omega, Iw).x) / inertia.x,
                                           (tauB.y - Vector3.Cross(omega, Iw).y) / inertia.y,
                                           (tauB.z - Vector3.Cross(omega, Iw).z) / inertia.z);
            omega += omegaDot * dt;
            float wm = omega.magnitude;
            if (wm > 1e-9f) attitude = (attitude * Quaternion.AngleAxis(wm * dt * Mathf.Rad2Deg, omega / wm)).normalized;
            if (onGround && F == Vector3.zero) { omega = Vector3.zero; attitude = Quaternion.Euler(0, attitude.eulerAngles.y, 0); }
            Integrate(dt);
            Vector3 f = attitude * Vector3.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (yaw < 0) yaw += 360f;
            yawRate = omega.y * Mathf.Rad2Deg;
        }

        static float[,] Invert4(float[,] a)
        {
            int n = 4; var m = new double[n, 2 * n];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) m[i, j] = a[i, j]; m[i, n + i] = 1; }
            for (int c = 0; c < n; c++)
            {
                int p = c; for (int r = c + 1; r < n; r++) if (System.Math.Abs(m[r, c]) > System.Math.Abs(m[p, c])) p = r;
                for (int j = 0; j < 2 * n; j++) { var t = m[c, j]; m[c, j] = m[p, j]; m[p, j] = t; }
                double d = m[c, c];
                for (int j = 0; j < 2 * n; j++) m[c, j] /= d;
                for (int r = 0; r < n; r++) if (r != c) { double f = m[r, c]; for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[c, j]; }
            }
            var inv = new float[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] = (float)m[i, n + j];
            return inv;
        }
    }
}
