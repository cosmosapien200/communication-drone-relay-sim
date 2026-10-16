using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CommDrone.EditorTools
{
    /// Builds training scenes from code and the Linux players used by mlagents-learn.
    /// Batch: Unity -batchmode -projectPath unity -executeMethod CommDrone.EditorTools.Builder.BuildNav -quit
    public static class Builder
    {
        const string BuildRoot = "../Builds";

        public static void BuildNav() => BuildTraining(TrainingManager.Kind.Nav, "NavTraining");
        public static void BuildLand() => BuildTraining(TrainingManager.Kind.Land, "LandTraining");

        public static void BuildEval()
        {
            string scenePath = "Assets/Scenes/Eval.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("EvalRunner").AddComponent<EvalRunner>();
            EditorSceneManager.SaveScene(scene, scenePath);
            Build(new[] { scenePath }, "Eval");
        }

        public static void BuildMission()
        {
            string scenePath = "Assets/Scenes/Mission.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("MissionRunner");
            go.AddComponent<MissionRunner>();
            go.AddComponent<MissionView>();
            EditorSceneManager.SaveScene(scene, scenePath);
            Build(new[] { scenePath }, "Mission");
        }

        public static void BuildTraining(TrainingManager.Kind kind, string name)
        {
            string scenePath = $"Assets/Scenes/{name}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("TrainingManager");
            var tm = go.AddComponent<TrainingManager>();
            tm.kind = kind;
            EditorSceneManager.SaveScene(scene, scenePath);
            Build(new[] { scenePath }, name);
        }

        public static void Build(string[] scenes, string name)
        {
            ConfigurePlayer();
            string outDir = Path.Combine(BuildRoot, name);
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, name + ".x86_64"),
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"Build {name}: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F1} MB, {report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("build failed: " + name);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 640;
            PlayerSettings.defaultScreenHeight = 480;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }
    }
}
