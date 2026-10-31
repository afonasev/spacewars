using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class SentryRingTraversalThreadedTests
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        static bool CrossesGap(NavPoint from,NavPoint to,NavPoint a,NavPoint b,out NavPoint crossing)
        {
            crossing=default;double dx=b.X-a.X,dz=b.Z-a.Z;
            double before=dx*(from.Z-a.Z)-dz*(from.X-a.X),after=dx*(to.Z-a.Z)-dz*(to.X-a.X);
            if(before==after||before*after>0)return false;
            double t=before/(before-after);if(t<0||t>1)return false;
            crossing=new NavPoint(from.X+t*(to.X-from.X),from.Z+t*(to.Z-from.Z));
            double along=((crossing.X-a.X)*dx+(crossing.Z-a.Z)*dz)/(dx*dx+dz*dz);
            return along>=0&&along<=1;
        }

        // One bounded ordinary-host witness, not a synchronous navigation pump.
        [Test,Timeout(60000)] public void TankAndShkvalPassBothLeftGapsOfFullyBuiltEqualRadiusBase()
        {
            const int seed=813;const long generation=814;
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,generation,false);CertifiedRouteWorker worker=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Flags).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
            var trace=new List<string>{"tick,kind,x,z,crossed,arrived,outcome"};
            try
            {
                var home=TerritoryRules.Sites(p).Single(s=>s.Id==1);
                Call("AddCredits",PlayableOwner.Player,100000d);
                // A fully built base is the authored precondition. Movement below uses
                // real commands and the ordinary threaded authority/worker connection.
                foreach(var slot in home.Slots.Concat(home.DefenseSlots(p)))
                    Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot.Id,TerritoryRules.DefenseSlot(slot.Id)?PlayableBuildingKind.SentryTurret:PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                Assert.AreEqual(10,View().Buildings.Count(b=>b.SiteId==1&&b.Kind!=PlayableBuildingKind.Headquarters&&b.Phase==ConstructionPhase.Ready));
                var existing=View().Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray();
                foreach(var e in existing)domain.Navigation.Crowd.Stop(e.Id,true);
                var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
                var starts=new[]{new NavPoint(home.Position.X-4.5,.8),new NavPoint(home.Position.X-4.5,-.8)};
                var units=kinds.Select((k,i)=>(int)Call("SpawnUnit",starts[i],PlayableOwner.Player,k)).ToArray();
                var left=home.DefenseSlots(p).Single(s=>s.Id==1003).Position;
                Assert.AreEqual(p.SlotRingRadius+p.TurretPadOffset,Distance(home.Position,left),1e-9);
                var gateA=new NavPoint[2];var gateB=new NavPoint[2];var goals=new NavPoint[2];
                for(int i=0;i<2;i++)
                {
                    int sign=i==0?1:-1;var ordinary=home.Slots.Single(s=>s.Id==(i==0?3:4)).Position;
                    // A pass-through gate between the two closest foundation corners.
                    // Move completes when the clicked point enters the legal unit footprint,
                    // so a midpoint destination cannot prove passage through the gap.
                    gateA[i]=new NavPoint(left.X+p.TurretFootprintRadius,sign*p.TurretFootprintRadius);
                    gateB[i]=new NavPoint(ordinary.X-p.FactoryFootprintRadius,ordinary.Z-sign*p.FactoryFootprintRadius);
                    goals[i]=new NavPoint(home.Position.X-9.5,sign*3.2);
                }
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                long command=1;var crossed=new bool[2];var arrived=new bool[2];var moved=new bool[2];var previous=(NavPoint[])starts.Clone();
                for(int i=0;i<2;i++)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(generation,command++,"player-1",PlayableCommandKind.Move,new[]{units[i]},goals[i])).Status);
                var clock=Stopwatch.StartNew();
                while(domain.Tick<900&&clock.Elapsed.TotalSeconds<50)
                {
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}
                    Assert.IsNull(worker.Publication.Failure);var view=authority.Latest;Assert.IsNotNull(view);Assert.IsNull(view.Failure);
                    for(int i=0;i<2;i++)
                    {
                        var unit=view.Entities.Single(e=>e.Id==units[i]);moved[i]|=Distance(unit.Position,starts[i])>p.TankCollisionRadius;
                        double radius=PlayableUnitRules.Radius(p,kinds[i]);
                        Assert.True(view.Geometry.SegmentFree(previous[i],unit.Position,radius),"Every observed motion segment must retain physical clearance");
                        if(CrossesGap(previous[i],unit.Position,gateA[i],gateB[i],out var crossing))
                        {
                            Assert.True(view.Geometry.IsFree(crossing,radius),"Crossing must fit the unchanged unit footprint");
                            crossed[i]=true;
                        }
                        arrived[i]=unit.NavigationOutcome==NavigationOutcome.Arrived&&Distance(unit.Position,goals[i])<=radius;
                        trace.Add(FormattableString.Invariant($"{domain.Tick},{kinds[i]},{unit.Position.X:R},{unit.Position.Z:R},{crossed[i]},{arrived[i]},{unit.NavigationOutcome}"));
                        previous[i]=unit.Position;
                    }
                    if(crossed.All(x=>x)&&arrived.All(x=>x))break;
                    Thread.Sleep(1);
                }
                TestContext.WriteLine($"SENTRY_RING seed={seed} tick={domain.Tick} crossed={string.Join(",",crossed)} arrived={string.Join(",",arrived)} moved={string.Join(",",moved)} pumps={worker.Publication.Pumps} wall={clock.Elapsed.TotalSeconds:R}");
                Assert.That(crossed,Is.All.True,"Both units must physically cross their narrow gap between turret and factory");
                Assert.That(arrived,Is.All.True,"Both units must naturally Arrive at their exterior destination");
                Assert.That(moved,Is.All.True);Assert.Greater(worker.Publication.Pumps,0);Assert.True(domain.Navigation.HasCertifiedHost);
            }
            finally
            {
                string output=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../.local/equal-turret-radius/left-gap-trace.csv"));
                Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllLines(output,trace);
                worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}
