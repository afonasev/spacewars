using System.Linq;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;

public sealed class NativeRallyGestureTests
{
    private NativeLocalInputProfile Profile=>JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);
    [Test] public void MapTogglePreservesPlacementAndADoesNotBecomeCameraJumpOrSelection()
    {
        var gestures=new OfflinePadGestures();var p=Profile;gestures.SetMode("rallyTarget");gestures.Step(0,0,true,true,p);
        Assert.IsEmpty(gestures.Step(1,8,true,true,p));Assert.AreEqual("rallyMap",gestures.Mode);Assert.IsTrue(gestures.Map);gestures.Step(2,0,true,true,p);
        gestures.Step(3,8,true,true,p);Assert.AreEqual("rallyTarget",gestures.Mode);Assert.IsFalse(gestures.Map);gestures.Step(4,0,true,true,p);
        gestures.Step(5,8,true,true,p);gestures.Step(6,0,true,true,p);gestures.Step(7,1,true,true,p);
        CollectionAssert.AreEqual(new[]{"rally"},gestures.Step(8,0,true,true,p));Assert.AreEqual("buildingWheel",gestures.Mode);Assert.IsFalse(gestures.Map);
    }
    [TestCase("rallyTarget")][TestCase("rallyMap")]
    public void BCancelsPlacementWithoutDeselectOrMove(string mode)
    {
        var gestures=new OfflinePadGestures();var p=Profile;gestures.SetMode(mode);gestures.Step(0,0,true,true,p);
        CollectionAssert.AreEqual(new[]{"cancelRally"},gestures.Step(1,2,true,true,p));Assert.AreEqual("buildingWheel",gestures.Mode);
        Assert.IsEmpty(gestures.Step(2,0,true,true,p));
    }
    [Test] public void OrdinaryMapAStillJumpsCameraAndFocusLossReleasesHeldPlacement()
    {
        var gestures=new OfflinePadGestures();var p=Profile;gestures.SetMode("tacticalMap");gestures.Step(0,0,true,true,p);gestures.Step(1,1,true,true,p);
        CollectionAssert.AreEqual(new[]{"cameraJump"},gestures.Step(2,0,true,true,p));
        gestures.SetMode("rallyMap");gestures.Step(3,0,true,true,p);gestures.Step(4,1,true,true,p);Assert.IsEmpty(gestures.Step(5,1,true,false,p));Assert.AreEqual("world",gestures.Mode);
        Assert.IsEmpty(gestures.Step(6,0,true,true,p));
    }
}
