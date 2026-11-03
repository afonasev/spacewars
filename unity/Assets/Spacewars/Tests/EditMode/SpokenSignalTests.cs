using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SpokenSignalTests
    {
        static PlayableSnapshot View(long tick,PlayableOwner owner=PlayableOwner.Player,params PlayableMatchEvent[] facts)=>new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,owner:owner,ownerId:owner==PlayableOwner.Player?"player-1":"enemy-1",team:(int)owner).WithMatchEvents(facts);
        static GameplayAudioScreen Screen(PlayableSnapshot view,AiCommunication[] messages=null)=>new GameplayAudioScreen(view,default,new NavPoint(1,0),communications:messages);
        static PlayableMatchEvent Fact(long id,long tick,MatchEventKind kind,PlayableOwner owner=PlayableOwner.Player,long cause=0)=>new PlayableMatchEvent(id,tick,owner,kind,default,cause);
        static NativeSpeechPlan Start(GameplayAudioProfile settings=null)
        {var plan=new NativeSpeechPlan(settings??new GameplayAudioProfile());plan.Observe(new[]{Screen(View(0)),Screen(View(0,PlayableOwner.Enemy))},0,true);return plan;}
        [TestCase(false)][TestCase(true)] public void SameBlastKeepsEachViewTextButOnlyVictimSpeechRegardlessOfViewportOrder(bool reversed)
        {
            var hit=Fact(1,1,MatchEventKind.MineHit,PlayableOwner.Player,19);var triggered=Fact(2,1,MatchEventKind.MineTriggered,PlayableOwner.Enemy,19);
            var victim=View(1,PlayableOwner.Player,hit);var mineOwner=View(1,PlayableOwner.Enemy,triggered);var profile=PlayableProfile.Default;
            var a=new NativeMatchNotifications();var b=new NativeMatchNotifications();a.Update(victim,false,1,profile);b.Update(mineOwner,false,1,profile);
            Assert.AreEqual("Мина!",a.Latest.Text);Assert.AreEqual("Мина сработала",b.Latest.Text);Assert.AreEqual(default(NavPoint),a.Focus(false,1,profile).Value);
            var plan=Start();var screens=new[]{Screen(victim),Screen(mineOwner)};if(reversed)Array.Reverse(screens);plan.Observe(screens,1,true);
            Assert.AreEqual(1,plan.Pending);Assert.AreEqual("system-mine-hit",plan.Next(1,false).Key);Assert.Null(plan.Next(2,false));
            plan.Observe(screens,2,true);Assert.Zero(plan.Pending,"Counterpart must never be postponed");
        }
        [Test] public void RemoteVictimDoesNotSuppressTheLocalMineOwnersLine()
        {
            var p=new NativeSpeechPlan(new GameplayAudioProfile());p.Observe(new[]{Screen(View(0,PlayableOwner.Enemy))},0,true);
            p.Observe(new[]{Screen(View(1,PlayableOwner.Enemy,Fact(9,1,MatchEventKind.MineTriggered,PlayableOwner.Enemy,7)))},1,true);
            Assert.AreEqual("system-mine-triggered",p.Next(1,false).Key);
        }
        [Test] public void DisabledPauseAndPerspectiveChangesConsumeOldSpeechWithoutReplay()
        {
            var p=Start();var screens=new[]{Screen(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitCaptured))),Screen(View(1,PlayableOwner.Enemy))};
            p.Observe(screens,1,true,false);Assert.Zero(p.Pending);p.Observe(screens,1.1,true,true);Assert.Zero(p.Pending);
            p.Observe(screens,1.2,false);p.Observe(screens,1.3,true);Assert.Zero(p.Pending);
            p.Observe(new[]{Screen(View(2,PlayableOwner.Enemy,Fact(2,2,MatchEventKind.MineCaptured,PlayableOwner.Enemy)))},2,true);Assert.Zero(p.Pending);
        }
        [Test] public void QueueIsBoundedPrioritizedAndExpiresRatherThanCatchingUp()
        {
            var settings=new GameplayAudioProfile{speechQueueLimit=2};var p=Start(settings);
            p.Observe(new[]{Screen(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitReady),Fact(2,1,MatchEventKind.UnitCaptured),Fact(3,1,MatchEventKind.MineHit,PlayableOwner.Player,4))),Screen(View(1,PlayableOwner.Enemy))},1,true);
            Assert.AreEqual(2,p.Pending);Assert.AreEqual("system-unit-captured",p.Next(1,false).Key);Assert.Null(p.Next(1,true));Assert.Null(p.Next(4,false));
        }
        [Test] public void SameOwnerCamerasAndAlliedRecipientsDoNotMultiplySpeech()
        {
            var p=new NativeSpeechPlan(new GameplayAudioProfile());var baseline=Screen(View(0));p.Observe(new[]{baseline,baseline},0,true);
            var fresh=Screen(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitReady)));p.Observe(new[]{fresh,fresh},1,true);
            Assert.AreEqual(1,p.Pending);Assert.AreEqual("system-unit-ready",p.Next(1,false).Key);Assert.Null(p.Next(1,false));
        }
        [Test] public void AiVoiceRequiresTypedAllowedFreshFactAndNeverWaitsBehindAnotherLine()
        {
            var p=Start();var message=new AiCommunication(1,1,1,"foundry-3",0,"Выдвигаюсь",default(NavPoint),TeamSignalKind.Attack,voiceKey:"ally-advance");
            var screens=new[]{Screen(View(1),new[]{message}),Screen(View(1,PlayableOwner.Enemy))};p.Observe(screens,1,true);
            Assert.AreEqual(1,p.Pending);Assert.Null(p.Next(1,true));Assert.Zero(p.Pending);p.Observe(screens,1.1,true);Assert.Zero(p.Pending);
            var enemy=new AiCommunication(2,1,2,"foundry-4",1,"Нужна защита!",default(NavPoint),TeamSignalKind.Defend,true,"ally-need-defense");
            p.Observe(new[]{Screen(View(2),new[]{enemy}),Screen(View(2,PlayableOwner.Enemy))},2,true);Assert.Zero(p.Pending,"An opposing team voice must not leak");
        }
        [Test] public void SpecificMineWarningSuppressesDuplicateGenericDamageAndKeepsFullVocabulary()
        {
            var n=new NativeMatchNotifications();n.Update(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitsAttacked),Fact(2,1,MatchEventKind.MineHit,PlayableOwner.Player,8)),false,1,PlayableProfile.Default);
            Assert.AreEqual(1,n.Count);Assert.AreEqual("Мина!",n.Latest.Text);
            foreach(MatchEventKind kind in Enum.GetValues(typeof(MatchEventKind)))Assert.True(SpokenSignalCatalog.Phrases.ContainsKey(SpokenSignalCatalog.Key(kind)),kind.ToString());
            foreach(var phrase in SpokenSignalCatalog.Phrases.Values)Assert.False(phrase.StartsWith("Ваш"));
        }
        [Test] public void OverviewNeverAdmitsSpeechAndSelectedOwnerCannotHearOpposingEvents()
        {
            var p=new NativeSpeechPlan(new GameplayAudioProfile());
            var overview=new GameplayAudioScreen(View(0),default,new NavPoint(1,0),overview:true);
            p.Observe(new[]{overview},0,true);
            var message=new AiCommunication(9,1,1,"foundry-3",0,"Выдвигаюсь",default(NavPoint),voiceKey:"ally-advance");
            p.Observe(new[]{new GameplayAudioScreen(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitsAttacked)),default,new NavPoint(1,0),communications:new[]{message},overview:true)},1,true);
            Assert.Zero(p.Pending);Assert.Null(p.Next(1,false));
            p.Observe(new[]{Screen(View(2))},2,true);
            p.Observe(new[]{Screen(View(3,PlayableOwner.Player,Fact(2,3,MatchEventKind.UnitCaptured,PlayableOwner.Enemy),Fact(3,3,MatchEventKind.ResearchCompleted)))},3,true);
            Assert.AreEqual("system-research-ready",p.Next(3,false)?.Key);Assert.Null(p.Next(3,false));
        }
        [Test] public void PerspectiveEpochChangesClearPendingSpeechAndOverviewNotificationSounds()
        {
            var p=new NativeSpeechPlan(new GameplayAudioProfile());p.Observe(new[]{Screen(View(0))},0,true);int before=p.Epoch;
            p.Observe(new[]{Screen(View(1,PlayableOwner.Player,Fact(1,1,MatchEventKind.UnitCaptured)))},1,true);Assert.AreEqual(1,p.Pending);
            p.Observe(new[]{Screen(View(1,PlayableOwner.Enemy))},1.1,true);Assert.Greater(p.Epoch,before);Assert.Zero(p.Pending);
            var audio=new GameplayAudioPlan(new GameplayAudioProfile());
            GameplayAudioScreen Frame(long tick,params PlayableSoundEvent[] sounds)=>new GameplayAudioScreen(new PlayableSnapshot("test",1,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,sounds:sounds),default,new NavPoint(1,0),overview:true);
            audio.Observe(new[]{Frame(0)},0,true);
            var cues=audio.Observe(new[]{Frame(1,new PlayableSoundEvent(1,1,PlayableSoundKind.ProductionComplete,default,PlayableEntityKind.Tank),new PlayableSoundEvent(2,1,PlayableSoundKind.Shot,default,PlayableEntityKind.Tank))},1,true);
            Assert.False(cues.Any(c=>c.Notification));Assert.True(cues.Any(c=>c.Kind==PlayableSoundKind.Shot),"Ordinary world effects remain available");
        }
    }
}
