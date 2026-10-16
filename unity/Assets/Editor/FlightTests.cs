using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CommDrone.EditorTools
{
    /// WP3 Drones-subject experiments: per-axis step responses (full vs fast mode), gust response and
    /// waypoint tracking. Writes CSVs to results/flight/; python/eval/flight_analysis.py computes metrics and plots.
    public static class FlightTests
    {
        const string Dir = "../results/flight";
        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static void RunAll()
        {
            Directory.CreateDirectory(Dir);
            foreach (var mode in new[] { FlightMode.Full, FlightMode.Fast })
            {
                Step(mode, "vx", new Vector4(2f, 0, 0, 0));
                Step(mode, "vy", new Vector4(0, 2f, 0, 0));
                Step(mode, "vz", new Vector4(0, 0, 0.8f, 0));
                Step(mode, "yaw", new Vector4(0, 0, 0, 60f));
                Gust(mode);
                Square(mode);
            }
            Debug.Log("FlightTests done");
        }

        static FlightController Make(FlightMode m)
        {
            var fc = new FlightController(m);
            fc.Reset(new Vector3(0, 5, 0), 0);
            return fc;
        }

        /// Hover 1 s, then step the given setpoint (vx fwd, vy right, vz up, yawRate) for 5 s.
        static void Step(FlightMode mode, string axis, Vector4 sp)
        {
            var fc = Make(mode);
            var sb = new StringBuilder("t,cmd,resp,tilt_deg\n");
            float dt = 0.01f;
            for (int i = 0; i < 600; i++)
            {
                float t = i * dt;
                bool on = t >= 1f;
                fc.SetVelocity(on ? sp.x : 0, on ? sp.y : 0, on ? sp.z : 0, on ? sp.w : 0);
                fc.Step(dt, Vector3.zero);
                var bv = fc.BodyVelocity;
                float resp = axis switch { "vx" => bv.z, "vy" => bv.x, "vz" => fc.velocity.y, _ => fc.yawRate };
                float cmd = on ? axis switch { "vx" => sp.x, "vy" => sp.y, "vz" => sp.z, _ => sp.w } : 0f;
                float tilt = Vector3.Angle(fc.attitude * Vector3.up, Vector3.up);
                sb.Append(F(t)).Append(',').Append(F(cmd)).Append(',').Append(F(resp)).Append(',').Append(F(tilt)).Append('\n');
            }
            File.WriteAllText(Path.Combine(Dir, $"step_{mode}_{axis}.csv"), sb.ToString());
        }

        /// Position hold (P on position -> velocity setpoint) under a 3 m/s, 2 s raised-cosine gust.
        static void Gust(FlightMode mode)
        {
            var fc = Make(mode);
            var sb = new StringBuilder("t,wind,err_x,err_z,err\n");
            float dt = 0.01f;
            Vector3 hold = fc.position;
            for (int i = 0; i < 1000; i++)
            {
                float t = i * dt;
                float s = (t - 2f) / 2f;
                float w = s >= 0 && s <= 1 ? 3f * 0.5f * (1 - Mathf.Cos(2 * Mathf.PI * s)) : 0f;
                var wind = new Vector3(w, 0, 0);
                Vector3 e = hold - fc.position;
                Vector3 vw = 1.2f * e; // world velocity setpoint; yaw = 0 so body == world
                fc.SetVelocity(vw.z, vw.x, vw.y, 0);
                fc.Step(dt, wind);
                sb.Append(F(t)).Append(',').Append(F(w)).Append(',').Append(F(fc.position.x - hold.x)).Append(',').Append(F(fc.position.z - hold.z))
                  .Append(',').Append(F(new Vector2(fc.position.x - hold.x, fc.position.z - hold.z).magnitude)).Append('\n');
            }
            File.WriteAllText(Path.Combine(Dir, $"gust_{mode}.csv"), sb.ToString());
        }

        /// 20 m square at 2 m/s with a lookahead velocity controller; logs cross-track error.
        static void Square(FlightMode mode)
        {
            var fc = Make(mode);
            var wps = new[] { new Vector2(0, 0), new Vector2(0, 20), new Vector2(20, 20), new Vector2(20, 0), new Vector2(0, 0) };
            var sb = new StringBuilder("t,x,z,seg,cross_track\n");
            float dt = 0.01f; int seg = 0; float t = 0;
            while (seg < wps.Length - 1 && t < 80f)
            {
                Vector2 a = wps[seg], b = wps[seg + 1];
                var p = new Vector2(fc.position.x, fc.position.z);
                var ab = (b - a).normalized;
                float along = Vector2.Dot(p - a, ab);
                Vector2 closest = a + ab * along;
                float cross = (p - closest).magnitude;
                if ((b - p).magnitude < 0.5f) { seg++; continue; }
                Vector2 v = ab * Mathf.Min(2f, (b - p).magnitude) + (closest - p) * 1.5f;
                fc.SetVelocity(v.y, v.x, 0, 0); // yaw stays 0: body forward = +z, right = +x
                fc.Step(dt, Vector3.zero);
                t += dt;
                sb.Append(F(t)).Append(',').Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(seg).Append(',').Append(F(cross)).Append('\n');
            }
            File.WriteAllText(Path.Combine(Dir, $"square_{mode}.csv"), sb.ToString());
        }
    }
}
