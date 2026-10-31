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
    public sealed class GaussTowerThreadedTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        // One bounded ordinary-host integration witness. Fixed profile/seed/horizon; no unchanged reruns to fish for PASS.
        [Test,Timeout(60000)] public void CommandBuildsGaussTowerAndFiresThroughOrdinaryThreadedHost()
        {
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,813,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Flags).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,1000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,5,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,3,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                var at=TerritoryRules.Sites(p).Single(s=>s.Id==1).DefenseSlots(p).Single(s=>s.Id==1001).Position;
                int enemy=(int)Call("SpawnUnit",new NavPoint(at.X,at.Z+8),PlayableOwner.Enemy,PlayableEntityKind.Tank);domain.Navigation.Crowd.Stop(enemy,true);
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:1001,buildingKind:PlayableBuildingKind.GaussTower,parentId:1)).Status);
                var friendly=View().Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.Move,new[]{friendly.Id},new NavPoint(-11,-8))).Status);
                var clock=Stopwatch.StartNew();bool ready=false,locked=false,shot=false,damage=false,moved=false;
                while(domain.Tick<900&&clock.Elapsed.TotalSeconds<50)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);
                    var view=authority.Latest;if(view==null){Thread.Sleep(1);continue;}
                    Assert.IsNull(view.Failure);moved|=view.Entities.Any(e=>e.Id==friendly.Id&&!e.Position.Equals(friendly.Position));
                    var turret=view.Buildings.SingleOrDefault(b=>b.Kind==PlayableBuildingKind.GaussTower);
                    ready|=turret?.Phase==ConstructionPhase.Ready;
                    locked|=turret?.Turret?.TargetId==enemy;
                    shot|=turret?.Turret?.ShotSequence>0;
                    var target=view.Entities.SingleOrDefault(e=>e.Id==enemy);damage|=target!=null&&target.Health<p.TankHealth;
                    if(ready&&locked&&shot&&damage&&moved&&worker.Publication.Pumps>0)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"GAUSS_THREADED seed=7 tick={domain.Tick} ready={ready} locked={locked} shot={shot} damage={damage} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.True(ready&&locked&&shot&&damage,"Actual BuildAt command, foundation, authoritative target lock and piercing shot must connect");
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
