using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PostMatchStatisticsTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile profile;private long sequence;
        private object Call(string method,params object[] args)=>Domain.GetMethod(method,F).Invoke(domain,args);
        private MatchResult Result=>(MatchResult)Domain.GetProperty("Result",F).GetValue(domain);
        private PlayableSnapshot View()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        [SetUp]public void Setup(){profile=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,F,null,new object[]{profile,1L,false},null);Call("BindMatchSeed",7);sequence=0;}
        private void Finish()=>Call("FinishManually");
        private PlayableCommandStatus Send(PlayableCommandKind kind,int[] ids=null,PlayableOwner owner=PlayableOwner.Player,long orderId=0)=> (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,owner==PlayableOwner.Player?"player-1":"enemy-1",kind,ids,productionOrderId:orderId,origin:PlayableOrderOrigin.Human),null);
        private int Factory(bool ready=true,int slot=1)
        {Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");if(ready)Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds);return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SlotId==slot).Id;}
        [Test]public void InitialAssetsNeverScoreButStartingArmyAppears()
        {Finish();Assert.AreEqual(7,Result.Seed);Assert.IsTrue(Result.Manual);Assert.IsNull(Result.WinnerTeam);Assert.IsFalse(Result.TeamsVisible);Assert.That(Result.Players.All(p=>p.Score==0&&p.UnitsBuilt==0&&p.BuildingsBuilt==0));Assert.That(Result.Players.All(p=>Result.Army(p.Owner,0)==profile.ExplorerPopulationCost));Assert.That(Result.Facts.All(f=>f.Kind==MatchFactKind.Army));Assert.That(Result.Players.All(p=>!Result.IsMaximum(p,5)));}
        [Test]public void ActualIncomeIncludesZerosAndRefundsAreExcluded()
        {int factory=Factory(false);Send(PlayableCommandKind.CancelBuilding,new[]{factory});Call("Step",1d);Call("Step",1d);Finish();var p=Result.Players[0];Assert.AreEqual(profile.HeadquartersIncomePerSecond*2,p.EarnedCredits);Assert.AreEqual(0,p.BuildingsBuilt);Assert.AreEqual(Math.Floor(p.EarnedCredits/2),p.Score);Assert.AreEqual(0,Result.Income(p.Owner,0));Assert.AreEqual(profile.HeadquartersIncomePerSecond,Result.Income(p.Owner,1));Assert.AreEqual(0,Result.Income(p.Owner,3));Assert.IsTrue(Result.IsMaximum(p,0));Assert.IsTrue(Result.IsMaximum(Result.Players[1],0));Assert.AreEqual(0,Result.Ranked.First().Slot);}
        [Test]public void CompletionRecordsCurrentEventCostWithoutRewritingEarlierFacts()
        {int factory=Factory(false);var data=profile.CopyData();data.revision=2;data.factoryCreditCost+=123;data.tankCreditCost+=91;Call("ApplyBalance",PlayableProfile.Create(data));Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds);Call("SpawnProduced",new NavPoint(profile.ArenaHalfExtent-4,-profile.ArenaHalfExtent+4),null,PlayableOwner.Player,PlayableEntityKind.Tank);Finish();Assert.AreEqual(data.factoryCreditCost+data.tankCreditCost,Result.Players[0].Score);Assert.AreEqual(1,Result.Players[0].UnitsBuilt);Assert.AreEqual(1,Result.Players[0].BuildingsBuilt);Assert.AreEqual(profile.ExplorerPopulationCost+profile.TankPopulationCost,Result.Army(PlayableOwner.Player,Result.Duration));}
        [Test]public void NewDestructionUsesItsEventProfileAndEarlierCompletionKeepsItsValue()
        {int factory=Factory();var data=profile.CopyData();data.revision=2;data.factoryCreditCost+=123;data.explorerCreditCost+=55;Call("ApplyBalance",PlayableProfile.Create(data));var enemy=View().Entities.Single(e=>e.Owner==PlayableOwner.Enemy);Call("DamageWithOwner",enemy.Id,100000,0,(PlayableOwner?)PlayableOwner.Player);Call("DamageWithOwner",factory,100000,0,(PlayableOwner?)PlayableOwner.Enemy);Finish();Assert.AreEqual(profile.FactoryCreditCost+data.explorerCreditCost,Result.Players[0].Score);Assert.AreEqual(data.factoryCreditCost,Result.Players[1].Score);}
        [Test]public void LastDamageOwnerGetsOneKillEvenIfSourceHasDied()
        {var target=View().Entities.Single(u=>u.Owner==PlayableOwner.Enemy);Call("DamageWithOwner",target.Id,1,0,(PlayableOwner?)PlayableOwner.Enemy);Call("DamageWithOwner",target.Id,100000,0,(PlayableOwner?)PlayableOwner.Player);Call("DamageWithOwner",target.Id,100000,0,(PlayableOwner?)PlayableOwner.Player);Finish();Assert.AreEqual(1,Result.Players[0].UnitsDestroyed);Assert.AreEqual(profile.ExplorerCreditCost,Result.Players[0].Score);Assert.AreEqual(0,Result.Army(PlayableOwner.Enemy,Result.Duration));}
        [Test]public void HostileCenterCascadeIncludesReadyAndPendingChildren()
        {Factory();Call("BuildAt",1,2,PlayableBuildingKind.Refinery,1,PlayableOwner.Player,null);Call("DamageWithOwner",1,100000,0,(PlayableOwner?)PlayableOwner.Enemy);Finish();Assert.AreEqual(3,Result.Players[1].BuildingsDestroyed);Assert.AreEqual(profile.HeadquartersCreditCost+profile.FactoryCreditCost+profile.RefineryCreditCost,Result.Players[1].Score);}
        [Test]public void FriendlyCascadeNeverContributesKills()
        {Factory();Call("DamageWithOwner",1,100000,0,(PlayableOwner?)PlayableOwner.Player);Finish();Assert.That(Result.Players.All(p=>p.BuildingsDestroyed==0));}
        [Test]public void GroupCommandAndSelectionAreSingleHumanActions()
        {var ids=View().Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).ToArray();Call("RecordHumanAction","player-1");Send(PlayableCommandKind.Hold,ids);Call("Step",1d);Call("Step",1d);Finish();Assert.AreEqual(2,Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));Assert.AreEqual(60,Result.Apm(PlayableOwner.Player,2,60));Assert.AreEqual(0,Result.Apm(PlayableOwner.Enemy,2,60));}
        [Test]public void RollingMinuteNormalizesStartupAndExpiresOldHumanActions()
        {Call("RecordHumanAction","player-1");Call("Step",10d);Call("RecordHumanAction","player-1");Call("Step",50d);Call("RecordHumanAction","player-1");Call("Step",10d);Finish();Assert.AreEqual(0,Result.Apm(PlayableOwner.Player,0,60));Assert.AreEqual(12,Result.Apm(PlayableOwner.Player,10,60));Assert.AreEqual(3,Result.Apm(PlayableOwner.Player,60,60));Assert.AreEqual(1,Result.Apm(PlayableOwner.Player,70,60));Assert.AreEqual(0,Result.Apm(PlayableOwner.Enemy,70,60));}
        [Test]public void AdmittedGameplayAttemptCountsEvenWhenTheTargetHasGone()
        {Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.QueueTank,new[]{99999}));Finish();Assert.AreEqual(1,Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));Assert.AreEqual(0,Result.Players[0].UnitsBuilt);}
        [TestCase(PlayableCommandKind.Move,false)][TestCase(PlayableCommandKind.Move,true)][TestCase(PlayableCommandKind.AttackMove,false)][TestCase(PlayableCommandKind.AttackMove,true)]
        public void UnsupportedSurfacePreservesAdmittedActionAccountingAndRecipientPriority(PlayableCommandKind kind,bool authored)
        {
            if(authored){profile=PlayableProfile.ThreeCrossingsDefault;domain=Activator.CreateInstance(Domain,F,null,new object[]{profile,1L,false},null);Call("BindMatchSeed",7);}
            int actor=View().Entities.First(e=>e.Owner==PlayableOwner.Player).Id;var target=authored?new NavPoint(0,18):new NavPoint(999,999);
            PlayableCommand Command(int[] ids,long generation=1)=>new PlayableCommand(generation,++sequence,"player-1",kind,ids,target).AsHuman();
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("Apply",Command(new[]{actor}),null));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Call("Apply",Command(new[]{99999}),null));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Call("Apply",Command(new[]{actor,actor}),null));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("ApplyWithoutHumanStatistics",Command(new[]{actor}),null));
            Assert.AreEqual(PlayableCommandStatus.StaleGeneration,Call("Apply",Command(new[]{actor},2),null));
            Finish();Assert.AreEqual(3,Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));
        }
        [Test]public void AutomaticProductionAddsCompletionsWithoutHumanActions()
        {int factory=Factory();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ToggleRepeatProduction,new[]{factory}));Call("AdvanceProduction",(double)profile.TankProductionSeconds+1);Call("AdvanceProduction",(double)profile.TankProductionSeconds+1);Finish();Assert.AreEqual(1,Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));Assert.AreEqual(2,Result.Players[0].UnitsBuilt);}
        [Test]public void AcceptedRallyIsCountedOnceAtIngress()
        {int factory=Factory();Assert.AreEqual(PlayableCommandStatus.Accepted,Send(PlayableCommandKind.SetRally,new[]{factory}));Call("AdvanceRally");Finish();Assert.AreEqual(1,Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));}
        [TestCase(true)][TestCase(false)]public void CorruptHistoryNumbersAreRejected(bool eventTime)
        {
            Call("Step",1d);byte[] bytes;using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))Call("WriteMatchHistory",writer);bytes=stream.ToArray();}
            int offset=eventTime?33:49;BitConverter.GetBytes(double.NaN).CopyTo(bytes,offset);
((System.Collections.IList)Domain.GetField("matchFacts",F).GetValue(domain)).Clear();
            using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(bytes))){var error=Assert.Throws<TargetInvocationException>(()=>Call("ReadMatchHistory",reader));Assert.IsInstanceOf<ArgumentException>(error.InnerException);Assert.That(error.InnerException.Message,Does.Contain("history fact"));}
        }
        [Test]public void NaturalVictoryFreezesTheActualWinnerExactlyOnce()
        {Call("DamageWithOwner",2,100000,0,(PlayableOwner?)PlayableOwner.Player);Call("Step",1d/30d);var result=Result;Assert.IsNotNull(result);Assert.IsFalse(result.Manual);Assert.AreEqual(0,result.WinnerTeam);Assert.AreEqual(profile.HeadquartersCreditCost,result.Players[0].Score);Call("Step",10d);Assert.AreSame(result,Result);}
        [Test]public void FrozenResultIsSameObjectAndCannotBeMutatedByFurtherOperations()
        {Call("Step",1d);Finish();var result=Result;Call("Step",20d);Call("RecordHumanAction","player-1");Finish();Assert.AreSame(result,Result);Assert.AreEqual(1,result.Tick);Assert.Throws<NotSupportedException>(()=>((System.Collections.Generic.IList<MatchFact>)result.Facts).Clear());}
        [Test]public void HistoryAndFinishedSnapshotRoundTripExactly()
        {Call("RecordHumanAction","player-1");Call("Step",1d);var bytes=(byte[])Call("CaptureWorldBytes",7,"statistics-test");var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{bytes,profile,7,"statistics-test"});Assert.AreEqual(bytes,Domain.GetMethod("CaptureWorldBytes",F).Invoke(restored,new object[]{7,"statistics-test"}));Call("Step",1d);Domain.GetMethod("Step",F).Invoke(restored,new object[]{1d});Assert.AreEqual(Call("CaptureWorldBytes",7,"statistics-test"),Domain.GetMethod("CaptureWorldBytes",F).Invoke(restored,new object[]{7,"statistics-test"}));Finish();bytes=(byte[])Call("CaptureWorldBytes",7,"statistics-test");restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{bytes,profile,7,"statistics-test"});Assert.AreEqual(Result.Players[0].Score,((MatchResult)Domain.GetProperty("Result",F).GetValue(restored)).Players[0].Score);Assert.AreEqual(bytes,Domain.GetMethod("CaptureWorldBytes",F).Invoke(restored,new object[]{7,"statistics-test"}));}
        [TestCase(2,false)][TestCase(4,true)][TestCase(8,false)]public void AllConfiguredSlotsAndTeamsAreRetained(int count,bool teams)
        {var config=OfflineParticipantAuthorityTests.Config(count,teams);var authority=new OfflineParticipantAuthority(config,1);authority.FinishManually();Assert.AreEqual(count,authority.Result.Players.Count);Assert.AreEqual(teams,authority.Result.TeamsVisible);Assert.AreEqual(config.Roster.Select(p=>p.Id),authority.Result.Players.Select(p=>p.Id));}
        [Test]public void FoundrySixSlotHistoryRestoresBeforeFreezingItsTeamResult()
        {
            var map=new FoundryMap(new FoundryProfileData());var foundry=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var config=map.Configuration(foundry,19092026);var authority=new OfflineParticipantAuthority(config,1);var human=config.Roster.First(p=>p.Control==OfflineControl.Human);authority.RecordHumanAction(human.Id);var restored=OfflineParticipantAuthority.Restore(authority.CaptureBytes(),config);restored.FinishManually();
            Assert.AreEqual(6,restored.Result.Players.Count);Assert.IsTrue(restored.Result.TeamsVisible);Assert.AreEqual(config.Seed,restored.Result.Seed);Assert.AreEqual(1,restored.Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));Assert.That(restored.Result.Players.All(p=>p.Score==0&&p.UnitsBuilt==0&&restored.Result.Army(p.Owner,0)>0));Assert.IsNull(restored.Result.WinnerTeam);
        }
    }
}
