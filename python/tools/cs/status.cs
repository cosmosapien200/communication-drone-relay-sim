using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("compiling={0} updating={1} playing={2}", EditorApplication.isCompiling, EditorApplication.isUpdating, EditorApplication.isPlaying);
    }
}
