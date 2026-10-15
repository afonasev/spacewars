using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiOwnerLoopTests
    {
        private static PlayableAiOwnerCheckpoint Enemy(PlayableRuntime runtime)
        {
            var legacy=(PlayableAiOwnerCheckpoint)typeof(PlayableRuntime).GetProperty("EnemyAiCheckpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            if(legacy!=null)return legacy;
            // Configured runtimes expose trusted immutable diagnostics through their
            // actual authority, rather than the absent legacy single-opponent shortcut.
            var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            return authority.CaptureDiagnosticCheckpoints().SingleOrDefault(o=>o.OwnerId=="enemy-1");
        }

        [Test]
        public void OrdinaryRuntimeAppliesIndependentEnemyOwnerReceiptsWithoutPublishingPrivateState()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,707,19092026))
            {
                Assert.True(Until(runtime,()=>Enemy(runtime).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                var enemy=Enemy(runtime);var player=runtime.AiCheckpoint;
                var applied=enemy.Records.First(r=>r.Status==PlayableAiDeliveryStatus.Applied);
                Assert.AreEqual("enemy-1",enemy.OwnerId);
                Assert.AreEqual("enemy-1",enemy.Opening.OwnerId);
                Assert.AreEqual(19092026,enemy.Seed);
                Assert.AreEqual(player.SourceIdentity,enemy.SourceIdentity);
                Assert.AreEqual(player.ProfileId,enemy.ProfileId);
                Assert.AreEqual(player.ProfileRevision,enemy.ProfileRevision);
                Assert.AreEqual(707,enemy.Generation);
                Assert.GreaterOrEqual(enemy.DecisionTick,45);
                var identityParts=applied.ObservationIdentity.Split(':');
                Assert.AreEqual(30,applied.DueTick-long.Parse(identityParts[identityParts.Length-2]));
                Assert.AreEqual(applied.DueTick,applied.ApplicationTick);
                Assert.GreaterOrEqual(applied.CommandSequence,1L<<61);
                Assert.True(applied.ObservationIdentity.Contains(":enemy-1:707:"));
                Assert.True(enemy.Records.All(r=>r.OwnerId=="enemy-1"));
                Assert.True(player.Records.All(r=>r.OwnerId=="player-1"));
                Assert.IsTrue(enemy.LastActionKind.HasValue);
                Assert.False(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.PrivateState!=null));
                Assert.IsEmpty(runtime.DrainReceipts());
            }
        }

        [Test]
        public void EnemyPendingIsCancelledOnPauseAndStoppedOnShutdownThenRestartedFresh()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,708,19092026);
            try
            {
                Assert.True(Until(runtime,()=>Enemy(runtime).PendingActionId!=0));
                var action=Enemy(runtime).PendingActionId;
                runtime.RequestPause(true);
                Assert.True(Until(runtime,()=>Enemy(runtime).Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                runtime.RequestPause(false);
                Assert.True(Until(runtime,()=>Enemy(runtime).PendingActionId!=0));
                action=Enemy(runtime).PendingActionId;
                runtime.RequestStop();Assert.True(Until(runtime,()=>runtime.IsStopped));
                Assert.True(Enemy(runtime).Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Stopped));
                using(var next=new PlayableRuntime(PlayableProfile.Default,709,19092026))
                {
                    Assert.AreEqual(709,Enemy(next).Generation);
                    Assert.Zero(Enemy(next).LastCommandSequence);
                    Assert.IsEmpty(Enemy(next).Records);
                    Assert.True(Until(next,()=>Enemy(next).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                    Assert.True(Enemy(next).Records.All(r=>r.ObservationIdentity.Contains(":709:")));
                }
            }
            finally{runtime.Dispose();}
        }

        [Test]
        public void ScriptedPatrolExistsOnlyWithoutEnemyOwnerLoop()
        {
            var field=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true)
                .GetField("scriptedEnemyPatrol",BindingFlags.Instance|BindingFlags.NonPublic);
            var domain=typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic);
            using(var ordinary=new PlayableRuntime(PlayableProfile.Default,710,19092026))
            using(var diagnostic=new PlayableRuntime(PlayableProfile.Default,711,19092026,false))
            {
                Assert.False((bool)field.GetValue(domain.GetValue(ordinary)));
                Assert.True((bool)field.GetValue(domain.GetValue(diagnostic)));
                Assert.IsNull(Enemy(diagnostic));
            }
        }

        private static OfflineMatchConfiguration ScoutDeliveryConfiguration()
        {
            var p=PlayableProfile.Default;
            var sites=TerritoryRules.Sites(p).Where(x=>x.Kind==PlayableBuildingKind.Headquarters).ToArray();
            var starts=new[]{new OfflineStart("west",1,sites.Single(x=>x.Id==1).Position,new NavPoint(p.PlayerHeadquartersX+p.DefenderOffsetX,p.HeadquartersZ-p.DefenderOffsetZ),pin:1),
                new OfflineStart("east",2,sites.Single(x=>x.Id==2).Position,new NavPoint(p.EnemyHeadquartersX-p.DefenderOffsetX,p.HeadquartersZ),pin:2)};
            return new OfflineMatchConfiguration(p,PlayableAiOpeningComposition.SourceIdentity,"scout-delivery-no-neutral-sites-v1","UnityHostRouteService",19092026,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)},starts,sites,PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}});
        }
        [Test]
        public void EnemyScoutDeliveryDoesNotBlockSubsequentPolicyActions()
        {
            // Isolate the scout callback contract with genuine authored geography that
            // has no safe neutral capture objective, without disabling expansion policy.
            using(var runtime=new PlayableRuntime(ScoutDeliveryConfiguration(),712))
            {
                Assert.True(runtime.ParticipantView("enemy-1").PublicScoutObjectives.All(g=>g.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart));
                Assert.True(Until(runtime,()=>Enemy(runtime).Records.Count(r=>r.Status==PlayableAiDeliveryStatus.Applied)>=3&&
                    Enemy(runtime).Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied)&&
                    Enemy(runtime).Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied)||
                    Enemy(runtime).Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Rejected),10));
                foreach(var row in Enemy(runtime).Records)TestContext.WriteLine("enemy receipt policy="+row.Policy+" kind="+row.Kind+" status="+row.Status+" due="+row.DueTick+" applied="+row.ApplicationTick+" sequence="+row.CommandSequence);
                TestContext.WriteLine("enemy tick="+Enemy(runtime).DecisionTick+" expansion="+PlayableAiCanonical.Encode(Enemy(runtime).Expansion));
                var rejected=Enemy(runtime).Records.FirstOrDefault(r=>r.Status==PlayableAiDeliveryStatus.Rejected);
                Assert.IsNull(rejected,rejected==null?null:rejected.RuntimeStatus+": "+rejected.Message);
                Assert.True(Enemy(runtime).Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(Enemy(runtime).Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied));
            }
        }

        [Test]
        public void HumanPlayerCommandCancelsOnlyThePlayerOwnerPendingAction()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,713,19092026))
            {
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.PendingActionId!=0&&Enemy(runtime).PendingActionId!=0));
                var playerAction=runtime.AiCheckpoint.PendingActionId;
                var enemyAction=Enemy(runtime).PendingActionId;
                Assert.True(runtime.TrySubmit(new PlayableCommand(713,1,"player-1",PlayableCommandKind.Stop,new[]{3})).Accepted);
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==playerAction&&r.Status==PlayableAiDeliveryStatus.Cancelled)&&
                    Enemy(runtime).Records.Any(r=>r.ActionId==enemyAction&&r.Status==PlayableAiDeliveryStatus.Applied)));
                Assert.False(Enemy(runtime).Records.Any(r=>r.ActionId==enemyAction&&r.Status==PlayableAiDeliveryStatus.Cancelled));
            }
        }
        [Test]
        public void ExternalHumanAdmissionStillRejectsEnemyOwner()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,706,19092026,false))
            {
                var result=runtime.TrySubmit(new PlayableCommand(706,1,"enemy-1",PlayableCommandKind.Stop,new[]{3}));
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,result.Status);
                Assert.IsEmpty(runtime.DrainReceipts());
            }
        }
        private static bool Until(PlayableRuntime runtime,Func<bool> condition,int seconds=7)
        {
            // A runtime fixture is a Unity host too: resolve routes with the same
            // native service, never a pure solver substituted for tank NavMesh.
            using(var routes=new UnityHostRouteService())
            {
                var end=DateTime.UtcNow.AddSeconds(seconds);
                while(DateTime.UtcNow<end){routes.Service(runtime,PlayableRuntime.MaxOutstandingCommands);if(condition())return true;Thread.Sleep(20);}
                return condition();
            }
        }

        private static CertifiedRouteWorker RouteWorker(UnityHostRouteService routes)
            =>typeof(UnityHostRouteService).GetField("worker",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(routes) as CertifiedRouteWorker;

        private static object RouteHostReadback(string stage,PlayableRuntime runtime,UnityHostRouteService routes,
            CertifiedRouteWorker worker,int barrierPolls)
        {
            var snapshot=runtime.Latest;var binding=runtime.NavigationBinding;var admission=binding?.Admission;
            var publication=worker?.Publication??routes?.CertifiedPublication;
            var counters=publication?.Counters??routes?.CertifiedCounters;
            return new
            {
                Stage=stage,BarrierPolls=barrierPolls,
                Runtime=new{StopRequested=runtime.IsStopRequested,Stopped=runtime.IsStopped,Status=snapshot?.Status,Failure=snapshot?.Failure,
                    NavigationPending=snapshot?.Metrics?.NavigationPending,Errors=snapshot?.Metrics?.Errors,
                    RequestMailboxCount=runtime.Requests.Count,AnswerMailboxCount=runtime.Answers.Count},
                RouteBinding=binding==null?null:(object)new{Generation=admission?.Generation,PublishedThroughRequestSequence=admission?.PublishedThroughRequestSequence,
                    SurfaceProviderId=admission?.SurfaceProviderId,SurfaceSemanticsVersion=admission?.SurfaceSemanticsVersion,
                    GeometryRevision=binding.Geometry?.Revision,ProfileId=binding.Profile?.Navigation.ProfileId,ProfileRevision=binding.Profile?.Navigation.Revision,
                    OutstandingRequests=admission?.Requests.Select(r=>new{r.Session,r.Request,r.Entity,r.Order,r.ProfileId,r.ProfileRevision,
                        GeometryRevision=r.Geometry.Revision,BaseGeometryRevision=r.BaseGeometry.Revision,
                        Start=new{x=r.Start.X,z=r.Start.Z},Goal=new{x=r.Goal.X,z=r.Goal.Z},r.HeldIdentity}).ToArray()},
                Worker=worker==null?null:(object)new{worker.IsWaiting,worker.IsStopped,Publication=publication==null?null:new{publication.Epoch,publication.Pumps,
                    publication.Failure,publication.ProjectionFailure,publication.RetainedBytes,publication.PeakRetainedBytes,
                    PendingSubscriptions=publication.Counters.PendingSubscriptions,ReadyResults=publication.Counters.ReadyResults}},
                RouteService=routes==null?null:(object)new{routes.NavMeshRequests,routes.FlowRequests,routes.NavMeshBuilds,routes.FlowRebuilds,
                    routes.FlowCacheHits,routes.FlowExpandedCells,routes.LastProjectionFailure,
                    Counters=counters==null?null:new{counters.PendingSubscriptions,counters.ReadyResults,counters.LogicalGraphWork,counters.PhysicalGraphWork,
                        counters.LogicalFieldWork,counters.PhysicalFieldWork,counters.ConnectorWork,counters.GraphBuilds,counters.FieldBuilds,
                        counters.Cancelled,counters.Evictions,counters.MaintenanceWork,counters.PeakQueue,counters.PeakQueueAge}}
            };
        }

        private static bool UntilWithRouteHostLifecycle(PlayableRuntime runtime,Func<bool> condition,
            UnityHostRouteService routes,System.Collections.Generic.List<object> lifecycle,int seconds=7)
        {
            CertifiedRouteWorker worker=null;int barrierPolls=0;
            var end=DateTime.UtcNow.AddSeconds(seconds);
            while(DateTime.UtcNow<end)
            {
                routes.Service(runtime,PlayableRuntime.MaxOutstandingCommands);barrierPolls++;worker=RouteWorker(routes)??worker;
                if(barrierPolls==1)lifecycle.Add(RouteHostReadback("shared-host-after-first-service",runtime,routes,worker,barrierPolls));
                if(condition())return true;Thread.Sleep(20);
            }
            return condition();
        }

        // Approved Q5 lifecycle: first Applied retains its original 7s bound.
        private static bool UntilFactoryAtOpeningDeadline(PlayableRuntime runtime,
            System.Diagnostics.Stopwatch hostClock,System.Collections.Generic.List<object> routeHostLifecycle,
            UnityHostRouteService routes,double firstAppliedWallSeconds,
            out PlayableAiTraceRecord factoryApplied,out string failureRecord)
        {
            factoryApplied=null;failureRecord=null;
            var builds=new System.Collections.Generic.Dictionary<string,(long ActionId,int Site,int Slot,string CandidateId,long DecisionOrdinal)>();
            var previousBuildings=new System.Collections.Generic.Dictionary<int,PlayableBuildingSnapshot>();
            var buildingTransitions=new System.Collections.Generic.List<object>();
            long lastTick=runtime.Latest.Tick;
            var lastAdvance=hostClock.Elapsed;
            string Failure(string branch,long? deadline,AiOpeningExecutionState execution,PlayableSnapshot snapshot,
                PlayableAiOwnerCheckpoint checkpoint,UnityHostRouteService hostRoutes,int barrierPolls)
            {
                var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
                var ownerAi=typeof(PlayableRuntime).GetField("ownerAi",flags).GetValue(runtime);
                var budget=(AiBudgetLedger)ownerAi.GetType().GetProperty("Budget",flags).GetValue(ownerAi);
                var ledger=budget.Capture();
                var finance=ownerAi.GetType().GetField("expansionFinancial",flags).GetValue(ownerAi);
                var arbiter=ownerAi.GetType().GetField("arbiter",flags).GetValue(ownerAi);
                int financeSite=(int)finance.GetType().GetMethod("ActiveSite",flags).Invoke(finance,new[]{arbiter});
                var decision=checkpoint.EconomyDecision;
                // Snapshot only: never dequeue mailbox items or inspect the mutable route solver.
                // The tick-stall watchdog may fire while a route publication is still in flight.
                var binding=runtime.NavigationBinding;var admission=binding?.Admission;var publication=hostRoutes?.CertifiedPublication;
                var counters=publication?.Counters??hostRoutes?.CertifiedCounters;
                return Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    ExitBranch=branch,Tick=snapshot.Tick,Deadline=deadline,Opening=execution==null?null:new{execution.Template,execution.Phase,execution.StartedTick,execution.ProgressTick,execution.Replans,execution.Reason,execution.Completed},
                    BarrierPolls=barrierPolls,PriorRouteHostLifecycle=routeHostLifecycle.ToArray(),
                    Runtime=new{StopRequested=runtime.IsStopRequested,Stopped=runtime.IsStopped,Status=snapshot.Status,Failure=snapshot.Failure,Errors=snapshot.Metrics?.Errors,
                        NavigationPending=snapshot.Metrics?.NavigationPending,RequestMailboxCount=runtime.Requests.Count,AnswerMailboxCount=runtime.Answers.Count},
                    RouteBinding=binding==null?null:(object)new{Generation=admission?.Generation,PublishedThroughRequestSequence=admission?.PublishedThroughRequestSequence,
                        SurfaceProviderId=admission?.SurfaceProviderId,SurfaceSemanticsVersion=admission?.SurfaceSemanticsVersion,
                        GeometryRevision=binding.Geometry?.Revision,ProfileId=binding.Profile?.Navigation.ProfileId,ProfileRevision=binding.Profile?.Navigation.Revision,
                        OutstandingRequests=admission?.Requests.Select(r=>new{r.Session,r.Request,r.Entity,r.Order,r.ProfileId,r.ProfileRevision,
                            GeometryRevision=r.Geometry.Revision,BaseGeometryRevision=r.BaseGeometry.Revision,
                            Start=new{x=r.Start.X,z=r.Start.Z},Goal=new{x=r.Goal.X,z=r.Goal.Z},r.HeldIdentity}).ToArray()},
                    RouteService=hostRoutes==null?null:(object)new{hostRoutes.NavMeshRequests,hostRoutes.FlowRequests,hostRoutes.NavMeshBuilds,hostRoutes.FlowRebuilds,
                        hostRoutes.FlowCacheHits,hostRoutes.FlowExpandedCells,hostRoutes.LastProjectionFailure,
                        Publication=publication==null?null:new{publication.Epoch,publication.Pumps,publication.Failure,publication.ProjectionFailure,
                            publication.RetainedBytes,publication.PeakRetainedBytes,publication.PumpElapsedMilliseconds,
                            publication.PreparationElapsedMilliseconds,publication.ProviderElapsedMilliseconds,publication.SolveElapsedMilliseconds,
                            publication.ProjectionElapsedMilliseconds,publication.TransportWaitMilliseconds,publication.TransportHoldMilliseconds},
                        Counters=counters==null?null:new{counters.PendingSubscriptions,counters.ReadyResults,counters.LogicalGraphWork,counters.PhysicalGraphWork,
                            counters.LogicalFieldWork,counters.PhysicalFieldWork,counters.ConnectorWork,counters.GraphBuilds,counters.FieldBuilds,
                            counters.Cancelled,counters.Evictions,counters.MaintenanceWork,counters.PeakQueue,counters.PeakQueueAge}},
                    ActorOrders=snapshot.Entities.Select(u=>{var order=u.CurrentOrder;var stamp=u.OrderStamp;return new{u.Id,u.Owner,u.Kind,u.Moving,u.Held,u.NavigationOutcome,
                        Position=new{x=u.Position.X,z=u.Position.Z},CurrentOrder=order==null?null:(object)new{order.Kind,order.CommandSequence,order.IssuedTick,
                            Destination=new{x=order.Destination.X,z=order.Destination.Z},order.TargetId,order.DestinationLocation},
                        OrderStamp=stamp==null?null:(object)new{stamp.UnitId,stamp.Owner,stamp.Generation,stamp.Revision,stamp.Sequence,stamp.Tick,stamp.Origin,stamp.Kind,stamp.Source,stamp.JobId,stamp.ActionId}};}).ToArray(),
                    EconomyDecision=decision==null?null:new{decision.Identity,decision.Tick,decision.ObservationIdentity,decision.Reason,decision.Blockers,decision.Liquid,decision.Reserved,decision.Unpaid,decision.SafetyReserve,decision.Available,
                        FactoryCandidates=decision.Candidates.Where(c=>c.Policy=="economy"&&c.Kind==PlayableCommandKind.BuildAt&&c.BuildingKind==PlayableBuildingKind.Factory).Select(c=>new{c.Id,c.Selected,c.SiteId,c.SlotId,FactoryQuoteCost=c.Credits,c.Reason}).ToArray()},
                    FactoryReceipts=builds.Select(x=>new{ReceiptId=x.Key,x.Value.ActionId,x.Value.Site,x.Value.Slot,Receipts=checkpoint.Records.Where(r=>r.ReceiptIdentity?.Id==x.Key).Select(r=>new{r.Status,r.RuntimeStatus,r.ActionId,r.CommandSequence,r.ApplicationTick,r.Message}).ToArray()}).ToArray(),
                    EconomyBuildAtReceipts=checkpoint.Records.Where(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt).Select(r=>new{r.Status,r.RuntimeStatus,r.ActionId,ReceiptId=r.ReceiptIdentity?.Id,r.CommandSequence,r.ApplicationTick,r.Message}).ToArray(),
                    SnapshotExactCredits=snapshot.ExactCredits,
                    OwnFactories=snapshot.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).Select(b=>new{b.Id,b.SiteId,b.SlotId,b.Health,b.Phase}).ToArray(),
                    OwnBuildings=snapshot.Buildings.Where(b=>b.Owner==PlayableOwner.Player).Select(b=>new{b.Id,b.Kind,b.SiteId,b.SlotId,b.Phase,b.Health,Selling=b.PrivateState?.Lifecycle?.Selling}).ToArray(),
                    FinanceSite=financeSite,Expansion=checkpoint.Expansion,BuildingTransitions=buildingTransitions.ToArray(),
                    CompletedIncome=execution?.Completed?.Contains("income"),Replans=execution?.Replans,
                    OwnerLedger=ledger.Reserved.Select(e=>new{State="reserved",e.Purpose,e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,BuildingKind=e.Action.BuildingKind,UnitKind=e.Action.UnitKind,ReceiptId=e.Receipt?.Id})
                        .Concat(ledger.Unpaid.Select(e=>new{State="unpaid",e.Purpose,e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,BuildingKind=e.Action.BuildingKind,UnitKind=e.Action.UnitKind,ReceiptId=e.Receipt?.Id}))
                        .Concat(ledger.Paid.Select(e=>new{State="paid",e.Purpose,e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,BuildingKind=e.Action.BuildingKind,UnitKind=e.Action.UnitKind,ReceiptId=e.Receipt?.Id})).ToArray(),
                    ActorClaims=checkpoint.Armies?.Armies.Select(a=>new{a.Id,a.Role,a.TacticalOwner,a.LeaderId,a.Objective,a.Members}).ToArray()
                });
            }
            routeHostLifecycle.Add(RouteHostReadback("factory-phase-start-same-host",runtime,routes,RouteWorker(routes),0));
            int barrierPolls=0;
            while(hostClock.Elapsed.TotalSeconds<180)
            {
                routes.Service(runtime,PlayableRuntime.MaxOutstandingCommands);
                barrierPolls++;var routeWorker=RouteWorker(routes);
                if(barrierPolls==1)routeHostLifecycle.Add(RouteHostReadback("second-host-after-first-service",runtime,routes,routeWorker,barrierPolls));
                var snapshot=runtime.Latest;
                var checkpoint=runtime.AiCheckpoint;
                var currentBuildings=snapshot.Buildings.Where(b=>b.Owner==PlayableOwner.Player).ToDictionary(b=>b.Id);
                foreach(var building in snapshot.Buildings.Where(b=>b.Owner==PlayableOwner.Player).OrderBy(b=>b.Id))
                {
                    if(!previousBuildings.TryGetValue(building.Id,out var prior))
                        buildingTransitions.Add(new{Tick=snapshot.Tick,Transition="appeared",building.Id,building.Kind,building.SiteId,building.SlotId,building.Phase,building.Health,Selling=building.PrivateState?.Lifecycle?.Selling,PreviousHealth=(int?)null,
                            ActualBank=snapshot.ExactCredits,SettledIncome=snapshot.SettledIncome});
                    else if(prior.Phase!=building.Phase||prior.PrivateState?.Lifecycle?.Selling!=building.PrivateState?.Lifecycle?.Selling||building.Health<prior.Health)
                        buildingTransitions.Add(new{Tick=snapshot.Tick,Transition=prior.Phase!=building.Phase?"phase-changed":prior.PrivateState?.Lifecycle?.Selling!=building.PrivateState?.Lifecycle?.Selling?"selling-changed":"health-decreased",building.Id,building.Kind,building.SiteId,building.SlotId,building.Phase,building.Health,Selling=building.PrivateState?.Lifecycle?.Selling,PreviousHealth=(int?)prior.Health,
                            ActualBank=snapshot.ExactCredits,SettledIncome=snapshot.SettledIncome});
                }
                foreach(var lost in previousBuildings.Keys.Where(id=>!currentBuildings.ContainsKey(id)).OrderBy(id=>id).ToArray())
                {
                    var sale=checkpoint.Records.Where(r=>r.Kind==PlayableCommandKind.SellBuilding&&r.Status==PlayableAiDeliveryStatus.Applied).OrderBy(r=>r.ApplicationTick).ToArray();
                    var prior=previousBuildings[lost];
                    buildingTransitions.Add(new{Tick=snapshot.Tick,Transition="disappeared-or-ownership-changed",Id=lost,Kind=prior.Kind,SiteId=prior.SiteId,SlotId=prior.SlotId,Phase=prior.Phase,Health=prior.Health,Selling=prior.PrivateState?.Lifecycle?.Selling,
                        ActualBank=snapshot.ExactCredits,SettledIncome=snapshot.SettledIncome,SaleAuthorityEvents=sale.Select(r=>new{r.ApplicationTick,r.ActionId,ReceiptId=r.ReceiptIdentity?.Id,r.Message}).ToArray(),SaleCause="unknown-target-building-id-is-not-exposed-by-checkpoint",
                        DeathAuthorityEvent="unknown-no-building-death-event-readback-in-this-fixture"});
                }
                previousBuildings.Clear();foreach(var pair in currentBuildings)previousBuildings.Add(pair.Key,pair.Value);
                if(snapshot.Tick>lastTick){lastTick=snapshot.Tick;lastAdvance=hostClock.Elapsed;}
                if((hostClock.Elapsed-lastAdvance).TotalSeconds>=15){failureRecord=Failure("tick-stalled",null,checkpoint.OpeningExecution,snapshot,checkpoint,routes,barrierPolls);return false;}
                var execution=checkpoint.OpeningExecution;
                if(execution==null||execution.StartedTick<0){Thread.Sleep(20);continue;}
                long deadline=execution.StartedTick+Spacewars.Simulation.Ai.AiProfile.SecondsToTicks(runtime.NativeAiProfile.Value("opening.deadlineSeconds"),30);
                var decision=checkpoint.EconomyDecision;
                var candidate=decision?.Candidates.SingleOrDefault(c=>c.Selected&&c.Policy=="economy"&&c.Kind==PlayableCommandKind.BuildAt&&c.BuildingKind==PlayableBuildingKind.Factory);
                if(candidate!=null)
                {
                    Assert.Contains("income",execution.Completed.ToArray());
                    Assert.GreaterOrEqual(snapshot.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Refinery&&b.Health>0&&b.Phase==ConstructionPhase.Ready),runtime.NativeAiProfile.Value("opening.greedyRefineries"));
                    var scheduled=checkpoint.Records.SingleOrDefault(x=>x.Policy=="economy"&&x.Kind==PlayableCommandKind.BuildAt&&x.Status==PlayableAiDeliveryStatus.Scheduled&&x.ReceiptIdentity?.DecisionOrdinal==decision.Ordinal&&x.ObservationIdentity==decision.ObservationIdentity);
                    Assert.NotNull(scheduled,"selected Factory must have its own scheduled receipt");
                    builds[scheduled.ReceiptIdentity.Id]=(scheduled.ActionId,candidate.SiteId,candidate.SlotId,candidate.Id,decision.Ordinal);
                }
                foreach(var receipt in checkpoint.Records.Where(x=>x.Policy=="economy"&&x.Kind==PlayableCommandKind.BuildAt&&x.Status==PlayableAiDeliveryStatus.Applied))
                {
                    if(receipt.ReceiptIdentity==null||!builds.TryGetValue(receipt.ReceiptIdentity.Id,out var target)||receipt.ActionId!=target.ActionId)continue;
                    if(receipt.ApplicationTick>deadline||snapshot.Tick>deadline){failureRecord=Failure("factory-applied-after-opening-deadline",deadline,execution,snapshot,checkpoint,routes,barrierPolls);return false;}
                    if(snapshot.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==target.Site&&b.SlotId==target.Slot&&b.Health>0))
                    {
                        factoryApplied=receipt;
                        var ownFactory=snapshot.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==target.Site&&b.SlotId==target.Slot&&b.Health>0);
                        TestContext.WriteLine("Q5_FACTORY_PROOF typedCandidate={0}:Factory scheduledReceipt={1} appliedReceipt={2} decision={3} action={4} site={5} slot={6} ownBuilding={7} appliedTick={8} openingDeadlineTick={9} firstAppliedWallSeconds={10:F3}",
                            target.CandidateId,receipt.ReceiptIdentity.Id,receipt.ReceiptIdentity.Id,target.DecisionOrdinal,receipt.ActionId,target.Site,target.Slot,ownFactory.Id,receipt.ApplicationTick,deadline,firstAppliedWallSeconds);
                        return true;
                    }
                }
                if(snapshot.Tick>=deadline||execution.Phase==PlayableAiOpeningPhase.Aborted||snapshot.Outcome!=PlayableMatchOutcome.Playing||snapshot.Status==RuntimeStatus.Failed)
                {string branch=snapshot.Tick>=deadline?"deadline-reached":execution.Phase==PlayableAiOpeningPhase.Aborted?"opening-aborted":snapshot.Outcome!=PlayableMatchOutcome.Playing?"match-ended":"runtime-failed";failureRecord=Failure(branch,deadline,execution,snapshot,checkpoint,routes,barrierPolls);return false;}
                Thread.Sleep(20);
            }
            var finalSnapshot=runtime.Latest;var finalCheckpoint=runtime.AiCheckpoint;var finalExecution=finalCheckpoint?.OpeningExecution;
            long? finalDeadline=finalExecution==null||finalExecution.StartedTick<0?null:finalExecution.StartedTick+Spacewars.Simulation.Ai.AiProfile.SecondsToTicks(runtime.NativeAiProfile.Value("opening.deadlineSeconds"),30);
            failureRecord=Failure("host-time-cap-180-seconds",finalDeadline,finalExecution,finalSnapshot,finalCheckpoint,routes,barrierPolls);return false;
        }

        [Test]
        public void NormalRuntimeAppliesOwnerAiEconomyWithAuthorityReceipt()
        {
            var hostClock=System.Diagnostics.Stopwatch.StartNew();
            var routeHostLifecycle=new System.Collections.Generic.List<object>();
            var routes=new UnityHostRouteService();
            try
            {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,701,19092026))
            {
                Assert.True(UntilWithRouteHostLifecycle(runtime,()=>runtime.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied),routes,routeHostLifecycle));
                var firstAppliedWallSeconds=hostClock.Elapsed.TotalSeconds;
                routeHostLifecycle.Add(RouteHostReadback("first-applied-same-host",runtime,routes,RouteWorker(routes),0));
                var checkpoint=runtime.AiCheckpoint;
                var applied=checkpoint.Records.First(r=>r.Status==PlayableAiDeliveryStatus.Applied);
                Assert.AreEqual("player-1",checkpoint.OwnerId);
                Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,checkpoint.SourceIdentity);
                Assert.AreEqual(19092026,checkpoint.Seed);
                Assert.AreEqual(PlayableProfile.Default.ProfileId,checkpoint.ProfileId);
                Assert.AreEqual(PlayableProfile.Default.Revision,checkpoint.ProfileRevision);
                Assert.AreEqual(701,checkpoint.Generation);
                var decision=checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled).GroupBy(r=>r.ReceiptIdentity.DecisionOrdinal).First();
                Assert.LessOrEqual(decision.Count(),runtime.NativeAiProfile.DifficultyValue(Spacewars.Simulation.Ai.AiDifficulty.Fighter,"actionsPerDecision"));
                Assert.AreEqual("player-1",checkpoint.Opening.OwnerId);
                Assert.Greater(applied.CommandSequence,0);
                Assert.Greater(applied.ApplicationTick,0);
                bool factoryBeforeDeadline=UntilFactoryAtOpeningDeadline(runtime,hostClock,routeHostLifecycle,routes,firstAppliedWallSeconds,out var factoryApplied,out var failureRecord);
                if(!factoryBeforeDeadline)TestContext.WriteLine("Q5_FACTORY_DEADLINE_FAILURE "+failureRecord);
                routeHostLifecycle.Add(RouteHostReadback("whole-case-before-dispose",runtime,routes,RouteWorker(routes),0));
                Assert.True(factoryBeforeDeadline,"Factory Applied plus own world fact required before the existing opening deadline");
                Assert.Greater(factoryApplied.CommandSequence,0);
                Assert.Greater(factoryApplied.ApplicationTick,0);
                Assert.True(runtime.AiCheckpoint.Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory));
                Assert.False(runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&b.PrivateState!=null));
                Assert.False(runtime.DrainReceipts().Any(),"AI receipts must not corrupt external-input backlog accounting.");
            }
            }
            finally
            {
                routes.Dispose();
            }
        }

        [Test]
        public void HumanAdmissionCancelsPendingAiActionBeforeDelivery()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,702,19092026))
            {
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.PendingActionId!=0));
                var pending=runtime.AiCheckpoint.PendingActionId;
                Assert.True(runtime.TrySubmit(new PlayableCommand(702,1,"player-1",PlayableCommandKind.Stop,new[]{3})).Accepted);
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==pending&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                Assert.False(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==pending&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(Until(runtime,()=>runtime.DrainReceipts().Any(r=>r.Sequence==1)));
            }
        }

        [Test]
        public void PauseAndStopTerminatePendingWithoutApplication()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,703,19092026);
            try
            {
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.PendingActionId!=0));
                var action=runtime.AiCheckpoint.PendingActionId;
                runtime.RequestPause(true);
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Cancelled)));
                Assert.False(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Applied));
                runtime.RequestPause(false);
                Assert.True(Until(runtime,()=>runtime.AiCheckpoint.PendingActionId!=0));
                action=runtime.AiCheckpoint.PendingActionId;
                runtime.RequestStop();
                Assert.True(Until(runtime,()=>runtime.IsStopped));
                Assert.True(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Stopped));
            }
            finally{runtime.Dispose();}
        }

        [Test]
        public void NewGenerationStartsFreshOwnerCheckpoint()
        {
            var old=new PlayableRuntime(PlayableProfile.Default,704,19092026);
            try
            {
                Assert.True(Until(old,()=>old.AiCheckpoint.PendingActionId!=0));
                var oldAction=old.AiCheckpoint.PendingActionId;
                old.RequestStop();
                Assert.True(Until(old,()=>old.IsStopped));
                Assert.True(old.AiCheckpoint.Records.Any(r=>r.ActionId==oldAction&&r.Status==PlayableAiDeliveryStatus.Stopped));
                using(var next=new PlayableRuntime(PlayableProfile.Default,705,19092026))
                {
                    Assert.AreEqual(705,next.AiCheckpoint.Generation);
                    Assert.Zero(next.AiCheckpoint.LastCommandSequence);
                    Assert.Zero(next.AiCheckpoint.Records.Count);
                    Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity,next.AiCheckpoint.SourceIdentity);
                    Assert.True(Until(next,()=>next.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied)));
                    Assert.True(next.AiCheckpoint.Records.All(r=>r.ObservationIdentity.Contains(":705:")));
                }
            }
            finally{old.Dispose();}
        }
    }
}
