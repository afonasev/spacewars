using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CombatMicroLivingCoreThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [Test,Timeout(90000)] public void ShortWithdrawalPhysicallyRejoinsLivingCurrentCore()=>Run(false);
        [Test,Timeout(90000)] public void StronglyDamagedWithdrawalUsesActualTowerThenPhysicallyRejoinsCurrentCore()=>Run(true);
        [Test,Timeout(90000)] public void EarlyRecastShortWithdrawalUsesOwningMotionPublication()
        {
            var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=1;Run(false,PlayableProfile.Create(data));
        }
        [Test,Timeout(90000)] public void RealPendingCurrentSourceRebindsAfterHumanReplacementWithoutPolicyReplay()
        {
            var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=1;Run(false,PlayableProfile.Create(data),true);
        }
        void Run(bool repairTower,PlayableProfile selectedProfile=null,bool pendingControl=false)
        {
            const int seed=7;long generation=repairTower?904:903;var p=selectedProfile??PlayableProfile.Default;var ai=AiProfile.Initial;
            var ownStart=repairTower?new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1):new OfflineStart("west",1,new NavPoint(-25,-5),new NavPoint(-20,-5),pin:1);
            var starts=new[]{ownStart,new OfflineStart("east",2,new NavPoint(-9,21),new NavPoint(2,27),pin:2)};
            var ownSite=repairTower?new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p):new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>());
            var towerPad=repairTower?ownSite.DefenseSlots(p).Single(slot=>slot.Id==1003):null;
            var towerPoint=towerPad?.Position??new NavPoint(-27,23);
            var sites=new[]{ownSite,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-16,15)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-30,19)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-30,11)),
                new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-7,12),Math.PI),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-6.5,15),Math.PI),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-7,18),Math.PI)};
            var config=new OfflineMatchConfiguration(p,repairTower?"combat-micro-repair-living-v1":"combat-micro-living-v1","living-core-flat-seed7","ordinary-certified-threaded",seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:repairTower?new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,towerPad.Id,towerPad.Position,towerPad.Heading)}:Array.Empty<OfflineScenarioBuilding>());
            var d=new PlayableDomain(p,generation,config);
            var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe,aiProfile:ai);
            var loop=new PlayableAiOwnerLoop(p,opening,generation,null,ai,AiDifficulty.Veteran);
            // Authored midgame setup follows the same complete opening milestone contract.
            var executor=Field(loop,"openingExecutor");executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);
            executor.GetType().GetField("initialForceFulfilled",F).SetValue(executor,true);
            foreach(var key in new[]{"income","factory","force","pressure"})((HashSet<string>)Field(executor,"completed")).Add(key);
            var scheduler=new AiAuthorityScheduler(new[]{loop});var authority=new PlayableAuthorityTick(d,scheduler,seed,config);CertifiedRouteWorker worker=null;PlayableAuthorityTick pendingCold=null;CertifiedRouteWorker pendingColdWorker=null;
            var samples=new List<string>();object failurePayload=null;
            var coreFrames=new List<object>(2048);int coreDropped=0,coreErrors=0;string coreError=null;
            Action<string,long,PlayableAiObservation,AiArmyRegistryState,PlayableAiAction[]> ownedCoreObserver=null;
            bool coreObserverInstalled=false;int coreThread=Thread.CurrentThread.ManagedThreadId;
            string coreOutput=System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath,"../../.local/ai-combat-attention/applied-preemption/tower-core-attempt-1"));
            try
            {
                var initial=authority.ParticipantView("player-1");var tanks=initial.Entities.Where(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                int victim=tanks[0].Id;var origin=tanks[0].Position;
                foreach(var unit in initial.Entities.Where(u=>u.Kind!=PlayableEntityKind.Hawk))Assert.True(d.Geometry.IsFree(unit.Position,PlayableUnitRules.Radius(p,unit.Kind)),"Every authored spawn must use a legal free pad");
                Assert.AreEqual(generation,initial.Generation);Assert.AreEqual(p.ProfileId,initial.ProfileId);Assert.AreEqual(p.Revision,initial.ProfileRevision);
                TestContext.WriteLine($"CURRENT world={PlayableWorldState.CurrentVersion} owner={AiStateWire.Version} observation={PlayableAiObservation.From(initial).SchemaVersion} command={PlayableCommand.CurrentSchemaVersion} profile={p.ProfileId}@{p.Revision} ai={ai.Id}@{ai.Revision} hash={ai.Hash} source={config.SourceIdentity} map={config.MapIdentity}");
                var enemies=authority.ParticipantView("enemy-1").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                var planner=(AiArmyPlanner)Field(loop,"armyPlanner");var registry=(AiArmyRegistry)Field(loop,"armies");var o=PlayableAiObservation.From(initial);
                var openingAttack=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,generation,o.SnapshotSequence,PlayableCommandKind.Attack,tanks.Select(u=>u.Id).ToArray(),starts[1].Position,targetId:initial.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters).Id,seed:seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                planner.Commit(o,openingAttack,registry);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,o.OwnerId,openingAttack.Kind,openingAttack.CopyEntityIds(),openingAttack.Target,targetId:openingAttack.TargetId,origin:PlayableOrderOrigin.Ai,source:openingAttack.SourceIdentity,jobId:1,actionId:1)).Status);
                planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,0,1,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"authored midgame order",o.OwnerId,openingAttack.SourceIdentity,new AiReceiptIdentity(generation,o.OwnerId,1,1),AiArmyPlanner.Policy,openingAttack.Kind));
                typeof(AiAuthorityScheduler).GetField("nextActionId",F).SetValue(scheduler,1L);
                long enemySequence=10;
                foreach(var enemy in enemies)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,enemySequence++,"enemy-1",PlayableCommandKind.Attack,new[]{enemy.Id},origin,targetId:victim)).Status);
                var heldExplorer=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Explorer);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,2,"player-1",PlayableCommandKind.Hold,new[]{heldExplorer.Id},origin:PlayableOrderOrigin.Human)).Status);
                // This real human job keeps the extra starter from adding hidden HQ
                // DPS. Native stats, damage and autonomous battle remain unchanged.
                Assert.AreEqual(400,authority.ParticipantView("enemy-1").Buildings.Single(b=>b.Id==openingAttack.TargetId&&b.Kind==PlayableBuildingKind.Headquarters).Health);
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");long repairTick=-1,healTick=-1,followTick=-1,joinTick=-1,nativeJoinTick=-1;int anchor=0;
                bool repairApplied=false,repairMotion=false,towerLocked=false,healed=false,returnMotion=false;NavPoint followOrigin=origin;double repairStartHealth=100,maxHealth=0;NavPoint repairOrigin=origin;
                Dictionary<string,object> CoreScalars(object value)=>value==null?null:value.GetType().GetFields(F).Where(f=>f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string)||f.FieldType==typeof(NavPoint)).ToDictionary(f=>f.Name,f=>f.GetValue(value));
                object CoreAction(PlayableAiAction a)=>a==null?null:new{a.ActionId,a.Generation,a.SnapshotSequence,a.PlayerId,a.SourceIdentity,a.Kind,a.Target,a.TargetId,a.ParentId,Entities=a.CopyEntityIds()};
                var coreMethod=typeof(AiCombatMicroPolicy).GetMethod("ReturnCore",F|BindingFlags.Static);
                var matchMethod=typeof(AiCombatMicroPolicy).GetMethods(F|BindingFlags.Static).Single(m=>m.Name=="Matches"&&m.GetParameters().Length==3);
                void RecordCore(string stage,PlayableAiObservation observation,AiArmyRegistryState groups)
                {
                    try{
                        if(Thread.CurrentThread.ManagedThreadId!=coreThread)throw new InvalidOperationException("Unexpected background owner observation; mutable owner not inspected");
                        if(coreFrames.Count==2048){coreDropped++;return;}
                        var patient=observation.Entities.FirstOrDefault(u=>u.Id==victim&&u.Owner==observation.Owner);
                        var map=(IDictionary)Field(micro,"clients");var certs=(IDictionary)Field(micro,"certificates");var client=map.Contains(victim)?map[victim]:null;var cert=certs.Contains(victim)?certs[victim]:null;
                        long storedArmy=client==null?0:(long)Field(client,"Army");var actual=groups.Armies.FirstOrDefault(g=>g.Members.Contains(victim)&&g.Phase!=AiArmyPhase.Disbanded);
                        int[] Core(long id)=>patient==null||id==0?Array.Empty<int>():((PlayableEntitySnapshot[])coreMethod.Invoke(null,new object[]{observation,p,ai,AiDifficulty.Veteran,registry,patient,id})).Select(u=>u.Id).ToArray();
                        var proposed=Field(micro,"proposed");var pendingPlan=Field(micro,"pending");
                        coreFrames.Add(new{
                            Stage=stage,ObservationTick=observation.Tick,WorldTick=d.Tick,OwnerTick=(long)Field(loop,"authorityTick"),Thread=coreThread,
                            Client=CoreScalars(client),Certificate=CoreScalars(cert),MicroReserved=micro.Reserves(victim),MicroPendingId=micro.PendingId,
                            Proposed=proposed==null?null:new{Values=CoreScalars(proposed),Action=CoreAction((PlayableAiAction)Field(proposed,"Action"))},
                            PendingMicro=pendingPlan==null?null:new{Values=CoreScalars(pendingPlan),Action=CoreAction((PlayableAiAction)Field(pendingPlan,"Action"))},
                            StoredArmy=storedArmy,CurrentArmy=actual?.Id??0,
                            CoreForStoredArmy=Core(storedArmy),CoreForCurrentArmy=Core(actual?.Id??0),
                            Guards=new{PatientPresent=patient!=null,Human=patient?.OrderStamp?.Origin==PlayableOrderOrigin.Human,Held=patient?.Held,Departing=patient?.Departing,
                                HealthReady=patient!=null&&PlayableUnitRules.HealthRatio(p,patient)>=ai.DifficultyValue(AiDifficulty.Veteran,"micro.repairReturnHealth"),
                                CertificateMatches=cert!=null&&patient!=null&&(bool)matchMethod.Invoke(null,new[]{cert,(object)observation,patient}),
                                PhaseTimeout=client!=null&&observation.Tick-(long)Field(client,"Started")>AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"micro.maxPhaseSeconds"),30)},
                            Own=observation.Entities.Where(u=>u.Owner==observation.Owner).OrderBy(u=>u.Id).Select(u=>new{u.Id,u.Kind,u.Health,u.ExactHealth,u.MaxHealth,u.Position,u.Held,u.Departing,u.NavigationOutcome,u.CurrentOrder,u.OrderStamp,u.Queue}).ToArray(),
                            Armies=groups.Armies.Select(g=>new{g.Id,g.OwnerId,g.Phase,g.Major,g.TacticalOwner,g.MinimumViableForce,g.ProgressTick,g.CreatedTick,Members=g.Members.ToArray(),Reinforcements=g.Reinforcements.ToArray(),g.LeaderId,g.Rally}).ToArray(),
                            Towers=observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.RepairTower).Select(b=>new{b.Id,b.Health,b.Phase,b.Position,b.Healing}).ToArray(),
                            Records=((IEnumerable)Field(loop,"records")).Cast<PlayableAiTraceRecord>().ToArray(),
                            OwnerPending=((IEnumerable)Field(loop,"pendingItems")).Cast<object>().Select(item=>new{Values=CoreScalars(item),Action=CoreAction((PlayableAiAction)Field(item,"Action")),Intent=CoreScalars(Field(item,"Intent"))}).ToArray(),
                            PatientRoutes=observation.RouteProofs.Where(r=>r.UnitId==victim).Select(r=>new{r.UnitId,r.TargetId,r.Kind,r.Generation,r.Tick,r.GeometryRevision,r.Origin,r.Target,r.Goal,Points=r.Path.Count}).ToArray(),
                            Consistency="Pre-decision is after defense observation, before micro Observe/selection. Post-frame reuses existing view; owner and view phases may differ. Guard flags are observations, not hooked release reasons."
                        });
                    }catch(Exception error){coreErrors++;if(coreError==null)coreError=error.ToString();}
                }
                if(repairTower){
                    Assert.False((bool)Field(scheduler,"backgroundReviews"),"Observer requires the frozen synchronous owner-review fixture");
                    Assert.IsNull(PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests);Assert.IsNull(PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests);
                    Assert.False(System.IO.File.Exists(System.IO.Path.Combine(coreOutput,"core.json")),"Never overwrite a previous attempt");
                    ownedCoreObserver=(owner,tick,observation,groups,actions)=>RecordCore("pre-decision",observation,groups);
                    PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="player-1";PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=ownedCoreObserver;coreObserverInstalled=true;
                }
                if(pendingControl){loop.BindExecutionDomain(d);d.BindCombatMotionAuthority(()=>scheduler.Owners);d.CombatMotionAuthority.WorkGrantForTests=_=>0;}
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool applied=false,moved=false,releasedFocus=false,returned=false;long firstApplied=-1,firstMotion=-1;NavPoint withdrawal=origin;
                var appliedRecords=new Dictionary<long,PlayableAiTraceRecord>();var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("player-1");var actor=view.Entities.FirstOrDefault(u=>u.Id==victim);Assert.NotNull(actor,"Focal unit must survive the recovery cycle");
                    if(pendingControl&&d.CombatMotionAuthority.HasPending){
                        // A real producer input is accepted by the actual AI
                        // consumer; refusing private work leaves its physical
                        // publication pending. Human custody changes afterward.
                        Assert.Zero(d.CombatMotionAuthority.AffectedPublications);
                        long operation=d.Navigation.MotionOperationWatermark;Assert.Greater(operation,0);
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,d.AllocateAiSequence(),"player-1",PlayableCommandKind.Hold,new[]{victim},origin:PlayableOrderOrigin.Human)).Status);
                        var changed=d.CombatExecutionActor(victim);Assert.True(changed.Held);Assert.AreEqual(PlayableOrderOrigin.Human,changed.OrderStamp.Origin);
                        byte[] saved=authority.CaptureBytes();pendingCold=PlayableAuthorityTick.RestoreBytes(saved,config);
                        Assert.True(pendingCold.Domain.Navigation.AwaitingMotionSources);var restoredOwner=pendingCold.Scheduler.Owners.Single(o=>o.OwnerId=="player-1");Assert.True(restoredOwner.AwaitingExecutionSources);
                        CollectionAssert.AreEqual(saved,pendingCold.CaptureBytes(),"Current quiescent capture is exact before source rebind.");
                        Assert.Throws<InvalidOperationException>(()=>pendingCold.Domain.Step(1d/30d));
                        restoredOwner.BindExecutionDomain(pendingCold.Domain);pendingCold.Domain.BindCombatMotionAuthority(()=>pendingCold.Scheduler.Owners);
                        Assert.False(pendingCold.Domain.Navigation.AwaitingMotionSources);Assert.False(restoredOwner.AwaitingExecutionSources);Assert.True(pendingCold.Domain.CombatMotionAuthority.HasPending);
                        CollectionAssert.AreEqual(saved,pendingCold.CaptureBytes(),"Both paid source rebinds preserve the exact saved stale promise.");
                        pendingColdWorker=new CertifiedRouteWorker(pendingCold.NavigationBinding,p.NavigationRequestsPerFrame);long savedTick=pendingCold.Tick;
                        while(pendingCold.Tick<=savedTick&&clock.Elapsed.TotalSeconds<60){if(!pendingCold.TryAdvance())Thread.Sleep(1);Assert.IsNull(pendingColdWorker.Publication.Failure);}
                        Assert.Greater(pendingCold.Tick,savedTick);pendingCold.Domain.CaptureCombatMotionBoundary();
                        Assert.False(pendingCold.Domain.CombatMotionAuthority.HasPending);Assert.Zero(pendingCold.Domain.CombatMotionAuthority.AffectedPublications,"A stale accepted turn must not install factual escape history.");
                        Assert.True(pendingCold.Domain.CombatExecutionActor(victim).Held);Assert.AreEqual(PlayableOrderOrigin.Human,pendingCold.Domain.CombatExecutionActor(victim).OrderStamp.Origin);
                        string output=System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath,"../../.local/ai-combat-attention/consumer-current/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")));System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"pending-human-current.world"),saved);
                        TestContext.WriteLine($"REAL_CURRENT_PENDING operation={operation} tick={savedTick} bytes={saved.Length} immediateExact=True reboundExact=True humanPriority=True staleAffectedCommits=0 output={output}");
                        return; // Dedicated current-state control, not withdrawal/return acceptance.
                    }
                    if(coreObserverInstalled)RecordCore("post-frame",PlayableAiObservation.From(view),registry.Capture());
                    bool liveVisibleHq=view.Buildings.Any(b=>b.Id==openingAttack.TargetId&&b.Health>0&&view.Vision?.IsVisible(b.Position)==true);
                    if(!liveVisibleHq)
                    {
                        var selected=view.Buildings.FirstOrDefault(b=>b.Id==openingAttack.TargetId);
                        var harnessEnemy=authority.ParticipantView("enemy-1");var actualHq=harnessEnemy.Buildings.FirstOrDefault(b=>b.Id==openingAttack.TargetId);
                        var clientMap=(IDictionary)Field(micro,"clients");var client=clientMap.Contains(victim)?clientMap[victim]:null;
                        failurePayload=new{Tick=d.Tick,view.Status,view.Outcome,SelectedId=openingAttack.TargetId,FrozenHqPosition=starts[1].Position,
                            PlayerHq=selected==null?null:new{selected.Id,selected.Health,selected.Position},ActualHq=actualHq==null?null:new{actualHq.Id,actualHq.Health,actualHq.Position},
                            VisionAtFrozenPosition=view.Vision?.IsVisible(starts[1].Position),VisionSources=view.Vision?.Sources.Select(v=>new{v.Position,v.Radius}).ToArray(),
                            Own=view.Entities.Where(u=>u.Owner==view.Owner).Select(u=>new{u.Id,u.Health,u.Position,u.Held,u.NavigationOutcome,u.CurrentOrder,u.OrderStamp,Queue=u.Queue==null?null:new{u.Queue.Active,u.Queue.Pending,Deferred=u.Queue.Deferred.ToArray()}}).ToArray(),
                            Registry=registry.Capture().Armies.Select(g=>new{g.Id,g.Phase,g.TacticalOwner,Members=g.Members.ToArray(),Reinforcements=g.Reinforcements.ToArray()}).ToArray(),
                            Mission=planner.Capture(),Client=client==null?null:client.GetType().GetFields(F).Where(f=>f.FieldType.IsValueType||f.FieldType==typeof(string)).ToDictionary(f=>f.Name,f=>f.GetValue(client)),
                            LatestReceipts=loop.Checkpoint.Records.Reverse().Take(8).Reverse().Select(r=>new{r.Policy,r.ActionId,r.Kind,r.Status,r.ApplicationTick,r.CommandSequence,r.Message}).ToArray(),
                            Partial=new{applied,moved,releasedFocus,firstApplied,firstMotion,repairTick,healTick,followTick,joinTick}};
                    }
                    Assert.True(liveVisibleHq,"Live selected HQ visibility must persist independently during the positive recovery cycle");
                    foreach(var record in loop.Checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied))appliedRecords[record.CommandSequence]=record;
                    var receipt=loop.Checkpoint.Records.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ActionId==actor.OrderStamp?.ActionId);
                    if(receipt!=null&&!applied){var liveClients=(IDictionary)Field(micro,"clients");Assert.True(liveClients.Contains(victim));Assert.AreEqual(AiCombatMicroPhase.Withdraw,(AiCombatMicroPhase)Field(liveClients[victim],"Phase"));applied=true;firstApplied=receipt.ApplicationTick;}
                    if(applied&&actor!=null&&actor.Position.X<origin.X-1){moved=true;if(firstMotion<0)firstMotion=d.Tick;withdrawal=actor.Position;}
                    if(moved&&!releasedFocus)
                    {
                        // Scripted opponent stops focusing and leaves by ordinary human orders.
                        var remaining=authority.ParticipantView("enemy-1").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Health>0).Select(u=>u.Id).ToArray();
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,enemySequence++,"enemy-1",PlayableCommandKind.Move,remaining,new NavPoint(8,25))).Status);releasedFocus=true;
                    }
                    var clients=(IDictionary)Field(micro,"clients");var active=clients.Contains(victim)?clients[victim]:null;
                    if(repairTower&&active!=null&&(AiCombatMicroPhase)Field(active,"Phase")==AiCombatMicroPhase.Repair&&(long)Field(active,"Sequence")>0)
                    {
                        if(!repairApplied){repairApplied=true;repairTick=(long)Field(((IDictionary)Field(micro,"certificates"))[victim],"Tick");repairOrigin=actor.Position;repairStartHealth=actor.ExactHealth??actor.Health;maxHealth=repairStartHealth;Assert.Less(repairStartHealth,30);Assert.AreEqual(initial.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower).Id,(int)Field(active,"Tower"));}
                        repairMotion|=Distance(actor.Position,repairOrigin)>1&&Distance(actor.Position,towerPoint)<Distance(repairOrigin,towerPoint)-1;
                    }
                    towerLocked|=repairTower&&view.Buildings.Any(b=>b.Kind==PlayableBuildingKind.RepairTower&&b.Healing?.TargetId==victim);
                    maxHealth=Math.Max(maxHealth,actor.ExactHealth??actor.Health);
                    if(repairApplied&&(actor.ExactHealth??actor.Health)>=90&&!healed){healed=true;healTick=d.Tick;}
                    var follow=loop.Checkpoint.Records.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(follow!=null&&followTick<0)
                    {
                        followTick=follow.ApplicationTick;followOrigin=actor.Position;if(repairTower)Assert.True(healed,"Follow requires actual healing to the configured90percent threshold");
                        anchor=actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow?actor.CurrentOrder.TargetId:actor.Queue?.Pending?.Kind==PlayableCommandKind.Follow?actor.Queue.Pending.VisibleTargetId:0;Assert.Greater(anchor,0);
                        var group=registry.Capture().Armies.SingleOrDefault(g=>g.Id==registry.ArmyFor(victim)&&g.Major&&g.Phase!=AiArmyPhase.Disbanded&&g.Phase!=AiArmyPhase.Retreating&&g.Phase!=AiArmyPhase.Recovering&&g.Phase!=AiArmyPhase.Regrouping);Assert.NotNull(group);Assert.Contains(anchor,group.Members);Assert.False(group.Reinforcements.Contains(anchor));
                        var core=view.Entities.Where(u=>u.Id!=victim&&u.Owner==view.Owner&&group.Members.Contains(u.Id)&&!group.Reinforcements.Contains(u.Id)&&PlayableUnitRules.HealthRatio(p,u)>=ai.DifficultyValue(AiDifficulty.Veteran,"micro.returnCoreHealth")).ToArray();Assert.GreaterOrEqual(core.Length,2);
                        Assert.True(core.Any(a=>a.Id==anchor&&core.Count(b=>Distance(a.Position,b.Position)<=ai.DifficultyValue(AiDifficulty.Veteran,"micro.supportRadius"))>=2));
                        Assert.True(view.Buildings.Any(b=>b.Id==openingAttack.TargetId&&b.Health>0&&view.Vision?.IsVisible(b.Position)==true),"The selected HQ must independently remain alive and actually visible");
                    }
                    var leader=view.Entities.FirstOrDefault(u=>u.Id==anchor&&u.Owner==view.Owner&&u.Health>0);
                    if(followTick>=0&&leader!=null){var currentGoal=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);returnMotion|=Distance(actor.Position,followOrigin)>1&&Distance(actor.Position,currentGoal)<Distance(followOrigin,currentGoal)-1;}
                    bool nativeJoined=returnMotion&&followTick>=0&&leader!=null&&actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&actor.CurrentOrder.TargetId==anchor&&
                        registry.ArmyFor(victim)>0&&registry.ArmyFor(victim)==registry.ArmyFor(anchor)&&Distance(actor.Position,leader.Position)<=p.FollowDistance+p.FollowArrivalTolerance;
                    if(!nativeJoined)nativeJoinTick=-1;else if(nativeJoinTick<0)nativeJoinTick=d.Tick;
                    if(nativeJoined&&!micro.Reserves(victim))
                    {
                        returned=true;joinTick=d.Tick;Assert.LessOrEqual(joinTick-nativeJoinTick,AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"decisionSeconds"),30),"Release at next actual review after native ground join");
                        var group=registry.Capture().Armies.Single(g=>g.Id==registry.ArmyFor(victim)&&g.Major&&g.Phase!=AiArmyPhase.Disbanded);var core=view.Entities.Where(u=>u.Id!=victim&&u.Owner==view.Owner&&group.Members.Contains(u.Id)&&!group.Reinforcements.Contains(u.Id)&&PlayableUnitRules.HealthRatio(p,u)>=ai.DifficultyValue(AiDifficulty.Veteran,"micro.returnCoreHealth")).ToArray();Assert.GreaterOrEqual(core.Length,2);Assert.True(core.Any(a=>a.Id==anchor&&core.Count(b=>Distance(a.Position,b.Position)<=ai.DifficultyValue(AiDifficulty.Veteran,"micro.supportRadius"))>=2));
                        Assert.True(view.Buildings.Any(b=>b.Id==openingAttack.TargetId&&b.Health>0&&view.Vision?.IsVisible(b.Position)==true));break;
                    }
                    if(d.Tick%30==0)samples.Add($"tick={d.Tick} hp={actor?.Health} pos={actor?.Position.X:F2},{actor?.Position.Z:F2} order={actor?.CurrentOrder?.Kind} attention={loop.CombatAttention.Applied} pending={loop.Checkpoint.PendingActionId} policy={loop.Checkpoint.LastPolicy}");
                    Thread.Sleep(1);
                }
                if(selectedProfile!=null){Assert.NotNull(d.CombatMotionAuthority);Assert.Greater(d.CombatMotionAuthority.Publications,0);Assert.Greater(d.CombatMotionAuthority.AffectedPublications,0,"Real movement must have adopted its affected micro cursor.");TestContext.WriteLine("MOTION_AUTHORITY publications="+d.CombatMotionAuthority.Publications+" refusal="+d.CombatMotionAuthority.LastRefusal);}
                TestContext.WriteLine($"MICRO seed{seed} gen{generation} tick={d.Tick} Applied={firstApplied} Motion={firstMotion} withdrawn={moved} returned={returned} pumps={worker.Publication.Pumps} receipts={appliedRecords.Count}");
                TestContext.WriteLine($"LIVING_CORE tower={repairTower} repair={repairTick} heal={healTick} follow={followTick} join={joinTick} nativejoin={nativeJoinTick} repairHP={repairStartHealth:R} maxHP={maxHealth:R} towerlock={towerLocked} repairmotion={repairMotion} returnmotion={returnMotion}");
                if(repairTower&&!(repairApplied&&repairMotion&&towerLocked&&healed))
                {
                    // Failure only: Latest is already immutable. ParticipantView would
                    // refresh vision; do not project, capture, advance or query routes here.
                    var finalView=authority.Latest;var finalActor=finalView.Entities.FirstOrDefault(u=>u.Id==victim);
                    object At(object map,int key)
                    {
                        if(map==null)return null;var args=new object[]{key,null};
                        return (bool)map.GetType().GetMethod("TryGetValue").Invoke(map,args)?args[1]:null;
                    }
                    Dictionary<string,object> Scalars(object value)
                    {
                        if(value==null)return null;
                        return value.GetType().GetFields(F).Where(f=>{
                            var t=Nullable.GetUnderlyingType(f.FieldType)??f.FieldType;
                            return t.IsPrimitive||t.IsEnum||t==typeof(string)||t==typeof(NavPoint)||t==typeof(NavLocation);
                        }).ToDictionary(f=>f.Name,f=>f.GetValue(value));
                    }
                    object Issuance(object value)=>value==null?null:new{Values=Scalars(value),Command=Field(value,"Command")};
                    object Execution(object value)=>value==null?null:new{Values=Scalars(value),Stamp=Field(value,"Stamp"),Issuance=Issuance(Field(value,"Issuance")),TerminalFailure=Scalars(Field(value,"TerminalFailure"))};
                    object Rows(object map)=>((IEnumerable)map).Cast<object>().Select(row=>new{Key=row.GetType().GetProperty("Key").GetValue(row),Value=row.GetType().GetProperty("Value").GetValue(row)}).ToArray();
                    var finalClient=At(Field(micro,"clients"),victim);var finalCertificate=At(Field(micro,"certificates"),victim);
                    var queue=At(Field(d,"tacticalQueues"),victim);var active=queue==null?null:Field(queue,"Active");var pendingQueue=queue==null?null:Field(queue,"Pending");
                    var nativeUnit=At(Field(d,"units"),victim);d.Navigation.Crowd.TryGet(victim,out var nativeActor);
                    var tower=finalView.Buildings.FirstOrDefault(b=>b.Kind==PlayableBuildingKind.RepairTower&&b.Owner==finalView.Owner);
                    var nativeTower=tower==null?null:At(Field(d,"buildings"),tower.Id);
                    NavPoint? Point(object value,string key)=>value==null?null:(NavPoint?)Field(value,key);
                    NavPoint? Assigned(object value)=>value==null?null:((NavLocation?)Field(value,"Assigned"))?.Position;
                    double? TowerDistance(NavPoint? point)=>point.HasValue?Distance(point.Value,towerPoint):(double?)null;
                    var pendingRequest=At(Field(d.Navigation,"pending"),victim);var allocatingRequest=At(Field(d.Navigation,"allocating"),victim);
                    TestContext.WriteLine("REPAIR_FINAL_FAILURE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{
                        Tick=d.Tick,ViewTick=finalView.Tick,Seed=seed,Generation=generation,Profile=p.ProfileId,AiHash=ai.Hash,
                        Result=new{applied,moved,repairApplied,repairMotion,towerLocked,healed,returned,firstApplied,firstMotion,repairTick,healTick,followTick,joinTick},
                        Actor=finalActor,NativeActor=nativeActor==null?null:new{Values=Scalars(nativeActor),nativeActor.Location,nativeActor.GoalLocation,nativeActor.Goal,nativeActor.Outcome,nativeActor.Moving,
                            nativeActor.InstalledGroupId,nativeActor.InstalledRevision,nativeActor.InstalledRequest,nativeActor.InstalledOrder,nativeActor.FailedGroupId,nativeActor.FailedRevision,nativeActor.FailedRequest,nativeActor.FailedOrder,nativeActor.FailedGoal,nativeActor.LastFailure,Route=nativeActor.Route.ToArray()},
                        DomainUnit=Scalars(nativeUnit),DefaultCombat=nativeUnit==null?null:Scalars(Field(nativeUnit,"DefaultCombat")),
                        MicroClient=Scalars(finalClient),MicroCertificate=Scalars(finalCertificate),MicroReserved=micro.Reserves(victim),
                        AllMicroClients=((IDictionary)Field(micro,"clients")).Keys.Cast<object>().Select(key=>new{Unit=key,Values=Scalars(((IDictionary)Field(micro,"clients"))[key])}).ToArray(),
                        AllMicroCertificates=((IDictionary)Field(micro,"certificates")).Keys.Cast<object>().Select(key=>new{Unit=key,Values=Scalars(((IDictionary)Field(micro,"certificates"))[key])}).ToArray(),
                        Queue=queue==null?null:new{Values=Scalars(queue),Active=Execution(active),Pending=Execution(pendingQueue),Deferred=((IEnumerable)Field(queue,"Deferred")).Cast<object>().Select(Issuance).ToArray(),Completion=Field(queue,"Completion")},
                        Group=d.Navigation.GroupFor(victim),PendingRequest=Scalars(pendingRequest),AllocatingRequest=Scalars(allocatingRequest),
                        NativeOrder=At(Field(d.Navigation,"orders"),victim),NativeRetainedGoal=At(Field(d.Navigation,"retainedGoals"),victim),NativeReservation=At(Field(d.Navigation,"reservations"),victim),
                        Reservations=Rows(Field(d.Navigation,"reservations")),TechnicalFailure=At(Field(d.Navigation,"technicalFailures"),victim),
                        Tower=tower,NativeTower=Scalars(nativeTower),NativeTowerHealing=nativeTower==null?null:Scalars(Field(nativeTower,"Healing")),
                        Distances=new{PhysicalToTower=nativeActor==null?(double?)null:TowerDistance(nativeActor.Position),ClientGoalToTower=TowerDistance(Point(finalClient,"Goal")),CertificateGoalToTower=TowerDistance(Point(finalCertificate,"Goal")),
                            NativeGoalToTower=nativeActor==null?(double?)null:TowerDistance(nativeActor.Goal),ActiveAssignedToTower=TowerDistance(Assigned(active)),PendingAssignedToTower=TowerDistance(Assigned(pendingQueue)),RepairRange=p.ExplorerRange*p.TurretRangeMultiplier},
                        ActualAppliedRecords=appliedRecords.Values.OrderBy(r=>r.CommandSequence).ToArray(),RetainedOwnerRecords=loop.Checkpoint.Records.ToArray(),
                        OwnerPending=((IEnumerable)Field(loop,"pendingItems")).Cast<object>().Select(item=>new{Values=Scalars(item),Action=Field(item,"Action"),Intent=Field(item,"Intent")}).ToArray(),
                        ArmyRegistry=registry.Capture(),Mission=planner.Capture(),Worker=new{worker.Publication.Pumps,worker.Publication.Failure}}));
                }
                if(repairTower)Assert.True(repairApplied&&repairMotion&&towerLocked&&healed,"Strongly wounded withdrawal must convert to a real tower approach, lock and90percent healing before return");
                var explorer=authority.ParticipantView("player-1").Entities.Single(u=>u.Id==heldExplorer.Id);Assert.True(explorer.Held);Assert.AreEqual(PlayableOrderOrigin.Human,explorer.OrderStamp.Origin);
                Assert.True(applied,"Observed real focus must issue an Applied micro withdrawal for an army member");
                Assert.True(moved,"Applied command must cause physical movement away from attackers through the ordinary threaded host");
                Assert.True(returned,"After focus clears, unit physically returns toward a current viable friendly core");
                int burst=(int)ai.DifficultyValue(AiDifficulty.Veteran,"control.burst");double rate=ai.DifficultyValue(AiDifficulty.Veteran,"control.ordersPerMinute");
                Assert.LessOrEqual(appliedRecords.Count,burst+Math.Floor(d.Tick*rate/1800));
                Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                if(selectedProfile!=null)TestContext.WriteLine("MOTION_FINAL "+Newtonsoft.Json.JsonConvert.SerializeObject(new{d.Tick,Publications=d.CombatMotionAuthority?.Publications,AffectedPublications=d.CombatMotionAuthority?.AffectedPublications,Refusal=d.CombatMotionAuthority?.LastRefusal,NavRefusal=d.Navigation.LastOwnedMovementRefusalMessage,NavNull=d.Navigation.LastOwnedMovementNullDetail,NavStage=d.Navigation.LastOwnedMovementRefusal,ResultFailure=d.Navigation.LastResultBatchFailure,Worker=worker?.Publication.Pumps,Records=loop.Checkpoint.Records,Clients=Field(Field(loop,"combatMicro"),"clients")}));
                TestContext.WriteLine(string.Join("\n",samples));
                if(failurePayload!=null)TestContext.WriteLine("HQ_FAILURE "+Newtonsoft.Json.JsonConvert.SerializeObject(failurePayload));
                if(pendingCold!=null){pendingCold.Stop();pendingColdWorker?.Dispose();Assert.True(pendingColdWorker==null||SpinWait.SpinUntil(()=>pendingColdWorker.IsStopped,5000));pendingCold.Domain.Navigation.ReleaseCommittedHostRoot();pendingCold.Domain.DestroyOwnedNavigation();Assert.Zero(pendingCold.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(pendingCold.Domain.Navigation.PhysicalHostBudget.HeldBytes);}
                worker?.Dispose();bool stopped=worker==null||SpinWait.SpinUntil(()=>worker.IsStopped,5000);
                if(coreObserverInstalled){
                    bool owned=ReferenceEquals(PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests,ownedCoreObserver);
                    if(owned){PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;}
                    try{
                        System.IO.Directory.CreateDirectory(coreOutput);
                        System.IO.File.WriteAllText(System.IO.Path.Combine(coreOutput,"core.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Schema=1,Seed=seed,Generation=generation,Capacity=2048,Frames=coreFrames,Dropped=coreDropped,Errors=coreErrors,FirstError=coreError,WorkerStopped=stopped,ObserverOwned=owned,
                            Coverage="All successful authority frames and all actual pre-decisions, including before heal/readiness. Same source private pure ReturnCore/Matches readbacks; no Plan, world projection, route query, step or loop IO. Missing/racy guard cause remains explicit; observer may perturb scheduling."}));
                        TestContext.WriteLine("CORE_TRACE "+coreOutput+" frames="+coreFrames.Count+" dropped="+coreDropped+" errors="+coreErrors);
                    }catch(Exception error){TestContext.WriteLine("CORE_TRACE_WRITE_ERROR "+error);}
                }
                Assert.True(stopped,"Owned worker must stop before navigation destruction");
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
