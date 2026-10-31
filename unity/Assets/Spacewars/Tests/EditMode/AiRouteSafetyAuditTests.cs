using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void AuditActualFrozen400EightOwnerReviewRouteSafetyRepeats()
        {
            Contact30Source(true,(config,views,profile)=>{
                var authority=new PlayableAuthorityTick(config,1);var domain=(PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain",CostFlags).GetValue(authority);
                var scheduler=(AiAuthorityScheduler)typeof(PlayableAuthorityTick).GetField("scheduler",CostFlags).GetValue(authority);var budget=domain.Navigation.PhysicalHostBudget;
                try{
                    // Exact native400 genesis with observation tick advanced, without
                    // simulating or fabricating routes/registry/mission to force hits.
                    typeof(PlayableDomain).GetProperty("Tick",CostFlags).SetValue(domain,60L);
                    var snapshots=scheduler.Owners.Select(owner=>domain.PlayerSnapshotForArmyAi(60,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState)).ToArray();
                    Assert.AreEqual(8,snapshots.Length);Assert.AreEqual(400,snapshots.Sum(s=>s.Entities.Count(e=>e.Owner==s.Owner)));
                    var hits=new Dictionary<Tuple<PlayableAiObservation,PlayableRouteProof,PlayableProfile,bool>,int>();int calls=0,safe=0,withdrawal=0;
                    AiTacticalExecutor.RouteSafetyAuditForQa=(o,r,p,w)=>{calls++;if(w)withdrawal++;else safe++;var key=Tuple.Create(o,r,p,w);hits.TryGetValue(key,out int count);hits[key]=count+1;};
                    for(int i=0;i<8;i++)scheduler.Owners[i].Review(snapshots[i],0);
                    AiTacticalExecutor.RouteSafetyAuditForQa=null;
                    TestContext.WriteLine("AI_ROUTE_AUDIT owners=8 ownActors=400 proofCount="+snapshots.Sum(s=>s.RouteProofs.Count)+" calls="+calls+" safe="+safe+" withdrawal="+withdrawal+" uniqueObservationRouteProfileKind="+hits.Count+" repeatedCalls="+hits.Sum(x=>x.Value-1)+" maxMultiplicity="+(hits.Count==0?0:hits.Max(x=>x.Value)));
                    foreach(var group in hits.GroupBy(x=>x.Key.Item1.OwnerId).OrderBy(g=>g.Key))TestContext.WriteLine("AI_ROUTE_OWNER "+group.Key+" calls="+group.Sum(x=>x.Value)+" unique="+group.Count()+" repeats="+group.Sum(x=>x.Value-1));
                    Assert.AreEqual(8,scheduler.Owners.Count); // Qualification reports absence honestly; no manufactured repeat assertion.
                }finally{AiTacticalExecutor.RouteSafetyAuditForQa=null;authority.Stop();domain.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
            });
        }
    }
}
