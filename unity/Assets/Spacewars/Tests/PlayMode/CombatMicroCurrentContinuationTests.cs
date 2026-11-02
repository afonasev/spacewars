using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
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
    public sealed class CombatMicroCurrentContinuationTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        [Test,Timeout(90000)]
        public void CurrentFullAuthorityContinuesExactlyAcrossLegitimateOldTargetLoss()
        {
            const int seed=7;const long generation=901;var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-25,-5),new NavPoint(-20,-5),pin:1),new OfflineStart("east",2,new NavPoint(24,-5),new NavPoint(19,-5),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-16,15)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20,19)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20,11)),
                new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-7,12),Math.PI),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-6.5,15),Math.PI),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-7,18),Math.PI)};
            var config=new OfflineMatchConfiguration(p,"combat-micro-control-v1","micro-flat-seed7","ordinary-certified-threaded",seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario);
            var d=new PlayableDomain(p,generation,config);
            var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe,aiProfile:ai);
            var loop=new PlayableAiOwnerLoop(p,opening,generation,null,ai,AiDifficulty.Veteran);
            // Authored midgame setup follows the same complete opening milestone contract.
            var executor=Field(loop,"openingExecutor");executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);
            executor.GetType().GetField("initialForceFulfilled",F).SetValue(executor,true);
            foreach(var key in new[]{"income","factory","force","pressure"})((HashSet<string>)Field(executor,"completed")).Add(key);
            var scheduler=new AiAuthorityScheduler(new[]{loop});var authority=new PlayableAuthorityTick(d,scheduler,seed,config);CertifiedRouteWorker worker=null;
            var samples=new List<string>();var frames=new List<object>();int dropped=0;int victimId=0;long lastBefore=-1;bool advancingCold=false;
            PlayableAuthorityTick cold=null;CertifiedRouteWorker coldWorker=null;object captureBinding=null;
            byte[] currentBytes=null;string captureResult="NotRun",captureError=null;bool immediateExact=false,continuedExact=false;
            var previousDecision=PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests;
            var previousDecisionFilter=PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests;
            var previousPreselection=PlayableAiOwnerLoop.PreselectionReadbackForTests;
            var previousPreselectionFilter=PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests;
            Dictionary<string,object> Scalars(object value)=>value==null?null:value.GetType().GetFields(F).Where(f=>f.FieldType.IsValueType||f.FieldType==typeof(string)).ToDictionary(f=>f.Name,f=>f.GetValue(value));
            object[] Map(object dictionary)=>((IDictionary)dictionary).Keys.Cast<int>().OrderBy(id=>id).Select(id=>(object)new{Id=id,Values=Scalars(((IDictionary)dictionary)[id])}).ToArray();
            void Capture(string stage,PlayableAiObservation view)
            {
                if(view.Tick>180||victimId==0)return;if(frames.Count>=120){dropped++;return;}
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");var registry=(AiArmyRegistry)Field(loop,"armies");
                var actor=view.Entities.FirstOrDefault(u=>u.Id==victimId);var groups=registry.Capture().Armies;
                var clientMap=(IDictionary)Field(micro,"clients");var client=clientMap.Contains(victimId)?clientMap[victimId]:null;
                long army=client==null?registry.ArmyFor(victimId):(long)Field(client,"Army");var group=groups.FirstOrDefault(g=>g.Id==army);
                var core=actor==null?Array.Empty<PlayableEntitySnapshot>():(PlayableEntitySnapshot[])typeof(AiCombatMicroPolicy).GetMethod("ReturnCore",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{view,p,ai,AiDifficulty.Veteran,registry,actor,army});
                var proposed=(AiCombatMicroPlan)Field(micro,"proposed");var mission=((AiArmyPlanner)Field(loop,"armyPlanner")).Capture();
                frames.Add(new{Stage=stage,view.Tick,Actor=actor==null?null:new{actor.Id,actor.Health,actor.ExactHealth,actor.Position,actor.NavigationOutcome,actor.CurrentOrder,actor.OrderStamp,Queue=actor.Queue==null?null:new{actor.Queue.Active,actor.Queue.Pending,Deferred=actor.Queue.Deferred.ToArray(),actor.Queue.Completion},actor.TargetId},
                    Own=view.Entities.Where(u=>u.Owner==view.Owner).Select(u=>new{u.Id,u.Health,u.Position,u.CurrentOrder,u.OrderStamp}).ToArray(),
                    ObservedEnemy=view.Entities.Where(u=>view.IsHostile(u.Owner)).Select(u=>new{u.Id,u.Health,u.Position,u.Moving}).ToArray(),
                    Groups=groups.Select(g=>new{g.Id,g.OwnerId,g.Phase,g.TacticalOwner,Members=g.Members.ToArray(),Reinforcements=g.Reinforcements.ToArray()}).ToArray(),
                    Mission=Scalars(mission),Clients=Map(clientMap),Certificates=Map(Field(micro,"certificates")),Samples=Map(Field(micro,"samples")),
                    CurrentOverride=actor!=null&&micro.CurrentOverride(view,group,actor),CoreIds=core.Select(u=>u.Id).ToArray(),
                    Proposed=proposed==null?null:new{proposed.Army,proposed.Phase,proposed.Survival,proposed.Action.Kind,Ids=proposed.Action.CopyEntityIds(),proposed.Action.TargetId,proposed.Action.Target},
                    Pending=((IEnumerable)Field(loop,"pendingItems")).Cast<object>().Select(item=>new{Policy=(string)Field(item,"PolicyName"),Local=(long)Field(item,"LocalActionId"),Due=(long)Field(item,"DueTick"),Priority=((AiIntent)Field(item,"Intent")).Priority,Action=(PlayableAiAction)Field(item,"Action")}).ToArray(),
                    Records=loop.Checkpoint.Records.Select(r=>new{r.Policy,r.ActionId,r.Kind,r.Status,r.RuntimeStatus,r.ApplicationTick,r.CommandSequence,r.Message}).ToArray()});
            }
            PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="player-1";
            PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(owner,tick,view,groups,pending)=>{if(!advancingCold)Capture("after-army-before-micro-observe",view);};
            PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests="player-1";
            PlayableAiOwnerLoop.PreselectionReadbackForTests=readback=>{if(!advancingCold)Capture("after-micro-observe-before-micro-plan",readback.Observation);};
            try
            {
                var initial=authority.ParticipantView("player-1");var tanks=initial.Entities.Where(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                int victim=tanks[0].Id;victimId=victim;var origin=tanks[0].Position;
                var enemies=authority.ParticipantView("enemy-1").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                var planner=(AiArmyPlanner)Field(loop,"armyPlanner");var registry=(AiArmyRegistry)Field(loop,"armies");var o=PlayableAiObservation.From(initial);
                var openingAttack=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,generation,o.SnapshotSequence,PlayableCommandKind.Attack,tanks.Select(u=>u.Id).ToArray(),enemies[0].Position,targetId:enemies[0].Id,seed:seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                planner.Commit(o,openingAttack,registry);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,o.OwnerId,openingAttack.Kind,openingAttack.CopyEntityIds(),openingAttack.Target,targetId:openingAttack.TargetId,origin:PlayableOrderOrigin.Ai,source:openingAttack.SourceIdentity,jobId:1,actionId:1)).Status);
                planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,0,1,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"authored midgame order",o.OwnerId,openingAttack.SourceIdentity,new AiReceiptIdentity(generation,o.OwnerId,1,1),AiArmyPlanner.Policy,openingAttack.Kind));
                typeof(AiAuthorityScheduler).GetField("nextActionId",F).SetValue(scheduler,1L);
                long enemySequence=10;
                foreach(var enemy in enemies)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,enemySequence++,"enemy-1",PlayableCommandKind.Attack,new[]{enemy.Id},origin,targetId:victim)).Status);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool applied=false,moved=false,releasedFocus=false,returned=false;long firstApplied=-1,firstMotion=-1;NavPoint withdrawal=origin;
                var appliedRecords=new Dictionary<long,PlayableAiTraceRecord>();var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60)
                {
                    if(d.Tick>=30&&d.Tick<=180&&d.Tick%5==0&&lastBefore!=d.Tick){lastBefore=d.Tick;Capture("before-next-advance",PlayableAiObservation.From(authority.ParticipantView("player-1")));}
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    if(d.Tick>=30&&d.Tick<=180&&d.Tick%5==0)Capture("after-advance-and-plan",PlayableAiObservation.From(authority.ParticipantView("player-1")));
                    if(d.Tick==90&&captureResult=="NotRun")
                    {
                        // Current envelope and lifecycle only; rejected authored
                        // state is a control FAIL and cannot substitute a legacy import.
                        try{
                            currentBytes=authority.CaptureBytes();
                            using(var state=PlayableWorldState.Decode(currentBytes)){
                                Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(generation,state.Generation);Assert.AreEqual(seed,state.Seed);Assert.AreEqual(90,state.Tick);Assert.AreEqual(config.SourceIdentity,state.SourceIdentity);
                                CollectionAssert.AreEqual(WorldWire.Binding(p),state.Binding);
                                captureBinding=new{Envelope=state.Version,state.Generation,state.Tick,state.Seed,state.SourceIdentity,Map=config.MapIdentity,Profile=p.ProfileId,Revision=p.Revision,Ai=ai.Id,AiRevision=ai.Revision,AiHash=ai.Hash,OwnerSchema=AiStateWire.Version,ObservationSchema=PlayableAiObservation.CurrentSchemaVersion,RuntimeContractSource="52752775e",ProducerAssembly=typeof(AiCombatMicroPolicy).Assembly.ManifestModule.ModuleVersionId,CommandSchema=PlayableCommand.CurrentSchemaVersion};
                            }
                            cold=PlayableAuthorityTick.RestoreBytes(currentBytes,config);CollectionAssert.AreEqual(currentBytes,cold.CaptureBytes());immediateExact=true;
                            coldWorker=new CertifiedRouteWorker(cold.NavigationBinding,p.NavigationRequestsPerFrame);captureResult="ImmediateExactPass; continuation pending";
                        }
                        catch(Exception error){captureResult="FAIL";captureError=error.ToString();}
                    }
                    if(coldWorker!=null&&d.Tick>90&&d.Tick<=135&&captureError==null)
                    {
                        advancingCold=true;
                        try{
                            while(cold.Tick<d.Tick&&clock.Elapsed.TotalSeconds<60){if(!cold.TryAdvance())Thread.Sleep(1);Assert.IsNull(coldWorker.Publication.Failure);}
                            Assert.AreEqual(d.Tick,cold.Tick,"Bounded ordinary cold worker must reach the hot tick");
                            if(d.Tick==135){CollectionAssert.AreEqual(authority.CaptureBytes(),cold.CaptureBytes(),"Exact full current authority/Nav/owner continuation for45 ticks");continuedExact=true;captureResult="ImmediateAnd45TickExactPass";}
                        }
                        catch(Exception error){captureResult="FAIL";captureError=error.ToString();}
                        finally{advancingCold=false;}
                    }
                    Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("player-1");var actor=view.Entities.FirstOrDefault(u=>u.Id==victim);
                    foreach(var record in loop.Checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied))appliedRecords[record.CommandSequence]=record;
                    var receipt=loop.Checkpoint.Records.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(receipt!=null&&!applied){applied=true;firstApplied=receipt.ApplicationTick;}
                    if(applied&&actor!=null&&actor.Position.X<origin.X-1){moved=true;if(firstMotion<0)firstMotion=d.Tick;withdrawal=actor.Position;}
                    if(moved&&!releasedFocus)
                    {
                        // Scripted opponent stops focusing and leaves by ordinary human orders.
                        var remaining=authority.ParticipantView("enemy-1").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Health>0).Select(u=>u.Id).ToArray();
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,enemySequence++,"enemy-1",PlayableCommandKind.Move,remaining,new NavPoint(8,25))).Status);releasedFocus=true;
                    }
                    var follow=loop.Checkpoint.Records.FirstOrDefault(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Follow&&r.Status==PlayableAiDeliveryStatus.Applied);
                    if(follow!=null&&actor!=null&&Math.Sqrt(Math.Pow(actor.Position.X-withdrawal.X,2)+Math.Pow(actor.Position.Z-withdrawal.Z,2))>1){returned=true;break;}
                    if(d.Tick%30==0)samples.Add($"tick={d.Tick} hp={actor?.Health} pos={actor?.Position.X:F2},{actor?.Position.Z:F2} order={actor?.CurrentOrder?.Kind} attention={loop.CombatAttention.Applied} pending={loop.Checkpoint.PendingActionId} policy={loop.Checkpoint.LastPolicy}");
                    Thread.Sleep(1);
                }
                TestContext.WriteLine(string.Join("\n",samples));
                TestContext.WriteLine($"MICRO seed7 gen901 tick={d.Tick} Applied={firstApplied} Motion={firstMotion} withdrawn={moved} returned={returned} pumps={worker.Publication.Pumps} receipts={appliedRecords.Count}");
                Assert.True(immediateExact&&continuedExact,"Current full authority capture and45tick cold continuation must be exact; failure is preserved without fallback: "+captureError);
                Assert.True(applied,"Observed real focus must issue an Applied micro withdrawal for an army member");
                Assert.True(moved,"Applied command must cause physical movement away from attackers through the ordinary threaded host");
                // The frozen original positive was invalidated by a proven lost
                // target. Keep its failure in Git/evidence; assert the actual
                // negative premise alongside the separate live-core positives.
                var observed=Newtonsoft.Json.Linq.JArray.Parse(JsonConvert.SerializeObject(frames));
                Assert.True(observed.Any(f=>(long)f["Tick"]==150&&(bool)f["CurrentOverride"]&&f["CoreIds"].Count()==2));
                Assert.True(observed.Any(f=>(long)f["Tick"]==165&&(string)f["Mission"]["Reason"]=="target not at remembered visible position; whereabouts unconfirmed"&&!(bool)f["CurrentOverride"]&&f["CoreIds"].Count()==0));
                Assert.False(returned,"No stale old-army Follow; separate living-core companions require actual physical return");
                int burst=(int)ai.DifficultyValue(AiDifficulty.Veteran,"control.burst");double rate=ai.DifficultyValue(AiDifficulty.Veteran,"control.ordersPerMinute");
                Assert.LessOrEqual(appliedRecords.Count,burst+Math.Floor(d.Tick*rate/1800));
                Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=previousDecision;
                PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=previousDecisionFilter;
                PlayableAiOwnerLoop.PreselectionReadbackForTests=previousPreselection;
                PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=previousPreselectionFilter;
                var path=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"..","..",".local","ai-combat-attention","diagnostic-6d3"));Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path,"frames.json"),JsonConvert.SerializeObject(new{Frames=frames.ToArray(),Dropped=dropped,CaptureResult=captureResult,CaptureError=captureError,Binding=captureBinding,ImmediateExact=immediateExact,ContinuedExact=continuedExact},Formatting.Indented));
                if(currentBytes!=null)File.WriteAllBytes(Path.Combine(path,"current-authority.bin"),currentBytes);
                TestContext.WriteLine($"CAUSAL frames={frames.Count} dropped={dropped} capture={captureResult} evidence={path}");
                coldWorker?.Dispose();if(coldWorker!=null)Assert.True(SpinWait.SpinUntil(()=>coldWorker.IsStopped,5000));
                if(cold!=null){cold.Stop();cold.Domain.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.HeldBytes);}
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
