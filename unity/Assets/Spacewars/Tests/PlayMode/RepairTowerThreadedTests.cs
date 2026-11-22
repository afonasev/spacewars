using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RepairTowerThreadedTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test, Timeout(60000)]
        public void SkatLaunchesAndRepairsThroughOrdinaryThreadedHost()
        {
            var profile = PlayableProfile.Default;
            var domain = new PlayableDomain(profile, 813, false);
            CertifiedRouteWorker worker = null;
            object Call(string name, params object[] args) => typeof(PlayableDomain).GetMethod(name, Flags).Invoke(domain, args);
            var authority = new PlayableAuthorityTick(domain, new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()), 7);
            long sequence = 0;
            PlayableCommandStatus Send(PlayableCommandKind kind, int[] ids = null, int slot = 0,
                PlayableBuildingKind building = default, int target = 0, NavPoint point = default) =>
                authority.Apply(new PlayableCommand(813, ++sequence, "player-1", kind, ids, point,
                    targetId: target, siteId: 1, slotId: slot, parentId: 1, buildingKind: building)).Status;
            try
            {
                Call("AddCredits", PlayableOwner.Player, 2000d);
                worker = new CertifiedRouteWorker(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.BuildAt, slot: 1, building: PlayableBuildingKind.Hangar));
                var clock = Stopwatch.StartNew();
                bool scienceOrdered = false, queued = false, platform = false, repairOrdered = false, repaired = false;
                int damaged = 0, ground = 0;
                while (domain.Tick < 2400 && clock.Elapsed.TotalSeconds < 50)
                {
                    authority.TryAdvance();
                    Assert.IsNull(worker.Publication.Failure);
                    var view = authority.Latest;
                    if (view == null) { Thread.Sleep(1); continue; }
                    Assert.IsNull(view.Failure);
                    var hangar = view.Buildings.FirstOrDefault(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Hangar);
                    if (!scienceOrdered && hangar?.Phase == ConstructionPhase.Ready)
                    {
                        Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.BuildAt, slot: 2,
                            building: PlayableBuildingKind.ScientificCenter));
                        scienceOrdered = true;
                        ground = view.Entities.First(unit => unit.Owner == PlayableOwner.Player && unit.Kind == PlayableEntityKind.Explorer).Id;
                        var groundCommand = Send(PlayableCommandKind.Move, new[] { ground }, point: new NavPoint(-11, -8));
                        Assert.That(groundCommand, Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted));
                    }
                    if (!queued && hangar != null && view.Buildings.Any(b => b.Owner == PlayableOwner.Player &&
                        b.Kind == PlayableBuildingKind.ScientificCenter && b.Phase == ConstructionPhase.Ready))
                    {
                        Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.QueueSkat, new[] { hangar.Id }));
                        queued = true;
                    }
                    platform |= hangar?.LaunchKind == PlayableEntityKind.Skat && hangar.LaunchPhase == 3;
                    var drone = view.Entities.FirstOrDefault(unit => unit.Owner == PlayableOwner.Player && unit.Kind == PlayableEntityKind.Skat);
                    if (!repairOrdered && drone != null && !drone.Departing)
                    {
                        damaged = view.Buildings.Single(b => b.Id == 1).Health - 40;
                        Call("Damage", 1, 40);
                        Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.RepairTarget, new[] { drone.Id }, target: 1));
                        repairOrdered = true;
                    }
                    repaired = repairOrdered && view.Buildings.Single(b => b.Id == 1).Health > damaged &&
                        view.Orbital.RepairedUnits.Contains(1);
                    if (repaired && worker.Publication.Pumps > 0) break;
                    Thread.Sleep(1);
                }
                Assert.True(queued && platform && repairOrdered && repaired,
                    $"tick={domain.Tick} science={scienceOrdered} queued={queued} platform={platform} order={repairOrdered} healed={repaired}");
                Assert.True(domain.Navigation.HasCertifiedHost);
                Assert.Greater(worker.Publication.Pumps, 0);
            }
            finally
            {
                worker?.Dispose();
                if (worker != null) Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, 5000));
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);
            }
        }

        // One bounded ordinary-host integration witness. Fixed profile/seed/horizon; no unchanged reruns to fish for PASS.
        [Test,Timeout(60000)] public void CommandBuildsRepairTowerAndHealsThroughOrdinaryThreadedHost()
        {
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,813,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Flags).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,1000d);
                int enemy=(int)Call("SpawnUnit",new NavPoint(-21,10),PlayableOwner.Player,PlayableEntityKind.Tank);domain.Navigation.Crowd.Stop(enemy,true);Call("Damage",enemy,80);
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:1001,buildingKind:PlayableBuildingKind.RepairTower,parentId:1)).Status);
                var friendly=View().Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.Move,new[]{friendly.Id},new NavPoint(-11,-8))).Status);
                var clock=Stopwatch.StartNew();bool ready=false,locked=false,shot=false,damage=false,moved=false;
                while(domain.Tick<900&&clock.Elapsed.TotalSeconds<50)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);
                    var view=authority.Latest;if(view==null){Thread.Sleep(1);continue;}
                    Assert.IsNull(view.Failure);moved|=view.Entities.Any(e=>e.Id==friendly.Id&&!e.Position.Equals(friendly.Position));
                    var turret=view.Buildings.SingleOrDefault(b=>b.Kind==PlayableBuildingKind.RepairTower);
                    ready|=turret?.Phase==ConstructionPhase.Ready;
                    locked|=turret?.Healing?.TargetId==enemy;
                    shot|=turret?.Healing?.TargetId>0;
                    var target=view.Entities.SingleOrDefault(e=>e.Id==enemy);damage|=target!=null&&target.Health>p.TankHealth-80;
                    if(ready&&locked&&shot&&damage&&moved&&worker.Publication.Pumps>0)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"REPAIR_THREADED seed=7 tick={domain.Tick} ready={ready} locked={locked} shot={shot} damage={damage} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.True(ready&&locked&&shot&&damage,"Actual BuildAt command, foundation, authoritative target lock and fixed-rate healing must connect");
                Assert.True(moved,"The fixture must issue an actual route request and observe physical movement");Assert.True(domain.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
