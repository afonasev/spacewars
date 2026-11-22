using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiSafeReinforcementsThreadedTests
    {
        private const BindingFlags RuntimeFields=BindingFlags.Instance|BindingFlags.NonPublic;

        private static void StopRuntimeAndRouteWorker(PlayableRuntime runtime,UnityHostRouteService routes)
        {
            var worker=routes==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",RuntimeFields).GetValue(routes);
            runtime?.RequestStop();
            routes?.Dispose();
            runtime?.Dispose();
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>(runtime==null||runtime.IsStopped)&&(worker==null||worker.IsStopped),5000),
                "Both the match and certified route worker must stop before releasing navigation storage.");
        }
        [UnityTest,Timeout(150000)] public IEnumerator ProducedUnitUsesAppliedFollowMovesAndActuallyJoinsBeforeCurrentRestore()
        {
            const int seed=7;const long generation=108;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery};
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).ToArray();
            // Leave room between the field core and the factory, and keep
            // remote objectives alive until the reinforcement can join.
            var units=Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,
                new NavPoint(7+(i%4)*3,-12+(i/4)*3))).Concat(Enumerable.Range(0,3).Select(i=>
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(7+i*3,-5)))).ToArray();
            var reserves=new[]{
                new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(24,-24),Array.Empty<TerritorySlot>()),
                new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(-26,24),Array.Empty<TerritorySlot>()),
                // The opposite side of the northern passage keeps a live
                // objective when the advancing core has cleared the east.
                new TerritorySite(5,PlayableBuildingKind.Outpost,new NavPoint(-7,28),Array.Empty<TerritorySlot>())
            };
            var config=new OfflineMatchConfiguration(p,"ai-safe-reinforcements-ordinary-v1","flat-public-active-v1",
                "UnityHostRouteService",seed,new[]{new OfflineParticipant("active-owner",1,1,OfflineControl.Ai),
                    new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy}.Concat(reserves).ToArray(),
                PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,
                scenarioBuildings:buildings.Concat(reserves.Select(site=>new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,
                    site.Id,0,site.Position))).ToArray(),spectators:new[]{PlayableRuntime.LocalSpectatorId});
            var source=Environment.GetEnvironmentVariable("AI_REINFORCEMENT_SOURCE_SHA");Assert.IsNotEmpty(source);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-safe-reinforcements/ordinary-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=PlayableRuntime.CreateWithOpeningForTests(config,generation,AiDifficulty.Veteran,ai,PlayableAiOpening.Safe);
            var routes=new UnityHostRouteService();
            PlayableRuntime restored=null;PlayableAuthorityTick restoredAuthority=null;UnityHostRouteService restoreRoutes=null;bool originalStopped=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void StopOriginal()
            {
                if(originalStopped)return;
                StopRuntimeAndRouteWorker(runtime,routes);
                var a=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                a.Domain.Navigation.ReleaseCommittedHostRoot();a.Domain.DestroyOwnedNavigation();originalStopped=true;
            }
            var observed=new System.Collections.Concurrent.ConcurrentQueue<(PlayableAiObservation observation,AiArmyRegistryState armies)>();
            PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="active-owner";
            PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(owner,tick,o,armies,actions)=>observed.Enqueue((o,armies));
            PlayableAiObservation lastObservation=null;AiArmyRegistryState lastArmies=null;
            var initial=new HashSet<int>();var assigned=new HashSet<int>();var joined=new HashSet<int>();var followMotion=new Dictionary<int,(NavPoint point,long tick,long action)>();var physicallyMoved=new HashSet<int>();
            var seenFollowReceipts=new HashSet<(int unit,long action)>();
            var motion=new Dictionary<int,(NavPoint point,long tick,long action)>();var moved=new HashSet<int>();bool damage=false;var hostileHealth=new Dictionary<int,int>();
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                float deadline=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&
                    Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=f.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    while(observed.TryDequeue(out var observation))
                    {
                        var o=observation.observation;lastObservation=o;lastArmies=observation.armies;
                        if(initial.Count==0)foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner))initial.Add(u.Id);
                        foreach(var a in observation.armies.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)))
                        foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&!initial.Contains(u.Id)&&a.Members.Contains(u.Id)))
                        {
                            if(a.Reinforcements.Contains(u.Id))assigned.Add(u.Id);
                            if(assigned.Contains(u.Id)&&!a.Reinforcements.Contains(u.Id))joined.Add(u.Id);
                            if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Kind==PlayableCommandKind.Follow)
                            {
                                var action=u.OrderStamp.ActionId;
                                bool liveCore=a.Members.Contains(u.CurrentOrder.TargetId)&&!a.Reinforcements.Contains(u.CurrentOrder.TargetId);
                                if(seenFollowReceipts.Add((u.Id,action)))
                                    Assert.True(liveCore,"A newly applied Follow targets its live core, never an enemy objective.");
                                if(!liveCore)
                                {
                                    followMotion.Remove(u.Id);
                                    physicallyMoved.Remove(u.Id);
                                    continue;
                                }
                                if(!followMotion.TryGetValue(u.Id,out var before)||before.action!=action)
                                {
                                    followMotion[u.Id]=(u.Position,o.Tick,action);
                                    physicallyMoved.Remove(u.Id);
                                }
                                else if(o.Tick>before.tick&&AiOpponentSelector.Distance(before.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))
                                    physicallyMoved.Add(u.Id);
                            }
                        }
                        // Capture the first proved join before a later army reshuffle.
                        if(joined.Any(physicallyMoved.Contains))break;
                    }
                    foreach(var b in f.Overview.Buildings.Where(b=>b.Owner==PlayableOwner.Enemy&&b.Health>0))
                    {if(hostileHealth.TryGetValue(b.Id,out var previous)&&b.Health<previous&&moved.Count>0)damage=true;hostileHealth[b.Id]=b.Health;}
                    foreach(var u in own.Entities.Where(u=>u.Owner==own.Owner&&u.Health>0&&u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)))
                    {
                        if(!motion.TryGetValue(u.Id,out var start)||start.action!=u.OrderStamp.ActionId)motion[u.Id]=(u.Position,own.Tick,u.OrderStamp.ActionId);
                        else if(own.Tick>start.tick&&AiOpponentSelector.Distance(start.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))moved.Add(u.Id);
                    }
                    if(joined.Any(physicallyMoved.Contains))break;yield return null;
                }
                TestContext.WriteLine("POSITIVE_CAPTURE_PREMISE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{runtime.Latest.Tick,runtime.IsStopped,runtime.Latest.Outcome,assigned=assigned.ToArray(),joined=joined.ToArray(),physicallyMoved=physicallyMoved.ToArray()}));
                Assert.False(runtime.IsStopped,"The positive scenario must keep its ordinary match running through actual physical joining and capture");
                Assert.True(joined.Any(physicallyMoved.Contains),
                    "A newly produced follower must physically move and join its army before the match ends or the fixture budget expires.");
                var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted,"Fixed wall deadline includes capture");Assert.False(capture.IsFaulted,capture.Exception?.ToString());File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                long tick;using(var envelope=PlayableWorldState.Decode(capture.Result)){tick=envelope.Tick;Assert.Greater(envelope.RuntimeTransport.Length,0);}
                StopOriginal();byte[] roundtrip=null;PlayableAiOwnerCheckpoint checkpoint=null;Exception restoreError=null;
                restoreRoutes=new UnityHostRouteService();restored=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>
                {
                    try{roundtrip=r.CaptureBeforeStartForTests();restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);checkpoint=restoredAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="active-owner");}
                    catch(Exception e){restoreError=e;r.RequestStop();}
                });
                Assert.IsNull(restoreError);bool exact=capture.Result.SequenceEqual(roundtrip);
                var applied=checkpoint.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&(r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                var followed=checkpoint.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                bool realJoin=joined.Any(id=>physicallyMoved.Contains(id)&&followed.Any(r=>followMotion.TryGetValue(id,out var motionBefore)&&motionBefore.action==r.ActionId));
                bool receiptMotion=applied.Any(r=>motion.Any(m=>m.Value.action==r.ActionId&&moved.Contains(m.Key)));
                float restoreDeadline=Time.realtimeSinceStartup+10;
                while(restored.Latest.Tick<tick+30&&!restored.IsStopped&&Time.realtimeSinceStartup<restoreDeadline){restoreRoutes.Service(restored,p.NavigationRequestsPerFrame);yield return null;}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,tick,p.ProfileId,p.Revision,ai.Id,ai.Hash,assigned=assigned.ToArray(),joined=joined.ToArray(),physicallyMoved=physicallyMoved.ToArray(),followMotion,realJoin,moved=moved.ToArray(),damage,receiptMotion,exact,continued=restored.Latest.Tick>=tick+30,checkpoint.ArmyMission,records=checkpoint.Records},Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("SAFE_REINFORCEMENT_RESULT "+output);
                Assert.Greater(assigned.Count,0,"Real factory output assigned without waiting for a wave");Assert.True(realJoin,"Same ordinary Applied Follow produced physical movement and actual joined registry membership");Assert.True(exact,"Exact current full runtime including transport before restart");Assert.IsNull(restored.Latest.Failure);Assert.GreaterOrEqual(restored.Latest.Tick,tick+30,"Actual restored threaded continuation");
            }
            finally
            {
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                if(!originalStopped)StopOriginal();
                if(lastObservation!=null)File.WriteAllText(Path.Combine(output,"observer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{initial=initial.ToArray(),assigned=assigned.ToArray(),joined=joined.ToArray(),physicallyMoved=physicallyMoved.ToArray(),lastObservation.Tick,lastArmies,units=lastObservation.Entities.Where(u=>u.Owner==lastObservation.Owner),routes=lastObservation.RouteProofs,diagnostic=lastArmies.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)).Select(a=>new{a.Id,core=AiReinforcementPolicy.ReceivingCore(lastObservation,a,p,ai).Select(u=>u.Id),ready=AiTacticalExecutor.Ready(lastObservation,a,p).Select(u=>u.Id),proposal=AiTacticalExecutor.Propose(lastObservation,a,p,999999,ai:ai)})},Newtonsoft.Json.Formatting.Indented));
                try
                {
                    StopRuntimeAndRouteWorker(restored,restoreRoutes);
                    restoredAuthority?.Domain.Navigation.ReleaseCommittedHostRoot();
                    restoredAuthority?.Domain.DestroyOwnedNavigation();
                }
                finally { TestMatchLogSession.Shutdown(); }
            }
        }
        [UnityTest,Timeout(150000)] public IEnumerator InterceptedCoreRedirectsEnRouteProductionWithAppliedMoveAndCurrentRestore()
        {
            const int seed=7;const long generation=109;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery};
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).ToArray();
            // One opening core: two Tanks, this Explorer and the ordinary home
            // Explorer. The positive witness retains the multi-front roster.
            var units=Enumerable.Range(0,2).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-14+i*3,-2)))
                .Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-14,5))}).ToArray();
            // The intercepted army must remain in a running match after the first
            // enemy HQ falls. A normal reserve center preserves that lifecycle;
            // it adds no combat force or altered health to the interception.
            var reserve=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(24,-24),Array.Empty<TerritorySlot>());
            // Keep the real 18-Tank interception formation on the near flank,
            // outside the initial army approach's weapon envelope. The current
            // public survey approach passes closer to the former x=23 staging;
            // those waiting tanks fired before factory output could travel.
            // Stock radius/tolerance supply physical spawn clearance; no combat or
            // movement profile changes are used to create the interception.
            double interceptionSpacing=2*PlayableUnitRules.Radius(p,PlayableEntityKind.Tank)+2*p.Navigation.ArrivalTolerance;
            double interceptionOrigin=p.ArenaHalfExtent-PlayableUnitRules.Radius(p,PlayableEntityKind.Tank)-2*interceptionSpacing-p.Navigation.ArrivalTolerance;
            var config=new OfflineMatchConfiguration(p,"ai-safe-reinforcements-ordinary-v1","flat-public-active-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("active-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,reserve},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units.Concat(Enumerable.Range(0,18).Select(i=>new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(interceptionOrigin+(i%3)*interceptionSpacing,2-(i/3)*interceptionSpacing)))).ToArray(),scenarioBuildings:buildings.Concat(new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,reserve.Position)}).ToArray(),spectators:new[]{PlayableRuntime.LocalSpectatorId});
            var source=Environment.GetEnvironmentVariable("AI_REINFORCEMENT_SOURCE_SHA");Assert.IsNotEmpty(source);
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-safe-reinforcements/ordinary-loss-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            // The interception scenario requires this exact opening core. Binding the
            // opening through the match setup keeps it stable across profile revisions.
            var runtime=PlayableRuntime.CreateWithOpeningForTests(config,generation,AiDifficulty.Veteran,ai,PlayableAiOpening.Safe);
            var routes=new UnityHostRouteService();
            PlayableRuntime restored=null;PlayableAuthorityTick restoredAuthority=null;UnityHostRouteService restoreRoutes=null;bool originalStopped=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void StopOriginal()
            {
                if(originalStopped)return;
                StopRuntimeAndRouteWorker(runtime,routes);
                var a=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                a.Domain.Navigation.ReleaseCommittedHostRoot();a.Domain.DestroyOwnedNavigation();originalStopped=true;
            }
            var observed=new System.Collections.Concurrent.ConcurrentQueue<(PlayableAiObservation observation,AiArmyRegistryState armies)>();
            PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="active-owner";
            PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(owner,tick,o,armies,actions)=>observed.Enqueue((o,armies));
            var humanOrigins=new Dictionary<int,NavPoint>();
            int enRoute=0,intrusionCandidate=0,receiverAnchor=0;long triggerAction=0,triggerSequence=0,triggerTick=0;PlayableCommandKind triggerKind=PlayableCommandKind.Move;long receiver=0;NavPoint receiverPoint=default;bool humanAttackSent=false,humanMoved=false,regroupSeen=false,fallbackMoved=false,soloAttack=false;long fallbackAction=0,fallbackTick=0;NavPoint fallbackOrigin=default;
            PlayableAiObservation lastObservation=null;AiArmyRegistryState lastArmies=null;
            var initial=new HashSet<int>();var assigned=new HashSet<int>();var joined=new HashSet<int>();var followMotion=new Dictionary<int,(NavPoint point,long tick,long action)>();var physicallyMoved=new HashSet<int>();
            var motion=new Dictionary<int,(NavPoint point,long tick,long action)>();var moved=new HashSet<int>();bool damage=false;var hostileHealth=new Dictionary<int,int>();
            try
            {
                var initialView=PlayableAiObservation.From(runtime.ParticipantView("active-owner"));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                var initialOpening=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="active-owner").Opening;
                Assert.AreEqual(PlayableAiOpening.Safe,initialOpening.Opening,"Fixed seed/profile ordinary opening premise");
                var initialExecutor=new AiOpeningExecutor(initialOpening,generation);initialExecutor.Observe(initialView,null,null,ai);
                Assert.AreEqual(4,typeof(AiOpeningExecutor).GetProperty("RequiredForce",flags).GetValue(initialExecutor),"One ordinary opening core");
                Assert.AreEqual(2,initialView.Entities.Count(u=>u.Owner==initialView.Owner&&u.Kind==PlayableEntityKind.Tank));
                Assert.AreEqual(2,initialView.Entities.Count(u=>u.Owner==initialView.Owner&&u.Kind==PlayableEntityKind.Explorer));
                var initialInterceptors=runtime.ParticipantView("human-owner").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).ToArray();
                Assert.AreEqual(18,initialInterceptors.Length);Assert.True(initialInterceptors.All(u=>!u.Moving));
                foreach(var u in initialInterceptors)foreach(var peer in initialInterceptors.Where(v=>v.Id>u.Id))
                    Assert.GreaterOrEqual(AiOpponentSelector.Distance(u.Position,peer.Position),2*PlayableUnitRules.Radius(p,PlayableEntityKind.Tank),"The actual interceptor bodies must not overlap");
                TestContext.WriteLine("INTERCEPTION_PREMISE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{seed,ai.Id,ai.Hash,interceptionSpacing,InitialCore=initialView.Entities.Where(u=>u.Owner==initialView.Owner).Select(u=>new{u.Id,u.Kind,u.Position}),Interceptors=initialInterceptors.Select(u=>new{u.Id,u.Position,u.Health,u.HullHeading})}));
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                float deadline=Time.realtimeSinceStartup+120;
                while(runtime.Latest.Tick<3600&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var f=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=f.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    while(observed.TryDequeue(out var observation))
                    {
                        var o=observation.observation;lastObservation=o;lastArmies=observation.armies;
                        if(initial.Count==0)foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner))initial.Add(u.Id);
                        foreach(var a in observation.armies.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)))
                        foreach(var u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&!initial.Contains(u.Id)&&a.Members.Contains(u.Id)))
                        {
                            if(a.Reinforcements.Contains(u.Id))assigned.Add(u.Id);
                            // Start interception during real production travel. Waiting
                            // for the final arrival correction can let the original
                            // mission finish before the opposing formation arrives.
                            // The actual healthy receiver and its travelling client
                            // are latched before the same opposing human input.
                            if(intrusionCandidate==0&&a.Reinforcements.Contains(u.Id)&&u.Moving&&
                                u.CurrentOrder?.Kind is (PlayableTacticalOrderKind.Move or PlayableTacticalOrderKind.Follow or PlayableTacticalOrderKind.AttackMove)&&
                                u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Generation==o.Generation&&u.OrderStamp.Owner==o.Owner&&
                                u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&u.OrderStamp.Sequence==u.CurrentOrder.CommandSequence&&u.OrderStamp.Tick==u.CurrentOrder.IssuedTick&&u.OrderStamp.JobId==u.OrderStamp.ActionId)
                            {
                                var core=AiReinforcementPolicy.ReceivingCore(o,a,p,ai)
                                    .OrderBy(c=>AiOpponentSelector.Distance(u.Position,c.Position)).ThenBy(c=>c.Id).FirstOrDefault();
                                if(core!=null){intrusionCandidate=enRoute=u.Id;receiver=a.Id;receiverAnchor=core.Id;receiverPoint=core.Position;
                                    triggerAction=u.OrderStamp.ActionId;triggerSequence=u.OrderStamp.Sequence;triggerTick=u.OrderStamp.Tick;triggerKind=u.OrderStamp.Kind;}
                            }
                            if(enRoute==u.Id&&receiver==a.Id&&(a.Phase==AiArmyPhase.Regrouping||a.Phase==AiArmyPhase.Retreating||a.Phase==AiArmyPhase.Recovering))
                            {
                                regroupSeen=true;
                                if(u.CurrentOrder?.Kind is (PlayableTacticalOrderKind.Attack or PlayableTacticalOrderKind.AttackMove)&&
                                    u.OrderStamp?.Kind is (PlayableCommandKind.Attack or PlayableCommandKind.AttackMove)&&
                                    u.OrderStamp.Sequence==u.CurrentOrder.CommandSequence&&u.OrderStamp.Tick>triggerTick)soloAttack=true;
                                if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                                    u.OrderStamp.ActionId!=triggerAction&&u.OrderStamp.Tick>triggerTick)
                                {
                                    if(fallbackAction!=u.OrderStamp.ActionId){fallbackAction=u.OrderStamp.ActionId;fallbackTick=o.Tick;fallbackOrigin=u.Position;}
                                    else if(o.Tick>fallbackTick&&AiOpponentSelector.Distance(fallbackOrigin,u.Position)>PlayableUnitRules.Radius(p,u.Kind))fallbackMoved=true;
                                }
                            }
                            if(assigned.Contains(u.Id)&&!a.Reinforcements.Contains(u.Id))joined.Add(u.Id);
                            if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Kind==PlayableCommandKind.Follow)
                            {
                                Assert.True(a.Members.Contains(u.CurrentOrder.TargetId)&&!a.Reinforcements.Contains(u.CurrentOrder.TargetId),"Follows its live core, never the enemy objective");
                                if(!followMotion.TryGetValue(u.Id,out var before)||before.action!=u.OrderStamp.ActionId)followMotion[u.Id]=(u.Position,o.Tick,u.OrderStamp.ActionId);
                                else if(o.Tick>before.tick&&AiOpponentSelector.Distance(before.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))physicallyMoved.Add(u.Id);
                            }
                        }
                    }
                    if(humanAttackSent&&runtime.ParticipantView("human-owner").Entities.Any(u=>humanOrigins.TryGetValue(u.Id,out var origin)&&u.CurrentOrder?.CommandSequence is >=1 and <=3&&AiOpponentSelector.Distance(origin,u.Position)>PlayableUnitRules.Radius(p,u.Kind)))humanMoved=true;
                    if(intrusionCandidate!=0&&!humanAttackSent)
                    {
                        var attackers=f.Overview.Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Health>0&&u.Kind==PlayableEntityKind.Tank).Select(u=>u.Id).OrderBy(id=>id).ToArray();
                        Assert.AreEqual(18,attackers.Length);
                        foreach(var u in runtime.ParticipantView("human-owner").Entities.Where(u=>attackers.Contains(u.Id)))humanOrigins[u.Id]=u.Position;
                        // Three columns converge through distinct ground slots. One 18-unit
                        // command to one point kept most of the formation behind its own tanks.
                        for(int column=0;column<3;column++)
                        {
                            var squad=attackers.Where((_,index)=>index%3==column).ToArray();
                            var intercept=new NavPoint(receiverPoint.X,receiverPoint.Z+(column-1)*2*interceptionSpacing);
                            Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(generation,column+1,"human-owner",PlayableCommandKind.AttackMove,squad,intercept)).Status);
                        }
                        humanAttackSent=true;
                    }
                    foreach(var b in f.Overview.Buildings.Where(b=>b.Owner==PlayableOwner.Enemy&&b.Health>0))
                    {if(hostileHealth.TryGetValue(b.Id,out var previous)&&b.Health<previous&&moved.Count>0)damage=true;hostileHealth[b.Id]=b.Health;}
                    foreach(var u in own.Entities.Where(u=>u.Owner==own.Owner&&u.Health>0&&u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&(u.OrderStamp.Kind==PlayableCommandKind.Attack||u.OrderStamp.Kind==PlayableCommandKind.AttackMove)))
                    {
                        if(!motion.TryGetValue(u.Id,out var start)||start.action!=u.OrderStamp.ActionId)motion[u.Id]=(u.Position,own.Tick,u.OrderStamp.ActionId);
                        else if(own.Tick>start.tick&&AiOpponentSelector.Distance(start.point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))moved.Add(u.Id);
                    }
                    if(regroupSeen&&fallbackMoved)break;yield return null;
                }
                var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted,"Fixed wall deadline includes capture");Assert.False(capture.IsFaulted,capture.Exception?.ToString());File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);
                long tick;using(var envelope=PlayableWorldState.Decode(capture.Result)){tick=envelope.Tick;Assert.Greater(envelope.RuntimeTransport.Length,0);}
                StopOriginal();byte[] roundtrip=null;PlayableAiOwnerCheckpoint checkpoint=null;Exception restoreError=null;
                restoreRoutes=new UnityHostRouteService();restored=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>
                {
                    try{roundtrip=r.CaptureBeforeStartForTests();restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);checkpoint=restoredAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="active-owner");}
                    catch(Exception e){restoreError=e;r.RequestStop();}
                });
                Assert.IsNull(restoreError);bool exact=capture.Result.SequenceEqual(roundtrip);
                var applied=checkpoint.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&(r.Kind==PlayableCommandKind.Attack||r.Kind==PlayableCommandKind.AttackMove)&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                var followed=checkpoint.Records.Where(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner").ToArray();
                bool fallbackApplied=checkpoint.Records.Any(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Kind==PlayableCommandKind.Move&&r.ActionId==fallbackAction&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.Generation==generation&&r.OwnerId=="active-owner");
                bool realJoin=joined.Any(id=>physicallyMoved.Contains(id)&&followed.Any(r=>followMotion.TryGetValue(id,out var motionBefore)&&motionBefore.action==r.ActionId));
                bool receiptMotion=applied.Any(r=>motion.Any(m=>m.Value.action==r.ActionId&&moved.Contains(m.Key)));
                float restoreDeadline=Time.realtimeSinceStartup+10;
                while(restored.Latest.Tick<tick+30&&!restored.IsStopped&&Time.realtimeSinceStartup<restoreDeadline){restoreRoutes.Service(restored,p.NavigationRequestsPerFrame);yield return null;}
                bool triggerApplied=checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==triggerKind&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ActionId==triggerAction&&r.CommandSequence==triggerSequence&&r.ApplicationTick==triggerTick&&r.OwnerId=="active-owner"&&r.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&r.ReceiptIdentity?.Generation==generation&&r.ReceiptIdentity.OwnerId=="active-owner");
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,tick,p.ProfileId,p.Revision,ai.Id,ai.Hash,intrusionCandidate,receiverAnchor,triggerKind,triggerAction,triggerSequence,triggerTick,triggerApplied,enRoute,receiver,humanAttackSent,humanMoved,regroupSeen,fallbackMoved,soloAttack,fallbackAction,fallbackApplied,assigned=assigned.ToArray(),joined=joined.ToArray(),physicallyMoved=physicallyMoved.ToArray(),followMotion,realJoin,moved=moved.ToArray(),damage,receiptMotion,exact,continued=restored.Latest.Tick>=tick+30,checkpoint.ArmyMission,records=checkpoint.Records},Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("SAFE_REINFORCEMENT_RESULT "+output);
                Assert.Greater(assigned.Count,0,"Real factory output assigned without waiting for a wave");Assert.True(humanAttackSent,"Real opposing AttackMove accepted through ordinary human authority");Assert.True(humanMoved,"Accepted opposing command produces real physical motion");Assert.AreEqual(intrusionCandidate,enRoute,"Interception and fallback stay bound to the same actually travelling production client");Assert.True(triggerApplied,"The exact ordinary joining command must actually apply before interception");Assert.True(regroupSeen,"Threatened/withdrawing core stops old feed");Assert.True(fallbackMoved&&fallbackApplied,"Applied ordinary Move physically redirects the en-route produced unit");Assert.False(soloAttack,"Reinforcement cannot attack independently after receiver loss");Assert.True(exact,"Exact current full runtime including transport before restart");Assert.IsNull(restored.Latest.Failure);Assert.GreaterOrEqual(restored.Latest.Tick,tick+30,"Actual restored threaded continuation");
            }
            finally
            {
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                if(!originalStopped)StopOriginal();
                if(lastObservation!=null)File.WriteAllText(Path.Combine(output,"observer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{initial=initial.ToArray(),assigned=assigned.ToArray(),joined=joined.ToArray(),physicallyMoved=physicallyMoved.ToArray(),lastObservation.Tick,lastArmies,units=lastObservation.Entities.Where(u=>u.Owner==lastObservation.Owner),routes=lastObservation.RouteProofs,diagnostic=lastArmies.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)).Select(a=>new{a.Id,core=AiReinforcementPolicy.ReceivingCore(lastObservation,a,p,ai).Select(u=>u.Id),ready=AiTacticalExecutor.Ready(lastObservation,a,p).Select(u=>u.Id),proposal=AiTacticalExecutor.Propose(lastObservation,a,p,999999,ai:ai)})},Newtonsoft.Json.Formatting.Indented));
                try
                {
                    StopRuntimeAndRouteWorker(restored,restoreRoutes);
                    restoredAuthority?.Domain.Navigation.ReleaseCommittedHostRoot();
                    restoredAuthority?.Domain.DestroyOwnedNavigation();
                }
                finally { TestMatchLogSession.Shutdown(); }
            }
        }
    }
}
