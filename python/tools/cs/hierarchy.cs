using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var s = SceneManager.GetActiveScene();
        result.Log("Scene: {0} ({1})", s.name, s.path);
        foreach (var go in s.GetRootGameObjects()) Walk(go.transform, 0, result);
    }
    void Walk(Transform t, int d, ExecutionResult r)
    {
        if (d > 2) return;
        r.Log("{0}{1}", new string(' ', d * 2), t.name);
        foreach (Transform c in t) Walk(c, d + 1, r);
    }
}
