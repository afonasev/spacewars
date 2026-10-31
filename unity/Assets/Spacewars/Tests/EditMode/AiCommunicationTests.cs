using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiCommunicationTests
    {
        NativeLobbyConfiguration Roster(bool observer=false)=>new NativeLobbyConfiguration{Participants=new List<NativeLobbyParticipant>{
            new NativeLobbyParticipant{Human=false,Team=1,Color=0},new NativeLobbyParticipant{Human=false,Team=2,Color=1},
            new NativeLobbyParticipant{Human=!observer,Name="Союзник",Team=1,Color=2},new NativeLobbyParticipant{Human=!observer,Name="Противник",Team=2,Color=3}}};
        [Test] public void AiAudienceIsAllTeamsForSpectatorAndOnlyAlliesForPlayers()
        {
            var chat=new NativeMatchChat(Roster(),PlayableProfile.Default);chat.RegisterSpectator(-1,"Зритель");
            Assert.True(chat.SendAi(0,new AiCommunication(1,1,1,"player-1",1,"Первый ИИ",new NavPoint(3,4)),0));
            Assert.True(chat.SendAi(1,new AiCommunication(2,1,1,"foundry-2",2,"Второй ИИ"),0));
            Assert.False(chat.SendAi(0,new AiCommunication(1,1,1,"player-1",1,"Повтор",new NavPoint(3,4)),0));
            Assert.True(chat.Send(2,1,"Приватный текст человека",0));
            CollectionAssert.AreEqual(new[]{"Первый ИИ","Второй ИИ"},chat.Visible(-1,true,0).Select(m=>m.Text));
            CollectionAssert.AreEqual(new[]{"Первый ИИ","Второй ИИ"},chat.Visible(1,true,0).Select(m=>m.Text));
            CollectionAssert.AreEqual(new[]{"Первый ИИ","Приватный текст человека"},chat.Visible(2,false,0).Select(m=>m.Text));
            Assert.AreEqual("Второй ИИ",chat.Visible(3,false,0).Single().Text);
            Assert.False(chat.SendAi(2,new AiCommunication(3,1,1,"foundry-3",1,"Человек притворяется ИИ"),0));
            Assert.False(chat.SendAi(0,new AiCommunication(3,1,1,"player-1",2,"Подмена команды"),0));
            Assert.False(chat.Send(0,1,"ИИ через человеческий ввод",0));Assert.False(chat.Send(-1,1,"Командование зрителя",0));
        }
        [Test] public void SpectatorBudgetRejectsOrdinaryBurstButAllowsCriticalEvent()
        {
            var data=PlayableProfile.Default.CopyData();data.aiChatObserverLimit=1;
            var chat=new NativeMatchChat(Roster(true),PlayableProfile.Create(data));
            Assert.True(chat.SendAi(0,new AiCommunication(1,1,1,"player-1",1,"План"),0));
            Assert.False(chat.SendAi(1,new AiCommunication(2,1,1,"foundry-2",2,"Ещё план"),0));
            Assert.True(chat.SendAi(1,new AiCommunication(3,1,1,"foundry-2",2,"Угроза",critical:true),0));
            Assert.True(chat.SendAi(1,new AiCommunication(4,1,1,"foundry-2",2,"Новый план"),data.chatReadableSeconds));
        }
        [Test] public void SignalsKeepPerAuthorCapAndFrozenPointsWithPulseAndExpiration()
        {
            var profile=PlayableProfile.Default;var settings=profile.CopyData();var signals=new NativeTeamSignals(Roster());
            signals.Publish(1,1,new NavPoint(7,8),TeamSignalKind.Defend,0,profile);
            for(int i=0;i<10;i++)signals.Publish(i+2,0,new NavPoint(i,0),TeamSignalKind.Attack,0,profile);
            var all=signals.Visible(0,true,0);Assert.AreEqual(profile.TeamSignalMaximumPerAuthor+1,all.Length);
            Assert.AreEqual((PlayableOwner)1,signals.Visible(2,false,0).Single().Author);
            Assert.AreEqual(profile.TeamSignalMaximumPerAuthor,signals.Visible(1,false,0).Length);
            CollectionAssert.AreEqual(new[]{7d,8d,9d},signals.Visible(1,false,0).Select(s=>s.Point.X));
            var mark=all[0];Assert.AreEqual(1,mark.Alpha(0,profile),1e-8);
            Assert.AreEqual(profile.TeamSignalMinimumAlpha,mark.Alpha(profile.TeamSignalPulseSeconds*30/2,profile),1e-8);
            Assert.Greater(mark.Alpha((profile.TeamSignalLifetimeSeconds-.1)*30,profile),0);
            Assert.IsEmpty(signals.Visible(0,true,(long)(profile.TeamSignalLifetimeSeconds*30)));
        }
        [Test] public void CommunicationSettingsShareMetadataAndValidation()
        {
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("presentation.aiChat.")).ToArray();
            Assert.AreEqual(5,fields.Length);
            foreach(var field in fields){Assert.True(NativeBalanceFields.Editable(field),field.Path);Assert.IsNotEmpty(field.Description);Assert.Greater(field.Step,0);Assert.Greater(field.Maximum,field.Minimum);}
            var data=PlayableProfile.Default.CopyData();data.aiChatObserverLimit=2.5;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data=PlayableProfile.Default.CopyData();data.aiChatMaxAgeSeconds=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        PlayableSnapshot Snapshot(long tick,PlayableEntitySnapshot unit=null)=>new PlayableSnapshot("release",1,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100,null,
            unit==null?Array.Empty<PlayableEntitySnapshot>():new[]{unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,ownerId:"player-1",team:1);
        PlayableAiOwnerCheckpoint Checkpoint(PlayableProfile profile,AiArmyState army=null)=>new PlayableAiOwnerCheckpoint("player-1",PlayableAiOpeningComposition.SourceIdentity,profile.ProfileId,profile.Revision,7,71,0,0,0,0,0,null,null,
            PlayableAiOpeningComposition.Initialize(7,"player-1"),null,null,null,Array.Empty<PlayableAiTraceRecord>()).BindArmies(new AiArmyRegistryState{OwnerId="player-1",Generation=71,Armies=army==null?Array.Empty<AiArmyState>():new[]{army}});
        PlayableEntitySnapshot Unit(double x,bool moving,bool valid=true)=>new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(x,0),100,moving,0,0,0,
            new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,71,5,10,PlayableTacticalOrderKind.AttackMove,new NavPoint(20,0),0),orderStamp:
            new PlayableOrderStamp(1,PlayableOwner.Player,71,1,5,10,valid?PlayableOrderOrigin.Ai:PlayableOrderOrigin.Human,PlayableCommandKind.AttackMove,PlayableAiOpeningComposition.SourceIdentity,1,1));
        [Test] public void DepartureRequiresObservedMotionAndEffectiveAiOrderAndPublishesOnce()
        {
            var data=PlayableProfile.Default.CopyData();data.aiChatAuthorSeconds=0;data.aiChatTeamSeconds=0;var profile=PlayableProfile.Create(data);
            var narrator=new AiCommunicationNarrator();var army=new AiArmyState{Id=1,OwnerId="player-1",Role=AiArmyRole.Raid,Phase=AiArmyPhase.Advancing,Members=new[]{1},TacticalOwner=AiArmyPlanner.Policy};var ai=Checkpoint(profile,army);
            narrator.Observe(Snapshot(10,Unit(0,false)),ai,profile);Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue));
            narrator.Observe(Snapshot(11,Unit(0,true)),ai,profile);Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue),"Moving flag without changed position is not departure");
            narrator.Observe(Snapshot(12,Unit(1,true,false)),ai,profile);Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue),"Human order is not an AI operation");
            narrator.Observe(Snapshot(13,Unit(2,true)),ai,profile);var evt=narrator.Snapshot.Single(e=>e.Point.HasValue);StringAssert.Contains("рейд",evt.Text);Assert.AreEqual(new NavPoint(20,0),evt.Point.Value);
            narrator.Observe(Snapshot(14,Unit(3,true)),ai,profile);Assert.AreEqual(1,narrator.Snapshot.Count(e=>e.Point.HasValue));
            army.Phase=AiArmyPhase.Regrouping;narrator.Observe(Snapshot(15,Unit(4,true)),ai,profile);
            army.Phase=AiArmyPhase.Advancing;narrator.Observe(Snapshot(16,Unit(5,true)),ai,profile);
            Assert.AreEqual(1,narrator.Snapshot.Count(e=>e.Point.HasValue),"Regrouping is still the same operation");
        }
        [Test] public void MovingToFriendlyGuardPositionIsNotAnnouncedAsAnAttack()
        {
            var data=PlayableProfile.Default.CopyData();data.aiChatAuthorSeconds=0;data.aiChatTeamSeconds=0;var profile=PlayableProfile.Create(data);var narrator=new AiCommunicationNarrator();
            var army=new AiArmyState{Id=1,OwnerId="player-1",Role=AiArmyRole.Attack,Phase=AiArmyPhase.Advancing,Members=new[]{1},Objective=((int)PlayableRouteTargetKind.FriendlyAnchor)+":1",TacticalOwner=AiArmyPlanner.Policy};
            var ai=Checkpoint(profile,army);narrator.Observe(Snapshot(10,Unit(0,true)),ai,profile);narrator.Observe(Snapshot(11,Unit(1,true)),ai,profile);
            Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue));
        }
        [Test] public void SuppressedDepartureNeverReappearsAfterCooldown()
        {
            var profile=PlayableProfile.Default;var narrator=new AiCommunicationNarrator();
            var army=new AiArmyState{Id=1,OwnerId="player-1",Role=AiArmyRole.Attack,Phase=AiArmyPhase.Advancing,Members=new[]{1},TacticalOwner=AiArmyPlanner.Policy};var ai=Checkpoint(profile,army);
            narrator.Observe(Snapshot(10,Unit(0,true)),ai,profile);narrator.Observe(Snapshot(11,Unit(1,true)),ai,profile);
            Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue));narrator.Observe(Snapshot(1000,Unit(2,true)),ai,profile);Assert.False(narrator.Snapshot.Any(e=>e.Point.HasValue));
        }
    }
}
