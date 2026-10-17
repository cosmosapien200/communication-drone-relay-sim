using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity");
        result.Log("opened {0}", EditorSceneManager.GetActiveScene().path);
    }
}
