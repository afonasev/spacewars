using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiProductionRecoveryTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic;
        static PlayableAiObservation View(bool producer=false,double income=7,bool pending=false)
        {
            var p=PlayableProfile.Default;
            var basis=(PlayableAiObservation)typeof(NativeAiOpeningTests).GetMethod("HomeEconomyObservation",F).Invoke(null,new object[]{900,1,producer,false,false,4});
            var buildings=basis.Buildings.ToArray();
            if(pending)
            {
                var site=basis.Sites.Single();var slot=site.Site.Slots[1];
                buildings=buildings.Concat(new[]{new PlayableBuildingSnapshot(70,PlayableOwner.Player,PlayableBuildingKind.Factory,slot.Position,100,.5,0,0,default,siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,phase:ConstructionPhase.Constructing)}).ToArray();
            }
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,900,900,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,238,null,basis.Entities.ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:basis.Sites.ToArray(),homeSiteId:basis.HomeSiteId,incomePerSecond:income,population:new PlayablePopulationSnapshot(basis.Entities.Sum(u=>PlayableUnitRules.Population(p,u.Kind)),0,p.ArmyCapacity)));
        }
        static AiOpeningExecutor Executor(bool fulfilled)
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.GreedySafe),71);
            foreach(var pair in new[]{new {Name="producerChosen",Value=(object)true},new{Name="air",Value=(object)false},new{Name="initialForceFulfilled",Value=(object)fulfilled},new{Name="refineries",Value=(object)2}})
                typeof(AiOpeningExecutor).GetField(pair.Name,F).SetValue(e,pair.Value);
            return e;
        }
        [TestCase(true,7,true)][TestCase(false,7,false)][TestCase(true,0,false)]
        public void OnlyCompletedFundedRecoverySuppressesCheaperIncome(bool fulfilled,double income,bool suppress)
        {
            var p=PlayableProfile.Default;var o=View(income:income);var e=Executor(fulfilled);var normal=new AiEconomyPlanner().Plan(o,p).Candidates;
            var result=e.MacroRequests(o,p,AiProfile.Initial,normal);
            Assert.True(result.Any(c=>c.Action.BuildingKind==PlayableBuildingKind.Factory&&c.Reason=="insufficient credits"));
            Assert.AreEqual(!suppress,result.Any(c=>c.Policy=="economy"&&c.Action.BuildingKind==PlayableBuildingKind.Refinery&&c.Legal));
            if(suppress)Assert.AreEqual(p.FactoryCreditCost,e.HomeEconomyFundingProtection(o,p,AiProfile.Initial,null),"One producer quote; not producer plus replacement income");
            byte[] Save(AiOpeningExecutor x){using var s=new MemoryStream();using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))x.WriteState(w);return s.ToArray();}
            var wire=Save(e);var restored=Executor(fulfilled);using(var s=new MemoryStream(wire))using(var r=new BinaryReader(s))restored.ReadState(r,900);CollectionAssert.AreEqual(wire,Save(restored));
            CollectionAssert.AreEqual(result.Select(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)+":"+c.Reason),restored.MacroRequests(o,p,AiProfile.Initial,normal).Select(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)+":"+c.Reason));
        }
        [TestCase(false)][TestCase(true)] public void NormalCoreQuoteRequiresActualMacroAdmission(bool continuation)
        {
            var p=PlayableProfile.Default;var o=View(producer:true);var e=Executor(true);
            typeof(AiOpeningExecutor).GetField("reinforceChosen",F).SetValue(e,continuation);
            typeof(AiOpeningExecutor).GetField("reinforce",F).SetValue(e,false);
            typeof(AiOpeningExecutor).GetField("refineries",F).SetValue(e,1);
            var producer=o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{producer.Id},unitKind:PlayableEntityKind.Tank,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var candidate=new AiEconomyCandidate("production",action,AiEconomyAdmission.Reject(o,p,action,true),1);Assert.True(candidate.Legal,candidate.Reason);
            Assert.AreEqual(!continuation,e.AllowsNormalProductionFunding(o,p,AiProfile.Initial,candidate),"A quote may not freeze the mandatory income branch that suppresses that production");
        }
        [Test] public void MissingComplementaryDoctrineProducerAlsoProtectsItsBuildPrice()
        {
            var p=PlayableProfile.Default;var o=View(producer:true);var site=o.Sites.Single();var slot=site.Site.Slots[2];
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:PlayableBuildingKind.Hangar,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var request=new AiEconomyCandidate("economy",action,AiEconomyAdmission.Reject(o,p,action),1);
            Assert.AreEqual("insufficient credits",request.Reason);
            var result=Executor(true).MacroRequests(o,p,AiProfile.Initial,new AiEconomyPlanner().Plan(o,p).Candidates,doctrineProducers:new[]{request});
            Assert.True(result.Any(c=>c.Action.BuildingKind==PlayableBuildingKind.Hangar));
            Assert.False(result.Any(c=>c.Policy=="economy"&&c.Action.BuildingKind==PlayableBuildingKind.Refinery&&c.Legal));
        }
        [TestCase(false)][TestCase(true)] public void ExistingOrAlreadyPaidProducerReleasesRecoveryProtection(bool pending)
        {
            var p=PlayableProfile.Default;var o=View(producer:!pending,pending:pending);var result=Executor(true).MacroRequests(o,p,AiProfile.Initial,new AiEconomyPlanner().Plan(o,p).Candidates);
            Assert.True(result.Any(c=>c.Action.BuildingKind==PlayableBuildingKind.Refinery&&c.Legal));
            Assert.False(result.Any(c=>c.Action.BuildingKind==PlayableBuildingKind.Factory&&c.Reason=="insufficient credits"));
        }
    }
}
