using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        result.Log("refresh requested; compiling={0}", EditorApplication.isCompiling);
    }
}
