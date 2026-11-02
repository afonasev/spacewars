using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CombatMicroAirRepairThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [Test,Timeout(90000)] public void HawkRepairUsesActualAirPublicationAndExactCurrentContinuation()=>Run();
        [Test,Timeout(90000)] public void RealPendingAirCurrentRebindPreservesHumanHoldAndCancelsStaleAdoption()=>Run(true);
        [Test,Timeout(90000)] public void FirstOrdinaryHawkMoveHasBoundedCausalAttribution()=>Run(false,true);
        [Test,Timeout(90000)] public void OffGuardTowerHawkRepairUsesActualAirPublicationAndExactCurrentContinuation()=>Run(separatedTower:true);
        [Test,Timeout(90000)] public void OffGuardTowerPendingAirCurrentPreservesHumanHold()=>Run(pendingHumanControl:true,separatedTower:true);
        void Run(bool pendingHumanControl=false,bool attributionControl=false,bool separatedTower=false)
        {
            const int seed=7;const long generation=906;var data=PlayableProfile.Default.CopyData();data.groundRouteBackend=1;var p=PlayableProfile.Create(data);var ai=AiProfile.Initial;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,21),new NavPoint(25,27),pin:2)};
            var hawkAt=new NavPoint(-16,15);
            var own=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            if(separatedTower){
                // A lawful authored base whose defensive ring is outside its HQ's
                // healing envelope. Ordinary Guard still owns its actual HQ goal;
                // reaching that goal cannot stand in for a micro repair flight.
                starts[0]=new OfflineStart("west",1,new NavPoint(-12,0),new NavPoint(-18,-20),pin:1);
                hawkAt=new NavPoint(-8,5);
                double ring=p.ExplorerRange*p.TurretRangeMultiplier+2*p.TurretFootprintRadius;
                own=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,
                    own.Slots.Select(slot=>new TerritorySlot(slot.Id,new NavPoint(starts[0].Position.X+Math.Cos(slot.Heading)*ring,starts[0].Position.Z+Math.Sin(slot.Heading)*ring),slot.Heading)).ToArray());
            }
            var pad=separatedTower?own.DefenseSlots(p).OrderByDescending(s=>Distance(s.Position,hawkAt)).First():own.DefenseSlots(p).Single(s=>s.Id==1003);
            if(separatedTower)Assert.Greater(Distance(starts[0].Position,pad.Position),p.ExplorerRange*p.TurretRangeMultiplier+PlayableUnitRules.Radius(p,PlayableEntityKind.Hawk),"The ordinary Guard goal cannot heal this Hawk.");
            var config=new OfflineMatchConfiguration(p,separatedTower?"combat-micro-hawk-offguard-repair-v1":"combat-micro-hawk-repair-v1",separatedTower?"flat-offguard-tower-seed7":"flat-seed7","ordinary-certified-threaded-air-repair",seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,
                new[]{own,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},
                scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)});
            var d=new PlayableDomain(p,generation,config);
            // Existing Native midgame spawn, as in HawkMatchStateTests. The
            // ordinary lobby/scenario roster remains unchanged and ground-only.
            typeof(PlayableDomain).GetMethod("SpawnUnit",F).Invoke(d,new object[]{hawkAt,PlayableOwner.Player,PlayableEntityKind.Hawk});
            var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe,aiProfile:ai),generation,null,ai,AiDifficulty.Veteran);
            var executor=Field(loop,"openingExecutor");executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);executor.GetType().GetField("initialForceFulfilled",F).SetValue(executor,true);foreach(var key in new[]{"income","factory","force","pressure"})((HashSet<string>)Field(executor,"completed")).Add(key);
            var scheduler=new AiAuthorityScheduler(new[]{loop});var authority=new PlayableAuthorityTick(d,scheduler,seed,config);CertifiedRouteWorker worker=null,coldWorker=null;PlayableAuthorityTick cold=null;
            string output=Environment.GetEnvironmentVariable("COMBAT_MICRO_AIR_OUTPUT")??Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../.local/ai-combat-attention/air-consumer/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")));
            if(separatedTower)output=Path.Combine(output,"offguard-tower");
            if(pendingHumanControl)output=Path.Combine(output,"human-pending-current");if(attributionControl)output=Path.Combine(output,"first-ordinary-attribution");
            var samples=new List<object>();byte[] saved=null;long applied=-1,motion=-1,healed=-1,released=-1,captured=-1;double initialHealth=0,maxHealth=0;bool towerLock=false,currentExact=false;
            try{
                var initial=authority.ParticipantView("player-1");var patient=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Hawk);int id=patient.Id;var explorer=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Explorer);
                Assert.Greater(Distance(patient.Position,pad.Position),p.ExplorerRange*p.TurretRangeMultiplier,"Real flight is required before healing.");
                int damage=checked((int)Math.Ceiling(patient.MaxHealth*.72));typeof(PlayableDomain).GetMethod("Damage",F).Invoke(d,new object[]{id,damage});initialHealth=d.CombatExecutionActor(id).ExactHealth.Value;maxHealth=initialHealth;Assert.AreEqual(patient.MaxHealth-damage,initialHealth);Assert.Greater(initialHealth,0);Assert.Less(initialHealth/patient.MaxHealth,ai.DifficultyValue(AiDifficulty.Veteran,"micro.idleRepairHealth"));
                // An independent actual human ground route exercises the worker
                // without adding an AI fighter and changing Guard redistribution.
                // After physical movement a fresh HumanHold retains final priority.
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,"player-1",separatedTower?PlayableCommandKind.Move:PlayableCommandKind.Hold,new[]{explorer.Id},separatedTower?new NavPoint(-8,-20):default,origin:PlayableOrderOrigin.Human)).Status);
                bool explorerHeld=!separatedTower;
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);var clock=Stopwatch.StartNew();NavPoint origin=patient.Position;
                if(pendingHumanControl){d.BindCombatMotionAuthority(()=>scheduler.Owners);d.CombatAirMotionAuthority.WorkGrantForTests=_=>0;}
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("player-1");var actor=view.Entities.Single(u=>u.Id==id);Assert.Greater(actor.Health,0);
                    if(!explorerHeld){
                        var ground=view.Entities.Single(u=>u.Id==explorer.Id);
                        if(Distance(ground.Position,explorer.Position)>1){
                            Assert.Greater(worker.Publication.Pumps,0,"Human ground motion came through the actual threaded host.");
                            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,d.AllocateAiSequence(),"player-1",PlayableCommandKind.Hold,new[]{explorer.Id},origin:PlayableOrderOrigin.Human)).Status);
                            explorerHeld=true;
                        }
                    }
                    if(attributionControl&&actor.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&actor.OrderStamp.Kind==PlayableCommandKind.Move){
                        var receipt=loop.Checkpoint.Records.SingleOrDefault(r=>r.Status==PlayableAiDeliveryStatus.Applied&&r.CommandSequence==actor.OrderStamp.Sequence);
                        Assert.NotNull(receipt);Assert.AreNotEqual(AiCombatMicroPolicy.Policy,receipt.Policy);Assert.Zero(d.CombatAirMotionAuthority.AffectedPublications);
                        var pending=((IEnumerable)Field(loop,"pendingItems")).Cast<object>().Select(item=>new{Policy=(string)Field(item,"PolicyName"),Action=(PlayableAiAction)Field(item,"Action")}).ToArray();
                        TestContext.WriteLine("AIR_ORDINARY_ATTRIBUTION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{d.Tick,Receipt=receipt,actor.Position,actor.CurrentOrder,actor.OrderStamp,MicroProposed=Field(micro,"proposed"),MicroPending=Field(micro,"pending"),Clients=Field(micro,"clients"),Registry=((AiArmyRegistry)Field(loop,"armies")).Capture(),Pending=pending}));
                        return; // Causal attribution only; original positive stays FAIL.
                    }
                    if(pendingHumanControl&&d.CombatAirMotionAuthority.HasPending){
                        Assert.True(explorerHeld,"Independent human ground motion and final Hold must complete before pending Air current control.");
                        Assert.Zero(d.CombatAirMotionAuthority.AffectedPublications);Assert.Greater(d.AirOperationWatermark,0);long operation=d.AirOperationWatermark;
                        Assert.True(loop.Checkpoint.Records.Any(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.CommandSequence==actor.OrderStamp?.Sequence),"The actual accepted Air source belongs to the real Applied micro command.");
                        applied=d.Tick;
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,d.AllocateAiSequence(),"player-1",PlayableCommandKind.Hold,new[]{id},origin:PlayableOrderOrigin.Human)).Status);
                        Assert.True(d.CombatExecutionActor(id).Held);Assert.AreEqual(PlayableOrderOrigin.Human,d.CombatExecutionActor(id).OrderStamp.Origin);
                        saved=authority.CaptureBytes();captured=d.Tick;cold=PlayableAuthorityTick.RestoreBytes(saved,config);var restoredOwner=cold.Scheduler.Owners.Single(o=>o.OwnerId=="player-1");
                        Assert.True(cold.Domain.AwaitingAirSources);Assert.True(restoredOwner.AwaitingExecutionSources);CollectionAssert.AreEqual(saved,cold.CaptureBytes());Assert.Throws<InvalidOperationException>(()=>cold.Domain.Step(1d/30d));
                        restoredOwner.BindExecutionDomain(cold.Domain);cold.Domain.BindCombatMotionAuthority(()=>cold.Scheduler.Owners);
                        Assert.False(cold.Domain.AwaitingAirSources);Assert.False(restoredOwner.AwaitingExecutionSources);Assert.True(cold.Domain.CombatAirMotionAuthority.HasPending);CollectionAssert.AreEqual(saved,cold.CaptureBytes(),"Rebind preserves the exact old accepted Air promise despite current HumanHold.");
                        coldWorker=new CertifiedRouteWorker(cold.NavigationBinding,p.NavigationRequestsPerFrame);long deadline=captured+AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"decisionSeconds"),30);
                        while((cold.Tick<captured+2||restoredOwner.ReservesExecutionSubject(id))&&cold.Tick<=deadline&&clock.Elapsed.TotalSeconds<60){if(!cold.TryAdvance())Thread.Sleep(1);Assert.IsNull(coldWorker.Publication.Failure);}
                        cold.Domain.CaptureCombatMotionBoundary();Assert.Greater(cold.Tick,captured);Assert.False(cold.Domain.CombatAirMotionAuthority.HasPending);Assert.Zero(cold.Domain.CombatAirMotionAuthority.AffectedPublications,"A stale pre-Human turn/height step must not adopt any escape history.");
                        Assert.False(restoredOwner.ReservesExecutionSubject(id));Assert.Greater(cold.Domain.CombatAirMotionAuthority.Publications,0,"Actual ordinary held Air publication bypasses micro policy.");var held=cold.Domain.CombatExecutionActor(id);Assert.True(held.Held);Assert.AreEqual(PlayableOrderOrigin.Human,held.OrderStamp.Origin);Assert.AreEqual(PlayableCommandKind.Hold,held.OrderStamp.Kind);Assert.IsNull(held.CurrentOrder);
                        currentExact=true;released=cold.Tick;TestContext.WriteLine($"AIR_HUMAN_CURRENT operation={operation} tick={captured} bytes={saved.Length} immediateExact=True reboundExact=True humanHeld=True released={released} staleAffectedAdoptions=0 ordinaryAirPublications={cold.Domain.CombatAirMotionAuthority.Publications}");
                        return; // Dedicated actual pending/current control, not repair/heal acceptance.
                    }
                    var clients=(IDictionary)Field(micro,"clients");var client=clients.Contains(id)?clients[id]:null;
                    if(applied<0&&client!=null&&(AiCombatMicroPhase)Field(client,"Phase")==AiCombatMicroPhase.Repair&&(long)Field(client,"Sequence")>0){applied=d.Tick;origin=actor.Position;Assert.True(loop.Checkpoint.Records.Any(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.CommandSequence==actor.OrderStamp?.Sequence));}
                    if(separatedTower&&applied<0)Assert.AreEqual(initialHealth,actor.ExactHealth??actor.Health,"Ordinary Guard must not heal before the actual micro repair command.");
                    if(applied>=0&&motion<0&&Distance(actor.Position,origin)>1&&Distance(actor.Position,pad.Position)<Distance(origin,pad.Position)-1)motion=d.Tick;
                    towerLock|=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower).Healing?.TargetId==id;maxHealth=Math.Max(maxHealth,actor.ExactHealth??actor.Health);
                    if(saved==null&&motion>=0&&d.CombatAirMotionAuthority.AffectedPublications>0&&!d.CombatAirMotionAuthority.HasPending){
                        saved=authority.CaptureBytes();captured=d.Tick;using(var state=PlayableWorldState.Decode(saved)){Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(seed,state.Seed);Assert.AreEqual(generation,state.Generation);Assert.AreEqual(config.SourceIdentity,state.SourceIdentity);CollectionAssert.AreEqual(WorldWire.Binding(p),state.Binding);}
                        cold=PlayableAuthorityTick.RestoreBytes(saved,config);CollectionAssert.AreEqual(saved,cold.CaptureBytes());foreach(var owner in cold.Scheduler.Owners)owner.BindExecutionDomain(cold.Domain);cold.Domain.BindCombatMotionAuthority(()=>cold.Scheduler.Owners);CollectionAssert.AreEqual(saved,cold.CaptureBytes());coldWorker=new CertifiedRouteWorker(cold.NavigationBinding,p.NavigationRequestsPerFrame);
                    }
                    if(coldWorker!=null&&d.Tick>captured&&d.Tick<=captured+45){
                        while(cold.Tick<d.Tick&&clock.Elapsed.TotalSeconds<60){if(!cold.TryAdvance())Thread.Sleep(1);Assert.IsNull(coldWorker.Publication.Failure);}Assert.AreEqual(d.Tick,cold.Tick);
                        if(d.Tick==captured+45){CollectionAssert.AreEqual(authority.CaptureBytes(),cold.CaptureBytes(),"Exact full actual Hawk repair continuation for45 ticks.");currentExact=true;}
                    }
                    if(healed<0&&PlayableUnitRules.HealthRatio(p,actor)>=ai.DifficultyValue(AiDifficulty.Veteran,"micro.repairReturnHealth"))healed=d.Tick;
                    if(healed>=0&&currentExact&&!micro.Reserves(id)){released=d.Tick;break;}
                    if(d.Tick%30==0)samples.Add(new{d.Tick,actor.Position,actor.Altitude,actor.Health,actor.ExactHealth,actor.CurrentOrder,actor.OrderStamp,Reserved=micro.Reserves(id),Publications=d.CombatAirMotionAuthority.Publications,Affected=d.CombatAirMotionAuthority.AffectedPublications,Refusal=d.CombatAirMotionAuthority.LastRefusal});
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"AIR_REPAIR seed7 gen906 tick={d.Tick} applied={applied} motion={motion} towerLock={towerLock} health={initialHealth:R}->{maxHealth:R} heal={healed} release={released} currentTick={captured} current45Exact={currentExact} publications={d.CombatAirMotionAuthority?.Publications} affected={d.CombatAirMotionAuthority?.AffectedPublications} departure={d.CombatAirMotionAuthority?.DeparturePublications} pumps={worker.Publication.Pumps}");
                Assert.GreaterOrEqual(applied,0);Assert.GreaterOrEqual(motion,applied);Assert.True(towerLock);Assert.Greater(maxHealth,initialHealth);Assert.GreaterOrEqual(healed,motion);Assert.GreaterOrEqual(released,healed);Assert.LessOrEqual(released-healed,AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"decisionSeconds"),30));Assert.True(currentExact);Assert.Greater(d.CombatAirMotionAuthority.AffectedPublications,0);
                var human=authority.ParticipantView("player-1").Entities.Single(u=>u.Id==explorer.Id);Assert.True(human.Held);Assert.AreEqual(PlayableOrderOrigin.Human,human.OrderStamp.Origin);Assert.Greater(worker.Publication.Pumps,0);
            }finally{
                Directory.CreateDirectory(output);if(saved!=null)File.WriteAllBytes(Path.Combine(output,pendingHumanControl?"actual-hawk-pending-human.world":"actual-hawk-repair-current.world"),saved);File.WriteAllText(Path.Combine(output,"actual-hawk-repair.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Source=pendingHumanControl?"Actual accepted Air pending source then HumanHold/current rebind/cancel; not repair acceptance":"Actual threaded AI/Native Air; scenario Hawk starts at cruise, not hangar departure acceptance",Scene=config.MapIdentity,GuardGoal=starts[0].Position,TowerGoal=pad.Position,HawkOrigin=hawkAt,Tick=d.Tick,applied,motion,towerLock,initialHealth,maxHealth,healed,released,captured,currentExact,Publications=d.CombatAirMotionAuthority?.Publications,Affected=d.CombatAirMotionAuthority?.AffectedPublications,Departure=d.CombatAirMotionAuthority?.DeparturePublications,Refusal=d.CombatAirMotionAuthority?.LastRefusal,Records=loop.Checkpoint.Records,Samples=samples},Newtonsoft.Json.Formatting.Indented));
                coldWorker?.Dispose();if(coldWorker!=null)Assert.True(SpinWait.SpinUntil(()=>coldWorker.IsStopped,5000));if(cold!=null){cold.Stop();cold.Domain.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Domain.Navigation.PhysicalHostBudget.HeldBytes);}
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));authority.Stop();d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
