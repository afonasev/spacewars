using System.Linq;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;

public sealed class NativeRbGroupGestureTests
{
    NativeLocalInputProfile p;OfflinePadGestures g;
    OfflinePadSector Slot(bool occupied=true,bool selection=true)=>new OfflinePadSector("1",occupied||selection){RecallEnabled=occupied,CanAssign=selection,CanAdd=occupied&&selection};
    string[] Step(double t,int mask,OfflinePadSector slot=null)=>g.StepDetailed(t,mask,true,true,p,sector:slot).Select(i=>i.Kind).ToArray();
    [SetUp] public void Setup(){p=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);g=new OfflinePadGestures();Step(0,0);}
    [TestCase("repeat")][TestCase("sell")][TestCase("other-hold")]
    public void PetalFeedbackWaitsAndHoldActionUsesDoubleTheFieldThreshold(string hold)
    {
        p.Validate();var slot=new OfflinePadSector("action",true,hold);g.SetMode("buildingWheel");Step(0,0,slot);Step(10,1,slot);
        Assert.AreEqual(2*p.holdMs,p.RadialHoldMs);
        Assert.IsEmpty(Step(10+p.RadialHoldFeedbackDelayMs,1,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(10+(p.RadialHoldFeedbackDelayMs+p.RadialHoldMs)/2,1,slot));Assert.AreEqual(.5,g.Progress,.00001);
        CollectionAssert.AreEqual(new[]{hold},Step(10+p.RadialHoldMs,1,slot));Assert.AreEqual(1,g.Progress);
        Assert.IsEmpty(Step(11+p.RadialHoldMs,1,slot));Assert.IsEmpty(Step(12+p.RadialHoldMs,0,slot));Assert.Zero(g.Progress);
    }
    [TestCase("repeat")][TestCase("sell")]
    public void QuickPetalTapNeverPaintsHoldFeedback(string hold)
    {
        var slot=new OfflinePadSector("action",true,hold);g.SetMode("buildingWheel");Step(0,0,slot);Step(10,1,slot);Step(10+p.RadialHoldFeedbackDelayMs/2,1,slot);Assert.Zero(g.Progress);
        var result=Step(11+p.RadialHoldFeedbackDelayMs/2,0,slot);CollectionAssert.AreEqual(hold=="sell"?new string[0]:new[]{"activate"},result);Assert.Zero(g.Progress);
    }
    double OpenRb(OfflinePadSector slot=null)
    {
        Step(10,32);
        double opened=10+p.RadialHoldMs;
        Assert.IsEmpty(Step(opened,32,slot));
        Assert.True(g.AuxiliaryWheelVisible);
        return opened+1;
    }
    [Test] public void GroupAFeedbackWaitsAndXWinsEvenAfterTheFillCompletes()
    {
        var slot=Slot();double start=OpenRb(slot);Step(start,33,slot);
        Assert.IsEmpty(Step(start+p.RadialHoldFeedbackDelayMs,33,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+(p.RadialHoldFeedbackDelayMs+p.RadialHoldMs)/2,33,slot));Assert.AreEqual(.5,g.Progress,.00001);
        Assert.IsEmpty(Step(start+p.RadialHoldMs,33,slot));Assert.AreEqual(1,g.Progress);
        CollectionAssert.AreEqual(new[]{"addGroup"},Step(start+p.RadialHoldMs+1,37,slot));Assert.Zero(g.Progress);Assert.True(g.Added);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+2,32,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+3,0,slot));
    }
    [Test] public void ABecomesScreenExactlyAtOneSecondAndDoesNotRepeat()
    {
        p.Validate();Assert.AreEqual(1000,p.selectAllHoldMs);Step(10,1);Assert.IsEmpty(Step(1009,1));Assert.False(g.ScreenPreview);
        CollectionAssert.AreEqual(new[]{"selectScreen"},Step(1010,1));Assert.True(g.ScreenPreview);
        Assert.IsEmpty(Step(3000,1));Assert.True(g.ScreenPreview);Assert.IsEmpty(Step(3001,0));Assert.False(g.ScreenPreview);
    }
    [Test] public void AReleasedBeforeThresholdSelectsCircleAndMapNeverSelectsScreen()
    {
        Step(10,1);CollectionAssert.AreEqual(new[]{"selectCircle"},Step(1009,0));g.SetMode("tacticalMap");Step(1010,0);Step(1020,1);
        Assert.IsEmpty(Step(5020,1));Assert.False(g.ScreenPreview);CollectionAssert.AreEqual(new[]{"selectMapCircle"},Step(5021,0));
    }
    [Test] public void RbRevealsAtDoubledThresholdAndShortARecallsWithoutFill()
    {
        var slot=Slot();Step(10,32);
        Assert.AreEqual("groupWheel",g.Mode);Assert.False(g.AuxiliaryWheelVisible);
        Step(10+p.RadialHoldMs-1,32,slot);Assert.False(g.AuxiliaryWheelVisible);
        Step(10+p.RadialHoldMs,32,slot);Assert.True(g.AuxiliaryWheelVisible);
        double start=11+p.RadialHoldMs;
        Assert.IsEmpty(Step(start+3*p.RadialHoldMs,32,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+3*p.RadialHoldMs+1,33,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+3*p.RadialHoldMs+2,33,slot));Assert.Zero(g.Progress);
        CollectionAssert.AreEqual(new[]{"recallGroup"},Step(start+3*p.RadialHoldMs+3,32,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+3*p.RadialHoldMs+4,0,slot));
        Assert.AreEqual("world",g.Mode);Assert.False(g.AuxiliaryWheelVisible);
    }
    [TestCase(true)][TestCase(false)] public void HoldingAReplacesOccupiedOrEmptySlotOnce(bool occupied)
    {
        var slot=Slot(occupied);double start=OpenRb(slot);
        Step(start,33,slot);Assert.IsEmpty(Step(start+p.RadialHoldMs-1,33,slot));Assert.Less(g.Progress,1);
        Assert.IsEmpty(Step(start+p.RadialHoldMs,33,slot));Assert.AreEqual(1,g.Progress);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+1,33,slot));
        CollectionAssert.AreEqual(new[]{"assignGroup"},Step(start+p.RadialHoldMs+2,32,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+3,0,slot));
    }
    [TestCase(false)][TestCase(true)] public void ChangingSectorOrReleasingRbCancelsPendingA(bool releaseBumper)
    {
        var slot=Slot();double start=OpenRb(slot);
        Step(start,33,slot);var second=Slot();second.Id="2";
        Assert.IsEmpty(Step(start+p.RadialHoldMs,releaseBumper?1:33,releaseBumper?slot:second));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+1,releaseBumper?0:32,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+2,0,slot));
    }
    [Test] public void WithoutSelectionOnlyShortARecallIsAvailable()
    {
        var slot=Slot(true,false);double start=OpenRb(slot);
        Step(start,33,slot);Assert.IsEmpty(Step(start+p.RadialHoldMs,33,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+1,32,slot));
        Step(start+p.RadialHoldMs+2,33,slot);
        CollectionAssert.AreEqual(new[]{"recallGroup"},Step(start+p.RadialHoldMs+3,32,slot));
    }
    [Test] public void XAddsOnceAndReleaseNeverStopsHoldsRecallsOrReplaces()
    {
        Step(10,32);Step(20,32,Slot());CollectionAssert.AreEqual(new[]{"addGroup"},Step(30,36,Slot()));
        Assert.IsEmpty(Step(900,36,Slot()));Assert.IsEmpty(Step(901,4,Slot()));Assert.IsEmpty(Step(902,0));Assert.IsEmpty(Step(1500,0));
    }
    [Test] public void XCannotAddToEmptySlotAndDoesNotLeakStop()
    {Step(10,32);Step(20,32,Slot(false));Assert.IsEmpty(Step(30,36,Slot(false)));Assert.IsEmpty(Step(40,32,Slot(false)));Assert.IsEmpty(Step(50,0,Slot(false)));}
    [Test] public void NeutralSingleAndDoubleRbStillCycleAndSelectArmy()
    {
        Step(10,32);CollectionAssert.AreEqual(new[]{"cycleGroup"},Step(20,0));Assert.IsEmpty(Step(20+p.doubleTapMs,0));
        CollectionAssert.AreEqual(new[]{"focusCycledGroup"},Step(20+p.doubleTapMs+1,0));
        Assert.IsEmpty(Step(20+p.doubleTapMs+2,0));
        Setup();Step(10,32);Step(20,0);Step(21,0);Step(100,32);CollectionAssert.AreEqual(new[]{"selectAllArmy"},Step(110,0));Step(111,0);Assert.IsEmpty(Step(1000,0));
    }
    [Test] public void XOnSecondRbCanAddInsteadOfDoubleTap()
    {Step(10,32);Step(20,0);Step(21,0);Step(100,32);CollectionAssert.AreEqual(new[]{"addGroup"},Step(110,36,Slot()));Assert.IsEmpty(Step(120,0,Slot()));}
    [Test] public void RtIsInertIncludingPendingSingleRb()
    {Assert.IsEmpty(Step(10,128));Assert.AreEqual("world",g.Mode);Step(11,0);Step(20,32);Step(30,0);Step(31,0);Assert.IsEmpty(Step(40,128));CollectionAssert.AreEqual(new[]{"focusCycledGroup"},Step(31+p.doubleTapMs,128));}
    [TestCase(32,"cycleGroup","selectAllArmy")][TestCase(64,"cycleBase","cycleBase")]
    public void ShortAndDoubleBumpersNeverRevealEvenWithStickSector(int bit,string first,string second)
    {
        Assert.IsEmpty(Step(10,bit));Assert.False(g.AuxiliaryWheelVisible);
        Assert.IsEmpty(Step(11,bit,Slot()));Assert.False(g.AuxiliaryWheelVisible);
        CollectionAssert.AreEqual(new[]{first},Step(20,0,Slot()));Assert.False(g.AuxiliaryWheelVisible);
        Assert.IsEmpty(Step(100,bit,Slot()));Assert.False(g.AuxiliaryWheelVisible);
        CollectionAssert.AreEqual(new[]{second},Step(110,0,Slot()));Assert.False(g.AuxiliaryWheelVisible);
        Step(111,0);Assert.IsEmpty(Step(1000,0));Assert.False(g.AuxiliaryWheelVisible);
    }
    [Test] public void LbRevealsOnlyWhileHeldAtThresholdAndClosesOnRelease()
    {
        Step(10,64);Assert.False(g.AuxiliaryWheelVisible);Step(10+p.RadialHoldMs-1,64);Assert.False(g.AuxiliaryWheelVisible);
        Step(10+p.RadialHoldMs,64,Slot());Assert.True(g.AuxiliaryWheelVisible);Assert.AreEqual("baseWheel",g.Mode);
        double start=11+p.RadialHoldMs;var slot=Slot();
        Assert.IsEmpty(Step(start,65,slot));Assert.IsEmpty(Step(start+p.RadialHoldMs,65,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+1,64,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+2,65,slot));Assert.Zero(g.Progress);
        CollectionAssert.AreEqual(new[]{"selectBase"},Step(start+p.RadialHoldMs+3,64,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+4,0,slot));Assert.False(g.AuxiliaryWheelVisible);
    }
    [Test] public void HoldingSecondRbRevealsWheelWithoutSelectingArmy()
    {
        Step(10,32);CollectionAssert.AreEqual(new[]{"cycleGroup"},Step(20,0));Step(21,0);Step(100,32);
        Assert.IsEmpty(Step(100+p.RadialHoldMs,32));Assert.True(g.AuxiliaryWheelVisible);
        Assert.IsEmpty(Step(101+p.RadialHoldMs,0));Assert.False(g.AuxiliaryWheelVisible);
    }
    [Test] public void FocusLossCancelsPendingAAndRequiresNeutral()
    {
        var slot=Slot();double start=OpenRb(slot);Step(start,33,slot);
        Assert.IsEmpty(g.StepDetailed(start+p.RadialHoldMs,33,true,false,p,sector:slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(start+p.RadialHoldMs+1,33,slot));
        Assert.IsEmpty(Step(start+p.RadialHoldMs+2,0,slot));Assert.AreEqual("world",g.Mode);
    }
    [Test] public void ScreenSelectionIgnoresUndefinedGroundAndKeepsOwnVisibleFilter()
    {
        var units=new[]{new OfflinePadSelectable(1,0,true,10,20,1,true),new OfflinePadSelectable(2,0,true,30,20,1,false),new OfflinePadSelectable(3,0,false,10,20,1,true)};
        CollectionAssert.AreEqual(new[]{1},OfflinePadSelection.Area(units,double.NaN,double.NaN,double.PositiveInfinity,false));
        Assert.IsEmpty(OfflinePadSelection.Area(units,double.NaN,double.NaN,10,false));
        CollectionAssert.AreEqual(new[]{1},OfflinePadSelection.Area(units,10,20,2,false));
    }
    [Test] public void GroupAddPreservesMembersAndPrunesDeadOrForeignUnits()
    {
        var units=new[]{new KeyboardSelectable(1,0,true,false,0,0),new KeyboardSelectable(2,0,true,false,1,0),new KeyboardSelectable(3,0,false,false,2,0)};
        var groups=new KeyboardControlGroups();groups.Apply(1,true,0,new[]{1},units,p.doubleTapMs,out _);Assert.True(groups.Add(1,new[]{2,3},units));CollectionAssert.AreEqual(new[]{1,2},groups.Members(1,units));
        Assert.True(groups.Add(1,new[]{2},units));CollectionAssert.AreEqual(new[]{1,2},groups.Members(1,units));
        var slot=groups.PadActions(new[]{2},units,false,new[]{1,2});Assert.True(slot[0].CanAdd);Assert.True(slot[1].CanAssign);Assert.False(slot[1].RecallEnabled);Assert.False(slot[1].CanAdd);
        CollectionAssert.AreEqual(new[]{2},groups.Members(1,new[]{units[1],units[2]}));
    }
    [Test] public void OtherSeatDoesNotShareRbOrGroupState()
    {var other=new OfflinePadGestures();other.Step(0,0,true,true,p);Step(10,32);Step(20,0);other.Step(30,32,true,true,p);CollectionAssert.AreEqual(new[]{"cycleGroup"},other.Step(40,0,true,true,p));}
}
