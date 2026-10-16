using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiExpansionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object o,string n)
        {
            if(o==null)throw new ArgumentNullException(nameof(o),"Cannot read field '"+n+"' from a null object.");
            var field=o.GetType().GetField(n,F);
            if(field==null)throw new MissingFieldException(o.GetType().FullName,"Expected instance field '"+n+"' (public or non-public).");
            return field.GetValue(o);
        }
        private static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
        private static OfflineMatchConfiguration Config(PlayableBuildingKind kind=PlayableBuildingKind.Mine,bool hidden=false)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,kind,new NavPoint(-11,0),p),new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(20,-20),p)};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            return new OfflineMatchConfiguration(p,"e4-safe-expansion","e4-public-flat-v1","UnityHostRouteService",19092026,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenarioBuildings:hidden?new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,4,0,new NavPoint(20,-20))}:null);
        }
        private static object Owner(PlayableAuthorityTick a)=>((System.Collections.IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static long NextAuthoritySequence(PlayableAuthorityTick a)=>(long)Get(Get(a,"domain"),"nextAiSequence");
        private static AiExpansionPlanner Planner(PlayableAuthorityTick a)=>(AiExpansionPlanner)Get(Owner(a),"expansion");
        private static PlayableSnapshot View(PlayableAuthorityTick a,OfflineMatchConfiguration c)=>(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshot",a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player);
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("route barrier did not complete");}
        private static object ScoutActorEvidence(PlayableEntitySnapshot u)
        {
            if(u==null)return null;var order=u.CurrentOrder;var stamp=u.OrderStamp;var completion=u.Completion;
            return new{u.Id,u.Owner,u.Kind,u.Health,u.Moving,u.Held,u.NavigationOutcome,Position=new{x=u.Position.X,z=u.Position.Z},
                CurrentOrder=order==null?null:new{order.Kind,order.CommandSequence,order.IssuedTick,Destination=new{x=order.Destination.X,z=order.Destination.Z},order.TargetId,order.DestinationLocation},
                OrderStamp=stamp==null?null:new{stamp.UnitId,stamp.Owner,stamp.Generation,stamp.Revision,stamp.Sequence,stamp.Tick,stamp.Origin,stamp.Kind,stamp.Source,stamp.JobId,stamp.ActionId},
                SpatialCompletion=completion==null?null:new{completion.GroupId,completion.Incarnation,completion.ActivationTick,completion.CompletionTick,
                    OriginalTarget=new{x=completion.OriginalTarget.X,z=completion.OriginalTarget.Z},completion.SurfaceId,
                    AssignedLocation=completion.AssignedLocation,OrderActionId=completion.Order?.ActionId,OrderSequence=completion.Order?.Sequence}};
        }
        private static object ScoutArmyEvidence(AiArmyRegistryState state,int actor)
            =>state?.Armies.Where(x=>x.Phase!=AiArmyPhase.Disbanded||x.Members.Contains(actor)).Select(x=>new{x.Id,x.OwnerId,x.Role,x.TacticalOwner,x.Phase,x.LeaderId,x.Members,x.Objective,x.CreatedTick,x.ProgressTick,x.Anchor,x.Rally}).ToArray();
        private static object ScoutKnowledgeEvidence(AiKnowledgeState state)
            =>state?.Areas.Select(x=>new{AreaId=x.AreaId,ActuallyCoveredTick=x.ActuallyCoveredTick,x.EmptyAtLastCoverage,x.RevisitDue}).ToArray();
        private static object ScoutReceiptEvidence(PlayableAiOwnerCheckpoint checkpoint,
            System.Collections.Generic.IDictionary<string,(long ActionId,int Site,long ObservationTick,long DueTick,NavPoint Target)> details)
            =>checkpoint.Records.Where(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.ReceiptIdentity!=null&&details.ContainsKey(r.ReceiptIdentity.Id))
                .Select(r=>{var d=details[r.ReceiptIdentity.Id];return new{ReceiptId=r.ReceiptIdentity.Id,r.Status,r.RuntimeStatus,r.ActionId,d.Site,d.ObservationTick,d.DueTick,Target=new{x=d.Target.X,z=d.Target.Z},r.CommandSequence,r.ApplicationTick,r.Message};}).ToArray();
        private static object ScoutTargetEvidence(PlayableSnapshot snapshot,AiKnowledgeState knowledge,int actor)
            =>snapshot.PublicScoutObjectives.Select(t=>{var area=knowledge?.Areas.SingleOrDefault(x=>x.AreaId==t.SiteId);return new{t.SiteId,t.Role,t.Reachable,Approach=new{x=t.Approach.X,z=t.Approach.Z},
                Coverage=area==null?null:(object)new{area.ActuallyCoveredTick,area.EmptyAtLastCoverage,area.RevisitDue},
                Routes=snapshot.RouteProofs.Where(r=>r.UnitId==actor&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==t.SiteId)
                    .Select(r=>new{r.UnitId,r.Generation,r.Tick,r.GeometryRevision,r.Kind,r.TargetId,Origin=new{x=r.Origin.X,z=r.Origin.Z},Target=new{x=r.Target.X,z=r.Target.Z},Goal=new{x=r.Goal.X,z=r.Goal.Z},PathCount=r.Path.Count}).ToArray()};}).ToArray();
        [Test] public void LayoutConstructorPreflight()
        {
            foreach(var kind in new[]{PlayableBuildingKind.Mine,PlayableBuildingKind.Outpost})foreach(bool hidden in new[]{false,true})
            {
                var c=Config(kind,hidden);var p=c.Profile;
                var envelopes=c.Sites.SelectMany(s=>new[]{Tuple.Create(s.Position,TerritoryRules.Radius(p,s.Kind),s.Id,true)}.Concat(s.Slots.Select(t=>Tuple.Create(t.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)),s.Id,false)))).ToArray();
                foreach(var e in envelopes)Assert.Less(Math.Max(Math.Abs(e.Item1.X),Math.Abs(e.Item1.Z))+e.Item2*Math.Sqrt(2)+p.ExplorerCollisionRadius,p.ArenaHalfExtent);
                foreach(var start in c.Starts)foreach(var e in envelopes)Assert.Greater(Math.Sqrt(Math.Pow(start.ExplorerAnchor.X-e.Item1.X,2)+Math.Pow(start.ExplorerAnchor.Z-e.Item1.Z,2)),e.Item2+p.ExplorerCollisionRadius);
                TestContext.WriteLine("preflight kind="+kind+" hidden="+hidden+" envelopes="+envelopes.Length);
            }
        }
        [TestCase(PlayableBuildingKind.Mine)][TestCase(PlayableBuildingKind.Outpost)]
        public void CaptureToIncomeUsesRealTravelCaptureBuildPaymentAndSettlement(PlayableBuildingKind kind)
        {
            var c=PostOpeningConfig(kind);var a=new PlayableAuthorityTick(c,71);int actor=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            double readyBaseline=0,readyBank=0;double before=View(a,c).SettledIncome;bool claimed=false,captured=false,paid=false,ready=false;int initial=View(a,c).Credits;long incomeReady=0,initialGoalCreatedTick=-1;
            AiExpansionState completionPostState=null;
            AiExpansionCompletionReadbackForTests completion=null;
            AiExpansionPlanner.CompletionReadbackOwnerFilterForTests="west-owner";
            AiExpansionPlanner.CompletionReadbackForTests=r=>{if(r.OwnerId=="west-owner"&&r.SiteId==3)completion=r;};
            try
            {
                using(var host=new UnityHostRouteService())
                {
                    for(int i=0;i<1800;i++)
                    {
                        Step(a,host);var state=Planner(a).Capture();var v=View(a,c);var s=v.Sites.Single(x=>x.Site.Id==3);
                        if(state.SiteId==3){claimed=true;if(initialGoalCreatedTick<0)initialGoalCreatedTick=state.CreatedTick;Assert.AreEqual(actor,state.ActorId);Assert.LessOrEqual(state.CreatedTick,state.ProgressTick);}
                        captured|=s.Progress>=1&&s.Claimant==PlayableOwner.Player;
                        var b=v.Buildings.FirstOrDefault(x=>x.SiteId==3&&x.SlotId==0&&x.Owner==PlayableOwner.Player);
                        if(b!=null){Assert.True(captured);paid=true;Assert.Less(v.Credits,initial+v.SettledIncome);}
                        if(b?.Phase==ConstructionPhase.Ready){ready=true;if(incomeReady==0){incomeReady=a.Tick;readyBaseline=v.SettledIncome;readyBank=v.ExactCredits;}Assert.AreEqual(PlayableOwner.Player,s.Owner);}
                        if(ready&&completion!=null){completionPostState=state;Assert.NotNull(completion,"site 3 completion must be captured inside AiExpansionPlanner.Observe before the next goal can replace its state");
                            TestContext.WriteLine("E2-07 completion="+Newtonsoft.Json.JsonConvert.SerializeObject(completion));
                            Assert.AreEqual(3,completion.SiteId);Assert.AreEqual(a.Tick,completion.OwnerTick);Assert.GreaterOrEqual(completion.OwnerTick,incomeReady);
                            Assert.AreEqual("income",completion.Phase);Assert.LessOrEqual(completion.CreatedTick,completion.OwnerTick);Assert.AreEqual(readyBaseline,completion.InitialIncome,1e-8);
                            Assert.AreEqual(v.SettledIncome,completion.SettledIncome,1e-8);Assert.AreEqual(v.ExactCredits,completion.ExactCredits,1e-8);Assert.AreEqual(v.IncomePerSecond,completion.IncomeRate,1e-8);
                            Assert.Greater(a.Tick,incomeReady);Assert.Greater(v.SettledIncome,before);
                            double otherRate=v.Buildings.Where(x=>x.Owner==PlayableOwner.Player&&x.Phase==ConstructionPhase.Ready&&x.SiteId!=3&&x.PrivateState?.Lifecycle?.Selling!=true)
                                .Sum(x=>x.RefineryUpgraded?c.Profile.RefineryUpgradedIncome:TerritoryRules.Income(c.Profile,x.Kind))/c.Profile.IncomePeriodSeconds;
                            double expansionRate=TerritoryRules.Income(c.Profile,kind)/c.Profile.IncomePeriodSeconds;
                            Assert.AreEqual(otherRate+expansionRate,v.SettledIncome-readyBaseline,1e-8,"actual first full payout includes the new income center");
                            Assert.AreEqual(otherRate+expansionRate,v.ExactCredits-readyBank,1e-8,"bank receives actual expansion payout alongside existing sources");
                            TestContext.WriteLine("E2-07 kind="+kind+" readyTick="+incomeReady+" settlementTick="+a.Tick+" bank="+readyBank+"->"+v.ExactCredits+" settled="+readyBaseline+"->"+v.SettledIncome+" otherRate="+otherRate+" expansionRate="+expansionRate);
                            break;}
                    }
                }
            }
            finally{AiExpansionPlanner.CompletionReadbackForTests=null;AiExpansionPlanner.CompletionReadbackOwnerFilterForTests=null;}
            Assert.True(claimed);Assert.True(captured);Assert.True(paid);Assert.True(ready);Assert.NotNull(completion,"original site 3 goal must complete even if a later goal is already active");Assert.NotNull(completionPostState);
            var checkpoint=(PlayableAiOwnerCheckpoint)Owner(a).GetType().GetProperty("Checkpoint",F).GetValue(Owner(a));
            var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            var paidEntry=ledger.Capture().Paid.Single(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==3&&e.Amount==TerritoryRules.Cost(c.Profile,kind));
            Assert.NotNull(paidEntry.Receipt,"paid original expansion entry must retain its authority receipt identity");
            Assert.AreEqual(3,completion.SiteId);Assert.AreEqual(initialGoalCreatedTick,completion.CreatedTick);
            Assert.True(completionPostState.SiteId!=3||completionPostState.CreatedTick!=completion.CreatedTick,
                "post-Step planner state must release the original site 3 goal; idle or a different goal is valid");
            Assert.True(checkpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
            Assert.True(checkpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ReceiptIdentity!=null&&r.ReceiptIdentity.Id==paidEntry.Receipt.Id));
        }
        private static OfflineMatchConfiguration CompetingSpendConfig()
        {
            var c=Config();var p=c.Profile;
            var starts=new[]{new OfflineStart("west",1,c.Starts[0].Position,new NavPoint(-24,7),pin:1),c.Starts[1]};
            var sites=c.Sites.Select(x=>x.Id==1?new TerritorySite(1,PlayableBuildingKind.Headquarters,x.Position,p):x).ToArray();
            return new OfflineMatchConfiguration(p,c.SourceIdentity,"e4-competing-spend-v1",c.RouteProvenance,c.Seed,c.Roster.ToArray(),starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}});
        }
        private static OfflineMatchConfiguration PostOpeningConfig(PlayableBuildingKind kind=PlayableBuildingKind.Mine)
            =>NativeAiPostOpeningFixtures.WithReadyHome(Config(kind),"capture-to-income");
        private static OfflineMatchConfiguration PostOpeningCompetingSpendConfig()
            =>NativeAiPostOpeningFixtures.WithReadyHome(CompetingSpendConfig(),"competing-spend");
        private static OfflineMatchConfiguration AcceptedExpansionMidOpeningConfig()
        {
            var prepared=NativeAiPostOpeningFixtures.WithReadyHome(CompetingSpendConfig(),"accepted-expansion-mid-opening");
            var factory=prepared.ScenarioBuildings.Single(b=>b.LogicalPlayer==1&&b.Kind==PlayableBuildingKind.Factory);
            var c=CompetingSpendConfig();
            var costs=new double[c.Starts.Count,c.Starts.Count];
            for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity+"-accepted-expansion-mid-opening",c.MapIdentity+"-accepted-expansion-mid-opening",
                c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),prepared.Sites.ToArray(),c.Obstacles.ToArray(),costs,
                c.Spectators.ToArray(),c.ScenarioUnits.ToArray(),scenarioBuildings:new[]{factory});
        }
        private static NavPoint[] AcceptedExpansionBusyWaypoints(OfflineMatchConfiguration c)
        {
            var homeParticipant=c.Roster.Single(x=>x.LogicalPlayer==1&&x.Control==OfflineControl.Ai);
            int homeIndex=Enumerable.Range(0,c.Roster.Count).Single(i=>c.Roster[i].Id==homeParticipant.Id);
            var homeStart=c.Starts[c.Assignments[homeIndex]];var mine=c.Sites.Single(x=>x.Id==3);
            double dx=homeStart.ExplorerAnchor.X-mine.Position.X,dz=homeStart.ExplorerAnchor.Z-mine.Position.Z;
            double length=Math.Sqrt(dx*dx+dz*dz);if(length<=0)throw new ArgumentException("Home Explorer anchor must be outside the Mine capture center.");
            double clearance=TerritoryRules.CaptureRadius(c.Profile,mine.Kind)+c.Profile.ExplorerCollisionRadius+1;
            var geometry=new NavGeometry(c.Profile.ArenaHalfExtent,c.Obstacles.ToArray(),1);
            var points=new[]{new NavPoint(homeStart.ExplorerAnchor.X+dx/length*clearance,homeStart.ExplorerAnchor.Z+dz/length*clearance),
                new NavPoint(homeStart.ExplorerAnchor.X+dx/length*(clearance+2),homeStart.ExplorerAnchor.Z+dz/length*(clearance+2))};
            foreach(var point in points)
            {
                double mx=point.X-mine.Position.X,mz=point.Z-mine.Position.Z;
                Assert.Greater(Math.Sqrt(mx*mx+mz*mz),TerritoryRules.CaptureRadius(c.Profile,mine.Kind)+c.Profile.ExplorerCollisionRadius,
                    "fixture waypoint stays outside Mine capture radius plus Explorer clearance");
                Assert.True(geometry.IsFree(point,c.Profile.ExplorerCollisionRadius),"fixture waypoint is free in the original navigation geometry");
            }
            return points;
        }
        private static OfflineMatchConfiguration FirstIncomePreparationConfig()
        {
            var c=CompetingSpendConfig();
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity+"-preparation",c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),
                c.Sites.Where(s=>s.Id!=4).ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-20,14))});
        }
        [Test] public void OwnerLoopFirstIncomePreparationPersistsThroughDeferralAndTransfersAtAppliedBuild()
        {
            var c=FirstIncomePreparationConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);var ledger=(AiBudgetLedger)Get(owner,"budget");
            var forcedOpening=PlayableAiOpeningComposition.Initialize(c.Seed,"west-owner",forcedOpening:PlayableAiOpening.GreedySafe);
            owner.GetType().GetField("opening",F).SetValue(owner,forcedOpening);owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(forcedOpening,71));
            int refineryQuote=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Refinery),factoryQuote=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Factory);
            int requiredRefineries=(int)AiProfile.Initial.Value("opening.greedyRefineries");
            int homeQuote=checked(refineryQuote*requiredRefineries+factoryQuote);
            int cost=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Mine);bool partial=false,full=false,deferred=false,scheduled=false,restoredPreparation=false,restoredQueuedBuild=false,constructionScout=false,sawPreHome=false,homeReadyAtPartial=false;
            Assert.Greater(homeQuote,c.Profile.StartingCredits,"this actual-authority fixture must fund the required home quotes through real income, not conflate Factory plus Mine with home readiness");
            int preparationSite=0,preparationActor=0;long buildAppliedTick=0;long? observedHomeReadyTick=null;CurrentExpansionDecisionReadback decision=null;
            var financialGoalTimeline=new List<object>();string previousGoalClock=null;bool sawFinancialGoal=false;
            object fullFundingReadback=null,goalDisappearanceReadback=null;
            Func<string> failureReadback=()=>Newtonsoft.Json.JsonConvert.SerializeObject(new{ObservedHomeReadyTick=observedHomeReadyTick,FinancialGoalTimeline=financialGoalTimeline,FullFunding=fullFundingReadback,GoalDisappearance=goalDisappearanceReadback});
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests="west-owner";
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>{if(r.Observation.OwnerId=="west-owner")decision=r;};
            using(var host=new UnityHostRouteService())
            try
            {
                long horizon=AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30)*2;
                for(long i=0;i<horizon&&!constructionScout;i++)
                {
                    Step(a,host);var planner=Planner(a);var checkpoint=Checkpoint(a);var armies=((AiArmyRegistry)Get(owner,"armies")).Capture();
                    var observation=PlayableAiObservation.From(View(a,c));var opening=(AiOpeningExecutor)Get(owner,"openingExecutor");bool homeReady=opening.HomeEconomyReady(observation,AiProfile.Initial);
                    if(homeReady&&observedHomeReadyTick==null)observedHomeReadyTick=a.Tick;
                    var prep=armies.Armies.SingleOrDefault(x=>x.Phase!=AiArmyPhase.Disbanded&&x.Role==AiArmyRole.Scout&&x.TacticalOwner==AiScoutPlanner.Policy&&
                        x.LeaderId>0&&x.Members.Length==1&&x.Members[0]==x.LeaderId&&x.Objective!=null&&x.Objective.StartsWith("prepare:",StringComparison.Ordinal));
                    var activeFinance=ledger.Capture().Reserved.SingleOrDefault(e=>e.Purpose=="expansion-financial");
                    int activeSite=(int)Call(Get(owner,"expansionFinancial"),"ActiveSite",Get(owner,"arbiter"));
                    if(activeFinance!=null)sawFinancialGoal=true;
                    string goalClock=activeFinance==null?"none":activeSite+":"+activeFinance.CreatedTick+":"+activeFinance.ExpiresTick;
                    if(goalClock!=previousGoalClock)
                    {
                        financialGoalTimeline.Add(new{Tick=a.Tick,HomeReady=homeReady,ObservedHomeReadyTick=observedHomeReadyTick,ActiveSite=activeSite,
                            FinancialGoal=activeFinance==null?null:(object)new{activeFinance.Id,Site=activeFinance.Action.SiteId,activeFinance.Amount,activeFinance.CreatedTick,activeFinance.ExpiresTick}});
                        previousGoalClock=goalClock;
                    }
                    if(!homeReady)
                    {
                        sawPreHome=true;if(activeFinance!=null&&activeFinance.Amount>0)TestContext.WriteLine("Q7_PREPARATION_GATE_READBACK "+failureReadback());
                        Assert.True(activeFinance==null||activeFinance.Amount==0,"expansion savings remain zero until required home READY facts exist");
                        bool noPreHomeExpansionApplied=!checkpoint.Records.Any(r=>r.Policy=="expansion"&&(r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.BuildAt)&&r.Status==PlayableAiDeliveryStatus.Applied);
                        if(!noPreHomeExpansionApplied)TestContext.WriteLine("Q7_PREPARATION_GATE_READBACK "+failureReadback());
                        Assert.False(checkpoint.Records.Any(r=>r.Policy=="expansion"&&(r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.BuildAt)&&r.Status==PlayableAiDeliveryStatus.Applied),"no new expansion move/build is accepted before home readiness");
                    }
                    bool financeReady=(bool)Call(Get(owner,"expansionFinancial"),"Ready",c.Profile,ledger,Get(owner,"arbiter"),a.Tick);
                    if(activeFinance!=null&&activeFinance.Amount>0&&activeFinance.Amount<cost)
                    {
                        partial=true;Assert.True(homeReady,"partial savings require actual home READY facts");homeReadyAtPartial=true;
                        Assert.IsNull(prep,"partial savings must not retain capture preparation");
                    }
                    var acceptedExpansion=planner.Capture();
                    if(preparationActor==0&&acceptedExpansion.SiteId>0)
                    {preparationActor=acceptedExpansion.ActorId;preparationSite=acceptedExpansion.SiteId;}
                    if(acceptedExpansion.SiteId>0&&!restoredPreparation)
                    {
                        var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);
                        try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"exact C9 fully funded capture obligation restore");restoredPreparation=true;}
                        finally{restored.Stop();}
                    }
                    Func<string,object> boundaryReadback=stage=>
                    {
                        var budget=ledger.Capture();var mission=Planner(a).Capture();
                        return new{Stage=stage,Tick=a.Tick,HomeReady=homeReady,ObservedHomeReadyTick=observedHomeReadyTick,ActiveSite=activeSite,
                            CurrentGoal=activeFinance==null?null:(object)new{activeFinance.Id,Site=activeFinance.Action.SiteId,Kind=activeFinance.Action.Kind,Slot=activeFinance.Action.SlotId,Entities=activeFinance.Action.CopyEntityIds(),activeFinance.Amount,activeFinance.CreatedTick,activeFinance.ExpiresTick,Receipt=activeFinance.Receipt?.Id,activeFinance.AwaitingPayment,activeFinance.ExactSettled},
                            Decision=decision==null?null:(object)new{ObservationTick=decision.Observation.Tick,decision.FinanceSite,decision.FinanceReady,
                                ExpansionAction=decision.ExpansionAction==null?null:(object)new{Kind=decision.ExpansionAction.Kind,SiteId=decision.ExpansionAction.SiteId,EntityIds=decision.ExpansionAction.CopyEntityIds()},
                                decision.Selected,ExpansionState=decision.ExpansionState==null?null:(object)new{decision.ExpansionState.SiteId,decision.ExpansionState.ActorId,decision.ExpansionState.CreatedTick,decision.ExpansionState.ExpiresTick,decision.ExpansionState.ProgressTick,decision.ExpansionState.Phase,decision.ExpansionState.Reason}},
                            Ledger=new{budget.Liquid,budget.SafetyReserve,budget.PaidTotal,Reserved=budget.Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Kind=e.Action.Kind,Site=e.Action.SiteId,Entities=e.Action.CopyEntityIds(),Receipt=e.Receipt?.Id,e.AwaitingPayment,e.ExactSettled}).ToArray(),Unpaid=budget.Unpaid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Kind=e.Action.Kind,Site=e.Action.SiteId,Entities=e.Action.CopyEntityIds(),Receipt=e.Receipt?.Id,e.AwaitingPayment,e.ExactSettled}).ToArray(),Paid=budget.Paid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Kind=e.Action.Kind,Site=e.Action.SiteId,Entities=e.Action.CopyEntityIds(),Receipt=e.Receipt?.Id,e.AwaitingPayment,e.ExactSettled}).ToArray()},
                            Trace=checkpoint.Records.Where(r=>r.Policy=="expansion"||r.ReceiptIdentity!=null).Select(r=>new{r.Policy,r.Kind,r.ActionId,r.DueTick,r.CommandSequence,r.ApplicationTick,r.Status,r.RuntimeStatus,Receipt=r.ReceiptIdentity?.Id,r.Message}).ToArray(),
                            Actors=observation.Entities.Select(e=>new{e.Id,e.Kind,e.Health,e.Moving,e.Held,e.NavigationOutcome,e.Position,Order=e.CurrentOrder==null?null:(object)new{e.CurrentOrder.Kind,e.CurrentOrder.CommandSequence,e.CurrentOrder.IssuedTick,e.CurrentOrder.Destination,e.CurrentOrder.TargetId}}).ToArray(),
                            Armies=armies.Armies,ArmyMission=checkpoint.ArmyMission,ExpansionMission=mission};
                    };
                    if(fullFundingReadback==null&&(financeReady||decision?.FinanceReady==true||activeFinance?.Amount==cost))fullFundingReadback=boundaryReadback("full-funding");
                    if(goalDisappearanceReadback==null&&sawFinancialGoal&&activeFinance==null)goalDisappearanceReadback=boundaryReadback("financial-goal-disappeared");
                    full|=financeReady||decision?.FinanceReady==true;
                    bool directBuildDeferral=financeReady&&decision!=null&&decision.Observation.Tick==a.Tick&&decision.FinanceSite==preparationSite&&
                        decision.ExpansionAction?.Kind==PlayableCommandKind.BuildAt&&decision.ExpansionAction.SiteId==preparationSite&&
                        !decision.Selected.Any(x=>x.StartsWith("expansion:",StringComparison.Ordinal)&&x.Contains(":BuildAt:"+preparationSite.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"));
                    deferred|=directBuildDeferral;
                    int currentPreparationSite=prep==null?0:int.Parse(prep.Objective.Substring("prepare:".Length),System.Globalization.CultureInfo.InvariantCulture);
                    var decisionBudget=ledger.Capture();
                    var protectedBuild=decisionBudget.Reserved.SingleOrDefault(e=>e.Purpose=="expansion-build"&&e.Amount==cost);
                    var expansionPlannerState=planner.Capture();
                    var pendingMoves=decisionBudget.Unpaid.Where(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.Move&&
                        e.Action.SiteId==preparationSite&&e.Action.CopyEntityIds().Contains(preparationActor)&&e.Receipt!=null).ToArray();
                    var pendingMove=pendingMoves.Length==1?pendingMoves[0]:null;
                    var scheduledMove=pendingMove==null?null:checkpoint.Records.SingleOrDefault(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&
                        r.Status==PlayableAiDeliveryStatus.Scheduled&&r.DueTick>a.Tick&&r.ReceiptIdentity?.Id==pendingMove.Receipt.Id);
                    bool noTargetBuildApplied=!checkpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>0);
                    bool noTargetCenter=!View(a,c).Buildings.Any(b=>b.SiteId==preparationSite&&b.SlotId==0&&b.Owner==PlayableOwner.Player);
                    bool fundedMoveDeferral=preparationActor>0&&
                        protectedBuild!=null&&protectedBuild.Action.Kind==PlayableCommandKind.BuildAt&&protectedBuild.Action.BuildingKind==PlayableBuildingKind.Mine&&protectedBuild.Action.SiteId==preparationSite&&protectedBuild.Action.CopyEntityIds().Contains(preparationActor)&&
                        expansionPlannerState.SiteId==preparationSite&&expansionPlannerState.ActorId==preparationActor&&planner.ClaimsSite(preparationSite)&&planner.ClaimsActor(preparationActor)&&
                        pendingMove!=null&&pendingMove.Action.CopyEntityIds().Length==1&&scheduledMove!=null&&scheduledMove.ReceiptIdentity.Id==pendingMove.Receipt.Id&&
                        noTargetBuildApplied&&noTargetCenter;
                    deferred|=fundedMoveDeferral;
                    if(prep!=null)
                    {
                        int site=int.Parse(prep.Objective.Substring("prepare:".Length),System.Globalization.CultureInfo.InvariantCulture);
                        var plannerState=planner.Capture();
                        if(site!=activeSite&&site!=plannerState.SiteId)
                        {
                            var financeLedger=ledger.Capture();
                            string diagnostic=string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "prep-site mismatch tick={0} objectiveSite={1} actor={2} financeSite={3} planner(site={4},actor={5},phase={6},created={7},reason={8}) financeReserve(site={9},amount={10}) ledger(reserved={11},unpaid={12}) decision(tick={13},financeSite={14},financeReady={15},action={16}/{17},expansion={18},selected=[{19}]) army(id={20},phase={21},members=[{22}])",
                                a.Tick,site,prep.LeaderId,activeSite,plannerState.SiteId,plannerState.ActorId,plannerState.Phase,plannerState.CreatedTick,plannerState.Reason,
                                activeFinance?.Action.SiteId??0,activeFinance?.Amount??0,
                                Newtonsoft.Json.JsonConvert.SerializeObject(financeLedger.Reserved.Select(e=>new{e.Purpose,e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Receipt=e.Receipt?.Id}).ToArray()),
                                Newtonsoft.Json.JsonConvert.SerializeObject(financeLedger.Unpaid.Select(e=>new{e.Purpose,e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Receipt=e.Receipt?.Id}).ToArray()),
                                decision?.Observation.Tick??-1,decision?.FinanceSite??0,decision?.FinanceReady??false,
                                decision?.ExpansionAction?.Kind.ToString()??"none",decision?.ExpansionAction?.SiteId??0,
                                decision?.ExpansionState==null?"none":decision.ExpansionState.SiteId+"/"+decision.ExpansionState.Phase,
                                decision==null?"":string.Join(",",decision.Selected),prep.Id,prep.Phase,string.Join(",",prep.Members));
                            Assert.Fail("preparation objective must bind to the current first-income or handed-off expansion site; "+diagnostic);
                        }
                        if(preparationActor==0){preparationActor=prep.LeaderId;preparationSite=site;}
                        Assert.AreEqual(preparationActor,prep.LeaderId);Assert.AreEqual(preparationSite,site);
                        if(activeFinance!=null)
                        {
                            if(activeFinance.Amount>0){Assert.True(homeReady,"first partial expansion savings require actual home READY facts");homeReadyAtPartial=true;}
                            Assert.AreEqual(cost,activeFinance.Amount,"unaccepted preparation has full construction funding");
                            full|=activeFinance.Amount==cost;
                        }
                        if(!restoredPreparation)
                        {
                            var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);
                            try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"exact C9 preparation objective restore");restoredPreparation=true;}
                            finally{restored.Stop();}
                        }
                    }
                    var pending=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Get(x,"PolicyName")=="expansion"&&((PlayableAiAction)Get(x,"Action")).Kind==PlayableCommandKind.BuildAt&&((PlayableAiAction)Get(x,"Action")).SiteId==preparationSite);
                    if(preparationSite>0&&pending!=null)
                    {
                        scheduled=true;
                        if(!restoredQueuedBuild)
                        {var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);
                            try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"exact C9 queued BuildAt keeps the preparation actor/objective bound");restoredQueuedBuild=true;}
                            finally{restored.Stop();}}
                    }
                    var applied=checkpoint.Records.FirstOrDefault(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>0&&planner.Capture().SiteId==preparationSite);
                    if(applied!=null)buildAppliedTick=applied.ApplicationTick;
                    var center=View(a,c).Buildings.FirstOrDefault(b=>b.SiteId==preparationSite&&b.SlotId==0&&b.Owner==PlayableOwner.Player);
                    if(buildAppliedTick>0&&center!=null&&center.Phase!=ConstructionPhase.Ready&&checkpoint.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>buildAppliedTick))
                    {
                        Assert.True(planner.ClaimsSite(preparationSite),"Applied construction remains claimed while its Explorer resumes scouting");
                        Assert.AreEqual("construction",planner.Capture().Phase);
                        constructionScout=true;
                    }
                }
            }
            finally{PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;}
            Assert.True(partial,"owner-loop exposes partial first-income savings without capture preparation"+(!partial?" "+failureReadback():""));
            Assert.True(sawPreHome&&homeReadyAtPartial,"real home opening is observed before the first partial expansion reserve"+(!(sawPreHome&&homeReadyAtPartial)?" "+failureReadback():""));
            Assert.True(full,"first-income site reaches full funding"+(!full?" "+failureReadback():""));Assert.True(deferred,"ready funding is deferred at least once while the preparation/owner obligations remain in force"+(!deferred?" "+failureReadback():""));
            Assert.True(scheduled,"ordinary owner loop schedules the exact first-income BuildAt"+(!scheduled?" "+failureReadback():""));Assert.True(restoredPreparation,"exact C9 preparation restore"+(!restoredPreparation?" "+failureReadback():""));Assert.True(restoredQueuedBuild,"exact C9 queued BuildAt restore"+(!restoredQueuedBuild?" "+failureReadback():""));
            Assert.True(buildAppliedTick>0,"first-income BuildAt receives its ordinary Applied receipt"+(buildAppliedTick<=0?" "+failureReadback():""));Assert.True(constructionScout,"the Applied BuildAt hands off to a later ordinary scout move without releasing the paid center"+(!constructionScout?" "+failureReadback():""));
        }
        [Test] public void AcceptedExpansionProtectsFutureBuildThenResumesScoutDuringConstruction()
        {
            var c=AcceptedExpansionMidOpeningConfig();var a=new PlayableAuthorityTick(c,71);var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            var initialView=View(a,c);int initialActorCount=initialView.Entities.Count(e=>e.Owner==PlayableOwner.Player);
            Assert.AreEqual(500,c.Profile.StartingCredits);Assert.AreEqual(1,c.ScenarioBuildings.Count(b=>b.LogicalPlayer==1&&b.Kind==PlayableBuildingKind.Factory));
            Assert.IsEmpty(c.ScenarioBuildings.Where(b=>b.LogicalPlayer==1&&b.Kind==PlayableBuildingKind.Refinery));Assert.IsEmpty(c.ScenarioUnits);
            Assert.AreEqual(1,initialActorCount,"the genesis has only the original automatic Explorer");
            var initialActor=initialView.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var initialHomeStart=c.Starts[c.Assignments[Enumerable.Range(0,c.Roster.Count).Single(i=>c.Roster[i].LogicalPlayer==1&&c.Roster[i].Control==OfflineControl.Ai)]];
            Assert.AreEqual(PlayableEntityKind.Explorer,initialActor.Kind);Assert.AreEqual(initialHomeStart.ExplorerAnchor,initialActor.Position,"original genesis Explorer position is retained");
            var busyWaypoints=AcceptedExpansionBusyWaypoints(c);
            bool held=false,paid=false,ready=false,completed=false,scoutOwnedDuringPending=false,scoutedBeforeIncome=false,actorReleased=false;double readyIncome=0,readyBank=0;long readyTick=0,releaseTick=0,buildAppliedTick=0;int actor=initialActor.Id;int moveIndex=0;Spacewars.Simulation.Ai.AiReceiptIdentity homeRefineryAppliedReceipt=null;
            var scoutMoveActors=new Dictionary<string,int>(StringComparer.Ordinal);
            var scoutMoveDetails=new Dictionary<string,(long ActionId,int Site,long ObservationTick,long DueTick,NavPoint Target)>(StringComparer.Ordinal);
            PlayableSnapshot releaseSnapshot=null;PlayableEntitySnapshot releaseActorSnapshot=null;AiArmyRegistryState releaseArmies=null;AiKnowledgeState releaseKnowledge=null;
            PlayableAiOwnerCheckpoint releaseCheckpoint=null;AiExpansionState releasePlannerState=null;
            using(var host=new UnityHostRouteService())
            {
                long matchStartTick=a.Tick;long expiry=AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30);
                long openingSetupTicks=AiProfile.SecondsToTicks(AiProfile.Initial.Value("opening.deadlineSeconds"),30)+1;
                long expansionWindowStartTick=-1,expansionWindowExpiresTick=-1,expansionObservationEndTick=-1;
                for(long i=0;!completed&&(expansionWindowStartTick<0?a.Tick-matchStartTick<openingSetupTicks:a.Tick<expansionObservationEndTick);i++)
                {
                    if(!actorReleased)
                    {
                    var beforeStep=View(a,c);var beforeActor=beforeStep.Entities.Single(e=>e.Id==actor);var beforeOwner=Owner(a);
                    var beforeLedger=ledger.Capture();var beforeCheckpoint=Checkpoint(a);int observedHomeSite=beforeStep.HomeSiteId;
                    if(homeRefineryAppliedReceipt==null&&observedHomeSite>0)
                    {
                        var homeRefineryPaid=beforeLedger.Paid.FirstOrDefault(e=>e.Purpose=="economy"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Refinery&&e.Action.SiteId==observedHomeSite&&e.Receipt!=null&&
                            beforeCheckpoint.Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ReceiptIdentity?.Id==e.Receipt.Id));
                        if(homeRefineryPaid!=null)homeRefineryAppliedReceipt=homeRefineryPaid.Receipt;
                    }
                    var currentStamp=beforeActor.OrderStamp;bool acceptedAiMove=currentStamp?.Origin==PlayableOrderOrigin.Ai&&currentStamp.Kind==PlayableCommandKind.Move;
                    var appliedScoutMove=acceptedAiMove?beforeCheckpoint.Records.FirstOrDefault(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.ActionId==currentStamp.ActionId&&
                        (r.Status==PlayableAiDeliveryStatus.Scheduled||r.Status==PlayableAiDeliveryStatus.Accepted||r.Status==PlayableAiDeliveryStatus.Applied)):null;
                    bool activeAiScoutMove=homeRefineryAppliedReceipt!=null&&appliedScoutMove!=null;
                    bool ordinaryMoveCompleted=beforeActor.CurrentOrder==null&&beforeActor.NavigationOutcome==NavigationOutcome.Arrived;
                    bool initialMove=beforeActor.CurrentOrder==null&&currentStamp==null;
                    if(!actorReleased&&(initialMove||homeRefineryAppliedReceipt!=null&&(ordinaryMoveCompleted||activeAiScoutMove)))
                    {
                        var owner=beforeOwner;var opening=(AiOpeningExecutor)Get(owner,"openingExecutor");var observation=PlayableAiObservation.From(beforeStep);
                        bool homeReady=opening.HomeEconomyReady(observation,AiProfile.Initial);int mineQuote=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Mine);
                        var finance=Get(owner,"expansionFinancial");var arbiter=Get(owner,"arbiter");
                        var heldMineQuote=beforeLedger.Reserved.SingleOrDefault(e=>e.Purpose=="expansion-financial"&&e.Action.SiteId==3&&e.Action.BuildingKind==PlayableBuildingKind.Mine&&e.Amount==mineQuote&&a.Tick<e.ExpiresTick&&(bool)Call(finance,"Valid",e,arbiter));
                        bool fullMineQuote=ledger.Available>=mineQuote||heldMineQuote!=null;
                        if(homeReady&&fullMineQuote&&!activeAiScoutMove)
                        {
                            var homeSiteId=beforeStep.HomeSiteId;Assert.Greater(homeSiteId,0);
                            var homeFactory=beforeStep.Buildings.Single(b=>b.SiteId==homeSiteId&&b.Kind==PlayableBuildingKind.Factory&&b.Owner==PlayableOwner.Player);
                            Assert.AreEqual(ConstructionPhase.Ready,homeFactory.Phase);
                            int requiredHomeRefineries=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(c.Seed,"west-owner",aiProfile:AiProfile.Initial),71).RequiredHomeRefineryCount(AiProfile.Initial);
                            var readyHomeRefineries=beforeStep.Buildings.Where(b=>b.SiteId==homeSiteId&&b.Kind==PlayableBuildingKind.Refinery&&b.Owner==PlayableOwner.Player&&b.Phase==ConstructionPhase.Ready).ToArray();
                            var checkpointBeforeStop=Checkpoint(a);
                            int readyRefineriesWithAppliedReceipts=readyHomeRefineries.Count(b=>beforeLedger.Paid.Any(e=>e.Purpose=="economy"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==homeSiteId&&e.Action.SlotId==b.SlotId&&e.Action.BuildingKind==PlayableBuildingKind.Refinery&&e.Receipt!=null&&checkpointBeforeStop.Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ReceiptIdentity?.Id==e.Receipt.Id)));
                            Assert.GreaterOrEqual(readyHomeRefineries.Length,requiredHomeRefineries);Assert.GreaterOrEqual(readyRefineriesWithAppliedReceipts,requiredHomeRefineries,
                                "home readiness is backed by actual READY refinery buildings and their Applied receipts");
                            var neutralMine=beforeStep.Sites.Single(x=>x.Site.Id==3);Assert.AreEqual(0,neutralMine.Progress);Assert.IsNull(neutralMine.Claimant);Assert.IsNull(neutralMine.Owner);
                            Assert.AreEqual(0,Planner(a).Capture().SiteId,"no expansion accepted before the release decision");
                            Assert.AreEqual(a.Tick,beforeStep.Tick,"fixture commands do not advance the match clock");Assert.AreEqual(19092026,c.Seed,"the original match seed is retained");
                            Assert.NotNull(beforeActor.OrderStamp,"the release witness has an ordinary order history");Assert.AreEqual(PlayableCommandKind.Move,beforeActor.OrderStamp.Kind);
                            Assert.AreEqual(NavigationOutcome.Arrived,beforeActor.NavigationOutcome,"Stop follows completion of the current ordinary Move");
                            Assert.IsFalse(checkpointBeforeStop.Records.Any(r=>r.Policy=="expansion"&&(r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.BuildAt)),"no accepted expansion move/build history exists before Stop");
                            Assert.IsFalse(beforeLedger.Unpaid.Concat(beforeLedger.Paid).Any(e=>e.Purpose=="expansion"&&e.Action.SiteId==3&&e.Action.Kind==PlayableCommandKind.BuildAt),"no accepted Mine BuildAt history exists before Stop");
                            TestContext.WriteLine("MIDOPENING_PRE_STOP_FINANCE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,Available=ledger.Available,MineQuote=mineQuote,FullMineHold=heldMineQuote==null?null:(object)new{heldMineQuote.Id,heldMineQuote.Amount,heldMineQuote.CreatedTick,heldMineQuote.ExpiresTick,Site=heldMineQuote.Action.SiteId,Kind=heldMineQuote.Action.BuildingKind},Reserved=beforeLedger.Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Building=e.Action.BuildingKind,Receipt=e.Receipt?.Id}).ToArray(),Unpaid=beforeLedger.Unpaid.Select(e=>new{e.Id,e.Purpose,e.Amount,Site=e.Action.SiteId,Kind=e.Action.Kind,Receipt=e.Receipt?.Id}).ToArray(),Paid=beforeLedger.Paid.Select(e=>new{e.Id,e.Purpose,e.Amount,Site=e.Action.SiteId,Kind=e.Action.Kind,Building=e.Action.BuildingKind,Receipt=e.Receipt?.Id}).ToArray()}));
                            long stopSequence=NextAuthoritySequence(a);
                            var stopped=a.Apply(new PlayableCommand(71,stopSequence,"west-owner",PlayableCommandKind.Stop,new[]{actor},origin:PlayableOrderOrigin.Human));
                            Assert.AreEqual(PlayableCommandStatus.Applied,stopped.Status,"ordinary Stop releases the real Explorer order");actorReleased=true;
                        }
                        else
                        {
                            var destination=busyWaypoints[moveIndex++%busyWaypoints.Length];long moveSequence=NextAuthoritySequence(a);
                            var moved=a.Apply(new PlayableCommand(71,moveSequence,"west-owner",PlayableCommandKind.Move,new[]{actor},destination,origin:PlayableOrderOrigin.Human));
                            Assert.AreEqual(PlayableCommandStatus.Applied,moved.Status,"reissue an ordinary Move only after the previous Move completes");Assert.AreEqual(a.Tick,moved.AppliedTick);
                            var acceptedMove=View(a,c).Entities.Single(e=>e.Id==actor);
                            Assert.NotNull(acceptedMove.OrderStamp,"accepted authority Move is durable before spatial activation");Assert.AreEqual(actor,acceptedMove.OrderStamp.UnitId);
                            Assert.AreEqual(a.Generation,acceptedMove.OrderStamp.Generation);Assert.AreEqual(moveSequence,acceptedMove.OrderStamp.Sequence);Assert.AreEqual(PlayableCommandKind.Move,acceptedMove.OrderStamp.Kind);Assert.AreEqual(PlayableOrderOrigin.Human,acceptedMove.OrderStamp.Origin);
                            var pendingRoute=Call(Get(Get(a,"domain"),"navigation"),"PendingRequestFor",actor);
                            Assert.NotNull(pendingRoute,"the accepted Move remains backed by its actual pending route until a later barrier installs CurrentOrder");
                        }
                    }
                    }
                    Step(a,host);var v=View(a,c);var state=Planner(a).Capture();
                    if(!actorReleased)Assert.NotNull(v.Entities.SingleOrDefault(e=>e.Id==actor&&e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer&&e.Health>0),"the original Explorer remains alive while preparing the accepted expansion; legitimate produced units may increase owner count");
                    if(expansionWindowStartTick<0&&state.SiteId==3)
                    {
                        expansionWindowStartTick=state.CreatedTick;expansionWindowExpiresTick=state.ExpiresTick;expansionObservationEndTick=expansionWindowExpiresTick+90;
                        Assert.GreaterOrEqual(expansionWindowStartTick,matchStartTick,"accepted expansion window is anchored to its actual planner creation tick");
                        Assert.AreEqual(expiry,expansionWindowExpiresTick-expansionWindowStartTick,"accepted expansion keeps the existing reservation TTL");
                    }
                    var fund=ledger.Capture().Reserved.FirstOrDefault(e=>e.Purpose=="expansion-build");
                    if(state.SiteId==3&&!paid&&!v.Buildings.Any(b=>b.SiteId==3&&b.Owner==PlayableOwner.Player))
                    {
                        var unpaid=ledger.Capture().Unpaid.FirstOrDefault(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==3);
                        Assert.AreEqual(1,(fund!=null?1:0)+(unpaid!=null?1:0),"one protected held or actually admitted unpaid build, never double hold");
                        var protection=fund??unpaid;Assert.AreEqual(c.Profile.MineCreditCost,protection.Amount);Assert.AreEqual(3,protection.Action.SiteId);CollectionAssert.AreEqual(new[]{actor},protection.Action.EntityIds);
                        if(fund!=null)held=true;
                        else
                        {
                            var owner=Owner(a);var failingCheckpoint=Checkpoint(a);var actorView=v.Entities.SingleOrDefault(e=>e.Id==actor);
                            bool scheduled=unpaid.Receipt!=null&&failingCheckpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Scheduled&&r.ReceiptIdentity?.Id==unpaid.Receipt.Id);
                            if(!held||!scheduled)
                            {
                                var observation=PlayableAiObservation.From(v);var opening=(AiOpeningExecutor)Get(owner,"openingExecutor");
                                var finance=Get(owner,"expansionFinancial");var arbiter=Get(owner,"arbiter");var budget=ledger.Capture();
                                var pending=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().Select(x=>
                                {
                                    var action=(PlayableAiAction)Get(x,"Action");var identity=(Spacewars.Simulation.Ai.AiReceiptIdentity)Get(x,"Identity");
                                    return new{Policy=(string)Get(x,"PolicyName"),action.ActionId,action.Kind,action.BuildingKind,action.SiteId,action.SlotId,Actors=action.EntityIds.ToArray(),
                                        ObservationTick=(long)Get(x,"ObservationTick"),DueTick=(long)Get(x,"DueTick"),Receipt=identity?.Id};
                                }).ToArray();
                                object Entry(AiBudgetEntry e)=>e==null?null:(object)new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Receipt=e.Receipt?.Id,e.AwaitingPayment,
                                    Action=new{e.Action.ActionId,e.Action.Kind,e.Action.BuildingKind,e.Action.SiteId,e.Action.SlotId,Actors=e.Action.EntityIds.ToArray()}};
                                TestContext.WriteLine("ACCEPTED_EXPANSION_UNPAID_READBACK "+Newtonsoft.Json.JsonConvert.SerializeObject(new
                                {
                                    Tick=a.Tick,Seed=c.Seed,HomeEconomyReady=opening.HomeEconomyReady(observation,AiProfile.Initial),BootstrapActive=(bool)Call(finance,"BootstrapActive",arbiter),MineQuote=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Mine),
                                    Opening=opening.Capture(),Expansion=state,Actor=actorView==null?null:(object)new{actorView.Id,actorView.Owner,actorView.Kind,Position=new{x=actorView.Position.X,z=actorView.Position.Z},actorView.Moving,actorView.Held,actorView.CurrentOrder,actorView.OrderStamp,actorView.NavigationOutcome},
                                    Claims=new{ExpansionActor=Planner(a).ClaimsActor(actor),ExpansionSite=Planner(a).ClaimsSite(3),Army=((AiArmyRegistry)Get(owner,"armies")).ArmyFor(actor),PendingActors=pending.SelectMany(x=>x.Actors).ToArray()},
                                    CurrentFund=Entry(fund),CurrentUnpaid=Entry(unpaid),Ledger=new{budget.OwnerId,budget.Generation,budget.Liquid,budget.SafetyReserve,budget.Reserved,budget.Unpaid,Available=ledger.Available,
                                        ReservedEntries=budget.Reserved.Select(Entry).ToArray(),UnpaidEntries=budget.Unpaid.Select(Entry).ToArray()},Pending=pending,
                                    Checkpoint=new{failingCheckpoint.OwnerId,failingCheckpoint.DecisionTick,failingCheckpoint.LastPolicy,failingCheckpoint.LastActionKind,failingCheckpoint.PendingActionId,failingCheckpoint.PendingDueTick,
                                        Records=failingCheckpoint.Records.Where(r=>r.Policy=="expansion"&&(r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.BuildAt))
                                            .Select(r=>new{r.ActionId,r.Kind,r.Status,r.RuntimeStatus,r.ApplicationTick,Receipt=r.ReceiptIdentity?.Id,r.Message}).ToArray()}
                                }));
                            }
                            Assert.True(actorReleased,"the real home and finance gates released the ordinary Explorer Move with Stop");
                            Assert.True(held,"the exact expansion-build hold was observed before the unpaid BuildAt");
                            Assert.NotNull(unpaid.Receipt,"unpaid expansion BuildAt must carry its admitted receipt");
                            Assert.True(scheduled,"the unpaid BuildAt receipt must match an exact Scheduled owner-checkpoint record");
                        }
                        Assert.GreaterOrEqual(ledger.Liquid-ledger.Unpaid,ledger.Reserved);
                        Assert.True(v.Sites.Single(x=>x.Site.Id==1).Site.Slots.Count>0);
                    }
                    var mine=v.Buildings.FirstOrDefault(b=>b.SiteId==3&&b.Owner==PlayableOwner.Player);
                    if(mine!=null)
                    {paid=true;Assert.True(held);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.False(ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==3));Assert.AreEqual(1,ledger.Capture().Paid.Count(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt));}
                    var checkpoint=Checkpoint(a);
                    var appliedBuild=checkpoint.Records.FirstOrDefault(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(buildAppliedTick==0&&appliedBuild!=null)
                    {
                        buildAppliedTick=appliedBuild.ApplicationTick;
                        // Applied BuildAt releases the Explorer; settlement completes the center's
                        // obligation later and must not restart the same scout-response deadline.
                        releaseTick=buildAppliedTick;
                        var releaseOwner=Owner(a);releaseSnapshot=v;
                        releaseActorSnapshot=v.Entities.SingleOrDefault(x=>x.Id==actor);
                        releaseArmies=((AiArmyRegistry)Get(releaseOwner,"armies")).Capture();releaseKnowledge=((AiKnowledgeTracker)Get(releaseOwner,"knowledge")).Capture();
                        releaseCheckpoint=checkpoint;releasePlannerState=state;
                    }
                    foreach(var item in ((System.Collections.IEnumerable)Get(Owner(a),"pendingItems")).Cast<object>().Where(x=>(string)Get(x,"PolicyName")=="scout"))
                    {
                        var action=(PlayableAiAction)Get(item,"Action");var identity=(Spacewars.Simulation.Ai.AiReceiptIdentity)Get(item,"Identity");
                        if(action.Kind!=PlayableCommandKind.Move||!action.EntityIds.Contains(actor)||identity==null||scoutMoveActors.ContainsKey(identity.Id)||buildAppliedTick<=0||
                            (long)Get(item,"DueTick")<=buildAppliedTick||mine==null||mine.Phase==ConstructionPhase.Ready)continue;
                        var scheduled=checkpoint.Records.SingleOrDefault(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Scheduled&&r.ReceiptIdentity?.Id==identity.Id);
                        if(scheduled==null)continue;
                        Assert.AreEqual(actor,action.EntityIds.Single(),"the sampled pending scout receipt belongs to the expansion Explorer");
                        Assert.Greater(((AiArmyRegistry)Get(Owner(a),"armies")).ArmyFor(actor),0,"selected scout ownership exists throughout its scheduled receipt interval");
                        scoutOwnedDuringPending=true;scoutMoveActors.Add(identity.Id,actor);
                        scoutMoveDetails[identity.Id]=(action.ActionId,action.SiteId,(long)Get(item,"ObservationTick"),(long)Get(item,"DueTick"),action.Target);
                    }
                    foreach(var receipt in checkpoint.Records.Where(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied))
                    {
                        if(receipt.ReceiptIdentity==null||!scoutMoveActors.TryGetValue(receipt.ReceiptIdentity.Id,out var receiptActor)||receiptActor!=actor||receipt.ApplicationTick<=buildAppliedTick||mine==null||mine.Phase==ConstructionPhase.Ready)continue;
                        Assert.AreEqual(3,state.SiteId,$"the accepted construction obligation remains live while its same-receipt Explorer scouts; reason={state.Reason}, phase={state.Phase}, tick={a.Tick}, centerId={mine.Id}, slot={mine.SlotId}, centerPhase={mine.Phase}");
                        Assert.True(Planner(a).ClaimsSite(3));Assert.AreEqual(actor,state.ActorId);scoutedBeforeIncome=true;
                    }
                    if(mine?.Phase==ConstructionPhase.Ready)
                    {
                        ready=true;if(readyTick==0){readyTick=a.Tick;readyIncome=v.SettledIncome;readyBank=v.ExactCredits;}
                        if(state.SiteId==0)
                        {
                            double expected=v.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true).Sum(b=>b.RefineryUpgraded?c.Profile.RefineryUpgradedIncome:TerritoryRules.Income(c.Profile,b.Kind))/c.Profile.IncomePeriodSeconds;
                            Assert.AreEqual(expected,v.SettledIncome-readyIncome,1e-8);Assert.GreaterOrEqual(v.ExactCredits-readyBank,expected-1e-8);
                            StringAssert.Contains("settled",state.Reason);completed=true;
                            TestContext.WriteLine("competing expansion ready="+readyTick+" release="+releaseTick+" bank="+readyBank+"->"+v.ExactCredits+" settled="+readyIncome+"->"+v.SettledIncome+" rate="+expected);
                        }
                    }
                }
                if(!held||!paid||!scoutOwnedDuringPending||!scoutedBeforeIncome||!ready||!completed)
                {
                    var endSnapshot=View(a,c);var endActor=endSnapshot.Entities.SingleOrDefault(e=>e.Id==actor);var endOwner=Owner(a);var endOpening=(AiOpeningExecutor)Get(endOwner,"openingExecutor");
                    var endObservation=PlayableAiObservation.From(endSnapshot);var endCheckpoint=Checkpoint(a);var endLedger=ledger.Capture();var endNavigation=Get(Get(a,"domain"),"navigation");var endRoute=Call(endNavigation,"PendingRequestFor",actor) as NavigationRequest;
                    var endPending=((System.Collections.IEnumerable)Get(endOwner,"pendingItems")).Cast<object>().Select(x=>
                    {var action=(PlayableAiAction)Get(x,"Action");var identity=(Spacewars.Simulation.Ai.AiReceiptIdentity)Get(x,"Identity");return new{Policy=(string)Get(x,"PolicyName"),action.ActionId,action.Kind,action.BuildingKind,action.SiteId,action.SlotId,Actors=action.EntityIds.ToArray(),ObservationTick=(long)Get(x,"ObservationTick"),DueTick=(long)Get(x,"DueTick"),Receipt=identity?.Id};}).ToArray();
                    var endHomeRefineryReceipts=endLedger.Paid.Where(e=>e.Purpose=="economy"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Refinery&&e.Action.SiteId==endSnapshot.HomeSiteId&&e.Receipt!=null)
                        .Select(e=>new{e.Action.SiteId,e.Action.SlotId,Receipt=e.Receipt.Id,Applied=endCheckpoint.Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ReceiptIdentity?.Id==e.Receipt.Id)}).ToArray();
                    TestContext.WriteLine("MIDOPENING_PRE_STOP_FINANCE_FAILURE_READBACK "+Newtonsoft.Json.JsonConvert.SerializeObject(new
                    {
                        Tick=a.Tick,Seed=c.Seed,OriginalOwnerActorCount=initialActorCount,HomeSiteId=endSnapshot.HomeSiteId,HomeEconomyReady=endOpening.HomeEconomyReady(endObservation,AiProfile.Initial),
                        AttemptWindow=new{MatchStartTick=matchStartTick,OpeningDeadlineSeconds=AiProfile.Initial.Value("opening.deadlineSeconds"),OpeningSetupTicks=openingSetupTicks,
                            ExpansionCreatedTick=expansionWindowStartTick,ExpansionExpiresTick=expansionWindowExpiresTick,ExpansionReservationTicks=expansionWindowExpiresTick-expansionWindowStartTick,
                            ExpansionObservationEndTick=expansionObservationEndTick,SetupWindowEnded=expansionWindowStartTick<0&&endSnapshot.Tick-matchStartTick>=openingSetupTicks,
                            ExpansionWindowEnded=expansionWindowStartTick>=0&&endSnapshot.Tick>=expansionObservationEndTick},
                        Opening=endOpening.Capture(),Expansion=Planner(a).Capture(),Mine=endSnapshot.Sites.Single(x=>x.Site.Id==3),Flags=new{held,paid,scoutOwnedDuringPending,scoutedBeforeIncome,actorReleased,ready,completed},
                        HomeRefineryAppliedReceipt=homeRefineryAppliedReceipt?.Id,HomeRefineryReceipts=endHomeRefineryReceipts,
                        Actor=endActor==null?null:(object)new{endActor.Id,endActor.Owner,endActor.Kind,endActor.Health,Position=new{x=endActor.Position.X,z=endActor.Position.Z},endActor.Moving,endActor.Held,endActor.NavigationOutcome,
                            Order=endActor.CurrentOrder==null?null:(object)new{endActor.CurrentOrder.Kind,endActor.CurrentOrder.CommandSequence,endActor.CurrentOrder.IssuedTick,Destination=endActor.CurrentOrder.Destination},
                            Stamp=endActor.OrderStamp==null?null:(object)new{endActor.OrderStamp.Generation,endActor.OrderStamp.Sequence,endActor.OrderStamp.Kind,endActor.OrderStamp.Origin,endActor.OrderStamp.Source,endActor.OrderStamp.JobId,endActor.OrderStamp.ActionId}},
                        PendingRoute=endRoute==null?null:(object)new{endRoute.Session,endRoute.Request,endRoute.Entity,endRoute.Order,endRoute.Start,endRoute.Goal},
                        Ledger=new{endLedger.OwnerId,endLedger.Generation,endLedger.Liquid,endLedger.SafetyReserve,ReservedTotalAmount=endLedger.Reserved.Sum(e=>e.Amount),UnpaidTotalAmount=endLedger.Unpaid.Sum(e=>e.Amount),Available=ledger.Available,
                            Reserved=endLedger.Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Building=e.Action.BuildingKind,Receipt=e.Receipt?.Id}).ToArray(),
                            Unpaid=endLedger.Unpaid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Building=e.Action.BuildingKind,Receipt=e.Receipt?.Id}).ToArray(),
                            Paid=endLedger.Paid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Kind=e.Action.Kind,Building=e.Action.BuildingKind,Receipt=e.Receipt?.Id}).ToArray()},
                        CurrentMineQuote=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Mine),CurrentFinanceHold=endLedger.Reserved.Where(e=>e.Purpose=="expansion-financial").Select(e=>new{e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,Site=e.Action.SiteId,Building=e.Action.BuildingKind}).ToArray(),
                        Pending=endPending,OwnerRecords=endCheckpoint.Records.Where(r=>r.Policy=="economy"||r.Policy=="scout"||r.Policy=="expansion").Select(r=>new{r.Policy,r.ActionId,r.Kind,r.Status,r.RuntimeStatus,r.ApplicationTick,Receipt=r.ReceiptIdentity?.Id,r.Message}).ToArray()
                    }));
                }
                Assert.True(held);
                Assert.True(paid);
                Assert.True(scoutOwnedDuringPending,"the expansion Explorer retains an army claim for the selected pending Scout receipt");
                Assert.True(scoutedBeforeIncome,"the exact scheduled Scout receipt is Applied before the first READY income center");Assert.True(ready);Assert.True(completed);Assert.False(Planner(a).ClaimsActor(actor));Assert.False(Planner(a).ClaimsSite(3));
                var afterIncomeCheckpoint=Checkpoint(a);
                bool appliedEconomyBuild=afterIncomeCheckpoint.Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied);
                if(!appliedEconomyBuild)
                {
                    var afterIncomeOwner=Owner(a);var afterIncomeView=View(a,c);var economyDecision=afterIncomeCheckpoint.EconomyDecision;
                    var economyBuildReceipts=afterIncomeCheckpoint.Records.Where(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt)
                        .Select(r=>new{r.Status,r.RuntimeStatus,r.ApplicationTick,r.Message,r.ReceiptIdentity}).ToArray();
                    var currentPending=((System.Collections.IEnumerable)Get(afterIncomeOwner,"pendingItems")).Cast<object>()
                        .Select(x=>{var action=(PlayableAiAction)Get(x,"Action");return new{Policy=(string)Get(x,"PolicyName"),action.Kind,action.BuildingKind,Actors=action.EntityIds.ToArray(),action.SiteId,action.SlotId,action.ParentId,
                            ObservationTick=(long)Get(x,"ObservationTick"),DueTick=(long)Get(x,"DueTick"),Identity=Get(x,"Identity")};}).ToArray();
                    TestContext.WriteLine("ECONOMY_BUILD_HISTORY_FAILURE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{
                        Tick=a.Tick,ReleaseTick=releaseTick,ReadyTick=readyTick,Bank=afterIncomeView.ExactCredits,Settled=afterIncomeView.SettledIncome,
                        ProfileQuotes=new{Mine=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Mine),Refinery=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Refinery),
                            Factory=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Factory),Science=TerritoryRules.Cost(c.Profile,PlayableBuildingKind.ScientificCenter)},
                        EconomyDecision=economyDecision==null?null:(object)new{economyDecision.Tick,economyDecision.Ordinal,economyDecision.Liquid,economyDecision.Reserved,economyDecision.Unpaid,economyDecision.SafetyReserve,economyDecision.Available,economyDecision.Reason,
                            Candidates=economyDecision.Candidates.Where(x=>x.Kind==PlayableCommandKind.BuildAt).Select(x=>new{x.Policy,x.Kind,x.BuildingKind,x.SiteId,x.SlotId,x.EntityIds,x.Credits,x.Selected,x.Reason}).ToArray()},
                        EconomyBuildReceipts=economyBuildReceipts,CurrentPending=currentPending,
                        Buildings=afterIncomeView.Buildings.Select(b=>new{b.Id,b.Owner,b.Kind,b.SiteId,b.SlotId,b.Phase,b.Health,Queue=b.PrivateState?.QueueCount,Selling=b.PrivateState?.Lifecycle?.Selling}).ToArray(),
                        Sites=afterIncomeView.Sites.Select(s=>new{Site=s.Site.Id,Owner=s.Owner,Claimant=s.Claimant,Progress=s.Progress,CenterId=s.CenterId,
                            Slots=s.Site.Slots.Select(slot=>new{slot.Id,slot.Position,slot.Heading}).ToArray()}).ToArray(),
                        ExpansionActor=ScoutActorEvidence(afterIncomeView.Entities.SingleOrDefault(x=>x.Id==actor)),
                        ExpansionArmy=ScoutArmyEvidence(((AiArmyRegistry)Get(afterIncomeOwner,"armies")).Capture(),actor),
                        Opening=afterIncomeCheckpoint.Opening,Ledger=ledger.Capture()
                    }));
                }
                Assert.True(appliedEconomyBuild,"an ordinary economy BuildAt Applied receipt must exist in cumulative owner history");
                long deadline=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30)+1;
                for(long i=0;i<deadline&&!Checkpoint(a).Records.Any(r=>r.Policy=="scout"&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=releaseTick);i++)
                {
                    Step(a,host);var postStepOwner=Owner(a);
                    foreach(var item in ((System.Collections.IEnumerable)Get(postStepOwner,"pendingItems")).Cast<object>().Where(x=>(string)Get(x,"PolicyName")=="scout"))
                    {
                        var action=(PlayableAiAction)Get(item,"Action");var identity=(Spacewars.Simulation.Ai.AiReceiptIdentity)Get(item,"Identity");
                        if(action.Kind!=PlayableCommandKind.Move||!action.EntityIds.Contains(actor)||identity==null||scoutMoveDetails.ContainsKey(identity.Id))continue;
                        scoutMoveActors[identity.Id]=actor;scoutMoveDetails[identity.Id]=(action.ActionId,action.SiteId,(long)Get(item,"ObservationTick"),(long)Get(item,"DueTick"),action.Target);
                    }
                }
                var postReleaseCheckpoint=Checkpoint(a);
                bool postReleaseScoutApplied=postReleaseCheckpoint.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=releaseTick&&r.ApplicationTick-releaseTick<=deadline);
                if(!postReleaseScoutApplied)
                {
                    var endSnapshot=View(a,c);var endOwner=Owner(a);var endArmies=((AiArmyRegistry)Get(endOwner,"armies")).Capture();
                    var endKnowledge=((AiKnowledgeTracker)Get(endOwner,"knowledge")).Capture();var endPlannerState=Planner(a).Capture();
                    var nativeProfile=(AiProfile)Get(endOwner,"nativeProfile");var openingExecutor=(AiOpeningExecutor)Get(endOwner,"openingExecutor");
                    var endActorSnapshot=endSnapshot.Entities.SingleOrDefault(x=>x.Id==actor);
                    object ScoutBindings()=>new{postReleaseCheckpoint.AiProfileId,postReleaseCheckpoint.AiRevision,postReleaseCheckpoint.AiHash,postReleaseCheckpoint.Difficulty,postReleaseCheckpoint.PersonalitySeed,
                        ScoutPlan=openingExecutor.ScoutPlan,ScoutBudget=(int)endOwner.GetType().GetProperty("ScoutBudget",F).GetValue(endOwner),
                        DecisionIntervalTicks=(long)Get(endOwner,"DecisionIntervalTicks"),ObservationDelayTicks=(long)Get(endOwner,"ObservationDelayTicks"),RepeatOrderTicks=(long)Get(endOwner,"repeatTicks"),
                        DecisionSeconds=nativeProfile.DifficultyValue(postReleaseCheckpoint.Difficulty,"decisionSeconds"),ReactionSeconds=nativeProfile.DifficultyValue(postReleaseCheckpoint.Difficulty,"reactionSeconds"),
                        RevisitSeconds=nativeProfile.Value("scouting.revisitSeconds"),RevisitTicks=AiProfile.SecondsToTicks(nativeProfile.Value("scouting.revisitSeconds"),30),
                        StallSeconds=nativeProfile.Value("armies.stallSeconds"),StallTicks=AiProfile.SecondsToTicks(nativeProfile.Value("armies.stallSeconds"),30),
                        RepeatOrderSeconds=nativeProfile.Value("decision.repeatOrderSeconds"),postReleaseCheckpoint.LastScoutTick,postReleaseCheckpoint.LastScoutOrderTick,postReleaseCheckpoint.DecisionTick};
                    var diagnostic=new{ReleaseTick=releaseTick,BuildAppliedTick=buildAppliedTick,WindowStartTick=releaseTick,DeadlineTicks=deadline,
                        ExpectedWindowEndTick=releaseTick+deadline,ActualWindowEndTick=endSnapshot.Tick,ScoutAppliedInWindow=false,
                        Release=new{SnapshotTick=releaseTick,Actor=ScoutActorEvidence(releaseActorSnapshot),Armies=ScoutArmyEvidence(releaseArmies,actor),Knowledge=ScoutKnowledgeEvidence(releaseKnowledge),
                            Objectives=ScoutTargetEvidence(releaseSnapshot,releaseKnowledge,actor),Expansion=releasePlannerState,LastScoutTick=releaseCheckpoint?.LastScoutTick,LastScoutOrderTick=releaseCheckpoint?.LastScoutOrderTick},
                        WindowEnd=new{SnapshotTick=endSnapshot.Tick,Actor=ScoutActorEvidence(endActorSnapshot),Armies=ScoutArmyEvidence(endArmies,actor),Knowledge=ScoutKnowledgeEvidence(endKnowledge),
                            Objectives=ScoutTargetEvidence(endSnapshot,endKnowledge,actor),Expansion=endPlannerState},
                        Bindings=ScoutBindings(),ScoutReceiptHistory=new{AtRelease=ScoutReceiptEvidence(releaseCheckpoint,scoutMoveDetails),AtWindowEnd=ScoutReceiptEvidence(postReleaseCheckpoint,scoutMoveDetails)}};
                    TestContext.WriteLine("POST_RELEASE_SCOUT_FAILURE "+Newtonsoft.Json.JsonConvert.SerializeObject(diagnostic));
                }
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=releaseTick&&r.ApplicationTick-releaseTick<=deadline));
                var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
            }
        }
        private static OfflineMatchConfiguration ProfileConfig(OfflineMatchConfiguration c,PlayableProfile p)=>new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,c.RouteCost(0,1)},{c.RouteCost(1,0),0}},scenarioBuildings:c.ScenarioBuildings.ToArray());
        [Test] public void LiveFundingHonorsPauseCancelledBuildRepriceRestoreAndPaidTerms()
        {
            var c=PostOpeningCompetingSpendConfig();var a=new PlayableAuthorityTick(c,71);var ledger=(AiBudgetLedger)Get(Owner(a),"budget");int actor=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                var first=ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build");Assert.AreEqual(c.Profile.MineCreditCost,first.Amount);
                long tick=a.Tick;Assert.True(a.TryAdvance(true));Assert.AreEqual(tick,a.Tick);Assert.AreEqual(first.Amount,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount);
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<80;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
                var data=c.Profile.CopyData();data.revision++;data.mineCreditCost=150;var next=PlayableProfile.Create(data);double bank=View(a,c).ExactCredits;long paid=ledger.PaidTotal;
                Assert.IsNull(a.ApplyProfile(next));c=ProfileConfig(c,next);Assert.AreEqual(bank,View(a,c).ExactCredits);Assert.AreEqual(paid,ledger.PaidTotal);
                Assert.AreEqual(150,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount);
                Assert.True(ledger.Capture().Paid.Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Factory).All(e=>e.Amount==PlayableProfile.Default.FactoryCreditCost));
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<900&&!ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);i++)Step(a,host);
                Assert.True(ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt));
                Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));
                Assert.False(Planner(a).AllowsScoutReuse(actor),"a selected or admitted BuildAt has not yet applied");
                Assert.True(a.TryAdvance(true));Assert.AreEqual(150,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount,"actual unpaid cancellation reprotects the valid commitment");
                Assert.False(Planner(a).AllowsScoutReuse(actor),"a cancelled BuildAt does not unlock scouting");
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<900&&!ledger.Capture().Paid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);i++)Step(a,host);
                var settled=ledger.Capture().Paid.Single(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);Assert.AreEqual(150,settled.Amount);
                data=next.CopyData();data.revision++;data.mineCreditCost=250;var later=PlayableProfile.Create(data);Assert.IsNull(a.ApplyProfile(later));c=ProfileConfig(c,later);
                Assert.AreEqual(150,ledger.Capture().Paid.Single(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt).Amount);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                var broken=PlayableWorldState.Decode(a.CaptureBytes());broken.AiAuthority[4]=4;Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(broken.Encode(),c),"older live schema explicitly refused");
            }
        }
        [Test] public void LiveFundingReleasesOnStopWithoutBankRefund()
        {
            var c=PostOpeningCompetingSpendConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                var ledger=(AiBudgetLedger)Get(Owner(a),"budget");Assert.True(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));double bank=View(a,c).ExactCredits;
                a.Stop();Assert.AreEqual(bank,View(a,c).ExactCredits);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().SiteId);Assert.Zero((long)Get(Planner(a),"pendingId"));
            }
        }
        [Test] public void ClaimPauseRestoreProfileAndRestartUseRealAuthority()
        {
            var c=NativeAiPostOpeningFixtures.WithReadyHome(Config(),"claim-pause-restore");var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                Assert.AreEqual(3,Planner(a).Capture().SiteId);var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                var state=Planner(a).Capture();long tick=a.Tick;for(int i=0;i<10;i++)Assert.True(a.TryAdvance(true));Assert.AreEqual(tick,a.Tick);Assert.AreEqual(state.ExpiresTick,Planner(a).Capture().ExpiresTick);
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);for(int i=0;i<80;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(x=>x.path=="economy.expansionRiskRatio").value=0.75;var ai=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,ai));
                Assert.AreEqual(3,Planner(a).Capture().SiteId);b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                var fresh=new PlayableAuthorityTick(c,72,ai);Assert.Zero(Planner(fresh).Capture().SiteId);Assert.AreEqual("idle",Planner(fresh).Capture().Phase);
            }
        }
        [TestCase(false)][TestCase(true)] public void ActorLossAndExpiryReleaseClaimWithoutFakeIncome(bool expiry)
        {
            var c=NativeAiPostOpeningFixtures.WithReadyHome(Config(),"actor-loss-expiry");var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);var state=Planner(a).Capture();Assert.AreEqual(3,state.SiteId);
                if(expiry)
                {
                    var o=PlayableAiObservation.From(View(a,c));var field=typeof(AiExpansionPlanner).GetField("expires",F);field.SetValue(Planner(a),a.Tick);
                    Step(a,host);StringAssert.Contains("expired",Planner(a).Capture().Reason);
                }
                else{Call(Get(a,"domain"),"Damage",state.ActorId,10000);Step(a,host);StringAssert.Contains("actor lost",Planner(a).Capture().Reason);}
                Assert.False(((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().ActorId);Assert.Zero(Planner(a).Capture().SiteId);Assert.False(View(a,c).Buildings.Any(b=>b.SiteId==3));
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            }
        }
        private static PlayableAiObservation RoutedObservation(PlayableAuthorityTick a,OfflineMatchConfiguration c)
        {
            var first=PlayableAiObservation.From(View(a,c));
            return PlayableAiObservation.From((PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotWithRoutes",a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,AiExpansionPlanner.RouteRequests(first,c.Profile)));
        }
        private static PlayableAiOwnerCheckpoint Checkpoint(PlayableAuthorityTick a)=>(PlayableAiOwnerCheckpoint)Owner(a).GetType().GetProperty("Checkpoint",F).GetValue(Owner(a));
        [TestCase(PlayableBuildingKind.Mine)][TestCase(PlayableBuildingKind.Outpost)]
        public void SingleProjectionPreservesFullObservationProofsAndActualPlans(PlayableBuildingKind kind)
        {
            var c=Config(kind);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                foreach(int targetTick in new[]{0,45,120,300,800,1000})
                {
                    while(a.Tick<targetTick)Step(a,host);
                    foreach(bool paused in new[]{false,true})
                    {
                        var first=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshot",a.Tick,paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player);
                        var requests=AiExpansionPlanner.RouteRequests(PlayableAiObservation.From(first),c.Profile).Concat(AiScoutPlanner.RouteRequests(first.Entities,first.PublicScoutObjectives,first.Owner,first.Generation,first.Tick,c.Profile)).GroupBy(r=>new {r.UnitId,r.Kind,r.TargetId}).Select(g=>g.First()).ToArray();
                        var legacy=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotWithRoutes",a.Tick,first.Status,paused,first.Metrics,null,c.Seed,PlayableOwner.Player,requests);
                        var one=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotForAi",a.Tick,first.Status,paused,first.Metrics,null,c.Seed,PlayableOwner.Player,true);
                        // AI snapshots intentionally omit presentation sound events. Preserve
                        // exact comparison of every gameplay/public field and check audio separately.
                        Assert.AreEqual(PlayableAiCanonical.Encode(first.Sounds),PlayableAiCanonical.Encode(legacy.Sounds));Assert.IsEmpty(one.Sounds);
                        legacy=new PlayableSnapshot(legacy.ProfileId,legacy.ProfileRevision,legacy.Generation,legacy.Seed,legacy.Sequence,legacy.Tick,legacy.Status,legacy.Paused,legacy.Outcome,legacy.Credits,legacy.Geometry,legacy.Entities.ToArray(),legacy.Buildings.ToArray(),legacy.Projectiles.ToArray(),legacy.Metrics,legacy.Failure,
                            sites:legacy.Sites.ToArray(),incomePerSecond:legacy.IncomePerSecond,vision:legacy.Vision,discoveredSites:legacy.DiscoveredSites.ToArray(),population:legacy.Population,impacts:legacy.Impacts.ToArray(),researchAvailability:legacy.ResearchAvailability.ToArray(),ownerResearch:legacy.OwnerResearch.ToArray(),publicScoutObjectives:legacy.PublicScoutObjectives.ToArray(),ownCenterDamage:legacy.OwnCenterDamage.ToArray(),routeProofs:legacy.RouteProofs.ToArray(),artillerySupport:legacy.ArtillerySupport.ToArray(),owner:legacy.Owner,exactCredits:legacy.ExactCredits,ownerId:legacy.OwnerId,team:legacy.Team,participants:legacy.Participants.ToArray(),activeProfile:legacy.ActiveProfile,homeSiteId:legacy.HomeSiteId,ownerEliminated:legacy.OwnerEliminated,settledIncome:legacy.SettledIncome,intelEnvelopes:legacy.IntelEnvelopes.ToArray());
                        Assert.AreEqual(PlayableAiCanonical.Encode(legacy),PlayableAiCanonical.Encode(one),"all AI public fields and real route proofs");
                        var lo=PlayableAiObservation.From(legacy);var ro=PlayableAiObservation.From(one);Assert.AreEqual(PlayableAiCanonical.Encode(lo),PlayableAiCanonical.Encode(ro));
                        var lp=(AiExpansionPlanner)typeof(AiExpansionPlanner).GetMethod("Fork",F).Invoke(Planner(a),null);
                        var rp=(AiExpansionPlanner)typeof(AiExpansionPlanner).GetMethod("Fork",F).Invoke(Planner(a),null);
                        Assert.AreEqual(PlayableAiCanonical.Encode(lp.TryPlan(lo,c.Profile,AiProfile.Initial)),PlayableAiCanonical.Encode(rp.TryPlan(ro,c.Profile,AiProfile.Initial)));
                        Assert.AreEqual(PlayableAiCanonical.Encode(lp.Capture()),PlayableAiCanonical.Encode(rp.Capture()));
                        Assert.AreEqual(PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(lo,c.Profile)),PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(ro,c.Profile)));
                    }
                }
                var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
            }
        }
        [Test] public void HiddenOccupancyDoesNotChangePublicGeometryObservationOrPlan()
        {
            // Real home prerequisites must be READY before waiting for an income expansion (Q7).
            var c=PostOpeningConfig();var a=new PlayableAuthorityTick(c,71);var b=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                // Same genesis and actual human capture prefix: own IDs, inputs, memory,
                // native profile and RNG are byte-identical before the hidden mutation.
                int enemy=View(a,c).Entities.FirstOrDefault(e=>e.Owner==PlayableOwner.Enemy)?.Id??((PlayableSnapshot)Call(Get(a,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed)).Entities.Single(e=>e.Owner==PlayableOwner.Enemy).Id;
                foreach(var cycle in new[]{a,b})Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(new PlayableCommand(71,1,"east-owner",PlayableCommandKind.Move,new[]{enemy},new NavPoint(20,-20))).Status);
                for(int i=0;i<1800;i++)
                {
                    Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                    var truth=(PlayableSnapshot)Call(Get(a,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);
                    if(truth.Sites.Single(x=>x.Site.Id==4).Progress>=1&&truth.Buildings.Any(x=>x.SiteId==3&&x.Owner==PlayableOwner.Player))break;
                }
                var prepared=(PlayableSnapshot)Call(Get(b,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);
                var captured=prepared.Sites.Single(x=>x.Site.Id==4);
                Assert.AreEqual(PlayableOwner.Enemy,captured.Claimant,"hidden-build fixture must actually capture its remote site");
                Assert.GreaterOrEqual(captured.Progress,1);Assert.Zero(captured.CenterId);
                Assert.AreEqual(PlayableCommandStatus.Applied,b.Apply(new PlayableCommand(71,2,"east-owner",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:4,buildingKind:PlayableBuildingKind.Outpost)).Status);
                for(int i=0;i<80;i++)
                {
                    var ao=RoutedObservation(a,c);var bo=RoutedObservation(b,c);
                    Assert.AreEqual(PlayableAiCanonical.Encode(ao),PlayableAiCanonical.Encode(bo),"every detached field, including route proofs, remains identical");
                    Assert.AreEqual(PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(ao,c.Profile)),PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(bo,c.Profile)));
                    Assert.AreEqual(PlayableAiCanonical.Encode(Planner(a).Capture()),PlayableAiCanonical.Encode(Planner(b).Capture()));
                    Assert.AreEqual(PlayableAiCanonical.Encode(Checkpoint(a)),PlayableAiCanonical.Encode(Checkpoint(b)),"actual plans, receipts, state, opening RNG");
                    Step(a,host);Step(b,host);
                }
                var hidden=(PlayableSnapshot)Call(Get(b,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);
                Assert.True(hidden.Buildings.Any(x=>x.SiteId==4&&x.Owner==PlayableOwner.Enemy));
                Assert.True(View(b,c).PublicScoutObjectives.Any(g=>g.SiteId==4));Assert.False(View(b,c).Sites.Any(s=>s.Site.Id==4));Assert.False(View(b,c).Buildings.Any(x=>x.SiteId==4));
            }
        }
        [TestCase(false)][TestCase(true)] public void ChangedKnownThreatReleasesPendingClaimAndAllowsReplanOrEmergency(bool emergency)
        {
            var c=Config();
            // This existing injected-danger ownership diagnostic is separate from A3's
            // unassisted native defense acceptance. Scouts never substitute for a major army.
            if(emergency)c=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12,2)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,2)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-8,2))});
            c=NativeAiPostOpeningFixtures.WithReadyHome(c,emergency?"changed-threat-emergency":"changed-threat-replan");
            var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                int actor=Planner(a).Capture().ActorId;Assert.AreEqual(3,Planner(a).Capture().SiteId);
                Assert.Greater((long)Get(Planner(a),"pendingId"),0,"actual pending command before terminal cancellation");
                Call(Get(a,"domain"),"SpawnProduced",new NavPoint(-12,8),null,PlayableOwner.Enemy,PlayableEntityKind.Tank);
                int threat=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                Step(a,host);Assert.False(((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().SiteId);Assert.Zero((long)Get(Planner(a),"pendingId"));Assert.False(Planner(a).ClaimsActor(actor));Assert.False(Planner(a).ClaimsSite(3));
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="expansion"&&r.Status==PlayableAiDeliveryStatus.Cancelled));
                if(!emergency)
                {
                    Call(Get(a,"domain"),"Damage",threat,10000);
                    for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                    Assert.AreEqual(3,Planner(a).Capture().SiteId,"changed allowed danger input reopens the site");Assert.AreEqual(actor,Planner(a).Capture().ActorId);
                }
                else
                {
                    for(int i=0;i<100&&!Checkpoint(a).Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);i++)Step(a,host);
                    Assert.True(Checkpoint(a).Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(Planner(a).ClaimsActor(actor));
                    var unpaid=((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Unpaid;
                    Assert.LessOrEqual(unpaid.Count(e=>e.Claims.Contains("recipient:"+actor)),1,"one tactical recipient through C3 arbitration");
                }
            }
        }
        [Test] public void NoRouteProofCannotBecomeStraightDistanceCapture()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var o=PlayableAiObservation.From(View(a,c));
            Assert.True(o.Sites.Any(s=>s.Site.Id==3));Assert.True(o.PublicScoutObjectives.Any(g=>g.SiteId==3));Assert.IsEmpty(o.RouteProofs);
            var planner=new AiExpansionPlanner(o.OwnerId);Assert.Null(planner.TryPlan(o,c.Profile,AiProfile.Initial));Assert.Zero(planner.Capture().SiteId);
        }
        [Test] public void LostHomeWithLowerIdMineStillUsesAppropriateOutpost()
        {
            var baseConfig=Config(PlayableBuildingKind.Outpost);var p=baseConfig.Profile;
            var sites=baseConfig.Sites.Where(s=>s.Id<=2).Concat(new[]{new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(20,-20),p),new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(-11,0),p)}).ToArray();
            var buildings=new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Mine,3,0,sites.Single(s=>s.Id==3).Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,4,0,sites.Single(s=>s.Id==4).Position)};
            var c=new OfflineMatchConfiguration(p,baseConfig.SourceIdentity,baseConfig.MapIdentity,baseConfig.RouteProvenance,baseConfig.Seed,baseConfig.Roster.ToArray(),baseConfig.Starts.ToArray(),sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenarioBuildings:buildings);
            var a=new PlayableAuthorityTick(c,71);int home=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Player).Id;
            Call(Get(a,"domain"),"Damage",home,10000);
            using(var host=new UnityHostRouteService())
            {
                long deadline=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30)+30;
                for(long i=0;i<deadline&&!View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==4);i++)Step(a,host);
                var factory=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==4);
                Assert.AreEqual(View(a,c).Sites.Single(x=>x.Site.Id==4).CenterId,factory.ParentId);Assert.Greater(factory.SlotId,0);
                Assert.True(View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Mine&&b.SiteId==3));
                Assert.False(new AiEconomyPlanner().Plan(PlayableAiObservation.From(View(a,c)),p).Candidates.Any(x=>x.Policy=="economy"&&(x.Action.SiteId==1||x.Action.SiteId==3)));
            }
        }
        [Test] public void LostHomeUsesLiveOutpostSlotsAndOrdinaryFactoryConstruction()
        {
            // This is post-opening home-loss recovery, not a second opening from a zero-slot home.
            var source=PostOpeningConfig(PlayableBuildingKind.Outpost);
            // The recovery bound below assumes a sole surviving outpost, not a competing new expansion.
            var costs=new double[,]{{0,100},{100,0}};
            var c=new OfflineMatchConfiguration(source.Profile,source.SourceIdentity+"-sole-outpost-recovery",source.MapIdentity+"-sole-outpost-recovery",
                source.RouteProvenance,source.Seed,source.Roster.ToArray(),source.Starts.ToArray(),source.Sites.Where(s=>s.Id!=4).ToArray(),source.Obstacles.ToArray(),costs,
                source.Spectators.ToArray(),source.ScenarioUnits.ToArray(),scenarioBuildings:source.ScenarioBuildings.ToArray());
            var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1800&&!View(a,c).Sites.Single(s=>s.Site.Id==3).Ready;i++)Step(a,host);
                var outpost=View(a,c).Sites.Single(s=>s.Site.Id==3);Assert.True(outpost.Ready);
                int home=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Player).Id;
                Call(Get(a,"domain"),"Damage",home,10000);
                // With original prices and only the surviving outpost, wait for the real
                // affordable threshold, then enforce the profile admission deadline.
                var budget=(AiBudgetLedger)Get(Owner(a),"budget");
                long decisionTicks=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30);
                // Conservative bound pays both ordinary live-center investments from the
                // slowest guaranteed outpost rate, including the refinery build delay.
                double needed=Math.Max(0,c.Profile.FactoryCreditCost+c.Profile.RefineryCreditCost-View(a,c).ExactCredits);
                long recoveryTicks=(long)Math.Ceiling(needed/(c.Profile.OutpostIncomePerPeriod/c.Profile.IncomePeriodSeconds)+c.Profile.RefineryBuildSeconds)*30+2*decisionTicks+30;
                long affordable=-1;bool refineryPaid=false,refineryReady=false;double previousBank=View(a,c).ExactCredits;long previousPaid=budget.PaidTotal;
                for(long i=0;i<recoveryTicks&&!View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);i++)
                {
                    Step(a,host);var v=View(a,c);var r=v.Buildings.FirstOrDefault(b=>b.Kind==PlayableBuildingKind.Refinery&&b.SiteId==3);
                    refineryPaid|=r!=null;refineryReady|=r?.Phase==ConstructionPhase.Ready;
                    if(budget.PaidTotal!=previousPaid||v.ExactCredits-previousBank>0)
                    {TestContext.WriteLine("E2-08 tick="+a.Tick+" bank="+v.ExactCredits+" settled="+v.SettledIncome+" income="+v.IncomePerSecond+" available="+budget.Available+" paid="+budget.PaidTotal+" refinery="+r?.Phase);previousPaid=budget.PaidTotal;}
                    previousBank=v.ExactCredits;
                    if(affordable<0&&budget.Available>=c.Profile.FactoryCreditCost&&new AiEconomyPlanner().Plan(PlayableAiObservation.From(v),c.Profile).Candidates.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.BuildAt&&x.Action.BuildingKind==PlayableBuildingKind.Factory))affordable=a.Tick;
                    if(affordable>=0)Assert.LessOrEqual(a.Tick-affordable,decisionTicks+30,"legal affordable factory cannot starve past actual C3/C4 admission window");
                }
                TestContext.WriteLine("sole-outpost recovery final="+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,World=View(a,c),Budget=((AiBudgetLedger)Get(Owner(a),"budget")).Capture(),Checkpoint=Checkpoint(a)}));
                Assert.True(refineryPaid);Assert.True(refineryReady);Assert.GreaterOrEqual(affordable,0);
                var factory=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);
                Assert.AreEqual(outpost.CenterId,factory.ParentId);Assert.Greater(factory.SlotId,0);Assert.AreEqual(PlayableMatchOutcome.Playing,View(a,c).Outcome);
                Assert.False(new AiEconomyPlanner().Plan(PlayableAiObservation.From(View(a,c)),c.Profile).Candidates.Any(x=>x.Action.SiteId==1));
            }
        }
    }
}
