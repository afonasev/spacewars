using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCompositionEdgeTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object o,string field)=>o.GetType().GetField(field,F).GetValue(o);
        private static object[] Owners(PlayableAuthorityTick a)=>((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().ToArray();
        private static void Step(PlayableAuthorityTick a)
        {while(a.Requests.TryDequeue(out var r))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>())));if(!a.TryAdvance()){while(a.Requests.TryDequeue(out var r))Assert.True(a.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>())));Assert.True(a.TryAdvance());}}
        [TestCase(false)][TestCase(true)] public void OrdinaryOwnerCannotDrainUnavailableCombatBudgetIntoExtraRecon(bool missingScout)
        {
            var basis=OfflineParticipantAuthorityTests.Config(3,false,seed:41);var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=i==j?0:1;
            var config=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select((p,i)=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,i==0?OfflineControl.Ai:OfflineControl.Human)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);
            var a=new PlayableAuthorityTick(config,71);var loop=Owners(a).Single();var id=config.Roster[0].Id;var opening=PlayableAiOpeningComposition.Initialize(config.Seed,id,forcedOpening:PlayableAiOpening.ExplorerAllIn);
            loop.GetType().GetField("opening",F).SetValue(loop,opening);loop.GetType().GetField("openingExecutor",F).SetValue(loop,new AiOpeningExecutor(opening,71));
            var domain=Get(a,"domain");object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            var own=(PlayableOwner)Call("OwnerFor",id);Call("AddCredits",own,10000d);var home=a.ParticipantView(id).Sites.Single(s=>s.Owner==own&&s.Site.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)config.Profile.FactoryBuildSeconds+1);double bank=(double)Call("Balance",own);Call("AddCredits",own,config.Profile.ExplorerCreditCost-bank);
            Assert.AreEqual(1,a.ParticipantView(id).Entities.Count(u=>u.Owner==own&&u.Kind==PlayableEntityKind.Explorer));
            if(missingScout)Call("Damage",a.ParticipantView(id).Entities.Single(u=>u.Owner==own&&u.Kind==PlayableEntityKind.Explorer).Id,1000000);
            for(int i=0;i<80;i++)Step(a);var ledger=(AiBudgetLedger)Get(loop,"budget");var checkpoint=(PlayableAiOwnerCheckpoint)loop.GetType().GetProperty("Checkpoint",F).GetValue(loop);
            TestContext.WriteLine("O4_AFFORDABLE_RECON_PROBE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Bank=ledger.Liquid,Paid=ledger.Capture().Paid,Phase=checkpoint.GamePhase,Composition=checkpoint.Composition,Opening=checkpoint.Opening.Opening}));
            Assert.AreEqual(missingScout,ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer),"Only genuine replacement demand may consume the scout share; spare cap cannot consume unavailable combat mass.");
            Assert.AreEqual(missingScout?0:1,a.ParticipantView(id).Entities.Count(u=>u.Owner==own&&u.Kind==PlayableEntityKind.Explorer));
            Assert.AreEqual(missingScout,a.ParticipantView(id).Buildings.Any(b=>b.Owner==own&&b.PrivateState!=null&&b.PrivateState.Orders.Any(q=>q.Kind==PlayableEntityKind.Explorer&&q.PaidCost==config.Profile.ExplorerCreditCost)));
            if(!missingScout){Assert.LessOrEqual(checkpoint.Composition.BudgetShares[0],AiProfile.Initial.Value("composition.early.reconBudgetShare"));Assert.GreaterOrEqual(ledger.Liquid,config.Profile.ExplorerCreditCost);}
        }
        [Test] public void CurrentOpeningFactoryPreconditionsAreExplicit()
        {
            var config=(OfflineMatchConfiguration)typeof(NativeAiEconomyTests).GetMethod("Config",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);var a=new PlayableAuthorityTick(config,71);
            for(int i=0;i<80;i++)Step(a);
            foreach(var loop in Owners(a))
            {
                var opening=(PlayableAiOpeningCompositionState)Get(loop,"opening");var ledger=(AiBudgetLedger)Get(loop,"budget");TestContext.WriteLine("O4_E1_OPENING_PRECONDITION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Owner=opening.OwnerId,opening.Opening,ProfileHash=AiProfile.Initial.Hash,FirstPaidBuild=ledger.Capture().Paid.FirstOrDefault(e=>e.Action.Kind==PlayableCommandKind.BuildAt)?.Action.BuildingKind}));
                Assert.True(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt));
            }
        }
        [Test] public void CounterCapBoundsTotalOfTwoPositiveRoleTransfers()
        {
            var p=PlayableProfile.Default;var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==PlayableEntityKind.Explorer).lineWeight=1;
            var catalog=new AiRosterCatalog(data);var planner=new AiPhaseCompositionPlanner("player-1",71,catalog);
            var d=AiProfile.Initial.CopyData();d.revision++;foreach(var f in d.fields){if(f.path=="composition.early.reconBudgetShare"||f.path=="composition.early.lineBudgetShare")f.value=.01;if(f.path=="composition.early.supportBudgetShare")f.value=.98;}
            var units=Enumerable.Range(0,3).Select(n=>new PlayableEntitySnapshot(10+n,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(n,0),p.TankHealth,false,0,0,0)).ToArray();
            PlayableAiObservation View(bool visible)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,visible?units.Concat(new[]{new PlayableEntitySnapshot(50,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(5,0),p.TankHealth,false,0,0,0)}).ToArray():units,new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,default(NavPoint),100,1,0,0,default(NavPoint))},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(9,0,p.ArmyCapacity)));
            foreach(var cap in new[]{.15,.05})
            {
                d.fields.Single(f=>f.path=="composition.counterMaximumBudgetTransfer").value=cap;var ai=new AiProfile(d);
                var baseline=planner.Assess(View(false),p,ai,usefulScoutDemand:true);var response=planner.Assess(View(true),p,ai,usefulScoutDemand:true);
                var positive=Enumerable.Range(0,3).Select(n=>Math.Max(0,response.BudgetShares[n]-baseline.BudgetShares[n])).ToArray();
                Assert.AreEqual(2,positive.Count(x=>x>0));Assert.AreEqual(cap,positive.Sum(),1e-12,"cap is total mass, not a cap independently granted to each role");Assert.True(positive.All(x=>x<cap));Assert.AreEqual(1,response.BudgetShares.Sum(),1e-12);
            }
        }
        [Test] public void UnavailableSupportTransfersToLegalZeroShareCombatAndPaysOrdinaryCommand()
        {
            var basis=OfflineParticipantAuthorityTests.Config(3,false,seed:41);var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=i==j?0:1;
            var config=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select((p,i)=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,i==0?OfflineControl.Ai:OfflineControl.Human)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);
            var d=AiProfile.Initial.CopyData();d.revision++;d.fields.Single(f=>f.path=="composition.early.reconBudgetShare").value=.4;d.fields.Single(f=>f.path=="composition.early.lineBudgetShare").value=0;d.fields.Single(f=>f.path=="composition.early.supportBudgetShare").value=.6;d.fields.Single(f=>f.path=="composition.counterMaximumBudgetTransfer").value=0;var ai=new AiProfile(d);
            var a=new PlayableAuthorityTick(config,71,ai);var loop=Owners(a).Single();var id=config.Roster[0].Id;var opening=PlayableAiOpeningComposition.Initialize(config.Seed,id,aiProfile:ai,forcedOpening:PlayableAiOpening.ExplorerAllIn);loop.GetType().GetField("opening",F).SetValue(loop,opening);loop.GetType().GetField("openingExecutor",F).SetValue(loop,new AiOpeningExecutor(opening,71));
            var domain=Get(a,"domain");object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);var own=(PlayableOwner)Call("OwnerFor",id);Call("AddCredits",own,10000d);var home=a.ParticipantView(id).Sites.Single(t=>t.Owner==own&&t.Site.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)config.Profile.FactoryBuildSeconds+1);double bank=(double)Call("Balance",own);Call("AddCredits",own,config.Profile.TankCreditCost-bank);
            for(int i=0;i<80;i++)Step(a);var ledger=(AiBudgetLedger)Get(loop,"budget");var check=(PlayableAiOwnerCheckpoint)loop.GetType().GetProperty("Checkpoint",F).GetValue(loop);
            TestContext.WriteLine("O4_ZERO_COMBAT_SHARE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Profile=ai.CopyData(),State=check.Composition,Paid=ledger.Capture().Paid,World=a.ParticipantView(id).Population}));
            Assert.False(check.Composition.ViableCover);Assert.AreEqual(.4,check.Composition.BudgetShares[0],1e-12);Assert.AreEqual(.6,check.Composition.BudgetShares[1],1e-12);Assert.Zero(check.Composition.BudgetShares[2]);
            Assert.True(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueTank&&e.Amount==config.Profile.TankCreditCost));Assert.False(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer));
            var order=a.ParticipantView(id).Buildings.SelectMany(b=>b.PrivateState?.Orders??Array.Empty<PlayableProductionOrderSnapshot>()).Single();Assert.AreEqual(PlayableEntityKind.Tank,order.Kind);Assert.AreEqual(config.Profile.TankCreditCost,order.PaidCost);Assert.AreEqual(config.Profile.TankPopulationCost,order.PopulationCost);Assert.AreEqual(config.Profile.TankProductionSeconds,order.Duration);
            var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,config,ai);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());for(int i=0;i<90;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes(),"zero-share exact suffix "+i);}
        }
    }
}
