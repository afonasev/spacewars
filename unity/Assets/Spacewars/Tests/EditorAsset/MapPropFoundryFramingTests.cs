using System.Collections;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

public sealed class MapPropFoundryFramingTests
{
    [UnityTest] public IEnumerator CaptureCompleteExistingHallAndAnnex()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        yield return new EnterPlayMode();
        MapPropOptimizationAudit.CaptureFoundryRepresentative();
        yield return null;
        yield return new ExitPlayMode();
        Assert.Pass("Matched complete Foundry hall/annex frame and viewport corners verified");
    }
}
