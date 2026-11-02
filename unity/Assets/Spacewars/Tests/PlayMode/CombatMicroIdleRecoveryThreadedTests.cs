using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class CombatMicroIdleRecoveryThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [Test,Timeout(90000)]
        public void IdleWoundedUsesActualTowerAndReleasesAtHealthThreshold()
        {
            const int seed=7;const long gen=905;var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-21,-5),new NavPoint(-11,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,-5),new NavPoint(25,25),pin:2)};
            var ownSite=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);var towerPad=ownSite.DefenseSlots(p).Single(slot=>slot.Id==1005);var towerAt=towerPad.Position;
            var sites=new[]{ownSite,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var config=new OfflineMatchConfiguration(p,"combat-micro-idle-repair-v1","idle-repair-flat-seed7","ordinary-certified-threaded",seed,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-30,-10))},scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,towerPad.Id,towerPad.Position,towerPad.Heading)});
            var d=new PlayableDomain(p,gen,config);var loop=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(seed,"player-1",forcedOpening:PlayableAiOpening.GreedySafe,aiProfile:ai),gen,null,ai,AiDifficulty.Veteran);
            var executor=Field(loop,"openingExecutor");executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);executor.GetType().GetField("initialForceFulfilled",F).SetValue(executor,true);
            foreach(var key in new[]{"income","factory","force","pressure"})((HashSet<string>)Field(executor,"completed")).Add(key);
            var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(new[]{loop}),seed,config);CertifiedRouteWorker worker=null;var samples=new List<string>();
            try
            {
                var initial=authority.ParticipantView("player-1");var patient=initial.Entities.Single(u=>u.Kind==PlayableEntityKind.Tank&&u.Owner==initial.Owner);var explorer=initial.Entities.Single(u=>u.Kind==PlayableEntityKind.Explorer&&u.Owner==initial.Owner);
                // Initial wounded state through native damage; no health writes,
                // artificial healing or policy changes after the first game tick.
                typeof(PlayableDomain).GetMethod("Damage",F).Invoke(d,new object[]{patient.Id,80});Assert.AreEqual(20,authority.ParticipantView("player-1").Entities.Single(u=>u.Id==patient.Id).Health);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(gen,1,"player-1",PlayableCommandKind.Hold,new[]{explorer.Id},origin:PlayableOrderOrigin.Human)).Status);
                Assert.AreEqual(gen,initial.Generation);Assert.AreEqual(p.ProfileId,initial.ProfileId);Assert.True(d.Geometry.IsFree(patient.Position,PlayableUnitRules.Radius(p,patient.Kind)));
                TestContext.WriteLine($"CURRENT world={PlayableWorldState.CurrentVersion} owner={AiStateWire.Version} observation={PlayableAiObservation.From(initial).SchemaVersion} command={PlayableCommand.CurrentSchemaVersion} profile={p.ProfileId}@{p.Revision} ai={ai.Id}@{ai.Revision} hash={ai.Hash} source={config.SourceIdentity} map={config.MapIdentity}");
                var micro=(AiCombatMicroPolicy)Field(loop,"combatMicro");worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool applied=false,moved=false,locked=false,healed=false,released=false;long appliedTick=-1,healTick=-1,releaseTick=-1;NavPoint repairOrigin=patient.Position;double startHp=20,maxHp=20;
                var clock=Stopwatch.StartNew();
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);var view=authority.ParticipantView("player-1");var actor=view.Entities.Single(u=>u.Id==patient.Id);var clients=(IDictionary)Field(micro,"clients");var client=clients.Contains(patient.Id)?clients[patient.Id]:null;
                    if(!applied&&client!=null&&(AiCombatMicroPhase)Field(client,"Phase")==AiCombatMicroPhase.Repair&&(long)Field(client,"Sequence")>0)
                    {applied=true;appliedTick=d.Tick;repairOrigin=actor.Position;startHp=actor.ExactHealth??actor.Health;Assert.True(loop.Checkpoint.Records.Any(r=>r.Policy==AiCombatMicroPolicy.Policy&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));}
                    moved|=applied&&Distance(actor.Position,repairOrigin)>1&&Distance(actor.Position,towerAt)<Distance(repairOrigin,towerAt)-1;
                    locked|=view.Buildings.Any(b=>b.Kind==PlayableBuildingKind.RepairTower&&b.Healing?.TargetId==patient.Id);maxHp=Math.Max(maxHp,actor.ExactHealth??actor.Health);
                    if(maxHp>=90&&!healed){healed=true;healTick=d.Tick;}
                    if(applied&&healed&&!micro.Reserves(patient.Id)){released=true;releaseTick=d.Tick;break;}
                    if(d.Tick%30==0)samples.Add($"tick={d.Tick} hp={actor.ExactHealth??actor.Health:R} order={actor.CurrentOrder?.Kind} reserve={micro.Reserves(patient.Id)} tower={view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower).Healing?.TargetId}");
                    Thread.Sleep(1);
                }
                TestContext.WriteLine(string.Join("\n",samples));TestContext.WriteLine($"IDLE_REPAIR seed7 gen905 tick={d.Tick} applied={appliedTick} heal={healTick} release={releaseTick} hp={startHp:R}→{maxHp:R} moved={moved} lock={locked} pumps={worker.Publication.Pumps}");
                Assert.True(applied&&moved&&locked&&healed&&released,"Idle wounded must physically approach, receive real tower healing and release micro at threshold");
                Assert.LessOrEqual(releaseTick-healTick,AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"decisionSeconds"),30),"Release on next actual review, not maxPhase timeout");
                var held=authority.ParticipantView("player-1").Entities.Single(u=>u.Id==explorer.Id);Assert.True(held.Held);Assert.AreEqual(PlayableOrderOrigin.Human,held.OrderStamp.Origin);Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}
