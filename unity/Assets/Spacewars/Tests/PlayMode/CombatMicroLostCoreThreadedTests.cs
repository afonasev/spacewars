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
    public sealed class CombatMicroLostCoreThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        [Test,Timeout(90000)]
        public void LostSelectedTargetRevokesOldArmyBeforeReturnAndNeverRestoresItsAuthority()
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
            var samples=new List<string>();
            try
            {
                var initial=authority.ParticipantView("player-1");var tanks=initial.Entities.Where(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                int victim=tanks[0].Id;var origin=tanks[0].Position;
                var enemies=authority.ParticipantView("enemy-1").Entities.Where(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).ToArray();
                var planner=(AiArmyPlanner)Field(loop,"armyPlanner");var registry=(AiArmyRegistry)Field(loop,"armies");var o=PlayableAiObservation.From(initial);
                var openingAttack=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,generation,o.SnapshotSequence,PlayableCommandKind.Attack,tanks.Select(u=>u.Id).ToArray(),enemies[0].Position,targetId:enemies[0].Id,seed:seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                planner.Commit(o,openingAttack,registry);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,o.OwnerId,openingAttack.Kind,openingAttack.CopyEntityIds(),openingAttack.Target,targetId:openingAttack.TargetId,origin:PlayableOrderOrigin.Ai,source:openingAttack.SourceIdentity,jobId:1,actionId:1)).Status);
                planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,0,1,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"authored midgame order",o.OwnerId,openingAttack.SourceIdentity,new AiReceiptIdentity(generation,o.OwnerId,1,1),AiArmyPlanner.Policy,openingAttack.Kind));
                typeof(AiAuthorityScheduler).GetField("nextActionId",F).SetValue(scheduler,1L);
                long enemySequence=10;
                foreach(var enemy in enemies)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,enemySequence++,"enemy-1",PlayableCommandKind.Attack,new[]{enemy.Id},origin,targetId:victim)).Status);
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");long oldArmy=registry.ArmyFor(victim),safeSince=-1,disbandTick=-1;string disbandReason=null;bool revoked=false,staleFollow=false;
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool applied=false,moved=false,releasedFocus=false,returned=false;long firstApplied=-1,firstMotion=-1;NavPoint withdrawal=origin;
                var appliedRecords=new Dictionary<long,PlayableAiTraceRecord>();var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("player-1");var actor=view.Entities.FirstOrDefault(u=>u.Id==victim);Assert.NotNull(actor,"The withdrawn unit must survive the whole lost-core scenario");
                    var clientMap=(IDictionary)Field(micro,"clients");if(clientMap.Contains(victim)){long safe=(long)Field(clientMap[victim],"SafeSince");if(safe>=0)safeSince=safe;}
                    var oldGroup=registry.Capture().Armies.Single(a=>a.Id==oldArmy);
                    if(oldGroup.Phase==AiArmyPhase.Disbanded){if(disbandTick<0){disbandTick=d.Tick;disbandReason=planner.Capture().Reason;}Assert.False(micro.CurrentOverride(PlayableAiObservation.From(view),oldGroup,actor),"An exact old receipt cannot grant authority over a disbanded group");revoked=true;
                        var certificates=(IDictionary)Field(micro,"certificates");if(certificates.Contains(victim))staleFollow|=(long)Field(certificates[victim],"Army")==oldArmy&&(PlayableCommandKind)Field(certificates[victim],"Kind")==PlayableCommandKind.Follow;
                    }
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
                Assert.True(applied,"Observed real focus must issue an Applied micro withdrawal for an army member");
                Assert.True(moved,"Applied command must cause physical movement away from attackers through the ordinary threaded host");
                TestContext.WriteLine($"LOST_CORE oldArmy={oldArmy} safeSince={safeSince} disband={disbandTick} reason={disbandReason} revoked={revoked} staleFollow={staleFollow}");
                Assert.GreaterOrEqual(safeSince,0);Assert.Greater(disbandTick,firstMotion);Assert.Less(disbandTick,safeSince+AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"micro.safeSeconds"),30),"Old objective must actually disappear before safe-return eligibility");
                StringAssert.Contains("target not at remembered visible position",disbandReason);Assert.True(revoked);Assert.False(staleFollow,"Never issue a Follow certified by the lost old army");Assert.False(returned,"This negative scenario has no original current core to rejoin; live-core positives are separate companions");
                int burst=(int)ai.DifficultyValue(AiDifficulty.Veteran,"control.burst");double rate=ai.DifficultyValue(AiDifficulty.Veteran,"control.ordersPerMinute");
                Assert.LessOrEqual(appliedRecords.Count,burst+Math.Floor(d.Tick*rate/1800));
                Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
