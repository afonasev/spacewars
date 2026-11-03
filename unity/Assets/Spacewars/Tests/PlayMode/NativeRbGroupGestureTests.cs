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
    public void PetalFeedbackWaitsButHoldActionKeepsItsOriginalDeadline(string hold)
    {
        p.Validate();var slot=new OfflinePadSector("action",true,hold);g.SetMode("buildingWheel");Step(0,0,slot);Step(10,1,slot);
        Assert.IsEmpty(Step(10+p.holdFeedbackDelayMs,1,slot));Assert.Zero(g.Progress);
        Assert.IsEmpty(Step(10+(p.holdFeedbackDelayMs+p.holdMs)/2,1,slot));Assert.AreEqual(.5,g.Progress,.00001);
        CollectionAssert.AreEqual(new[]{hold},Step(10+p.holdMs,1,slot));Assert.AreEqual(1,g.Progress);
        Assert.IsEmpty(Step(11+p.holdMs,1,slot));Assert.IsEmpty(Step(12+p.holdMs,0,slot));Assert.Zero(g.Progress);
    }
    [TestCase("repeat")][TestCase("sell")]
    public void QuickPetalTapNeverPaintsHoldFeedback(string hold)
    {
        var slot=new OfflinePadSector("action",true,hold);g.SetMode("buildingWheel");Step(0,0,slot);Step(10,1,slot);Step(10+p.holdFeedbackDelayMs/2,1,slot);Assert.Zero(g.Progress);
        var result=Step(11+p.holdFeedbackDelayMs/2,0,slot);CollectionAssert.AreEqual(hold=="sell"?new string[0]:new[]{"activate"},result);Assert.Zero(g.Progress);
    }
    [Test] public void GroupDwellFeedbackWaitsWithoutDelayingAssignmentAndXHasNoHoldFill()
    {
        Step(10,32);Step(10+p.holdMs,32);var slot=Slot();double start=20+p.holdMs;Step(start,32,slot);
        Step(start+p.holdFeedbackDelayMs,32,slot);Assert.Zero(g.Progress);Step(start+(p.holdFeedbackDelayMs+p.holdMs)/2,32,slot);Assert.AreEqual(.5,g.Progress,.00001);
        Step(start+p.holdMs,32,slot);Assert.AreEqual(1,g.Progress);CollectionAssert.AreEqual(new[]{"assignGroup"},Step(start+p.holdMs+1,0,slot));
        g=new OfflinePadGestures();Step(0,0);Step(10,32);Step(310,32,slot);CollectionAssert.AreEqual(new[]{"addGroup"},Step(320,36,slot));Assert.Zero(g.Progress);Assert.True(g.Added);
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
    [Test] public void RbRevealsOnlyAtHoldThresholdAndEarlyVisibleSectorReleaseRecalls()
    {
        Step(10,32);Assert.AreEqual("groupWheel",g.Mode);Assert.False(g.AuxiliaryWheelVisible);
        Step(10+p.holdMs-1,32);Assert.False(g.AuxiliaryWheelVisible);Step(10+p.holdMs,32);Assert.True(g.AuxiliaryWheelVisible);
        Step(11+p.holdMs,32,Slot());CollectionAssert.AreEqual(new[]{"recallGroup"},Step(12+p.holdMs,0,Slot()));Assert.AreEqual("world",g.Mode);Assert.False(g.AuxiliaryWheelVisible);
    }
    [TestCase(true)][TestCase(false)] public void RbDwellReplacesOccupiedOrEmptySlotOnce(bool occupied)
    {
        Step(10,32);Step(20,32,Slot(occupied));Assert.IsEmpty(Step(319,32,Slot(occupied)));
        Assert.IsEmpty(Step(320,32,Slot(occupied)));Assert.AreEqual(1,g.Progress);
        Assert.IsEmpty(Step(900,32,Slot(occupied)));CollectionAssert.AreEqual(new[]{"assignGroup"},Step(901,0,Slot(occupied)));Assert.IsEmpty(Step(902,0));
    }
    [Test] public void ChangingSectorRestartsDwellAndNeutralCancels()
    {
        Step(10,32);Step(20,32,Slot());var second=Slot();second.Id="2";Assert.IsEmpty(Step(319,32,second));Assert.IsEmpty(Step(400,32));Assert.IsEmpty(Step(450,0));Step(451,0);Assert.IsEmpty(Step(1000,0));
    }
    [Test] public void WithoutSelectionOnlyRecallIsAvailable()
    {
        Step(10,32);Assert.IsEmpty(Step(20,32,Slot(true,false)));Assert.IsEmpty(Step(900,32,Slot(true,false)));
        CollectionAssert.AreEqual(new[]{"recallGroup"},Step(901,0,Slot(true,false)));
    }
    [Test] public void XAddsOnceAndReleaseNeverStopsHoldsRecallsOrReplaces()
    {
        Step(10,32);Step(20,32,Slot());CollectionAssert.AreEqual(new[]{"addGroup"},Step(30,36,Slot()));
        Assert.IsEmpty(Step(900,36,Slot()));Assert.IsEmpty(Step(901,4,Slot()));Assert.IsEmpty(Step(902,0));Assert.IsEmpty(Step(1500,0));
    }
    [Test] public void XAtDwellThresholdWinsOverReplace()
    {Step(10,32);Step(20,32,Slot());Assert.IsEmpty(Step(900,32,Slot()));CollectionAssert.AreEqual(new[]{"addGroup"},Step(1000,36,Slot()));Assert.IsEmpty(Step(1001,0,Slot()));}
    [Test] public void XCannotAddToEmptySlotAndDoesNotLeakStop()
    {Step(10,32);Step(20,32,Slot(false));Assert.IsEmpty(Step(30,36,Slot(false)));Assert.IsEmpty(Step(40,32,Slot(false)));Assert.IsEmpty(Step(50,0,Slot(false)));}
    [Test] public void NeutralSingleAndDoubleRbStillCycleAndSelectArmy()
    {
        Step(10,32);CollectionAssert.AreEqual(new[]{"cycleGroup"},Step(20,0));Step(21,0);Assert.IsEmpty(Step(20+p.doubleTapMs+1,0));
        Setup();Step(10,32);Step(20,0);Step(21,0);Step(100,32);CollectionAssert.AreEqual(new[]{"selectAllArmy"},Step(110,0));Step(111,0);Assert.IsEmpty(Step(1000,0));
    }
    [Test] public void XOnSecondRbCanAddInsteadOfDoubleTap()
    {Step(10,32);Step(20,0);Step(21,0);Step(100,32);CollectionAssert.AreEqual(new[]{"addGroup"},Step(110,36,Slot()));Assert.IsEmpty(Step(120,0,Slot()));}
    [Test] public void RtIsInertIncludingPendingSingleRb()
    {Assert.IsEmpty(Step(10,128));Assert.AreEqual("world",g.Mode);Step(11,0);Step(20,32);Step(30,0);Step(31,0);Assert.IsEmpty(Step(40,128));Assert.IsEmpty(Step(31+p.doubleTapMs,128));}
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
        Step(10,64);Assert.False(g.AuxiliaryWheelVisible);Step(10+p.holdMs-1,64);Assert.False(g.AuxiliaryWheelVisible);
        Step(10+p.holdMs,64,Slot());Assert.True(g.AuxiliaryWheelVisible);Assert.AreEqual("baseWheel",g.Mode);
        CollectionAssert.AreEqual(new[]{"selectBase"},Step(11+p.holdMs,0,Slot()));Assert.False(g.AuxiliaryWheelVisible);
    }
    [Test] public void HoldingSecondRbRevealsWheelWithoutSelectingArmy()
    {
        Step(10,32);CollectionAssert.AreEqual(new[]{"cycleGroup"},Step(20,0));Step(21,0);Step(100,32);
        Assert.IsEmpty(Step(100+p.holdMs,32));Assert.True(g.AuxiliaryWheelVisible);
        Assert.IsEmpty(Step(101+p.holdMs,0));Assert.False(g.AuxiliaryWheelVisible);
    }
    [Test] public void FocusLossCancelsDwellAndRequiresNeutral()
    {Step(10,32);Step(20,32,Slot());Assert.IsEmpty(g.StepDetailed(400,32,true,false,p,sector:Slot()));Assert.IsEmpty(Step(500,32,Slot()));Assert.IsEmpty(Step(600,0,Slot()));Assert.True(g.Mode=="world");}
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
