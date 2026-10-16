using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CommDrone
{
    /// Demo visualisation for MissionRunner (SPEC Section 14): 3D town, drones, RSSI-coloured links, shared-map
    /// minimap, camera picture-in-picture with segmentation overlay and the on-screen display
    /// (mode, RSSI to parent, chain weakest link, battery, detection confidence, current reward).
    /// Args: -record <dir> writes one PNG per frame (python/tools/make_video.sh turns them into an MP4).
    public class MissionView : MonoBehaviour
    {
        MissionRunner runner;
        Camera mainCam, pipCam;
        RenderTexture pipRT;
        Texture2D minimap, segOverlay;
        GameObject droneGo;
        readonly List<GameObject> relayGos = new List<GameObject>();
        readonly List<LineRenderer> links = new List<LineRenderer>();
        LineRenderer trailLine;
        Material lineMat, droneMat, relayMat, baseMat, campMat;
        string recordDir;
        int frame;
        Vector3 camVel;
        TownMap shownMap;
        GUIStyle style, big;
        SegModel overlayModel;
        float overviewUntil;

        void Start()
        {
            runner = GetComponent<MissionRunner>();
            if (!runner.demo) { enabled = false; return; }
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-record") recordDir = a[i + 1];
            if (recordDir != null) { Directory.CreateDirectory(recordDir); Time.captureFramerate = 30; }
            mainCam = new GameObject("MainCamera").AddComponent<Camera>();
            mainCam.tag = "MainCamera"; mainCam.fieldOfView = 55; mainCam.farClipPlane = 600;
            mainCam.clearFlags = CameraClearFlags.SolidColor; mainCam.backgroundColor = new Color(0.62f, 0.68f, 0.78f);
            pipRT = new RenderTexture(256, 256, 24);
            pipCam = new GameObject("PipCamera").AddComponent<Camera>();
            pipCam.targetTexture = pipRT; pipCam.fieldOfView = NadirCamera.FovDeg; pipCam.nearClipPlane = 0.05f;
            pipCam.clearFlags = CameraClearFlags.SolidColor; pipCam.backgroundColor = mainCam.backgroundColor;
            var unlit = Shader.Find("Sprites/Default");
            lineMat = new Material(unlit);
            droneMat = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.55f, 0.1f) };
            relayMat = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.6f, 1f) };
            baseMat = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.9f, 0.2f) };
            campMat = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.15f, 0.15f) };
            trailLine = NewLine(0.12f);
            var seg = Resources.Load<Unity.InferenceEngine.ModelAsset>("Models/" + runner.segModel);
            if (seg) overlayModel = new SegModel(seg);
            segOverlay = new Texture2D(SegModel.Res, SegModel.Res, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            style = new GUIStyle { fontSize = 16, normal = { textColor = Color.white } };
            big = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            overviewUntil = Time.time + 6f;
        }

        LineRenderer NewLine(float w)
        {
            var lr = new GameObject("Line").AddComponent<LineRenderer>();
            lr.material = lineMat; lr.widthMultiplier = w; lr.positionCount = 0;
            return lr;
        }

        static GameObject Drone(Material m)
        {
            var root = new GameObject("Drone");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.12f, 0.35f); body.GetComponent<Renderer>().sharedMaterial = m;
            for (int i = 0; i < 4; i++)
            {
                float ang = 45 + 90 * i;
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.transform.SetParent(root.transform, false);
                arm.transform.localRotation = Quaternion.Euler(0, ang, 0); arm.transform.localScale = new Vector3(0.05f, 0.04f, 0.6f);
                arm.GetComponent<Renderer>().sharedMaterial = m;
                var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder); rotor.transform.SetParent(root.transform, false);
                rotor.transform.localPosition = Quaternion.Euler(0, ang, 0) * new Vector3(0, 0.06f, 0.3f);
                rotor.transform.localScale = new Vector3(0.3f, 0.01f, 0.3f);
                rotor.GetComponent<Renderer>().sharedMaterial = m;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Destroy(c);
            root.transform.localScale = Vector3.one * 1.6f; // slightly enlarged for visibility
            return root;
        }

        void LateUpdate()
        {
            if (runner.truth == null) return;
            if (shownMap != runner.truth) SetupMap();
            var d = runner.current;
            // drone
            if (d != null)
            {
                if (!droneGo) droneGo = Drone(droneMat);
                droneGo.SetActive(!d.Finished || d.mode == MissionMode.Done);
                droneGo.transform.position = d.World + Vector3.up * 0.1f;
                droneGo.transform.rotation = d.fc.mode == FlightMode.Full ? d.fc.attitude : Quaternion.Euler(0, d.fc.yaw, 0);
                trailLine.positionCount = d.trail.Count;
                for (int i = 0; i < d.trail.Count; i++) trailLine.SetPosition(i, runner.truth.ToWorld(d.trail[i], Sim.CruiseAltitude));
                trailLine.startColor = trailLine.endColor = new Color(1f, 0.6f, 0.2f, 0.6f);
            }
            // relays and links
            var nodes = runner.shared.nodes;
            while (relayGos.Count < nodes.Count - 1) relayGos.Add(Drone(relayMat));
            for (int i = 1; i < nodes.Count; i++) relayGos[i - 1].transform.position = nodes[i] - Vector3.up * 0.2f;
            int nLinks = nodes.Count - 1 + (d != null && !d.Finished ? 1 : 0) + (runner.covered ? 1 : 0);
            while (links.Count < nLinks) links.Add(NewLine(0.25f));
            int li = 0;
            for (int i = 1; i < nodes.Count; i++) SetLink(links[li++], nodes[i - 1], nodes[i], runner.wifi.RSSI(nodes[i - 1], nodes[i], true, false));
            if (d != null && !d.Finished) SetLink(links[li++], nodes[nodes.Count - 1], d.World, d.rssiToParent);
            if (runner.covered) SetLink(links[li++], nodes[nodes.Count - 1], runner.truth.ToWorld(runner.truth.TargetPos, 1.5f), runner.wifi.RSSI(nodes[nodes.Count - 1], runner.truth.ToWorld(runner.truth.TargetPos, 1.5f), true, false));
            for (int i = li; i < links.Count; i++) links[i].positionCount = 0;
            UpdateCamera(d);
            if (d != null && Time.frameCount % 3 == 0) UpdatePip(d);
            if (Time.frameCount % 15 == 0) UpdateMinimap(d);
            if (recordDir != null) ScreenCapture.CaptureScreenshot(Path.Combine(recordDir, $"f{frame++:D5}.png"));
        }

        void SetLink(LineRenderer lr, Vector3 a, Vector3 b, float rssi)
        {
            lr.positionCount = 2; lr.SetPosition(0, a + Vector3.up * 0.5f); lr.SetPosition(1, b + Vector3.up * 0.5f);
            float t = Mathf.InverseLerp(-80f, -55f, rssi);
            var c = rssi < -80f ? Color.red : Color.Lerp(new Color(1f, 0.8f, 0f), new Color(0.1f, 1f, 0.3f), t);
            lr.startColor = lr.endColor = c;
        }

        void SetupMap()
        {
            shownMap = runner.truth;
            foreach (var g in relayGos) Destroy(g);
            relayGos.Clear();
            var m = shownMap;
            var bs = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bs.transform.position = m.ToWorld(m.BasePos, 1.5f); bs.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
            bs.GetComponent<Renderer>().sharedMaterial = baseMat;
            var camp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            camp.transform.position = m.ToWorld(m.TargetPos, 1f); camp.transform.localScale = new Vector3(3f, 2f, 3f);
            camp.GetComponent<Renderer>().sharedMaterial = campMat;
            minimap = new Texture2D(m.W, m.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            mainCam.transform.position = m.ToWorld(new Vector2(m.W * 0.5f, -m.H * 0.25f), m.W * 0.9f);
            mainCam.transform.LookAt(m.ToWorld(new Vector2(m.W * 0.5f, m.H * 0.5f), 0f));
        }

        void UpdateCamera(DroneMission d)
        {
            var m = runner.truth;
            Vector3 target, look;
            if (d == null || Time.time < overviewUntil || d.Finished)
            {
                target = m.ToWorld(new Vector2(m.W * 0.5f, -m.H * 0.15f), m.W * 0.85f);
                look = m.ToWorld(new Vector2(m.W * 0.5f, m.H * 0.55f), 0f);
            }
            else
            {
                var back = Quaternion.Euler(0, d.fc.yaw, 0) * new Vector3(0, 0, -1f);
                bool landing = d.mode == MissionMode.Land;
                target = d.World + back * (landing ? 6f : 14f) + Vector3.up * (landing ? 5f : 16f);
                look = d.World;
            }
            mainCam.transform.position = Vector3.SmoothDamp(mainCam.transform.position, target, ref camVel, 0.6f);
            var q = Quaternion.LookRotation(look - mainCam.transform.position);
            mainCam.transform.rotation = Quaternion.Slerp(mainCam.transform.rotation, q, 0.15f);
        }

        void UpdatePip(DroneMission d)
        {
            bool nadir = d.mode == MissionMode.Land;
            pipCam.transform.SetPositionAndRotation(d.World, Quaternion.Euler(nadir ? 90f : 45f, d.fc.yaw, 0f));
            pipCam.Render();
            if (overlayModel == null || (d.mode != MissionMode.Explore && d.mode != MissionMode.Navigate && !nadir)) return;
            overlayModel.Run(pipRT);
            var px = new Color32[SegModel.Res * SegModel.Res];
            Color32[] pal = { new Color32(0, 0, 0, 0), new Color32(0, 255, 0, 110), new Color32(255, 0, 0, 60), new Color32(0, 80, 255, 70) };
            for (int v = 0; v < SegModel.Res; v++)
                for (int u = 0; u < SegModel.Res; u++)
                    px[(SegModel.Res - 1 - v) * SegModel.Res + u] = pal[overlayModel.argmax[v * SegModel.Res + u]];
            segOverlay.SetPixels32(px); segOverlay.Apply();
        }

        void UpdateMinimap(DroneMission d)
        {
            var s = d != null && !d.Finished ? d.map : runner.shared;
            var px = new Color32[s.W * s.H];
            for (int k = 0; k < px.Length; k++)
            {
                if (!s.explored[k]) px[k] = new Color32(20, 20, 25, 200);
                else if (s.obstacle[k]) px[k] = new Color32(90, 90, 110, 230);
                else if (s.IsLandable(k)) px[k] = new Color32(40, 230, 60, 255);
                else px[k] = new Color32(160, 160, 150, 230);
            }
            void Dot(Vector2 p, Color32 c, int r)
            {
                var cc = s.CellOf(p);
                for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                    if (s.InBounds(cc.x + x, cc.y + y)) px[s.Idx(cc.x + x, cc.y + y)] = c;
            }
            foreach (var n in runner.shared.nodes) Dot(runner.truth.ToLocal(n), new Color32(40, 140, 255, 255), 1);
            Dot(runner.truth.TargetPos, new Color32(255, 40, 40, 255), 2);
            if (d != null && !d.Finished) { Dot(d.Pos, new Color32(255, 150, 0, 255), 1); Dot(d.goal, new Color32(255, 255, 0, 255), 0); }
            minimap.SetPixels32(px); minimap.Apply();
        }

        void OnGUI()
        {
            if (runner == null || runner.truth == null) return;
            var d = runner.current;
            float W = Screen.width, H = Screen.height;
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(8, 8, 360, 210), Texture2D.whiteTexture); GUI.color = Color.white;
            float chain = float.NaN;
            var nodes = runner.shared.nodes;
            for (int i = 1; i < nodes.Count; i++) { float r = runner.wifi.RSSI(nodes[i - 1], nodes[i], true, false); chain = float.IsNaN(chain) ? r : Mathf.Min(chain, r); }
            GUI.Label(new Rect(16, 12, 350, 28), d != null ? $"Drone {d.index}   MODE: {d.mode.ToString().ToUpper()}" : "", big);
            if (d != null)
            {
                GUI.Label(new Rect(16, 44, 350, 22), $"RSSI to parent: {d.rssiToParent:F1} dBm  (qualify ≥ {runner.wifi.p.QualifyThreshold:F0})", style);
                GUI.Label(new Rect(16, 66, 350, 22), $"Chain weakest link: {(float.IsNaN(chain) ? "–" : chain.ToString("F1") + " dBm")}", style);
                GUI.Label(new Rect(16, 88, 350, 22), $"Battery: {100f * d.battery / runner.cfg.batteryCapacity:F0} %   t = {d.time:F0} s", style);
                GUI.Label(new Rect(16, 110, 350, 22), $"Detection confidence: {(d.mode == MissionMode.Land ? d.detConfidence.ToString("F2") : "–")}", style);
                GUI.Label(new Rect(16, 132, 350, 22), $"Nav reward (step): {d.lastReward:+0.000;-0.000}", style);
                GUI.Label(new Rect(16, 154, 350, 22), $"Wind: {d.wind.Current.magnitude:F1} m/s {(d.wind.InGust ? "GUST" : "")}", style);
                GUI.Label(new Rect(16, 176, 350, 22), $"Relays landed: {nodes.Count - 1}   target: {(runner.covered ? "COVERED" : "not covered")}", style);
            }
            if (!string.IsNullOrEmpty(runner.status) && (d == null || d.Finished || runner.covered))
                GUI.Label(new Rect(W * 0.5f - 160, 20, 320, 30), runner.status, big);
            // minimap
            if (minimap) { float s = Mathf.Min(260, H * 0.38f); GUI.DrawTexture(new Rect(W - s - 10, 10, s, s), minimap); GUI.Label(new Rect(W - s - 10, s + 12, s, 20), "shared map (green = landable)", style); }
            // camera PiP + segmentation
            if (pipRT && d != null && !d.Finished)
            {
                float s = Mathf.Min(256, H * 0.36f);
                var r = new Rect(W - s - 10, H - s - 34, s, s);
                GUI.DrawTexture(r, pipRT);
                if (segOverlay) GUI.DrawTexture(r, segOverlay);
                GUI.Label(new Rect(r.x, r.y + s + 4, s, 20), d.mode == MissionMode.Land ? "nadir camera + segmentation" : "45° camera + segmentation", style);
            }
        }

        void OnDestroy() { overlayModel?.Dispose(); }
    }
}
