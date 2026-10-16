using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AsyncRouteDeliveryTests
    {
        private static PlayableAuthorityTick Authority(bool asynchronous=true)=>new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71,asynchronousRoutes:asynchronous);
        private static NavGeometry Geometry(NavigationSession n)=>(NavGeometry)typeof(NavigationSession).GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(n);
        private static NavPoint Goal(NavigationSession n,NavUnit actor,double distance=3){
            foreach(var direction in new[]{new NavPoint(1,0),new NavPoint(-1,0),new NavPoint(0,1),new NavPoint(0,-1)}){
                var goal=new NavPoint(actor.Position.X+direction.X*distance,actor.Position.Z+direction.Z*distance);
                if(Geometry(n).SegmentFree(actor.Position,goal,actor.Radius))return goal;
            }
            Assert.Fail("fixture has no free short route");return default;
        }
        private static NavUnit Actor(PlayableAuthorityTick a)=>a.Domain.Navigation.Crowd.Units.First();
        private static object Call(PlayableAuthorityTick a,string method,params object[] args)=>a.Domain.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(a.Domain,args);
        [Test] public void WithheldRouteStopsOnlyRequesterWhileTicksMovementAndProductionContinue(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var config=OfflineParticipantAuthorityTests.Config(2);
            var hq=a.Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Call(a,"AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(a,"BuildAt",hq.SiteId,1,PlayableBuildingKind.Factory,hq.Id,PlayableOwner.Player,null));
            Call(a,"AdvanceFoundations");Call(a,"AdvanceBuildings",config.Profile.FactoryBuildSeconds+1d);
            var view=a.Domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
            var factory=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.QueueTank,new[]{factory.Id}).AsHuman()).Status);
            // Begin an old path before issuing another one; it must not keep moving while pending.
            Assert.True(n.Crowd.SetRoute(actor.Id,Goal(n,actor),new[]{Goal(n,actor)}));
            var start=actor.Position;long revision=actor.MobilityRevision;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},Goal(n,actor)).AsHuman()).Status);
            var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var otherStart=other.Position;
            Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
            for(int i=0;i<30;i++)Assert.True(a.TryAdvance());
            Assert.AreEqual(30,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);Assert.AreEqual(revision,actor.MobilityRevision);
            Assert.AreNotEqual(otherStart,other.Position);Assert.False(a.AwaitingRoutes);
            var order=a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single();
            Assert.True(order.Active);Assert.Less(order.Remaining,order.Duration,"Production must advance while path calculation is withheld.");
            a.Stop();
        }
        [Test] public void PhysicalHostPendingRoutePreservesIndependentDisplacementAndEconomy(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);
            var kernel=new CertifiedRouteKernel();
            try{
                kernel.Attach(n.RoutePort);
                // Establish the physical ownership lane before withholding A's work.
                kernel.Pump(a.NavigationBinding,1);
                Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
                var otherStart=other.Position;var start=actor.Position;
                Assert.True(n.Move(actor.Id,Goal(n,actor)));long publications=n.OwnedMovementPublications;
                for(int i=0;i<30;i++)Assert.True(a.TryAdvance());
                Assert.AreEqual(30,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);
                Assert.AreNotEqual(otherStart,other.Position,"Installed B must physically move while A remains pending in the owned Host lane.");
                Assert.Greater(n.OwnedMovementPublications,publications,"Movement must use the paid immutable carrier.");
            }finally{kernel.Clear();a.Stop();n.DestroyNavigationAuthority();}
        }
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)]
        public void ActualGroupPendingKeepsInstalledBAndProductionMovingThenCurrentAReachesArrival(PlayableCommandKind kind){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var kernel=new CertifiedRouteKernel();
            try{
                var hq=a.Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                Call(a,"AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(a,"BuildAt",hq.SiteId,1,PlayableBuildingKind.Factory,hq.Id,PlayableOwner.Player,null));
                Call(a,"AdvanceFoundations");Call(a,"AdvanceBuildings",config.Profile.FactoryBuildSeconds+1d);
                var view=a.Domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
                var factory=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.QueueTank,new[]{factory.Id}).AsHuman()).Status);
                Assert.True(n.Move(other.Id,Goal(n,other,6)));
                for(int i=0;i<4096&&n.IsPending(other.Id);i++){Assert.True(a.TryAdvance());kernel.Pump(a.NavigationBinding,4);}
                Assert.False(n.IsPending(other.Id));Assert.True(other.Moving);var bStart=other.Position;
                var goal=Goal(n,actor);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,kind,new[]{actor.Id},goal).AsHuman()).Status);
                var start=actor.Position;long tick=a.Tick;double remaining=a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().Remaining;
                for(int i=0;i<15;i++)Assert.True(a.TryAdvance());
                Assert.AreEqual(tick+15,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);Assert.AreNotEqual(bStart,other.Position);
                Assert.Less(a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().Remaining,remaining);
                using(var before=new QuantumPin(n.PinCommittedHostRoot())){
                    long serial=before.Value.QuantumSerial;for(int i=0;i<5;i++)Assert.True(a.TryAdvance(true));Assert.AreEqual(tick+15,a.Tick);
                    using(var after=new QuantumPin(n.PinCommittedHostRoot()))Assert.AreEqual(serial,after.Value.QuantumSerial);
                }
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,3,config.Roster[0].Id,kind,new[]{actor.Id},Goal(n,actor,2)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,4,config.Roster[0].Id,PlayableCommandKind.Stop,new[]{actor.Id}).AsHuman()).Status);
                Assert.True(a.TryAdvance());Assert.False(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,5,config.Roster[0].Id,kind,new[]{actor.Id},goal).AsHuman()).Status);
                for(int i=0;i<4096&&(n.IsPending(actor.Id)||actor.Outcome!=NavigationOutcome.Arrived);i++){Assert.True(a.TryAdvance());kernel.Pump(a.NavigationBinding,4);}
                Assert.False(n.IsPending(actor.Id));Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,"current A must install and physically arrive");Assert.AreNotEqual(start,actor.Position);
            }finally{kernel.Clear();a.Stop();n.DestroyNavigationAuthority();Assert.Zero(n.PhysicalHostBudget.PhysicalBytes);}
        }
        private sealed class QuantumPin:IDisposable{internal readonly NavigationHostRoot Value;internal QuantumPin(NavigationHostRoot value){Value=value;}public void Dispose()=>Value.Release();}
        [Test] public void CombatContinuesWhileAnotherActorsRouteIsWithheld(){
            var a=Authority();var n=a.Domain.Navigation;
            Call(a,"SpawnUnit",new NavPoint(-3,0),PlayableOwner.Player,PlayableEntityKind.Tank);
            Call(a,"SpawnUnit",new NavPoint(3,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            double TankHealth()=>a.Domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Sum(e=>e.Health);
            int requester=a.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer).Id;
            Assert.True(n.Crowd.TryGet(requester,out var actor));Assert.True(n.Move(requester,Goal(n,actor)));var start=actor.Position;
            double before=TankHealth();
            for(int i=0;i<60;i++)Assert.True(a.TryAdvance());
            Assert.AreEqual(60,a.Tick);Assert.True(n.IsPending(requester));Assert.AreEqual(start,actor.Position);
            Assert.Less(TankHealth(),before,"Actual firing/projectile damage must continue while another path is withheld.");a.Stop();
        }
        [Test] public void FrozenHeadlessPolicyStillRequiresReadyRoutes(){
            var a=Authority(false);var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            Assert.False(a.TryAdvance());Assert.Zero(a.Tick);Assert.True(a.AwaitingRoutes);a.Stop();
        }
        [Test] public void CertifiedReadyPathActivatesOnAnOrdinaryTick(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var start=actor.Position;
            Assert.True(n.Move(actor.Id,Goal(n,actor)));for(int i=0;i<5;i++)Assert.True(a.TryAdvance());Assert.AreEqual(start,actor.Position);
            using(var host=new Spacewars.Presentation.UnityHostRouteService()){
                for(int i=0;i<4096&&n.IsPending(actor.Id);i++){host.Service(a,4);Assert.True(a.TryAdvance());}
                Assert.False(n.IsPending(actor.Id));Assert.AreEqual(1,n.AppliedResults);Assert.True(actor.Moving);
                // Route activation can initially turn in place; retain actual movement verification.
                for(int i=0;i<60;i++)Assert.True(a.TryAdvance());Assert.AreNotEqual(start,actor.Position);
            }
            a.Stop();
        }
        [TestCase("stop")][TestCase("new-order")][TestCase("geometry")][TestCase("death")]
        public void LateResultsNeverReactivateObsoleteMovement(string cause){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var start=actor.Position;
            Assert.True(n.Move(actor.Id,Goal(n,actor)));Assert.True(n.Requests.TryDequeue(out var old));
            for(int i=0;i<5;i++)Assert.True(a.TryAdvance());
            if(cause=="stop")n.Stop(actor.Id,true);
            else if(cause=="new-order")Assert.True(n.Move(actor.Id,Goal(n,actor,2)));
            else if(cause=="geometry")n.ChangeGeometry(new NavGeometry(Geometry(n).HalfExtent,Geometry(n).Obstacles.ToArray(),Geometry(n).Revision+1));
            else Assert.True(n.Remove(actor.Id));
            Assert.True(n.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal})));
            Assert.True(a.TryAdvance());Assert.Zero(n.AppliedResults);Assert.Greater(n.RejectedResults,0);
            if(cause!="death"){Assert.AreEqual(start,actor.Position);Assert.False(actor.Moving);}
            a.Stop();
        }
        [Test] public void AsyncPolicySurvivesExactPendingRestoreAndPause(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            for(int i=0;i<5;i++)Assert.True(a.TryAdvance());var saved=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(saved,config);
            Assert.AreEqual(PlayableAuthorityTick.AsyncRouteDeliveryPolicy,b.EffectiveRouteDeliveryPolicy);Assert.True(b.Domain.Navigation.SuspendPendingMovement);
            CollectionAssert.AreEqual(saved,b.CaptureBytes());
            for(int i=0;i<15;i++){Assert.True(a.TryAdvance());Assert.True(b.TryAdvance());CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
            long tick=b.Tick;Assert.True(b.TryAdvance(true));Assert.AreEqual(tick,b.Tick);Assert.True(b.Domain.Navigation.IsPending(actor.Id));
            a.Stop();b.Stop();
        }
        [Test] public void UnknownSavedRoutePolicyStillFailsClosed(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var state=PlayableWorldState.Decode(a.CaptureBytes());
            var needle=System.Text.Encoding.UTF8.GetBytes(PlayableAuthorityTick.AsyncRouteDeliveryPolicy);int at=-1;
            for(int i=0;i<=state.AiAuthority.Length-needle.Length;i++)if(state.AiAuthority.Skip(i).Take(needle.Length).SequenceEqual(needle)){at=i;break;}
            Assert.GreaterOrEqual(at,0);state.AiAuthority[at]=(byte)'x';
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),config));a.Stop();
        }
        [Test] public void SolverAllocationReservationNeverFreezesAsyncTicksOrCancellation(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            Assert.True(a.TryAdvance());long issuedTick=a.Tick;
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var task=System.Threading.Tasks.Task.Run(()=>kernel.Pump(a.NavigationBinding,4));
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));var start=actor.Position;
                    var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var otherStart=other.Position;Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
                    for(int i=0;i<30;i++)Assert.True(a.TryAdvance());Assert.AreEqual(issuedTick+30,a.Tick);Assert.AreEqual(start,actor.Position);Assert.AreNotEqual(otherStart,other.Position,"B displacement while physical Host allocation is blocked");
                    n.Stop(actor.Id,true);Assert.True(a.TryAdvance());Assert.False(n.IsPending(actor.Id));
                }finally{release.Set();Assert.True(task.Wait(TimeSpan.FromSeconds(5)));kernel.Clear();a.Stop();}
            }
        }
        [Test] public void RealtimeOfflineCommandsRemainAvailableWithoutRouteService(){
            var config=OfflineParticipantAuthorityTests.Config(2);var runtime=new PlayableRuntime(config,71);
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,TimeSpan.FromSeconds(5)));var actor=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
                long tick=runtime.Latest.Tick;var goal=new NavPoint(actor.Position.X+2,actor.Position.Z);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},goal).AsHuman()).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+15,TimeSpan.FromSeconds(3)),"Realtime authority must not freeze without a solver.");
                Assert.Greater(runtime.Latest.Metrics.NavigationPending,0);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.Stop,new[]{actor.Id}).AsHuman()).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Metrics.NavigationPending==0,TimeSpan.FromSeconds(3)),"A new command must cancel pending work while the solver is absent.");
            }finally{runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,TimeSpan.FromSeconds(5)));}
        }
    }
}
