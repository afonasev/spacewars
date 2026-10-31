using System.Collections;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

public sealed class MapPropAuditTests
{
    [UnityTest] public IEnumerator CaptureActualRuntimeMaps()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        MapPropOptimizationAudit.Capture();
        yield return null;
        yield return new ExitPlayMode();
        Assert.Pass("Actual PlayMode constructors, exact mesh witness and native frames completed");
    }
}
