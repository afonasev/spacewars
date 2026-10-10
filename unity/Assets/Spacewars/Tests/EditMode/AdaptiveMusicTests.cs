using System;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AdaptiveMusicTests
    {
        private static PlayableSnapshot Frame(long tick,string owner="player-1",int health=100,
            bool entity=true,PlayableProjectileSnapshot[] shots=null,long generation=1)
        {
            return new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,generation,7,tick,tick,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,
                entity?new[]{new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,0),health,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>(),
                Array.Empty<PlayableBuildingSnapshot>(),shots??Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerId:owner);
        }
        private static PlayableProjectileSnapshot Shot(int id,bool visible=true)=>new PlayableProjectileSnapshot(id,10,11,new NavPoint(0,0),visible:visible);
        private static AdaptiveMusicProfile Fast(double gap=3)=>new AdaptiveMusicProfile
            {fadeSeconds=.5,attackSeconds=1,releaseSeconds=2,combatHoldSeconds=1,silenceSeconds=gap,minimumThemeSeconds=10};

        [Test] public void HiddenFlightDoesNotExposeCombat()
        {
            var quiet=new PlayableMusicObservation();var hidden=new PlayableMusicObservation();
            Assert.AreEqual(quiet.Observe(new[]{Frame(1)}),hidden.Observe(new[]{Frame(1,shots:new[]{Shot(42,false)})}));
            Assert.AreEqual(quiet.Observe(new[]{Frame(2)}),hidden.Observe(new[]{Frame(2,shots:new[]{Shot(42,false)})}));
        }
        [Test] public void HumanScreenUnionDeduplicatesShotsAndDamage()
        {
            var observer=new PlayableMusicObservation();
            Assert.AreEqual(0,observer.Observe(new[]{Frame(1),Frame(1,"player-2")}));
            Assert.AreEqual(2,observer.Observe(new[]{Frame(2,health:90,shots:new[]{Shot(42)}),Frame(2,"player-2",90,shots:new[]{Shot(42)})}));
            Assert.AreEqual(0,observer.Observe(new[]{Frame(2,health:90,shots:new[]{Shot(42)})}),"Repeated immutable frame is not a new event.");
            Assert.AreEqual(1,observer.Observe(new[]{Frame(3,health:90),Frame(3,"player-2",90,shots:new[]{Shot(43)})}));
        }
        [Test] public void RediscoveryAndGenerationResetDoNotInventDamage()
        {
            var observer=new PlayableMusicObservation();observer.Observe(new[]{Frame(1)});
            Assert.AreEqual(0,observer.Observe(new[]{Frame(2,entity:false)}));
            Assert.AreEqual(0,observer.Observe(new[]{Frame(3,health:20)}));
            Assert.AreEqual(1,observer.Observe(new[]{Frame(4,health:10)}));
            Assert.AreEqual(0,observer.Observe(new[]{Frame(1,health:5,generation:2)}));
        }
        [Test] public void LongVisibleProjectileDoesNotBecomeRepeatedCombat()
        {
            var observer=new PlayableMusicObservation();Assert.AreEqual(1,observer.Observe(new[]{Frame(1,shots:new[]{Shot(42)})}));
            for(int tick=2;tick<700;tick++)Assert.AreEqual(0,observer.Observe(new[]{Frame(tick,shots:new[]{Shot(42)})}));
        }
        [Test] public void StartsCalmAndSmoothlyAttacksHoldsAndReleases()
        {
            var state=new AdaptiveMusicState(Fast(),5,7);
            state.Advance(.5,0,false,180);Assert.AreEqual(0,state.Intensity);Assert.AreEqual(1,state.Envelope);
            state.Advance(.25,6,false,180);Assert.AreEqual(.25,state.Intensity,1e-6);
            state.Advance(.5,6,false,180);Assert.Greater(state.Intensity,.25);
            state.Advance(.25,0,false,180);Assert.Greater(state.Intensity,.75,"Brief gaps retain combat.");
            for(int i=0;i<50;i++)state.Advance(.1,0,false,180);
            Assert.AreEqual(0,state.Intensity,1e-6);
        }
        [Test] public void PauseFreezesThemeEnvelopeAndIntensity()
        {
            var state=new AdaptiveMusicState(Fast(),5,7);state.Advance(.25,6,false,180);
            var intensity=state.Intensity;var envelope=state.Envelope;var age=state.ThemeElapsed;
            state.Advance(120,0,true,180);
            Assert.AreEqual(intensity,state.Intensity);Assert.AreEqual(envelope,state.Envelope);Assert.AreEqual(age,state.ThemeElapsed);
        }
        [TestCase(0)] [TestCase(3)] [TestCase(8)]
        public void AlternatesDistinctThemesWithBoundedOptionalSilence(double gap)
        {
            var state=new AdaptiveMusicState(Fast(gap),5,7);int first=state.Theme;
            double silence=0;bool gapStarted=false;
            for(int i=0;i<300&&state.Serial==0;i++)
            {
                state.Advance(.1,0,false,10);
                if(state.Phase==MusicPhase.Gap)gapStarted=true;
                if(gapStarted&&state.Envelope==0)silence+=.1;
            }
            Assert.AreEqual(1,state.Serial);Assert.AreNotEqual(first,state.Theme);
            Assert.LessOrEqual(silence,AdaptiveMusicState.MaximumAudibleGapSeconds);
            Assert.LessOrEqual(silence,gap+.3);
        }
        [Test] public void CombatDefersRotationAndInterruptsSilence()
        {
            var state=new AdaptiveMusicState(Fast(8),5,7);
            for(int i=0;i<150;i++)state.Advance(.1,6,false,10);
            Assert.AreEqual(0,state.Serial);Assert.AreEqual(MusicPhase.Playing,state.Phase);
            for(int i=0;i<100&&state.Phase!=MusicPhase.Gap;i++)state.Advance(.1,0,false,10);
            Assert.AreEqual(MusicPhase.Gap,state.Phase);
            state.Advance(.1,6,false,10);Assert.AreEqual(1,state.Serial);Assert.AreEqual(MusicPhase.FadingIn,state.Phase);
        }
        [Test] public void RenewedCombatCancelsFadeBeforeSilence()
        {
            var state=new AdaptiveMusicState(Fast(3),5,7);
            for(int i=0;i<120&&state.Phase!=MusicPhase.FadingOut;i++)state.Advance(.1,0,false,10);
            state.Advance(.1,0,false,10);double envelope=state.Envelope;
            state.Advance(.1,6,false,10);Assert.GreaterOrEqual(state.Envelope,envelope);
            for(int i=0;i<8;i++)state.Advance(.1,0,false,10);
            Assert.AreEqual(0,state.Serial);Assert.AreEqual(MusicPhase.Playing,state.Phase);Assert.AreEqual(1,state.Envelope);
        }
        [Test] public void ElapsedGapIsConsumedAfterALongFrame()
        {
            var state=new AdaptiveMusicState(Fast(8),5,7);
            for(int i=0;i<130&&state.Phase!=MusicPhase.Gap;i++)state.Advance(.1,0,false,10);
            Assert.AreEqual(MusicPhase.Gap,state.Phase);
            state.Advance(9,0,false,10);
            Assert.AreEqual(1,state.Serial);Assert.AreEqual(MusicPhase.FadingIn,state.Phase);Assert.AreEqual(0,state.Envelope);
            state.AlignTransport(.5);Assert.AreEqual(MusicPhase.Playing,state.Phase);Assert.AreEqual(1,state.Envelope);
        }
        [Test] public void PlaylistContainsAllFiveBeforeRepeating()
        {
            var state=new AdaptiveMusicState(Fast(0),5,42);var found=new System.Collections.Generic.HashSet<int>{state.Theme};
            for(int i=0;i<600&&state.Serial<4;i++){state.Advance(.1,0,false,10);found.Add(state.Theme);}
            Assert.AreEqual(5,found.Count);
        }
        [Test] public void InvalidProfileCannotBreakSilenceContract()
        {
            Assert.Throws<ArgumentException>(()=>new AdaptiveMusicState(Fast(11),5,7));
            var profile=Fast();profile.fadeSeconds=double.NaN;
            Assert.Throws<ArgumentException>(()=>new AdaptiveMusicState(profile,5,7));
        }
    }
}
