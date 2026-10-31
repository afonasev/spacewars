using System;
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
    public sealed class ExplorerBaseDefenseThreadedTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Field(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
        [Test,Timeout(120000)] public void NearbyAssignedExplorerReceivesDefenseCommandMovesAndFiresThroughThreadedHost()
        {
            const int seed=19092026;const long generation=81;
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-4,0),new NavPoint(-13,5),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-13,-5)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(7,0))};
            var config=new OfflineMatchConfiguration(p,"explorer-base-defense","local-base","certified-threaded-host",seed,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario);
            var domain=new PlayableDomain(p,generation,config);
            var owner=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(seed,"west-owner",forcedOpening:PlayableAiOpening.GreedySafe),generation);
            var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(new[]{owner}),seed,config);CertifiedRouteWorker worker=null;
            try
            {
                var executor=Field(owner,"openingExecutor");
                // Authored midgame precondition, not a new opening policy: the scout
                // assignment exists before this local attack and must survive proposal.
                executor.GetType().GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);
                var initial=authority.ParticipantView("west-owner");var scout=initial.Entities.Single(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Explorer);
                var registry=(AiArmyRegistry)Field(owner,"armies");
                Assert.True(registry.TryCreate(PlayableAiObservation.From(initial),AiArmyRole.Scout,AiScoutPlanner.Policy,new[]{scout.Id},out var scoutArmy));
                var objective=initial.PublicScoutObjectives.First(t=>t.Reachable&&t.SiteId!=1);
                registry.UpdateMission(scoutArmy,AiScoutPlanner.Policy,AiArmyPhase.Advancing,0,"survey:"+objective.SiteId,rally:objective.Approach);
                var hq=initial.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                var enemy=authority.ParticipantView("east-owner").Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,1,"east-owner",PlayableCommandKind.Attack,new[]{enemy.Id},default,targetId:hq.Id)).Status);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                bool commanded=false,moved=false,fired=false,handoff=false;long commandTick=-1,motionTick=-1,shotTick=-1;
                var clock=Stopwatch.StartNew();
                while(authority.Tick<1800&&clock.Elapsed.TotalSeconds<90)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);
                    var view=authority.ParticipantView("west-owner");var u=view.Entities.FirstOrDefault(e=>e.Id==scout.Id);
                    bool defenseOrder=u?.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Kind==PlayableCommandKind.Attack&&u.CurrentOrder?.TargetId==enemy.Id;
                    if(defenseOrder){commanded=true;if(commandTick<0)commandTick=authority.Tick;}
                    if(commanded&&u!=null&&Math.Sqrt(Math.Pow(u.Position.X-scout.Position.X,2)+Math.Pow(u.Position.Z-scout.Position.Z,2))>p.ExplorerCollisionRadius){moved=true;if(motionTick<0)motionTick=authority.Tick;}
                    if(commanded&&view.Projectiles.Any(x=>x.OwnerId==scout.Id&&x.TargetId==enemy.Id)){fired=true;if(shotTick<0)shotTick=authority.Tick;}
                    var armies=registry.Capture().Armies;handoff|=armies.Any(a=>a.Role==AiArmyRole.MobileDefense&&a.TacticalOwner==AiDefensePlanner.Policy&&a.Members.Contains(scout.Id))&&armies.Single(a=>a.Id==scoutArmy).Phase==AiArmyPhase.Disbanded;
                    if(commanded&&moved&&fired&&handoff)break;
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"EXPLORER_DEFENSE seed={seed} tick={authority.Tick} command={commandTick} motion={motionTick} shot={shotTick} handoff={handoff} pumps={worker.Publication.Pumps}");
                Assert.True(commanded,"actual owner-loop defense Attack must reach the Explorer");
                Assert.True(handoff,"Scout assignment must yield to an actual MobileDefense army");
                Assert.True(moved,"Explorer must physically move after the defense command");Assert.True(fired,"Explorer must fire at the visible invader");
                Assert.Greater(worker.Publication.Pumps,0);Assert.IsNull(worker.Publication.Failure);registry.AssertInvariants();
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));authority.Stop();
            }
        }
    }
}
