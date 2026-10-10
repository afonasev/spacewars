using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GameplayAudioTests
    {
        private static PlayableSnapshot Frame(long tick,PlayableSoundEvent[] sounds=null,PlayableEntitySnapshot[] units=null,string owner="player-1",long generation=1)
            =>new PlayableSnapshot("test",1,generation,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                units??Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:owner,sounds:sounds);
        private static GameplayAudioScreen Screen(PlayableSnapshot view,double x=0,double z=0)=>new GameplayAudioScreen(view,new NavPoint(x,z),new NavPoint(1,0));
        private static PlayableSoundEvent Shot(long id,long tick,double x=0)=>new PlayableSoundEvent(id,tick,PlayableSoundKind.Shot,new NavPoint(x,0),PlayableEntityKind.Tank);
        private static GameplayAudioPlan Primed(GameplayAudioProfile profile=null)
        {var p=new GameplayAudioPlan(profile??new GameplayAudioProfile());p.Observe(new[]{Screen(Frame(0))},0,true);return p;}

        [Test] public void DuplicateScreensDoNotMultiplyGainAndHiddenCameraCannotWin()
        {
            var shot=Shot(1,1,20);var allowed=Frame(1,new[]{shot});var hidden=Frame(1,owner:"hidden");
            var one=Primed().Observe(new[]{Screen(allowed)},.1,true).Single();
            var duplicates=Primed().Observe(new[]{Screen(allowed),Screen(allowed),Screen(hidden,20)},.1,true).Single();
            Assert.AreEqual(one.Gain,duplicates.Gain);Assert.AreEqual(one.Pan,duplicates.Pan);
            var nearer=Primed().Observe(new[]{Screen(allowed),Screen(Frame(1,new[]{shot},owner:"other"),20)},.1,true).Single();
            Assert.AreEqual(new GameplayAudioProfile().shotGain,nearer.Gain);Assert.AreEqual(0,nearer.Pan);Assert.AreEqual(2,nearer.Observers.Count);
        }
        [Test] public void SingleShotKeepsDistanceGradientBelowBattleBudget()
        {
            var profile=new GameplayAudioProfile();
            var near=Primed(profile).Observe(new[]{Screen(Frame(1,new[]{Shot(1,1,8)}))},.1,true).Single();
            var middle=Primed(profile).Observe(new[]{Screen(Frame(1,new[]{Shot(1,1,20)}))},.1,true).Single();
            var far=Primed(profile).Observe(new[]{Screen(Frame(1,new[]{Shot(1,1,40)}))},.1,true).Single();
            Assert.Less(near.Gain,profile.effectsBudget);Assert.Greater(near.Gain,middle.Gain);Assert.Greater(middle.Gain,far.Gain);
            Assert.AreEqual(profile.shotGain,near.BaseGain);
        }
        [Test] public void FirstFrameRepeatedFrameOldEventsPauseAndRollbackDoNotReplay()
        {
            var p=new GameplayAudioPlan(new GameplayAudioProfile());
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(1,new[]{Shot(1,1)}))},0,true));
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(2,new[]{Shot(1,1)}))},.1,true));
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(20,new[]{Shot(2,2)}))},.2,true));
            Assert.AreEqual(1,p.Observe(new[]{Screen(Frame(21,new[]{Shot(3,21)}))},.3,true).Length);
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(21,new[]{Shot(3,21)}))},.4,true));
            p.Observe(Array.Empty<GameplayAudioScreen>(),.5,false);
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(22,new[]{Shot(3,21)}))},.6,true));
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(1,new[]{Shot(1,1)}))},.7,true));
        }
        [Test] public void FarDiscardedEventDoesNotPlayAfterCameraApproaches()
        {
            var p=Primed();var v=Frame(1,new[]{Shot(1,1,200)});
            Assert.IsEmpty(p.Observe(new[]{Screen(v)},.1,true));
            Assert.IsEmpty(p.Observe(new[]{Screen(v,200)},.2,true));
            Assert.AreEqual(0,p.Attenuation(70));Assert.AreEqual(1,p.Attenuation(0));
            Assert.Greater(p.Attenuation(20),p.Attenuation(40));
        }
        [Test] public void DenseBattleIsBoundedAndDestructionWinsOverShots()
        {
            var profile=new GameplayAudioProfile{voices=4,shots=3,movementGroups=2};var p=Primed(profile);
            var events=Enumerable.Range(1,100).Select(i=>Shot(i,1,i%9*9)).ToList();
            events.Add(new PlayableSoundEvent(101,1,PlayableSoundKind.Destroyed,new NavPoint(0,0),PlayableEntityKind.Tank));
            var cues=p.Observe(new[]{Screen(Frame(1,events.ToArray()))},.1,true);
            Assert.LessOrEqual(cues.Length,4);Assert.AreEqual(PlayableSoundKind.Destroyed,cues[0].Kind);
            Assert.LessOrEqual(cues.Count(c=>c.Kind==PlayableSoundKind.Shot),3);
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(1,events.ToArray()))},.2,true));
        }
        [Test] public void MovingColumnIsGroupedAndDuplicateEntitiesDoNotAddLoops()
        {
            var units=Enumerable.Range(1,50).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(i*.1,0),100,true,0,0,0)).ToArray();
            var view=Frame(1,units:units);
            var a=Primed().Observe(new[]{Screen(view)},.1,true);
            var b=Primed().Observe(new[]{Screen(view),Screen(view)},.1,true);
            Assert.AreEqual(1,a.Length);Assert.IsTrue(a[0].Loop);Assert.AreEqual(a[0].Gain,b.Single().Gain);
            Assert.IsEmpty(Primed().Observe(new[]{Screen(Frame(2))},.2,true));
        }
        [Test] public void NamedAudioLabMetadataControlsRangesWithoutWorldProfileExpansion()
        {
            var profile=new GameplayAudioProfile();profile.Validate();
            Assert.AreEqual(GameplayAudioMetadata.Fields.Length,GameplayAudioMetadata.Fields.Select(f=>f.Path).Distinct().Count());
            var near=GameplayAudioMetadata.Fields.Single(f=>f.Path=="audio.near");near.Write(profile,11.4);Assert.AreEqual(11,profile.near);
            Assert.Throws<ArgumentOutOfRangeException>(()=>near.Write(profile,double.NaN));
            profile.fadeSeconds=double.PositiveInfinity;Assert.Throws<ArgumentOutOfRangeException>(()=>profile.Validate());
        }
        [Test] public void LongStreamDoesNotAccumulateEventHistory()
        {
            var p=Primed();
            for(int i=1;i<2000;i++)p.Observe(new[]{Screen(Frame(i,new[]{Shot(i,i)}))},i*.04,true);
            Assert.LessOrEqual(p.SeenCount,31);
        }
        [Test] public void EconomicNotificationIgnoresDistanceAndDuplicatesButSpatialDemolitionDoesNot()
        {
            var ready=new PlayableSoundEvent(1,1,PlayableSoundKind.ProductionComplete,new NavPoint(200,0),PlayableEntityKind.Tank,true);
            var demolition=new PlayableSoundEvent(2,1,PlayableSoundKind.Demolished,new NavPoint(200,0),PlayableEntityKind.Tank,true);
            var view=Frame(1,new[]{ready,demolition});var cues=Primed().Observe(new[]{Screen(view),Screen(view)},.1,true);
            Assert.AreEqual(1,cues.Length);Assert.IsTrue(cues[0].Notification);Assert.AreEqual("ready",cues[0].Clip);
            var near=Primed().Observe(new[]{Screen(Frame(1,new[]{demolition}),200)},.1,true).Single();
            Assert.AreEqual("demolition",near.Clip);Assert.IsFalse(near.Notification);
        }
        [Test] public void ImportantWarningsWinAndRepeatedThreatsHaveNoBacklog()
        {
            var p=Primed();PlayableSoundEvent Event(long id,long tick,PlayableSoundKind kind)=>new PlayableSoundEvent(id,tick,kind,new NavPoint(0,0),PlayableEntityKind.Tank,true);
            var cues=p.Observe(new[]{Screen(Frame(1,new[]{Event(1,1,PlayableSoundKind.ProductionQueued),Event(2,1,PlayableSoundKind.BaseThreat)}))},.1,true);
            Assert.AreEqual("warning",cues.Single().Clip);
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(2,new[]{Event(3,2,PlayableSoundKind.BaseThreat)}))},.2,true));
            Assert.IsEmpty(p.Observe(new[]{Screen(Frame(3))},7,true));
            Assert.AreEqual("warning",p.Observe(new[]{Screen(Frame(4,new[]{Event(4,4,PlayableSoundKind.BaseThreat)}))},7.1,true).Single().Clip);
        }
        [Test] public void DamageAccentUsesExposedHealthAndMotionAccentIsShortNotAnEngineLoop()
        {
            var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,0),1,false,0,0,0);
            var hit=new PlayableSoundEvent(1,1,PlayableSoundKind.HeavyDamage,new NavPoint(0,0),PlayableEntityKind.Tank);
            Assert.AreEqual("damage",Primed().Observe(new[]{Screen(Frame(1,new[]{hit},new[]{unit}))},.1,true).Single().Clip);
            Assert.IsEmpty(Primed().Observe(new[]{Screen(Frame(1,new[]{hit}))},.1,true));
            var start=new PlayableSoundEvent(2,1,PlayableSoundKind.MovementStarted,new NavPoint(0,0),PlayableEntityKind.Tank);
            var cue=Primed().Observe(new[]{Screen(Frame(1,new[]{start}))},.1,true).Single();Assert.AreEqual("motion-start",cue.Clip);Assert.IsFalse(cue.Loop);
        }
    }
}
