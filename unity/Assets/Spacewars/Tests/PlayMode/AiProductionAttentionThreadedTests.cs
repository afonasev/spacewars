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
    public sealed class AiProductionAttentionThreadedTests
    {
        [Test,Timeout(80000)] public void FirstProductionAttentionQueuesPaysAndReleasesThroughRealHost()
        {
            const int seed=7;
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,84,false);CertifiedRouteWorker worker=null;
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,f).Invoke(d,args);
            PlayableSnapshot View()=>d.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",(double)p.FactoryBuildSeconds+1);
                var before=View();var original=before.Entities.Select(e=>e.Id).ToArray();var o=PlayableAiObservation.From(before);
                var candidate=new AiEconomyPlanner().Plan(o,p).Candidates.Single(c=>c.Policy=="production"&&c.Legal);
                var production=new AiIntent("production","production",candidate.Action,1000,p.TankCreditCost,p.TankPopulationCost,new[]{"policy:production"});
                var scoutAction=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{3},new NavPoint(0,0),seed:seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                var scout=new AiIntent("scout","scout",scoutAction,1000,0,0,new[]{"policy:scout"},1,overdue:true);
                var selected=new AiDecisionArbiter(AiProfile.Initial).Select(new[]{scout,production},d.Tick,o.Credits,o.Population.Capacity-o.Population.Living-o.Population.Reserved,1).Single();
                Assert.AreEqual(production,selected,"Fresh useful production gets the first ordinary attention slot");
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),seed);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                var factory=before.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.Owner==PlayableOwner.Player);
                // A queue without a rally correctly creates a parked unit and
                // requests no route. Supply the ordinary producer setting so
                // this witness also exercises its promised threaded release.
                var exit=FactoryReleaseRules.Exit(p,factory.Position,factory.Heading,PlayableEntityKind.Tank);
                var rally=new[]{new NavPoint(exit.X+8,exit.Z),new NavPoint(exit.X-8,exit.Z),new NavPoint(exit.X,exit.Z+8),new NavPoint(exit.X,exit.Z-8)}.First(t=>d.Geometry.SegmentFree(exit,t,p.TankCollisionRadius));
                var clock=Stopwatch.StartNew();
                Assert.AreEqual(PlayableCommandStatus.Accepted,authority.Apply(new PlayableCommand(84,1,"player-1",PlayableCommandKind.SetRally,new[]{factory.Id},rally)).Status);
                // Ground rally admission is asynchronous and validates every
                // compatible body before installing the point.
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60&&!View().Buildings.Single(b=>b.Id==factory.Id).PrivateState.HasRally){authority.TryAdvance();Assert.IsNull(worker.Publication.Failure);Thread.Sleep(1);}
                Assert.AreEqual(PlayableCommandStatus.Applied,d.DrainRallyReceipts().Single().Status,"Real host must validate and install the rally");
                int bank=View().Credits;
                var command=new PlayableCommand(84,2,"player-1",selected.Action.Kind,selected.Action.CopyEntityIds(),unitKind:selected.Action.UnitKind);
                var receipt=authority.Apply(command);Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                factory=View().Buildings.Single(b=>b.Id==factory.Id);
                Assert.AreEqual(1,factory.PrivateState.QueueCount);Assert.That(bank-View().Credits,Is.EqualTo(p.TankCreditCost));
                int born=0;NavPoint birth=default;bool moved=false;
                while(d.Tick<900&&clock.Elapsed.TotalSeconds<60){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var unit=View().Entities.FirstOrDefault(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank&&!original.Contains(e.Id));
                    if(unit!=null){if(born==0){born=unit.Id;birth=unit.Position;}else moved=Math.Pow(unit.Position.X-birth.X,2)+Math.Pow(unit.Position.Z-birth.Z,2)>p.TankCollisionRadius*p.TankCollisionRadius;}
                    if(moved)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"PRODUCTION_FIRST seed={seed} tick={d.Tick} born={born} moved={moved} pumps={worker.Publication.Pumps}");
                Assert.Greater(born,0,"The paid selected queue must create an owned unit");Assert.True(moved,"The produced unit must physically release through ordinary threaded navigation");Assert.Greater(worker.Publication.Pumps,0);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}
