using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class ProducerEgressThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test,Timeout(120000)] public void RepeatedFactoryReleaseAccumulatesOnBothSidesWithFootprintGaps()
        {
            var data=PlayableProfile.Default.CopyData();data.tankProductionSeconds=1;
            var p=PlayableProfile.Create(data);var d=new PlayableDomain(p,82,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                var factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
                var exit=FactoryReleaseRules.Exit(p,factory.Position,factory.Heading,PlayableEntityKind.Tank);
                // The native diagnostic world has no pre-existing player army.
                // Create the control actor well outside the factory apron before
                // capturing the exclusion set used by production assertions.
                int controlId=(int)Call("SpawnUnit",new NavPoint(p.PlayerHeadquartersX,p.HeadquartersZ-2*p.DefaultRallyDistance),PlayableOwner.Player,PlayableEntityKind.Tank);
                var original=View().Entities.Select(e=>e.Id).ToArray();
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),p.NavigationRequestsPerFrame);
                long sequence=0;int ordered=0;bool moved=false;var clock=Stopwatch.StartNew();
                // This no-rally production scenario otherwise has no route work.
                // Exercise the real threaded solver with a separate control actor,
                // keeping it outside the measured produced-unit set.
                var control=View().Entities.Single(e=>e.Id==controlId);
                var controlTarget=new[]{new NavPoint(control.Position.X+2,control.Position.Z),new NavPoint(control.Position.X-2,control.Position.Z),new NavPoint(control.Position.X,control.Position.Z+2),new NavPoint(control.Position.X,control.Position.Z-2)}
                    .First(target=>d.Geometry.SegmentFree(control.Position,target,p.TankCollisionRadius));
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(82,++sequence,"player-1",PlayableCommandKind.Move,new[]{control.Id},controlTarget)).Status);
                while(d.Tick<5400&&clock.Elapsed.TotalSeconds<110)
                {
                    if(ordered<12&&View().Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Count==0)
                    {
                        Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(82,++sequence,"player-1",PlayableCommandKind.QueueTank,new[]{factory.Id})).Status);ordered++;
                    }
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);
                    var produced=View().Entities.Where(e=>!original.Contains(e.Id)).ToArray();
                    moved|=produced.Any(e=>!e.Position.Equals(exit));
                    foreach(var e in produced)Assert.True(d.Geometry.IsFree(e.Position,p.TankCollisionRadius));
                    for(int i=0;i<produced.Length;i++)for(int j=i+1;j<produced.Length;j++)
                        Assert.GreaterOrEqual(Distance(produced[i].Position,produced[j].Position),2*p.TankCollisionRadius-1e-8,"Physical overlap during release");
                    if(produced.Length==12)break;Thread.Sleep(1);
                }
                var final=View().Entities.Where(e=>!original.Contains(e.Id)).ToArray();
                TestContext.WriteLine($"tick={d.Tick} seconds={clock.Elapsed.TotalSeconds:R} count={final.Length}");
                foreach(var e in final)TestContext.WriteLine($"parking {e.Id} {e.Position.X:R} {e.Position.Z:R}");
                Assert.AreEqual(12,final.Length,"Ordinary repeat release must keep progressing");Assert.True(moved);
                double Side(NavPoint point)=>(point.Z-exit.Z)*Math.Cos(factory.Heading)-(point.X-exit.X)*Math.Sin(factory.Heading);
                Assert.GreaterOrEqual(final.Count(e=>Side(e.Position)>p.TankCollisionRadius),3);
                Assert.GreaterOrEqual(final.Count(e=>Side(e.Position)<-p.TankCollisionRadius),3);
                for(int i=0;i<final.Length;i++)for(int j=i+1;j<final.Length;j++)
                    Assert.GreaterOrEqual(Distance(final[i].Position,final[j].Position),2*p.TankCollisionRadius+p.FactoryAssemblyGap-1e-8,"Accumulation must keep the configured footprint gap");
                Assert.True(d.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
            }
            finally
            {
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();
                Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [Test,Timeout(120000)] public void FactoryAndHangarReleasePastHeldBlockersThroughOrdinaryThreadedHost()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            PlayableSnapshot View()=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try
            {
                Call("AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                var factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);var hangar=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Hangar);
                var exit=FactoryReleaseRules.Exit(p,factory.Position,factory.Heading,PlayableEntityKind.Tank);
                int ground=(int)Call("SpawnUnit",exit,PlayableOwner.Player,PlayableEntityKind.Tank);
                d.Navigation.Crowd.Stop(ground,true);
                int air=(int)Call("SpawnUnit",hangar.Position,PlayableOwner.Player,PlayableEntityKind.Hawk);
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Hold,new[]{air}),out _));
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Accepted,authority.Apply(new PlayableCommand(81,2,"player-1",PlayableCommandKind.SetRally,new[]{factory.Id},new NavPoint(exit.X+8,exit.Z))).Status);
                var originalIds=View().Entities.Select(e=>e.Id).ToArray();
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,3,"player-1",PlayableCommandKind.QueueTank,new[]{factory.Id})).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,4,"player-1",PlayableCommandKind.QueueHawk,new[]{hangar.Id})).Status);
                var clock=Stopwatch.StartNew();bool groundMoved=false,airMoved=false,released=false,producedMoved=false;
                while(d.Tick<1800&&clock.Elapsed.TotalSeconds<110)
                {
                    authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);
                    var view=authority.Latest;if(view==null){Thread.Sleep(1);continue;}
                    groundMoved|=view.Entities.Any(e=>e.Id==ground&&!e.Position.Equals(exit));
                    airMoved|=view.Entities.Any(e=>e.Id==air&&!e.Position.Equals(hangar.Position));
                    released=view.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Count==0&&view.Buildings.Single(b=>b.Id==hangar.Id).PrivateState.Orders.Count==0;
                    producedMoved|=view.Entities.Any(e=>e.Kind==PlayableEntityKind.Tank&&!originalIds.Contains(e.Id)&&!e.Position.Equals(exit));
                    if(released&&producedMoved)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"tick={d.Tick} seconds={clock.Elapsed.TotalSeconds:R} groundMoved={groundMoved} airMoved={airMoved} released={released}");
                Assert.True(groundMoved&&airMoved&&released,"Ordinary threaded production must physically clear both exits and release");
                Assert.True(producedMoved,"Released ground unit must move toward commanded rally through threaded routing");
                Assert.True(d.Navigation.HasCertifiedHost);Assert.Greater(worker.Publication.Pumps,0);
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
