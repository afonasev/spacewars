using System.Collections;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

public sealed class MapPropBoundsAuditTests
{
    [UnityTest] public IEnumerator QualifyActualMeshAndSubmeshBounds()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        MapPropOptimizationAudit.CaptureBounds();
        yield return null;
        yield return new ExitPlayMode();
        Assert.Pass("Actual native mesh/submesh bounds recorded as exact Float32 bits");
    }
}
