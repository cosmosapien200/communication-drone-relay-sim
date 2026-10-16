using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeTest
{
    // Batch: Unity -batchmode -projectPath unity -executeMethod SmokeTest.Run -quit
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2, 1, 2);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        cube.transform.position = new Vector3(0, 0.5f, 0);
        cube.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.4f, 0.1f) };
        var cam = Camera.main;
        cam.transform.position = new Vector3(4, 4, -4);
        cam.transform.LookAt(Vector3.zero);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Smoke.unity");
        ScreenshotUtil.RenderCamera(cam, 512, 512, "../docs/screenshots/smoke.png");
        Debug.Log("SmokeTest done");
    }
}

public static class ScreenshotUtil
{
    public static void RenderCamera(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
    }
}
