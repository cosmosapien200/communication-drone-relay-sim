using UnityEngine;

namespace CommDrone
{
    /// Live-demo control panel (bottom-left). Keys: 1-9 pick a map, R restart, Space pause, +/- speed,
    /// C camera (chase / overview / top-down), V vision (real U-Net / ground truth), H hide panel.
    public class DemoMenu : MonoBehaviour
    {
        MissionRunner runner;
        MissionView view;
        bool hidden;
        GUIStyle title, text, button;

        void Start()
        {
            runner = GetComponent<MissionRunner>();
            view = GetComponent<MissionView>();
            if (!runner.demo) enabled = false;
        }

        void Update()
        {
            var n = DemoCatalog.Entries.Length;
            for (int i = 0; i < Mathf.Min(9, n); i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) runner.Restart(i);
            if (Input.GetKeyDown(KeyCode.R)) runner.Restart(Mathf.Max(0, runner.catalogIndex));
            if (Input.GetKeyDown(KeyCode.Space)) runner.paused = !runner.paused;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) runner.demoSpeed = Mathf.Min(16, runner.demoSpeed * 2);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) runner.demoSpeed = Mathf.Max(1, runner.demoSpeed / 2);
            if (Input.GetKeyDown(KeyCode.C) && view) view.camMode = (view.camMode + 1) % 3;
            if (Input.GetKeyDown(KeyCode.V)) ToggleVision();
            if (Input.GetKeyDown(KeyCode.H)) hidden = !hidden;
        }

        void ToggleVision() => runner.Restart(Mathf.Max(0, runner.catalogIndex), runner.detector.StartsWith("cnn") ? "gt" : "cnn");

        void OnGUI()
        {
            if (hidden) return;
            title ??= new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            text ??= new GUIStyle { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            button ??= new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            var n = DemoCatalog.Entries.Length;
            float w = 340, h = 150 + n * 30;
            var r = new Rect(8, Screen.height - h - 8, w, h);
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 10, r.y + 6, w, 24), "Demo maps", title);
            for (int i = 0; i < n; i++)
            {
                var e = DemoCatalog.Entries[i];
                bool cur = i == runner.catalogIndex;
                GUI.backgroundColor = cur ? new Color(0.3f, 0.8f, 0.4f) : Color.white;
                if (GUI.Button(new Rect(r.x + 10, r.y + 34 + i * 30, w - 20, 26), $"  {i + 1}. {e.name}", button)) runner.Restart(i);
            }
            GUI.backgroundColor = Color.white;
            float y = r.y + 40 + n * 30;
            if (runner.catalogIndex >= 0) GUI.Label(new Rect(r.x + 10, y, w - 20, 36), DemoCatalog.Entries[runner.catalogIndex].description, text);
            y += 38;
            GUI.Label(new Rect(r.x + 10, y, w - 20, 20),
                $"vision: {(runner.detector.StartsWith("cnn") ? "real U-Net" : "ground truth")}   speed: x{runner.demoSpeed}   camera: {(view ? MissionView.CamModes[view.camMode] : "-")}{(runner.paused ? "   PAUSED" : "")}", text);
            y += 22;
            GUI.Label(new Rect(r.x + 10, y, w - 20, 40), "1-9 map · R restart · Space pause · +/- speed · C camera · V vision · H hide", text);
        }
    }
}
