using System.Collections;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

public sealed class MapDetailAuditTests
{
    [Test] public void WreckBakeIsIdempotentAndSavedMeshesAreIndependent()
    {
        Assert.AreEqual(0,UnitWreckAssets.OptimizePreparedMeshes(),"Prepared immutable resources must be stable on repeated preparation");
        int saved=0;
        foreach(var prefab in UnityEngine.Resources.LoadAll<UnityEngine.GameObject>("Wrecks"))foreach(var f in prefab.GetComponentsInChildren<UnityEngine.MeshFilter>(true)){
            string path=UnityEditor.AssetDatabase.GetAssetPath(f.sharedMesh);
            if(!path.Contains("/SharedMeshes/"))continue;
            saved++;Assert.True(path.EndsWith(".asset"));Assert.IsNotNull(UnitModelResources.ExactMeshKey(f.sharedMesh));
        }
        Assert.AreEqual(72,saved,"Current-source wreck variants use seventy-two immutable baked mesh bindings; unchanged remaining slots retain imported meshes");
    }
    [UnityTest] public IEnumerator CaptureActualRuntimeMaps()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        MapDetailOptimizationAudit.Capture();
        yield return null;
        yield return new ExitPlayMode();
        Assert.Pass("Actual PlayMode constructors, exact mesh witness and native frames completed");
    }
}
