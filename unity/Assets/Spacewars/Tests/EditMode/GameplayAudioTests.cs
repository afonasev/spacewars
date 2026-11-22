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

        [TestCase(0,false,"shkval-impact-0")]
        [TestCase(1,false,"shkval-impact-1")]
        [TestCase(2,false,"shkval-impact-2")]
        [TestCase(3,false,"shkval-impact-3")]
        [TestCase(8,false,"shkval-impact-4")]
        [TestCase(0,true,"explosion")]
        [TestCase(2,true,"shkval-impact-2")]
        [TestCase(null,false,"explosion")]
        public void ShkvalImpactUsesOneVoiceForTheObservedUnitDamageCount(int? count,bool building,string clip)
        {
            var hit=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,default,PlayableEntityKind.Shkval,
                building:building,shkvalDamagedUnits:count);
            var frame=Frame(1,new[]{hit});
            var cue=Primed().Observe(new[]{Screen(frame),Screen(frame)},.1,true).Single();
            Assert.AreEqual(clip,cue.Clip);
            Assert.AreEqual(clip=="explosion"?.85f:1f,cue.Pitch);
            Assert.AreEqual(new GameplayAudioProfile().explosionGain,cue.BaseGain);
        }

        [Test] public void DifferentShkvalImpactCountsShareTheRepeatLimit()
        {
            var first=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,default,PlayableEntityKind.Shkval,
                shkvalDamagedUnits:0);
            var second=new PlayableSoundEvent(2,1,PlayableSoundKind.Impact,default,PlayableEntityKind.Shkval,
                shkvalDamagedUnits:3);
            Assert.AreEqual(1,Primed().Observe(new[]{Screen(Frame(1,new[]{first,second}))},.1,true).Length);
        }

        [Test] public void UnitDestructionDoesNotSuppressTheRocketImpactAtTheSamePosition()
        {
            var destruction=new PlayableSoundEvent(1,1,PlayableSoundKind.Destroyed,default,PlayableEntityKind.Tank);
            var impact=new PlayableSoundEvent(2,1,PlayableSoundKind.Impact,default,PlayableEntityKind.Shkval,
                shkvalDamagedUnits:3);
            var cues=Primed().Observe(new[]{Screen(Frame(1,new[]{destruction,impact}))},.1,true);
            CollectionAssert.AreEquivalent(new[]{"explosion","shkval-impact-3"},cues.Select(c=>c.Clip));
        }

        [Test] public void NuclearImpactUsesDedicatedSpatialClipOnceAtAuthoredPitch()
        {
            var profile=new GameplayAudioProfile{nuclearImpactGain=.41f};
            var blast=new PlayableSoundEvent(1,1,PlayableSoundKind.OrbitalNuclearImpact,new NavPoint(22,0),PlayableEntityKind.Shkval);
            var view=Frame(1,new[]{blast});
            var plan=Primed(profile);
            var cue=plan.Observe(new[]{Screen(view),Screen(view)},.1,true).Single();
            Assert.AreEqual("ability-nuclear-impact",cue.Clip);
            Assert.AreEqual(profile.nuclearImpactGain,cue.BaseGain);
            Assert.AreEqual(1,cue.Pitch);
            Assert.Greater(cue.Pan,0);
            Assert.Less(cue.Gain,cue.BaseGain);
            Assert.False(cue.Notification);
            Assert.IsEmpty(plan.Observe(new[]{Screen(view)},.2,true));
        }

        [Test] public void NuclearDescentUsesSharedAbilityGainAndExpiresAtImpact()
        {
            var profile=new GameplayAudioProfile{abilityGain=.23f};
            var descent=new PlayableSoundEvent(1,1,PlayableSoundKind.OrbitalNuclearDescent,
                new NavPoint(22,0),PlayableEntityKind.Shkval,phaseEndTick:16);
            var view=Frame(1,new[]{descent});
            var plan=Primed(profile);
            var cue=plan.Observe(new[]{Screen(view),Screen(view)},.1,true).Single();
            Assert.AreEqual("ability-nuclear-descent",cue.Clip);
            Assert.AreEqual(profile.abilityGain,cue.BaseGain);
            Assert.AreEqual(1,cue.Pitch);
            Assert.AreEqual(16,cue.PhaseEndTick);
            Assert.Greater(cue.Pan,0);
            Assert.Less(cue.Gain,cue.BaseGain);
            Assert.False(cue.Notification);
            Assert.IsEmpty(plan.Observe(new[]{Screen(view)},.2,true));
            Assert.IsEmpty(Primed(profile).Observe(new[]{Screen(Frame(16,new[]{descent}))},.5,true));
        }

        [TestCase(PlayableEntityKind.Explorer,true,false,0,"tank-shot")]
        [TestCase(PlayableEntityKind.Explorer,true,false,1,"tank-shot")]
        [TestCase(PlayableEntityKind.Hawk,false,false,0,"hawk-rocket-launch")]
        [TestCase(PlayableEntityKind.Hawk,false,false,1,"hawk-rocket-launch")]
        [TestCase(PlayableEntityKind.Tank,false,false,-1,"vepr-shot")]
        [TestCase(PlayableEntityKind.Explorer,false,false,-1,"explorer-burst-shot")]
        [TestCase(PlayableEntityKind.Explorer,false,false,0,"explorer-burst-shot")]
        [TestCase(PlayableEntityKind.Tank,false,true,-1,"gauss-tower-shot")]
        [TestCase(PlayableEntityKind.Explorer,false,true,0,"sentry-turret-shot")]
        [TestCase(PlayableEntityKind.Shkval,false,false,-1,"tank-shot")]
        [TestCase(PlayableEntityKind.Igla,false,false,-1,"igla-heavy-coil")]
        public void ShotClipMatchesTheWeaponAndEmitter(
            PlayableEntityKind weapon,bool rocket,bool building,int side,string clip)
        {
            var shot=new PlayableSoundEvent(1,1,PlayableSoundKind.Shot,default,weapon,building,shotSide:side,rocketLaunch:rocket);
            Assert.AreEqual(clip,Primed().Observe(new[]{Screen(Frame(1,new[]{shot}))},.1,true).Single().Clip);
        }

        [TestCase(PlayableEntityKind.Tank,false,true,"vepr-unit-impact")]
        [TestCase(PlayableEntityKind.Tank,false,false,"impact")]
        [TestCase(PlayableEntityKind.Explorer,false,true,"impact")]
        [TestCase(PlayableEntityKind.Explorer,true,false,"impact")]
        public void OnlyVeprDamageToMobileUnitsUsesTheBImpact(
            PlayableEntityKind weapon,bool building,bool damagedUnit,string clip)
        {
            var contact=new PlayableDirectContact(1,2,default,damagedUnit:damagedUnit);
            var impact=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,default,weapon,building,contact:contact);
            var cue=Primed().Observe(new[]{Screen(Frame(1,new[]{impact}))},.1,true).Single();
            Assert.AreEqual(clip,cue.Clip);
        }

        [TestCase(PlayableEntityKind.Hawk,true,true,"hawk-rocket-impact")]
        [TestCase(PlayableEntityKind.Hawk,true,false,"impact")]
        [TestCase(PlayableEntityKind.Hawk,false,true,"impact")]
        [TestCase(PlayableEntityKind.Explorer,true,true,"impact")]
        public void OnlyHawkRocketsThatDamageUnitsUseTheNewImpactClip(
            PlayableEntityKind weapon,bool rocket,bool damagedUnit,string clip)
        {
            var contact=new PlayableDirectContact(1,2,default,default(BallisticPoint),
                rocket:rocket,damagedUnit:damagedUnit);
            var fact=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,default,weapon,contact:contact);
            var screen=Screen(Frame(1,new[]{fact}));
            var cue=Primed().Observe(new[]{screen,screen},.1,true).Single();
            Assert.AreEqual(clip,cue.Clip);
            Assert.AreEqual(1,cue.Pitch);
        }

        [TestCase(PlayableEntityKind.Tank,true,true,"vepr-building-impact")]
        [TestCase(PlayableEntityKind.Tank,true,false,"impact")]
        [TestCase(PlayableEntityKind.Tank,false,true,"impact")]
        [TestCase(PlayableEntityKind.Explorer,true,true,"impact")]
        [TestCase(PlayableEntityKind.Hawk,true,true,"impact")]
        [TestCase(PlayableEntityKind.Shkval,true,true,"explosion")]
        public void VeprBuildingImpactClipRequiresDirectContactOnBuilding(
            PlayableEntityKind weapon,bool building,bool physicalContact,string clip)
        {
            var contact=new PlayableDirectContact(1,2,default,physicalContact?(BallisticPoint?)default(BallisticPoint):null);
            var hit=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,default,weapon,building,contact:contact);
            var plan=Primed();
            var cue=plan.Observe(new[]{Screen(Frame(1,new[]{hit}))},.1,true).Single();
            Assert.AreEqual(clip,cue.Clip);
            if(clip=="vepr-building-impact")
            {
                Assert.AreEqual(new GameplayAudioProfile().impactGain,cue.BaseGain);
                var ordinary=new PlayableSoundEvent(2,2,PlayableSoundKind.Impact,default,PlayableEntityKind.Hawk);
                Assert.IsEmpty(plan.Observe(new[]{Screen(Frame(2,new[]{ordinary}))},.125,true));
            }
        }

        [Test] public void ExplorerHitIsQuieterAndStillDeduplicatedAcrossScreens()
        {
            var profile=new GameplayAudioProfile();
            GameplayAudioCue Impact(PlayableEntityKind weapon)
            {
                var hit=new PlayableSoundEvent(1,1,PlayableSoundKind.Impact,new NavPoint(0,0),weapon);
                var view=Frame(1,new[]{hit});return Primed(profile).Observe(new[]{Screen(view),Screen(view)},.1,true).Single();
            }
            var explorer=Impact(PlayableEntityKind.Explorer);var tank=Impact(PlayableEntityKind.Tank);
            Assert.AreEqual("impact",explorer.Clip);Assert.AreEqual(profile.explorerImpactGain,explorer.BaseGain);
            Assert.Less(explorer.Gain,tank.Gain);Assert.AreEqual(profile.impactGain,tank.BaseGain);
        }
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
