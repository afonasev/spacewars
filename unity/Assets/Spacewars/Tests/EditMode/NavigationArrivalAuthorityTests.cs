using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalAuthorityTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private NavigationSession nav;private CertifiedRouteKernel host;private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,F).Invoke(domain,args);
        private int Spawn(double x,double z,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",new NavPoint(x,z),PlayableOwner.Player,kind);
        private void Move(int[] ids,NavPoint point,bool append=false,bool attack=false)=>Assert.AreEqual(PlayableCommandStatus.Applied,
            Call("Apply",new PlayableCommand(1,++sequence,"player-1",attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,ids,point,
                origin:PlayableOrderOrigin.Human,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace),null));
        private void Pump(int budget=1)=>host.Pump(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),budget);
        private byte[] Bytes()=>(byte[])Call("CaptureWorldBytes",7,"a4b-authority-tests");
        private void RestoreOnlyBytes(byte[] bytes,PlayableProfile expected=null)
        {
            host.Clear();host=null;nav=null;domain=null;GC.Collect();
            domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,expected??PlayableProfile.Default,7,"a4b-authority-tests"});
            nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);host=new CertifiedRouteKernel();
        }
        [SetUp] public void Setup(){domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);host=new CertifiedRouteKernel();sequence=0;}
        [TearDown] public void Cleanup(){host?.Clear();}
        private void Complete(){for(int n=0;n<20000&&nav.PendingCount>0;n++){Pump(4);nav.ApplyResults();}if(nav.PendingCount>0){
                foreach(var row in ((System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(nav)).Cast<object>()){
                    var a=row.GetType().GetProperty("Value").GetValue(row);var w=(NavigationArrivalWork)a.GetType().GetField("Work",F).GetValue(a);var c=w.RouteCounters;
                    TestContext.WriteLine("PENDING "+nav.ArrivalRetentionDiagnostic+" authority="+nav.ArrivalRetainedBytes+" host="+nav.HostRetainedBytes+" logicalgraph="+c.LogicalGraphWork+" logicalfield="+c.LogicalFieldWork+" connectors="+c.ConnectorWork+" allocation="+w.AllocationWorkConsumed+" phase="+w.WorkPhase+" member="+w.MemberCursor+" candidate="+w.CandidateCursor);
                    TestContext.WriteLine("SOLVER "+typeof(NavigationArrivalWork).GetProperty("RetentionBreakdown",F).GetValue(w));
                }
            }Assert.AreEqual(0,nav.PendingCount);}
        [Test] public void ActualCommandAllocatesWithoutRequestsAndFreshWorldBytesPreserveIdentity()
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(-10,-14));
            Assert.AreEqual(2,nav.AllocationCount);Assert.AreEqual(0,nav.Requests.Count);Assert.AreEqual(0,nav.ArrivalReservations.Count);
            var before=nav.GroupOrders.Single();byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());
            Assert.AreEqual(before.GroupId,nav.GroupOrders.Single().GroupId);Assert.AreEqual(before.CommandSequence,nav.GroupOrders.Single().CommandSequence);
            Complete();Assert.AreEqual(2,nav.AppliedResults);Assert.AreEqual(2,nav.ArrivalReservations.Count);
            Assert.True(nav.ArrivalReservations.All(r=>r.State==NavArrivalReservationKind.InstalledExecution));
            Assert.AreEqual(2,nav.ArrivalReservations.Select(r=>r.Endpoint).Distinct().Count());
            var queue=(PlayableUnitQueueSnapshot)Call("TacticalQueueProjection",a,PlayableOwner.Player);
            Assert.NotNull(queue.Active);Assert.Null(queue.Pending);Assert.Null(Call("SpatialCompletion",a,PlayableOwner.Player));
        }
        [Test] public void FreshBytesAtEveryPumpPreserveExactNextLedgerAndAppliedCounters()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));
            for(int n=0;n<20000&&nav.PendingCount>0;n++){
                Pump(4);byte[] bytes=Bytes();var original=domain;var originalNav=nav;var originalHost=host;
                domain=null;nav=null;host=null;
                domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"a4b-authority-tests"});
                nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);host=new CertifiedRouteKernel();
                CollectionAssert.AreEqual(bytes,Bytes(),"fresh byte capture at pump "+n);
                // Incremental derivative reconstruction completes before observable
                // authority publication, with no new accepted command/request IDs.
                for(int k=0;k<20000&&nav.RoutePort.ArrivalReconstructing;k++)Pump(4);
                Assert.False(nav.RoutePort.ArrivalReconstructing);
                host.Pump(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),4);nav.ApplyResults();
                originalHost.Pump(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(original),PlayableProfile.Default,originalNav.Admission,originalNav.RoutePort),4);originalNav.ApplyResults();
                Assert.AreEqual(originalNav.AppliedResults,nav.AppliedResults,"next applied "+n);
                CollectionAssert.AreEqual(originalNav.ArrivalReservations.Select(r=>r.Id+":"+r.Proof+":"+r.State),nav.ArrivalReservations.Select(r=>r.Id+":"+r.Proof+":"+r.State),"next ledger "+n);
                originalHost.Clear();original=null;originalNav=null;originalHost=null;
            }
            Assert.AreEqual(1,nav.AppliedResults);
        }
        [Test] public void RealWorkerWakesForAllocationOnlyAndCallbacksStayOnAuthorityThread()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            int authority=System.Threading.Thread.CurrentThread.ManagedThreadId;var threads=new System.Collections.Generic.List<int>();
            nav.EndpointAssigned+=r=>threads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
            nav.RouteRequested+=r=>threads.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
            using(var worker=new CertifiedRouteWorker(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),4)){
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>nav.HasReadyArrival,5000),worker.Publication.Failure??"allocation-only worker slept");
                Assert.Greater(worker.Publication.Pumps,0);Assert.IsEmpty(threads);Assert.AreEqual(0,nav.ArrivalReservations.Count);
                byte[] bytes=Bytes();Assert.DoesNotThrow(()=>Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"a4b-authority-tests"}));
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>{nav.ApplyResults();return nav.PendingCount==0;},5000),worker.Publication.Failure??"ready publication did not wake connector projection");
                Assert.AreEqual(1,nav.AppliedResults);Assert.AreEqual(2,threads.Count);Assert.True(threads.All(t=>t==authority));
            }
        }
        [Test] public void InstalledAAndPendingBHaveIndependentReservationIdsUntilAtomicInstall()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));Complete();var installed=nav.ArrivalReservations.Single();
            Move(new[]{id},new NavPoint(-10,-14));Assert.AreEqual(installed.Id,nav.ArrivalReservations.Single().Id);
            for(int n=0;n<20000&&!nav.ArrivalReservations.Any(r=>r.State==NavArrivalReservationKind.PendingRequest);n++){Pump(4);var ready=nav.HasReadyArrival;if(ready)nav.PrepareDeliveryBarrier();else nav.ApplyResults();}
            Assert.AreEqual(2,nav.ArrivalReservations.Count);Assert.AreEqual(installed.Id,nav.ArrivalReservations.Single(r=>r.State==NavArrivalReservationKind.InstalledExecution).Id);
            Assert.AreNotEqual(installed.Endpoint,nav.ArrivalReservations.Single(r=>r.State==NavArrivalReservationKind.PendingRequest).Endpoint);
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());Complete();Assert.AreEqual(1,nav.ArrivalReservations.Count);
            Assert.Greater(nav.ArrivalReservations.Single().Id,installed.Id);
        }
        [Test] public void ConcurrentWorkerSnapshotsNeverCaptureAnUncommittedJournalEpoch()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            using(var worker=new CertifiedRouteWorker(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),1)){
                for(int n=0;n<40;n++){
                    var bytes=Bytes();var fresh=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"a4b-authority-tests"});
                    var restored=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(fresh);var kernel=new CertifiedRouteKernel();
                    for(int k=0;k<20000&&restored.RoutePort.ArrivalReconstructing;k++)kernel.Pump(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(fresh),PlayableProfile.Default,restored.Admission,restored.RoutePort),1);
                    Assert.False(restored.RoutePort.ArrivalReconstructing);kernel.Clear();
                }
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>nav.HasReadyArrival,5000),worker.Publication.Failure??"worker failed before coherent ready snapshot");
            }
        }
        [Test] public void ControlledComputeCommitPauseCapturesOnlyThePriorCommittedWorldBytes()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            // Both entering ArrivalAdvance and creating an attempt are
            // committed boundaries. The next worker starts directly at the
            // observed compute boundary, without publishing either first.
            host.PauseAtPhysicalBoundary=()=>host.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance;
            try{Pump(1);}finally{host.PauseAtPhysicalBoundary=null;}
            Assert.AreEqual(CertifiedRouteKernel.PumpStage.ArrivalAdvance,host.CapturePumpFrontier().Stage);Assert.True(nav.PreparePhysicalArrivalAttempt());host.Clear();var prior=Bytes();
            using(var computed=new System.Threading.ManualResetEventSlim())using(var resume=new System.Threading.ManualResetEventSlim()){
                nav.BeforeArrivalAdvanceCommit=status=>{computed.Set();resume.Wait();};
                using(var worker=new CertifiedRouteWorker(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),1)){
                    try {
                        Assert.True(computed.Wait(5000),worker.Publication.Failure??"worker did not reach controlled compute/commit boundary");
                        byte[] frozen=Bytes();CollectionAssert.AreEqual(prior,frozen,"uncommitted compute must not alter the persisted logical point");
                        var fresh=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{frozen,PlayableProfile.Default,7,"a4b-authority-tests"});
                        var other=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(fresh);var kernel=new CertifiedRouteKernel();
                        for(int n=0;n<20000&&other.PendingCount>0;n++){kernel.Pump(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(fresh),PlayableProfile.Default,other.Admission,other.RoutePort),1);other.ApplyResults();}
                        Assert.AreEqual(1,other.AppliedResults);Assert.AreEqual(0,other.PendingCount);kernel.Clear();
                    }finally{nav.BeforeArrivalAdvanceCommit=null;resume.Set();}
                    Assert.True(System.Threading.SpinWait.SpinUntil(()=>{nav.ApplyResults();return nav.PendingCount==0;},5000),worker.Publication.Failure??"live authority did not resume");
                    Assert.AreEqual(1,nav.AppliedResults);
                }
            }
        }
        [Test] public void RealWorkerContinuesUnfinishedAllotmentsWithoutAnotherCommandOrWake()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();int first=-1,calls=0;
            nav.BeforeArrivalAdvanceCommit=status=>{if(System.Threading.Interlocked.Increment(ref calls)==1)System.Threading.Volatile.Write(ref first,(int)status);};
            using(var worker=new CertifiedRouteWorker(new PlayableRouteBinding((NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)Domain.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),1)){
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>nav.HasReadyArrival,5000),worker.Publication.Failure??"unfinished allocation lost its continuation wake");
                Assert.AreEqual((int)NavArrivalStatus.BudgetExhausted,System.Threading.Volatile.Read(ref first));Assert.GreaterOrEqual(worker.Publication.Pumps,2);
                Assert.AreEqual(1,sequence);Assert.AreEqual(0,nav.Requests.Count);Assert.AreEqual(0,nav.AppliedResults);
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>{nav.ApplyResults();return nav.PendingCount==0;},5000),worker.Publication.Failure??"install did not finish");
                Assert.AreEqual(1,nav.AppliedResults);
            }
            nav.BeforeArrivalAdvanceCommit=null;
        }
        [Test] public void ReadyWorldHydratesTypedProofAndLogicalQuotaWithoutReplayingGraphWork()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.HasReadyArrival;n++)Pump(1);Assert.True(nav.HasReadyArrival);
            var before=Bytes();RestoreOnlyBytes(before);Assert.False(nav.RoutePort.ArrivalReconstructing,"Ready must hydrate its primitive proof/counter snapshot");
            CollectionAssert.AreEqual(before,Bytes());nav.PrepareDeliveryBarrier();Assert.AreEqual(1,nav.ArrivalReservations.Count);
            Assert.LessOrEqual(nav.ArrivalRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);Complete();Assert.AreEqual(1,nav.AppliedResults);
        }
        private void Stop(int[] ids,bool hold=false)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++sequence,"player-1",hold?PlayableCommandKind.Hold:PlayableCommandKind.Stop,ids,origin:PlayableOrderOrigin.Human),null));
        private bool Interrupted(int id,string slot)
        {
            var map=(System.Collections.IDictionary)Domain.GetField("tacticalQueues",F).GetValue(domain);var q=map[id];var e=q.GetType().GetField(slot,F).GetValue(q);
            return e!=null&&(bool)e.GetType().GetField("Interrupted",F).GetValue(e);
        }
        [Test] public void IndependentGroupsUseExactRadiusSeparatedEndpointsAndReleaseOnActualArrival()
        {
            int a=Spawn(-18,-14),b=Spawn(-24,-14,PlayableEntityKind.Explorer);Move(new[]{a},new NavPoint(-10,-14));Move(new[]{b},new NavPoint(-10,-14));Complete();
            var rows=nav.ArrivalReservations;Assert.AreEqual(2,rows.Count);Assert.AreNotEqual(rows[0].Group,rows[1].Group);
            double spacing=Math.Max(PlayableProfile.Default.Navigation.ArrivalSlotSpacing,4*Math.Max(rows[0].Radius,rows[1].Radius)+2*PlayableProfile.Default.Navigation.ArrivalTolerance+2*PlayableProfile.Default.Navigation.LocalGridCell);
            double x=rows[0].Endpoint.Position.X-rows[1].Endpoint.Position.X,z=rows[0].Endpoint.Position.Z-rows[1].Endpoint.Position.Z;Assert.GreaterOrEqual(x*x+z*z,spacing*spacing-1e-9);
            Assert.AreNotEqual(rows[0].Radius,rows[1].Radius);Assert.AreNotEqual(rows[0].Profile,rows[1].Profile);
            for(int tick=0;tick<20000&&nav.Crowd.Units.Any(u=>u.Moving);tick++)Call("Step",1d/30);
            Assert.True(nav.Crowd.Units.Where(u=>u.Id==a||u.Id==b).All(u=>u.Outcome==NavigationOutcome.Arrived));Assert.AreEqual(0,nav.ArrivalReservations.Count);
            var parked=nav.CaptureArrivalOccupancySnapshot();Assert.AreEqual(2,parked.Occupants.Count(o=>o.Kind==NavArrivalOccupantKind.Actor&&o.BlocksPlacement&&(o.ActorId==a||o.ActorId==b)));
            Assert.NotNull(Call("SpatialCompletion",a,PlayableOwner.Player));byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());Assert.AreEqual(0,nav.ArrivalReservations.Count);
        }
        [Test] public void ShiftTailHasNoWorkAndActivationPreservesIssuanceWithoutEarlyReceiptOrInterruption()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));long request=nav.CaptureState().RequestSequence;
            Move(new[]{id},new NavPoint(-20,-22),append:true);Assert.AreEqual(request,nav.CaptureState().RequestSequence);Assert.AreEqual(1,nav.AllocationCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);
            Complete();Assert.False(Interrupted(id,"Active"));Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));
            for(int tick=0;tick<20000&&nav.CaptureState().RequestSequence==request;tick++)Call("Step",1d/30);
            Assert.Greater(nav.CaptureState().RequestSequence,request);Assert.AreEqual(1,nav.AllocationCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.False(Interrupted(id,"Pending"));
            var group=nav.GroupFor(id);Assert.AreEqual(2,group.CommandSequence);Assert.AreEqual(0,group.IssuedTick);
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());Complete();Assert.False(Interrupted(id,"Active"));Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));
            Assert.AreEqual(group.GroupId,nav.ArrivalReservations.Single().Group);Assert.AreEqual(group.CommandSequence,nav.ArrivalReservations.Single().Command);
        }
        [Test] public void SubsetCancellationDoesNotInvalidateHealthyOriginalMember()
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();Pump(1);var group=nav.GroupFor(b);
            Stop(new[]{a});Complete();Assert.AreEqual(1,nav.AppliedResults);var row=nav.ArrivalReservations.Single();Assert.AreEqual(b,row.Entity);Assert.AreEqual(group.GroupId,row.Group);Assert.AreEqual(group.CommandSequence,row.Command);Assert.AreEqual(0,nav.Crowd.Units.Single(u=>u.Id==a).InstalledRequest);
            Assert.Null(Call("SpatialCompletion",a,PlayableOwner.Player));Assert.False(Interrupted(b,"Active"));RestoreOnlyBytes(Bytes());
        }
        [Test] public void HoldBindingChangeRetriesTheCurrentIntentWithoutNewIssuanceOrFalseInterruption()
        {
            int id=Spawn(-18,-14),holder=Spawn(-24,-9);Stop(new[]{holder},hold:true);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();Pump(1);
            var original=nav.GroupFor(id);long request=nav.PendingRequestFor(id).Request;Stop(new[]{holder});Complete();
            var installed=nav.ArrivalReservations.Single();Assert.Greater(installed.Request,request);Assert.AreEqual(original.GroupId,installed.Group);Assert.AreEqual(original.CommandSequence,installed.Command);Assert.AreEqual(original.OrderRevision,installed.Root);
            Assert.False(Interrupted(id,"Active"));Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));RestoreOnlyBytes(Bytes());
        }
        [Test] public void GeometryChangeRejectsTheOldAttemptAndRetainsCurrentRootIdentity()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();Pump(1);var original=nav.GroupFor(id);long request=nav.PendingRequestFor(id).Request;
            var old=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);var next=new NavGeometry(old.HalfExtent,old.Obstacles.ToArray(),old.Revision+1);
            Domain.GetProperty("Geometry",F).SetValue(domain,next);nav.ChangeGeometry(next);Complete();
            var installed=nav.ArrivalReservations.Single();Assert.Greater(installed.Request,request);Assert.AreEqual(original.GroupId,installed.Group);Assert.AreEqual(original.CommandSequence,installed.Command);Assert.False(Interrupted(id,"Active"));RestoreOnlyBytes(Bytes());
        }
        [TestCase(false)][TestCase(true)] public void HiddenOccupancyAndObstacleFactsCannotChangeVisibleArmyPreflight(bool withdrawal)
        {
            int id=Spawn(-18,-14);var action=new PlayableAiAction(1,"player-1",PlayableProfile.Default.ProfileId,PlayableProfile.Default.Revision,1,0,PlayableCommandKind.Move,new[]{id},new NavPoint(-10,-14),seed:7);
            string method=withdrawal?"ArmyWithdrawalSafe":"ArmyMoveSafe";Assert.True((bool)Call(method,action));
            var baseGeometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);var hidden=new NavObstacle(-14,-32,-13,32);
            var geometry=new NavGeometry(baseGeometry.HalfExtent,baseGeometry.Obstacles.Concat(new[]{hidden}).ToArray(),baseGeometry.Revision+1);
            Domain.GetProperty("Geometry",F).SetValue(domain,geometry);nav.ChangeGeometry(geometry);
            int enemy=(int)Call("SpawnUnit",new NavPoint(22,14),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Assert.True((bool)Call(method,action),"same visible facts must preserve preflight despite hidden occupancy and an unobserved long wall");
            var visible=new NavObstacle(-10.8,-14.8,-9.2,-13.2);var observed=new NavGeometry(baseGeometry.HalfExtent,baseGeometry.Obstacles.Concat(new[]{visible}).ToArray(),geometry.Revision+1);
            Domain.GetProperty("Geometry",F).SetValue(domain,observed);nav.ChangeGeometry(observed);Assert.False((bool)Call(method,action),"a visible destination blocker remains effective");
            Assert.False(observed.IsFree(action.Target,PlayableProfile.Default.TankCollisionRadius));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},action.Target,origin:PlayableOrderOrigin.Human),null),"actual authority still rejects unsafe actual destination");
        }
        [TestCase(false)][TestCase(true)] public void CorruptArrivalAuthorityOrMissingMandatoryTailIsRejected(bool missing)
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();for(int n=0;n<20000&&!nav.HasReadyArrival;n++)Pump(1);Assert.True(nav.HasReadyArrival);
            var world=PlayableWorldState.Decode(Bytes());var state=WorldWire.Unpack(world.Navigation,WorldWire.ReadNavigationSessionState);
            if(missing)state.ArrivalAuthority=null;else state.ArrivalAuthority[0]^=0x10;
            world.Navigation=WorldWire.Pack(w=>WorldWire.Write(w,state));var error=Assert.Throws<TargetInvocationException>(()=>Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"a4b-authority-tests"}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
            Assert.AreEqual(1,nav.AllocationCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);
        }
        [Test] public void ActualMultiMemberSolverPendingRestoresFromFreshBytesWithExactNextQuotaAndOutcome()
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.ArrivalPhases.Any(p=>p.StartsWith("SolvePage:"));n++)Pump(4);
            Assert.True(nav.ArrivalPhases.Any(p=>p.StartsWith("SolvePage:")),string.Join(",",nav.ArrivalPhases));
            var phase=nav.ArrivalPhases.Single();byte[] pending=Bytes();Pump(4);nav.ApplyResults();byte[] next=Bytes();
            RestoreOnlyBytes(pending);CollectionAssert.AreEqual(pending,Bytes());Assert.AreEqual(phase,nav.ArrivalPhases.Single());
            Assert.True(nav.RoutePort.ArrivalReconstructing);
            for(int n=0;n<20000&&nav.RoutePort.ArrivalReconstructing;n++)Pump(4);Assert.False(nav.RoutePort.ArrivalReconstructing);
            Pump(4);nav.ApplyResults();CollectionAssert.AreEqual(next,Bytes(),"same next allotment after exact incremental graph/field/connector reconstruction");
            Complete();Assert.AreEqual(2,nav.AppliedResults);Assert.AreEqual(2,nav.ArrivalReservations.Count);
        }
        [Test] public void MixedProfilesAndHoldBlockedMemberDoNotCancelReachablePeer()
        {
            int trapped=Spawn(-18,-14),peer=Spawn(-24,-20,PlayableEntityKind.Explorer);
            int[] holds={Spawn(-16.5,-14),Spawn(-19.5,-14),Spawn(-18,-12.5),Spawn(-18,-15.5)};Stop(holds,hold:true);
            Move(new[]{trapped,peer},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();var original=nav.GroupFor(peer);Complete();
            Assert.AreEqual(1,nav.AppliedResults);var installed=nav.ArrivalReservations.Single();Assert.AreEqual(peer,installed.Entity);Assert.AreEqual(original.GroupId,installed.Group);
            Assert.AreEqual(PlayableProfile.Default.ExplorerCollisionRadius,installed.Radius);Assert.IsNotEmpty(installed.Hold);
            Assert.AreEqual(0,nav.Crowd.Units.Single(u=>u.Id==trapped).InstalledRequest);Assert.AreEqual(original.CommandSequence,nav.GroupFor(peer).CommandSequence);
            Assert.False(Interrupted(peer,"Active"));byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());
            Assert.AreEqual(peer,nav.ArrivalReservations.Single().Entity);
        }
        [TestCase(false)][TestCase(true)] public void DuplicateOrWrongIncarnationReservationIsRejectedFromActualWorldBytes(bool incarnation)
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));Complete();
            var ledger=(System.Collections.IDictionary)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(nav);
            var row=(NavigationArrivalReservation)ledger[nav.ArrivalReservations.Single().Id];
            if(incarnation)row.Incarnation++;else ledger.Add(row.Id+1,row);
            byte[] bad=Bytes();var error=Assert.Throws<TargetInvocationException>(()=>Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bad,PlayableProfile.Default,7,"a4b-authority-tests"}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
        }
        private object FirstAttempt()
        {
            var table=(System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(nav);
            var row=table.Cast<object>().First();return row.GetType().GetProperty("Value").GetValue(row);
        }
        private void RejectFreshBytes(byte[] bad)
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bad,PlayableProfile.Default,7,"a4b-authority-tests"}));
            Assert.IsInstanceOf<ArgumentException>(error.InnerException);
        }
        [TestCase("cursor")][TestCase("quota")][TestCase("proof")]
        public void SemanticCursorQuotaAndDuplicateProofAreRejectedByFreshWorldWire(string defect)
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.HasReadyArrival;n++)Pump(1);Assert.True(nav.HasReadyArrival);
            var attempt=FirstAttempt();var type=attempt.GetType();
            if(defect=="cursor")type.GetField("SnapshotCandidate",F).SetValue(attempt,-1);
            else if(defect=="quota"){
                var journal=(System.Collections.IList)type.GetField("Journal",F).GetValue(attempt);
                journal[0].GetType().GetField("Allocation",F).SetValue(journal[0],-1);
            }else{
                var work=(NavigationArrivalWork)type.GetField("Work",F).GetValue(attempt);
                var proposals=typeof(NavigationArrivalWork).GetField("assignments",F).GetValue(work);var first=proposals.GetType().GetProperty("Item").GetValue(proposals,new object[]{0});proposals.GetType().GetMethod("Add",F).Invoke(proposals,new[]{first});
            }
            RejectFreshBytes(Bytes());
        }
        [Test] public void FreshCandidateOccupancyCursorKeepsExactNextAllotment()
        {
            int id=Spawn(-18,-14);for(int n=0;n<80;n++)Call("SpawnUnit",new NavPoint(10+(n%10)*1.6,15+(n/10)*1.6),n<40?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.ArrivalPhases.Any(p=>p.StartsWith("ScanCandidates:")&&int.Parse(p.Split(':')[2])>0);n++)Pump(1);
            Assert.True(nav.ArrivalPhases.Any(p=>p.StartsWith("ScanCandidates:")&&int.Parse(p.Split(':')[2])>0),string.Join(",",nav.ArrivalPhases));
            byte[] before=Bytes();string phase=nav.ArrivalPhases.Single();Pump(1);nav.ApplyResults();byte[] next=Bytes();
            RestoreOnlyBytes(before);CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(phase,nav.ArrivalPhases.Single());
            for(int n=0;n<20000&&nav.RoutePort.ArrivalReconstructing;n++)Pump(1);Assert.False(nav.RoutePort.ArrivalReconstructing);
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(next,Bytes());Complete();Assert.AreEqual(1,nav.AppliedResults);
        }
        [Test] public void ActualDeathReleasesOnlyItsRecordsAndFreshPeerContinues()
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(-10,-14));Complete();
            Move(new[]{a,b},new NavPoint(-12,-20));nav.PrepareDeliveryBarrier();Pump(1);Call("Damage",a,100000);
            Assert.False(nav.ArrivalReservations.Any(r=>r.Entity==a));Assert.False(nav.Crowd.Units.Any(u=>u.Id==a));
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());Complete();
            Assert.AreEqual(b,nav.ArrivalReservations.Single().Entity);Assert.AreEqual(0,nav.AllocationCount);
        }
        [Test] public void ActualRestartRejectsObsoleteAllocatorAndClearsLedger()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();Pump(1);
            domain=Activator.CreateInstance(Domain,F,null,new object[]{PlayableProfile.Default,2L,false},null);
            Assert.AreEqual(PlayableCommandStatus.StaleGeneration,Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Stop,new[]{id}),null));
            nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);for(int n=0;n<5;n++)Pump();
            Assert.AreEqual(0,nav.PendingCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.AreEqual(0,nav.AppliedResults);
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());
        }
        [Test] public void ActualProfileRebindRetriesCurrentIssuanceAndFreshBytesKeepEffectiveTerms()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();Pump(1);
            var original=nav.GroupFor(id);long obsolete=nav.PendingRequestFor(id).Request;
            var data=PlayableProfile.Default.CopyData();data.revision++;data.tankSpeed*=1.1;var next=PlayableProfile.Create(data);
            Assert.Null(Call("ValidateBalance",next));Call("ApplyBalance",next);
            Assert.Greater(nav.PendingRequestFor(id).Request,obsolete);Assert.AreEqual(0,nav.Requests.Count);
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes,next);CollectionAssert.AreEqual(bytes,Bytes());Complete();
            var row=nav.ArrivalReservations.Single();Assert.AreEqual(original.CommandSequence,row.Command);Assert.AreEqual(original.IssuedTick,row.IssuedTick);
            Assert.AreEqual(NavigationAdmission.ExactProfileBinding(next.Navigation.ForUnit(next.TankCollisionRadius,next.TankSpeed,next.TankTurnSpeed)),row.Profile);
            Assert.AreEqual(next.TankSpeed,nav.Crowd.Units.Single(u=>u.Id==id).Speed);Assert.False(Interrupted(id,"Active"));
        }
        [TestCase(6)][TestCase(7)] public void PreviousGroupWireReadersAcceptActualV9WorldBytes(int version)
        {
            byte[] current=Bytes();var world=PlayableWorldState.Decode(current);var state=WorldWire.Unpack(world.Navigation,WorldWire.ReadNavigationSessionState);
            Assert.AreEqual(0,state.Groups.Length);Assert.AreEqual(0,state.AllocationRequestIndices.Length);Assert.NotNull(state.ArrivalAuthority);
            // Current v9 appends registered/delivered catalogs and the Host BODY
            // after arrival. Remove that complete tail before selecting v6/v7.
            int hostTail=state.HostAuthority==null?0:12+4*((state.RegisteredRequestIndices?.Length??0)+(state.DeliveredRequestIndices?.Length??0))+state.HostAuthority.Length;
            int tail=4+1+4+state.ArrivalAuthority.Length+hostTail+(version==6?4:0);
            var old=world.Navigation.Take(world.Navigation.Length-tail).ToArray();int tag=-1;
            for(int n=0;n<=old.Length-8;n++)if(BitConverter.ToInt32(old,n)==0x47525031){Assert.AreEqual(-1,tag);tag=n;}
            Assert.GreaterOrEqual(tag,0);Buffer.BlockCopy(BitConverter.GetBytes(version),0,old,tag+4,4);world.Navigation=old;
            byte[] legacy=world.Encode();RestoreOnlyBytes(legacy);Assert.AreEqual(0,nav.PendingCount);Assert.AreEqual(state.Crowd.Units.Length,nav.Crowd.Units.Count);
            Assert.AreEqual(0,nav.ArrivalReservations.Count);RestoreOnlyBytes(Bytes());
        }
        [Test] public void UnknownDequeuedAssignedRequestStillRejectsActualWorldCapture()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.HasReadyArrival;n++)Pump(1);Assert.True(nav.HasReadyArrival);nav.PrepareDeliveryBarrier();
            Assert.True(nav.Requests.TryDequeue(out var unknown));Assert.AreEqual(id,unknown.Entity);
            var error=Assert.Throws<TargetInvocationException>(()=>Bytes());Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
        }
        [Test] public void UnreachableFirstCandidateUsesLaterReachableTypedEndpoint()
        {
            int id=Spawn(-18,-14);var requested=new NavPoint(-10,-14);
            var holders=Enumerable.Range(0,12).Select(n=>Spawn(requested.X+4*Math.Cos(n*Math.PI/6),requested.Z+4*Math.Sin(n*Math.PI/6))).ToArray();
            Stop(holders,hold:true);
            Move(new[]{id},requested);nav.PrepareDeliveryBarrier();Complete();Assert.AreEqual(1,nav.AppliedResults);
            var row=nav.ArrivalReservations.Single();Assert.AreNotEqual(requested,row.Endpoint.Position);Assert.Less(row.Endpoint.Position.X,-14);
            Assert.AreEqual(NavLocation.FlatSurface,row.Endpoint.SurfaceId);Assert.IsNotEmpty(row.Proof);Assert.False(Interrupted(id,"Active"));
            byte[] bytes=Bytes();RestoreOnlyBytes(bytes);CollectionAssert.AreEqual(bytes,Bytes());Assert.AreEqual(row.Proof,nav.ArrivalReservations.Single().Proof);
        }
        [Test] public void ExactCombinedRetentionBoundaryDoesNotRefillOrJournalEmptyRetryAndReleaseResumes()
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(10,-14));nav.PrepareDeliveryBarrier();
            for(int n=0;n<20000&&!nav.ArrivalPhases.Any(p=>p.StartsWith("SolvePage:"));n++)Pump(4);
            Assert.True(nav.ArrivalPhases.Any(p=>p.StartsWith("SolvePage:")));byte[] before=Bytes();
            long remaining=LayeredNavigationScheduler.MaxRetainedBytes-nav.ArrivalRetainedBytes-64;Assert.Greater(remaining,0);
            nav.SetHostRetainedBytes(remaining);
            Assert.False(nav.RoutePort.AdvanceArrivals(4));CollectionAssert.AreEqual(before,Bytes());
            Assert.False(nav.RoutePort.AdvanceArrivals(4));CollectionAssert.AreEqual(before,Bytes(),"same blocked retry cannot refill quota/epoch or append empty journal");
            nav.SetHostRetainedBytes(0);nav.SignalHost();Assert.True(nav.RoutePort.AdvanceArrivals(4));Assert.False(before.SequenceEqual(Bytes()),"release resumes the same frontier");
        }
        [Test] public void PackedHeapAndLegacyDictionaryKeepExactEveryOperationAndCheckpointAcrossPages()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var graph=LayeredNavigationGraphCompiler.Compile(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('a',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000));
            var candidates=Enumerable.Range(0,8).Select(n=>new NavLocation(new NavPoint(-2+n%4,-1+n/4),NavLocation.FlatSurface)).ToArray();
            var region=new NavTerminalRegion("heap-equivalence",1,candidates[0],candidates);var packed=new NavSharedTerminalField(graph,region,2048);
            var legacy=new NavSharedTerminalField(graph,region,2048,true);
            int peak=0;
            for(int step=0;step<20000&&packed.Status==NavSolveStatus.Pending;step++){
                Assert.AreEqual(legacy.Advance(1).Consumed,packed.Advance(1).Consumed);Assert.AreEqual(legacy.Status,packed.Status);
                Assert.AreEqual(legacy.LabelCount,packed.LabelCount);Assert.AreEqual(legacy.FrontierCount,packed.FrontierCount);peak=Math.Max(peak,packed.FrontierCount);
                for(int c=0;c<candidates.Length;c++)for(int n=0;n<graph.Nodes.Count;n++){
                    Assert.AreEqual(legacy.TryGet(n,c,out var a),packed.TryGet(n,c,out var b));Assert.AreEqual(a,b);
                    Assert.AreEqual(legacy.TryNext(n,c,out var x),packed.TryNext(n,c,out var y));Assert.AreEqual(x,y);
                }
                var l=legacy.Save();var p=packed.Save();
                foreach(string key in new[]{"Phase","Candidate","Node","Current","ArcCursor","CurrentCandidate"})Assert.AreEqual(l.GetType().GetField(key,F).GetValue(l),p.GetType().GetField(key,F).GetValue(p));
                CollectionAssert.AreEqual((System.Array)l.GetType().GetField("Frontier",F).GetValue(l),(System.Array)p.GetType().GetField("Frontier",F).GetValue(p));
                long retained=packed.EstimatedRetainedBytes;l.GetType().GetProperty("Version").SetValue(l,1);
                legacy=NavSharedTerminalField.Restore(graph,region,2048,l);packed=NavSharedTerminalField.Restore(graph,region,2048,p);
                Assert.AreEqual(retained,packed.EstimatedRetainedBytes,"restore exact allocated heap/position/label capacities, including popped entries");
            }
            Assert.AreEqual(NavSolveStatus.Ready,packed.Status);Assert.Greater(peak,256,"cross actual label, heap and lazy-position page boundaries");
            Assert.Less(packed.EstimatedRetainedBytes,legacy.EstimatedRetainedBytes);
        }
        [TestCase("duplicate")][TestCase("cost")][TestCase("node")][TestCase("position")][TestCase("mode")]
        public void PackedFieldCheckpointRejectsCorruptFrontierAndAllocatedPages(string defect)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var graph=LayeredNavigationGraphCompiler.Compile(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('a',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000));
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("heap-corruption",1,at,new[]{at});var field=new NavSharedTerminalField(graph,region,2048);
            field.Advance(1);var state=field.Save();var type=state.GetType();var entries=(System.Array)type.GetField("Frontier",F).GetValue(state);Assert.AreEqual(1,entries.Length);
            if(defect=="mode")type.GetField("PackedStorage",F).SetValue(state,false);
            else if(defect=="position")type.GetField("FrontierPositionPages",F).SetValue(state,Array.Empty<int>());
            else if(defect=="duplicate"){
                var duplicated=System.Array.CreateInstance(entries.GetType().GetElementType(),2);duplicated.SetValue(entries.GetValue(0),0);duplicated.SetValue(entries.GetValue(0),1);type.GetField("Frontier",F).SetValue(state,duplicated);
            }else{var item=entries.GetValue(0);item.GetType().GetField(defect=="node"?"Node":"Cost",F).SetValue(item,defect=="node"?(object)graph.Nodes.Count:(object)123456d);entries.SetValue(item,0);}
            Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(graph,region,2048,state));
        }
        [Test] public void SimultaneousLabelHeapAndPositionGrowthWaitsForCombinedCapAndOwnerRelease()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var terms=new NavGraphProfile(new string('a',64),"distance",1,1);
            var candidates=Enumerable.Range(0,8).Select(n=>new NavLocation(new NavPoint(-2+n%4,-1+n/4),NavLocation.FlatSurface)).ToArray();
            var region=new NavTerminalRegion("heap-growth",1,candidates[0],candidates);
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,candidates[0],candidates[1],"");
            var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(new NavScheduledRequest(provider,clearance,terms,new NavGraphCompileLimits(2000,2000,10000),region,subscription,2048)));
            NavSharedTerminalField field=null;long epoch=0;
            for(;epoch<5000;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(2048,1,0,256,0),_=>true);
                var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",F).GetValue(scheduler);var first=((NavPersistentQueue<LayeredNavigationScheduler.Job>)jobs).First;Assert.NotNull(first);
                var job=(LayeredNavigationScheduler.Job)first;field=job.Field;
                if(field!=null&&field.LabelCount==256)break;
            }
            Assert.NotNull(field);Assert.AreEqual(256,field.LabelCount);int probes=0;typeof(LayeredNavigationGraph).GetField("BeforeAttachmentProbe",F).SetValue(field.Graph,(Action)(()=>probes++));long growth=(long)typeof(NavSharedTerminalField).GetProperty("NextGrowthBytes",F).GetValue(field);
            Assert.GreaterOrEqual(growth,4128+4128+1056,"one next operation allocates label+heap+position pages together");
            var labelDirectory=(NavStorageDirectory<NavPackedFieldCell>)typeof(NavPackedFieldStorage).GetField("pages",F).GetValue(typeof(NavSharedTerminalField).GetField("packed",F).GetValue(field));
            var frontier=(NavFieldFrontier)typeof(NavSharedTerminalField).GetField("frontier",F).GetValue(field);
            var heapDirectory=(NavStorageDirectory<NavQueueEntry>)typeof(NavFieldFrontier).GetField("heap",F).GetValue(frontier);
            var positionDirectory=(NavStorageDirectory<int>)typeof(NavFieldFrontier).GetField("positions",F).GetValue(frontier);
            var pagesBefore=new[]{labelDirectory.AllocatedPages,heapDirectory.AllocatedPages,positionDirectory.AllocatedPages};
            var blocksBefore=new[]{labelDirectory.Blocks,heapDirectory.Blocks,positionDirectory.Blocks};
            var capacitiesBefore=new[]{labelDirectory.Capacity,heapDirectory.Capacity,positionDirectory.Capacity};
            long labelPageBytes=labelDirectory.Get(0).StorageBytes,heapPageBytes=heapDirectory.Get(0).StorageBytes,positionPageBytes=positionDirectory.Get(0).StorageBytes;
            Assert.AreEqual(NavStorageLayout.Charge(96L+labelDirectory.Get(0).Values.Length*NavPackedFieldStorage.CellBytes),labelPageBytes);
            Assert.AreEqual(NavStorageLayout.Charge(96L+heapDirectory.Get(0).Values.Length*NavStorageLayout.ValueBytes(typeof(NavQueueEntry))),heapPageBytes);
            Assert.AreEqual(NavStorageLayout.Charge(96L+positionDirectory.Get(0).Values.Length*sizeof(int)),positionPageBytes);
            long netPageGrowth=labelPageBytes+heapPageBytes+positionPageBytes;
            long directoryHeadroom=labelDirectory.WriteGrowthBytes(labelDirectory.Get(0).Values.Length,NavPackedFieldStorage.CellBytes)-labelPageBytes+
                heapDirectory.WriteGrowthBytes(heapDirectory.Get(0).Values.Length,NavStorageLayout.ValueBytes(typeof(NavQueueEntry)))-heapPageBytes+
                positionDirectory.WriteGrowthBytes(positionDirectory.Get(0).Values.Length,sizeof(int))-positionPageBytes;
            Assert.AreEqual(netPageGrowth+directoryHeadroom,growth,"gross admission includes replaced COW directory paths; committed net counts new pages only");
            long retained=scheduler.RetainedBytes;scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-growth+1);
            Assert.AreEqual(0,scheduler.Advance(++epoch,new NavEpochQuota(0,1,0,0,0),_=>true));Assert.AreEqual(retained,scheduler.RetainedBytes);Assert.AreEqual(256,field.LabelCount);Assert.AreEqual(0,probes);
            Assert.AreEqual(0,scheduler.Advance(epoch,new NavEpochQuota(0,1,0,0,0),_=>true));Assert.AreEqual(0,probes);
            scheduler.ReserveExternalBytes(0);Assert.AreEqual(1,scheduler.Advance(epoch,new NavEpochQuota(0,1000,0,0,0),_=>true));
            field=((LayeredNavigationScheduler.Job)FirstScheduledJob(scheduler)).Field;
            var labelsAfter=(NavStorageDirectory<NavPackedFieldCell>)typeof(NavPackedFieldStorage).GetField("pages",F).GetValue(typeof(NavSharedTerminalField).GetField("packed",F).GetValue(field));
            var frontierAfter=(NavFieldFrontier)typeof(NavSharedTerminalField).GetField("frontier",F).GetValue(field);
            var heapAfter=(NavStorageDirectory<NavQueueEntry>)typeof(NavFieldFrontier).GetField("heap",F).GetValue(frontierAfter);
            var positionsAfter=(NavStorageDirectory<int>)typeof(NavFieldFrontier).GetField("positions",F).GetValue(frontierAfter);
            CollectionAssert.AreEqual(pagesBefore.Select(n=>n+1).ToArray(),new[]{labelsAfter.AllocatedPages,heapAfter.AllocatedPages,positionsAfter.AllocatedPages});
            CollectionAssert.AreEqual(blocksBefore,new[]{labelsAfter.Blocks,heapAfter.Blocks,positionsAfter.Blocks});
            CollectionAssert.AreEqual(capacitiesBefore,new[]{labelsAfter.Capacity,heapAfter.Capacity,positionsAfter.Capacity});
            Assert.AreEqual(retained+netPageGrowth,scheduler.RetainedBytes);Assert.AreEqual(257,field.LabelCount);Assert.AreEqual(1,probes);
            Assert.AreEqual(0,scheduler.Advance(epoch,new NavEpochQuota(0,1000,0,0,0),_=>true),"same epoch does not refill spent field quota");
        }
        private static object FirstScheduledJob(LayeredNavigationScheduler scheduler)
        {
            var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",F).GetValue(scheduler);
            var first=((NavPersistentQueue<LayeredNavigationScheduler.Job>)jobs).First;Assert.NotNull(first);
            return first;
        }
        [Test] public void FirstSeedReservesConstructionAndGrowthAndBlockedRetriesDoNotProbe()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(new string('a',64),"distance",1,1);
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);
            var region=new NavTerminalRegion("first-seed",1,at,new[]{at});
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,at,at,"");
            var scheduler=new LayeredNavigationScheduler();
            Assert.True(scheduler.Submit(new NavScheduledRequest(provider,clearance,profile,new NavGraphCompileLimits(2000,2000,10000),region,subscription,2048)));
            object job=null;LayeredNavigationGraph graph=null;long epoch=0;
            for(;epoch<5000;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(2048,0,0,256,0),_=>true);job=FirstScheduledJob(scheduler);
                graph=(LayeredNavigationGraph)job.GetType().GetProperty("Graph",F).GetValue(job);if(graph!=null)break;
            }
            Assert.NotNull(graph);Assert.Null(job.GetType().GetProperty("Field",F).GetValue(job));
            int probes=0;typeof(LayeredNavigationGraph).GetField("BeforeAttachmentProbe",F).SetValue(graph,(Action)(()=>probes++));
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            long initial=NavSharedTerminalField.InitialStorageBytes(graph,region,2048);
            long growth=(long)typeof(NavSharedTerminalField).GetMethod("FirstStepGrowthBytes",flags).Invoke(null,new object[]{graph,region});
            Assert.AreEqual(11392,growth);long retained=scheduler.RetainedBytes;
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-initial-growth+1);
            var before=scheduler.Counters;var quota=new NavEpochQuota(0,1,0,0,0);
            Assert.AreEqual(0,scheduler.Advance(++epoch,quota,_=>true));
            Assert.AreEqual(0,scheduler.Advance(epoch,quota,_=>true));Assert.AreEqual(0,probes);
            Assert.Null(job.GetType().GetProperty("Field",F).GetValue(job));Assert.AreEqual(retained,scheduler.RetainedBytes);
            Assert.AreEqual(before.LogicalFieldWork,scheduler.Counters.LogicalFieldWork);Assert.AreEqual(before.PhysicalFieldWork,scheduler.Counters.PhysicalFieldWork);
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-initial-growth);
            Assert.AreEqual(1,scheduler.Advance(epoch,quota,_=>true));Assert.AreEqual(1,probes);
            job=FirstScheduledJob(scheduler);
            var field=(NavSharedTerminalField)job.GetType().GetProperty("Field",F).GetValue(job);
            Assert.AreEqual(1,field.LabelCount);Assert.AreEqual(1,field.FrontierCount);
            var firstLabels=(NavStorageDirectory<NavPackedFieldCell>)typeof(NavPackedFieldStorage).GetField("pages",F).GetValue(typeof(NavSharedTerminalField).GetField("packed",F).GetValue(field));
            var firstFrontier=(NavFieldFrontier)typeof(NavSharedTerminalField).GetField("frontier",F).GetValue(field);
            var firstHeap=(NavStorageDirectory<NavQueueEntry>)typeof(NavFieldFrontier).GetField("heap",F).GetValue(firstFrontier);
            var firstPositions=(NavStorageDirectory<int>)typeof(NavFieldFrontier).GetField("positions",F).GetValue(firstFrontier);
            CollectionAssert.AreEqual(new[]{1,1,1},new[]{firstLabels.AllocatedPages,firstHeap.AllocatedPages,firstPositions.AllocatedPages});
            CollectionAssert.AreEqual(new[]{1,1,1},new[]{firstLabels.Blocks,firstHeap.Blocks,firstPositions.Blocks});
            long committedSeedGrowth=3*NavStorageLayout.Charge(352)+
                NavStorageLayout.Charge(96L+firstLabels.Get(0).Values.Length*NavPackedFieldStorage.CellBytes)+
                NavStorageLayout.Charge(96L+firstHeap.Get(0).Values.Length*NavStorageLayout.ValueBytes(typeof(NavQueueEntry)))+
                NavStorageLayout.Charge(96L+firstPositions.Get(0).Values.Length*sizeof(int));
            Assert.AreEqual(committedSeedGrowth,field.EstimatedRetainedBytes-initial);
            Assert.GreaterOrEqual(growth,committedSeedGrowth,"The original conservative admission reserve covers the committed page and block layouts.");
            Assert.AreEqual(retained+initial+committedSeedGrowth,scheduler.RetainedBytes);
            Assert.AreEqual(before.LogicalFieldWork+1,scheduler.Counters.LogicalFieldWork);Assert.AreEqual(before.PhysicalFieldWork+1,scheduler.Counters.PhysicalFieldWork);
            Assert.AreEqual(0,scheduler.Advance(epoch,quota,_=>true));Assert.AreEqual(1,probes);
            long next=(long)typeof(NavSharedTerminalField).GetProperty("NextGrowthBytes",F).GetValue(field);
            Assert.AreEqual(0,probes-1,"growth inspection never probes geometry");
            // The next seed shares already allocated pages; capacity admission still must not probe.
            long ExpectedDirectoryWriteGrowth(object directory,int length,int width)
            {
                int depth=(int)directory.GetType().GetField("depth",F).GetValue(directory);
                return NavStorageLayout.Charge(352)*(depth+2)+NavStorageLayout.Charge(96L+(long)length*width);
            }
            long nextLayout=ExpectedDirectoryWriteGrowth(firstLabels,64,NavPackedFieldStorage.CellBytes)+
                ExpectedDirectoryWriteGrowth(firstHeap,256,NavStorageLayout.ValueBytes(typeof(NavQueueEntry)))+
                ExpectedDirectoryWriteGrowth(firstPositions,256,sizeof(int));
            Assert.AreEqual(nextLayout,next,"The next packed insertion reserves the exact three directory write bounds, including already allocated-page COW paths.");
            var beforeNext=scheduler.Counters;retained=scheduler.RetainedBytes;
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-next+1);
            Assert.AreEqual(0,scheduler.Advance(++epoch,quota,_=>true));Assert.AreEqual(1,probes);
            Assert.AreEqual(retained,scheduler.RetainedBytes);Assert.AreEqual(beforeNext.PhysicalFieldWork,scheduler.Counters.PhysicalFieldWork);
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-next);
            Assert.AreEqual(1,scheduler.Advance(epoch,quota,_=>true));Assert.AreEqual(2,probes);
            Assert.AreEqual(before.PhysicalFieldWork+2,scheduler.Counters.PhysicalFieldWork);
        }
        [TestCase(false)][TestCase(true)] public void ActualPartitionPackedV1RestoresThroughV2AfterFrontierDrains(bool drained)
        {
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),"");
            var clearance=new NavClearanceProfile("ground",1,"ground",0,.58);
            Assert.True(provider.TryLocate(new NavPoint(-20,18),clearance,out var at));
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clearance,new NavGraphProfile(new string('a',64),"distance",1,.8),new NavGraphCompileLimits(300000,150000,600000));
            var region=new NavTerminalRegion("partition-v1",1,at,new[]{at});var field=new NavSharedTerminalField(graph,region,300000);
            int peak=0;bool captured=false;
            for(int step=0;step<1000000&&field.Status==NavSolveStatus.Pending;step++){
                field.Advance(1);peak=Math.Max(peak,field.FrontierCount);
                if(!drained&&peak>256&&field.FrontierCount>0&&field.FrontierCount<peak){captured=true;break;}
            }
            if(drained){Assert.AreEqual(NavSolveStatus.Ready,field.Status);Assert.AreEqual(0,field.FrontierCount);}else Assert.True(captured);
            var v1=field.Save();v1.GetType().GetProperty("Version").SetValue(v1,1);
            v1.GetType().GetField("FrontierHeapCapacity",F).SetValue(v1,0);v1.GetType().GetField("FrontierPositionPages",F).SetValue(v1,null);
            var imported=NavSharedTerminalField.Restore(graph,region,300000,v1);var v2=imported.Save();
            // The current checkpoint is v3 (physical heap/tree shape); v1 and
            // v2 import remain separate historical contracts. Exercise v2
            // explicitly instead of pretending the current writer emits it.
            Assert.AreEqual(3,v2.Version);v2.GetType().GetProperty("Version").SetValue(v2,2);Assert.AreEqual(2,v2.Version);
            var restored=NavSharedTerminalField.Restore(graph,region,300000,v2);Assert.AreEqual(3,restored.Save().Version);
            Assert.AreEqual(imported.EstimatedRetainedBytes,restored.EstimatedRetainedBytes);Assert.AreEqual(field.LabelCount,restored.LabelCount);Assert.AreEqual(field.FrontierCount,restored.FrontierCount);
            Assert.Greater(((int[])v2.GetType().GetField("FrontierPositionPages",F).GetValue(v2)).Length,0,"position pages cover popped labels even with zero frontier");
            for(int step=0;step<1000000&&field.Status==NavSolveStatus.Pending;step++){
                Assert.AreEqual(field.Advance(1).Consumed,restored.Advance(1).Consumed);Assert.AreEqual(field.LabelCount,restored.LabelCount);Assert.AreEqual(field.FrontierCount,restored.FrontierCount);Assert.AreEqual(field.Status,restored.Status);
            }
            Assert.AreEqual(NavSolveStatus.Ready,restored.Status);
            for(int n=0;n<graph.Nodes.Count;n++){Assert.AreEqual(field.TryGet(n,0,out var a),restored.TryGet(n,0,out var b));Assert.AreEqual(a,b);Assert.AreEqual(field.TryNext(n,0,out var x),restored.TryNext(n,0,out var y));Assert.AreEqual(x,y);}
        }
        [TestCase("negative")][TestCase("over-cap")][TestCase("count-mismatch")][TestCase("smaller-max")]
        public void PackedLabelCountCannotBypassCapOrActualCount(string defect)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var graph=LayeredNavigationGraphCompiler.Compile(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('a',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000));
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("count-cap",1,at,new[]{at});var field=new NavSharedTerminalField(graph,region,256);
            for(int n=0;n<20000&&field.Status==NavSolveStatus.Pending;n++)field.Advance(1);
            Assert.AreEqual(NavSolveStatus.Ready,field.Status);Assert.Greater(field.LabelCount,1);Assert.Less(field.LabelCount,256);
            var state=field.Save();int cap=256;
            if(defect=="smaller-max")cap=field.LabelCount-1;
            else state.GetType().GetField("PackedLabels",F).SetValue(state,defect=="negative"?-1:defect=="over-cap"?257:field.LabelCount-1);
            Assert.AreEqual(1,(cap+255)/256,"smaller max remains within the same rounded heap page");
            Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.Restore(graph,region,cap,state));
        }
        private static LayeredNavigationScheduler AttemptScheduler(string key="attempt-test")
        {
            var scheduler=new LayeredNavigationScheduler();
            using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","").ToLowerInvariant();
            typeof(LayeredNavigationScheduler).GetMethod("BindAttemptArtifacts",F).Invoke(scheduler,new object[]{key});return scheduler;
        }
        private static NavScheduledRequest AttemptRequest(long id,ILayeredNavigationProvider provider=null,string profile="a",string regionId="attempt-region",string hold="",int maxLabels=2048)
        {
            provider=provider??NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var origin=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var end=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);
            return new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string(profile[0],64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000),
                new NavTerminalRegion(regionId,1,end,new[]{end}),new NavRouteSubscription(1,1,1,1,0,1,id,1,1,1,id,origin,end,hold),maxLabels);
        }
        private static NavScheduledCompletion FinishAttemptRequest(LayeredNavigationScheduler scheduler,long firstEpoch)
        {
            for(long epoch=firstEpoch;epoch<firstEpoch+1000;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(2048,2048,2048,2048,256),_=>true);
                if(scheduler.TryTake(_=>true,out var result))return result;
            }
            Assert.Fail("Attempt request did not complete under unchanged finite quota.");return null;
        }
        private static string SchedulerContinuation(LayeredNavigationScheduler scheduler)=>(string)typeof(LayeredNavigationScheduler).GetProperty("LogicalContinuationBinding",F).GetValue(scheduler);
        [Test] public void AttemptOwnerSurvivesNoSubscribersAndSiblingAcquiresWithoutDebtWithExactCheckpoint()
        {
            var first=AttemptRequest(1);var sibling=AttemptRequest(2,first.Provider);var scheduler=AttemptScheduler();Assert.True(scheduler.Submit(first));
            Assert.AreEqual(NavSolveStatus.Ready,FinishAttemptRequest(scheduler,0).Status);Assert.AreEqual(0,scheduler.PendingCount);Assert.AreEqual(0,scheduler.ReadyCount);
            Assert.True((bool)typeof(LayeredNavigationScheduler).GetProperty("HasAttemptOwner",F).GetValue(scheduler));
            long retained=scheduler.RetainedBytes;var counters=scheduler.Counters;var saved=scheduler.Save();
            var restored=LayeredNavigationScheduler.Restore(saved,Array.Empty<NavScheduledRequest>());
            Assert.AreEqual(retained,restored.RetainedBytes);Assert.AreEqual(SchedulerContinuation(scheduler),SchedulerContinuation(restored));
            Assert.AreEqual(counters.PeakCacheBytes,restored.Counters.PeakCacheBytes);Assert.AreEqual(counters.PhysicalFieldWork,restored.Counters.PhysicalFieldWork);
            Assert.True(scheduler.Submit(sibling));Assert.True(restored.Submit(sibling));
            var quota=new NavEpochQuota(0,0,0,1,0);long epoch=1001;
            Assert.AreEqual(1,scheduler.Advance(epoch,quota,_=>true));Assert.AreEqual(1,restored.Advance(epoch,quota,_=>true));
            Assert.AreEqual(counters.LogicalGraphWork,scheduler.Counters.LogicalGraphWork);Assert.AreEqual(counters.PhysicalGraphWork,scheduler.Counters.PhysicalGraphWork);
            Assert.AreEqual(SchedulerContinuation(scheduler),SchedulerContinuation(restored));Assert.AreEqual(scheduler.RetainedBytes,restored.RetainedBytes);
            var acquisition=LayeredNavigationScheduler.Restore(scheduler.Save(),new[]{sibling});
            Assert.AreEqual(scheduler.RetainedBytes,acquisition.RetainedBytes);Assert.AreEqual(SchedulerContinuation(scheduler),SchedulerContinuation(acquisition));
            var outcome=FinishAttemptRequest(scheduler,1002);var other=FinishAttemptRequest(acquisition,1002);
            Assert.AreEqual(NavSolveStatus.Ready,outcome.Status);Assert.AreEqual(outcome.Route.Cost,other.Route.Cost);
            Assert.AreEqual(counters.PhysicalFieldWork,scheduler.Counters.PhysicalFieldWork);Assert.AreEqual(counters.LogicalFieldWork,scheduler.Counters.LogicalFieldWork);
            Assert.AreEqual(counters.GraphBuilds,scheduler.Counters.GraphBuilds);Assert.AreEqual(counters.FieldBuilds,scheduler.Counters.FieldBuilds);
            Assert.AreEqual(SchedulerContinuation(scheduler),SchedulerContinuation(acquisition));
            Assert.True((bool)typeof(LayeredNavigationScheduler).GetMethod("EndAttemptArtifacts",F).Invoke(scheduler,new object[]{2002L,new NavEpochQuota(0,0,0,1,0)}));
            Assert.AreEqual(0,scheduler.RetainedBytes);Assert.AreEqual(0,scheduler.ProviderRetainedBytes);
        }
        [TestCase("profile")][TestCase("provider")][TestCase("hold-provider")][TestCase("page")][TestCase("label-cap")]
        public void AttemptOwnerReleasesIncompatibleArtifactAndRebuildsActualWork(string key)
        {
            var first=AttemptRequest(1);var scheduler=AttemptScheduler();Assert.True(scheduler.Submit(first));FinishAttemptRequest(scheduler,0);
            var provider=first.Provider;
            if(key=="provider"||key=="hold-provider")provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,new[]{new NavObstacle(-.2,1,.2,1.5)},2));
            var next=AttemptRequest(2,provider,key=="profile"?"b":"a",key=="page"?"another-page":"attempt-region",key=="hold-provider"?"changed-hold":"",key=="label-cap"?2047:2048);
            Assert.AreNotEqual(first.ArtifactKey,next.ArtifactKey);Assert.True(scheduler.Submit(next));long physical=scheduler.Counters.PhysicalGraphWork;
            Assert.AreEqual(1,scheduler.Advance(1001,new NavEpochQuota(0,0,0,1,0),_=>true));Assert.AreEqual(0,scheduler.Counters.CacheFields);
            Assert.False((bool)typeof(LayeredNavigationScheduler).GetProperty("HasAttemptOwner",F).GetValue(scheduler));
            Assert.AreEqual(NavSolveStatus.Ready,FinishAttemptRequest(scheduler,1002).Status);Assert.Greater(scheduler.Counters.PhysicalGraphWork,physical);
            scheduler.ClearWorld();Assert.AreEqual(0,scheduler.RetainedBytes);Assert.AreEqual(0,scheduler.ProviderRetainedBytes);
        }
        [Test] public void AttemptOwnerTransferNearCapDoesNotDuplicateGraphOrFieldAndCancellationReleases()
        {
            var request=AttemptRequest(1);var scheduler=AttemptScheduler();Assert.True(scheduler.Submit(request));long epoch=0;object job=null;LayeredNavigationCompileWork compiler=null;
            for(;epoch<5000;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(1,0,0,0,0),_=>true);job=FirstScheduledJob(scheduler);
                compiler=(LayeredNavigationCompileWork)job.GetType().GetProperty("Compiler",F).GetValue(job);
                if(compiler!=null&&compiler.Phase==LayeredNavigationCompileWork.Stage.WrapAdjacency&&(int)compiler.Save().GetType().GetField("WrapCursor",F).GetValue(compiler.Save())==compiler.SampleCount)break;
            }
            Assert.NotNull(compiler);Assert.AreEqual(LayeredNavigationCompileWork.Stage.WrapAdjacency,compiler.Phase);
            long reserve=Math.Max(4096L+256L*compiler.SampleCount+request.Provider.EstimatedRetainedBytes+128,512L*compiler.SampleCount+256L*request.Provider.Portals.Count+128);
            long retained=scheduler.RetainedBytes;var before=scheduler.Counters;
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-reserve+1);
            Assert.AreEqual(0,scheduler.Advance(++epoch,new NavEpochQuota(1,0,0,0,0),_=>true));Assert.AreEqual(before.PhysicalGraphWork,scheduler.Counters.PhysicalGraphWork);Assert.AreEqual(retained,scheduler.RetainedBytes);
            scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained-reserve);
            Assert.AreEqual(1,scheduler.Advance(epoch,new NavEpochQuota(1,0,0,0,0),_=>true));
            Assert.LessOrEqual(scheduler.RetainedBytes+(long)typeof(LayeredNavigationScheduler).GetProperty("ExternalReservationBytes",F).GetValue(scheduler),LayeredNavigationScheduler.MaxRetainedBytes);
            job=FirstScheduledJob(scheduler);
            Assert.AreSame(job.GetType().GetProperty("Graph",F).GetValue(job),typeof(LayeredNavigationScheduler).GetProperty("cachedGraph",F).GetValue(scheduler));
            scheduler.ReserveExternalBytes(0);NavSharedTerminalField field=null;bool drained=false;
            for(int n=0;n<20000;n++){
                scheduler.Advance(++epoch,new NavEpochQuota(0,1,1,0,0),_=>true);job=FirstScheduledJob(scheduler);
                field=(NavSharedTerminalField)job.GetType().GetProperty("Field",F).GetValue(job);
                if(field==null||field.LabelCount==0)continue;var state=field.Save();
                drained=field.Status==NavSolveStatus.Pending&&field.FrontierCount==0&&(int)state.GetType().GetField("Phase",F).GetValue(state)==1&&(int)state.GetType().GetField("Current",F).GetValue(state)<0;
                if(drained)break;
            }
            Assert.True(drained);retained=scheduler.RetainedBytes;scheduler.ReserveExternalBytes(LayeredNavigationScheduler.MaxRetainedBytes-retained);
            Assert.AreEqual(1,scheduler.Advance(++epoch,new NavEpochQuota(0,1,0,0,0),_=>true));
            field=((LayeredNavigationScheduler.Job)FirstScheduledJob(scheduler)).Field;
            Assert.AreEqual(NavSolveStatus.Ready,field.Status);Assert.AreEqual(retained,scheduler.RetainedBytes,"completed field transfer aliases allocated pages with no second field charge");
            Assert.AreEqual(1,scheduler.Counters.CacheFields);var restored=LayeredNavigationScheduler.Restore(scheduler.Save(),new[]{request});
            Assert.AreEqual(scheduler.RetainedBytes,restored.RetainedBytes);Assert.AreEqual(SchedulerContinuation(scheduler),SchedulerContinuation(restored));
            Assert.True(scheduler.Cancel(request.Subscription.StableKey));Assert.AreEqual(0,scheduler.PendingCount);
            Assert.True((bool)typeof(LayeredNavigationScheduler).GetProperty("HasAttemptOwner",F).GetValue(scheduler));
            scheduler.ClearWorld();Assert.AreEqual(0,scheduler.RetainedBytes);Assert.AreEqual(0,scheduler.ProviderRetainedBytes);
        }
        [TestCase(false)][TestCase(true)] public void CertifiedZeroIngressStillChecksLaterDirectCandidateAndRestoresExactScan(bool laterDirect)
        {
            var walls=new[]{new NavObstacle(.1,.1,.2,.9),new NavObstacle(.8,.1,.9,.9),new NavObstacle(.1,.1,.9,.2),new NavObstacle(.1,.8,.9,.9)};
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,walls,1));
            var origin=new NavLocation(new NavPoint(.5,.5),NavLocation.FlatSurface);var outside=new NavLocation(new NavPoint(2,2),NavLocation.FlatSurface);
            var inside=new NavLocation(new NavPoint(.55,.5),NavLocation.FlatSurface);
            var member=new NavArrivalMember(1,1,1,1,origin,new NavClearanceProfile("ground",1,"ground",1,.05),1,1,1,1,0,1,1,1);
            var input=new NavArrivalInput("zero-ingress",new string('a',64),provider,new NavGraphProfile(new string('a',64),"distance",1,1),new NavigationProfile(),
                new NavGraphCompileLimits(2000,2000,10000),new NavArrivalCandidateSource("zero-ingress",1,outside,laterDirect?new[]{outside,inside}:new[]{outside}),new[]{member},new NavArrivalOccupancySnapshot(1,Array.Empty<NavArrivalOccupant>()),2048,independentMembers:true);
            var work=new NavigationArrivalWork(input);NavigationArrivalWork restored=null;long epoch=0;
            for(;epoch<5000;epoch++){
                work.Advance(epoch,256,new NavEpochQuota(2048,0,1,4,0));
                var scheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(work);
                if(scheduler.PendingCount==0)continue;var job=FirstScheduledJob(scheduler);
                if(job.GetType().GetField("Phase",F).GetValue(job).ToString()=="Origin"&&(int)job.GetType().GetField("OriginCursor",F).GetValue(job)>0){restored=NavigationArrivalWork.Restore(input,work.Save());break;}
            }
            Assert.NotNull(restored,"capture actual partial origin node scan");
            var originalScheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(work);
            var restoredScheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(restored);
            Assert.AreEqual(SchedulerContinuation(originalScheduler),SchedulerContinuation(restoredScheduler));Assert.AreEqual(originalScheduler.RetainedBytes,restoredScheduler.RetainedBytes);
            var originalJob=FirstScheduledJob(originalScheduler);var restoredJob=FirstScheduledJob(restoredScheduler);int originalProbes=0,restoredProbes=0;
            typeof(LayeredNavigationGraph).GetField("BeforeAttachmentProbe",F).SetValue(originalJob.GetType().GetProperty("Graph",F).GetValue(originalJob),(Action)(()=>originalProbes++));
            typeof(LayeredNavigationGraph).GetField("BeforeAttachmentProbe",F).SetValue(restoredJob.GetType().GetProperty("Graph",F).GetValue(restoredJob),(Action)(()=>restoredProbes++));
            var nextQuota=new NavEpochQuota(0,0,1,4,0);work.Advance(++epoch,256,nextQuota);restored.Advance(epoch,256,nextQuota);
            Assert.AreEqual(1,originalProbes);Assert.AreEqual(1,restoredProbes);Assert.AreEqual(SchedulerContinuation(originalScheduler),SchedulerContinuation(restoredScheduler));
            Assert.AreEqual(work.RouteCounters.ConnectorWork,restored.RouteCounters.ConnectorWork);Assert.AreEqual(work.RouteCounters.PhysicalFieldWork,restored.RouteCounters.PhysicalFieldWork);
            work.Advance(epoch,256,nextQuota);restored.Advance(epoch,256,nextQuota);Assert.AreEqual(1,originalProbes);Assert.AreEqual(1,restoredProbes,"same epoch does not refill scan quota");
            for(int n=0;n<5000&&work.Status!=NavArrivalStatus.Ready;n++){
                work.Advance(++epoch,256,new NavEpochQuota(2048,2048,1,4,0));restored.Advance(epoch,256,new NavEpochQuota(2048,2048,1,4,0));
                Assert.AreEqual(work.Status,restored.Status);Assert.AreEqual(SchedulerContinuation(originalScheduler),SchedulerContinuation(restoredScheduler));
            }
            Assert.AreEqual(NavArrivalStatus.Ready,work.Status);Assert.AreEqual(0,work.RouteCounters.PhysicalFieldWork,"zero ingress never builds a terminal field");
            if(laterDirect){Assert.AreEqual(1,work.Proposals.Count);Assert.AreEqual(inside,work.Proposals.Single().Endpoint);Assert.AreEqual(0,work.MemberOutcomes.Count);}
            else{Assert.AreEqual(0,work.Proposals.Count);Assert.AreEqual(NavArrivalStatus.NoAvailableSlot,work.MemberOutcomes[1]);}
            Assert.AreEqual(0,work.RouteCounters.RetainedBytes);
        }
        private NavigationArrivalWork CurrentArrivalWork()
        {
            var attempts=(System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",F).GetValue(nav);
            var entry=attempts.Cast<object>().Single();var attempt=entry.GetType().GetProperty("Value").GetValue(entry);
            return (NavigationArrivalWork)attempt.GetType().GetField("Work",F).GetValue(attempt);
        }
        [TestCase("origin-scan")][TestCase("no-subs-owner")][TestCase("sibling-acquire")]
        public void ActualFreshBytesPreserveAttemptOwnerAndOriginScanBoundaries(string boundary)
        {
            int a=Spawn(-18,-14),b=boundary=="origin-scan"?Spawn(-24,-20,PlayableEntityKind.Explorer):Spawn(-22,-14);
            if(boundary=="origin-scan"){int[] holds={Spawn(-16.5,-14),Spawn(-19.5,-14),Spawn(-18,-12.5),Spawn(-18,-15.5)};Stop(holds,hold:true);}
            if(boundary=="no-subs-owner"){
                var geometry=(NavGeometry)Domain.GetProperty("Geometry",F).GetValue(domain);double radius=PlayableProfile.Default.ExplorerCollisionRadius;int parked=0;
                for(double z=12;z<geometry.HalfExtent-radius&&parked<62;z+=2*radius+.2)
                    for(double x=-geometry.HalfExtent+radius+.2;x<geometry.HalfExtent-radius&&parked<62;x+=2*radius+.2){
                        var point=new NavPoint(x,z);if(!geometry.IsFree(point,radius)||nav.Crowd.Units.Any(u=>{double dx=u.Position.X-x,dz=u.Position.Z-z;return dx*dx+dz*dz<=Math.Pow(u.Radius+radius+.05,2);}))continue;
                        int id=(int)Call("SpawnUnit",point,parked<22?PlayableOwner.Player:PlayableOwner.Enemy,PlayableEntityKind.Explorer);Assert.False(nav.Crowd.Units.Single(u=>u.Id==id).Held);parked++;
                    }
                Assert.AreEqual(62,parked,"fixture parks distinct valid non-HOLD actors outside first terminal page");
                Assert.AreSame(geometry,Domain.GetProperty("Geometry",F).GetValue(domain));
            }
            Move(new[]{a,b},boundary=="origin-scan"?new NavPoint(-10,-14):new NavPoint(10,-14));nav.PrepareDeliveryBarrier();Pump(4);bool found=false;
            for(int n=0;n<20000;n++){
                var previous=CurrentArrivalWork();int budget=boundary=="no-subs-owner"&&previous.RouteCounters.FieldBuilds>0?1:4;
                Pump(budget);var work=CurrentArrivalWork();var scheduler=(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",F).GetValue(work);
                bool owner=(bool)typeof(LayeredNavigationScheduler).GetProperty("HasAttemptOwner",F).GetValue(scheduler);
                if(boundary=="no-subs-owner")found=owner&&scheduler.PendingCount==0&&scheduler.ReadyCount==0&&work.MemberCursor==1&&work.WorkPhase=="ScanCandidates"&&work.AllocationQuotaRemaining==0;
                else if(scheduler.PendingCount>0){var job=FirstScheduledJob(scheduler);bool origin=job.GetType().GetField("Phase",F).GetValue(job).ToString()=="Origin";
                    found=owner&&(boundary=="origin-scan"?origin&&(int)job.GetType().GetField("OriginCursor",F).GetValue(job)>0&&work.MemberCursor==0:
                        work.MemberCursor==1&&(int)job.GetType().GetField("GraphWork",F).GetValue(job)==0&&job.GetType().GetProperty("Graph",F).GetValue(job)!=null);
                }
                if(found)break;if(work.Status==NavArrivalStatus.Ready)break;
            }
            Assert.True(found,boundary+" must be a real actual-domain continuation frontier");
            if(boundary=="no-subs-owner"){
                var work=CurrentArrivalWork();Assert.AreEqual("ScanCandidates",work.WorkPhase);Assert.AreEqual(0,work.AllocationQuotaRemaining);Assert.AreEqual(0,work.PendingRouteCount);Assert.AreEqual(0,work.ReadyRouteCount);
                Assert.Greater(work.RouteCounters.FieldBuilds,0);TestContext.WriteLine("NO-SUBS-OWNER member="+work.MemberCursor+" candidate="+work.CandidateCursor+" occupancy="+work.OccupancyCursor+" allocationRemaining="+work.AllocationQuotaRemaining);
            }
            int nextBudget=boundary=="no-subs-owner"?1:4;
            byte[] before=Bytes();Pump(nextBudget);nav.ApplyResults();byte[] next=Bytes();
            RestoreOnlyBytes(before);CollectionAssert.AreEqual(before,Bytes());
            for(int n=0;n<20000&&nav.RoutePort.ArrivalReconstructing;n++)Pump(4);Assert.False(nav.RoutePort.ArrivalReconstructing);
            Pump(nextBudget);nav.ApplyResults();CollectionAssert.AreEqual(next,Bytes(),"exact next quota/counters/owner scan cursor/publication after fresh-only bytes at "+boundary);
            Complete();int expected=boundary=="origin-scan"?1:2;Assert.AreEqual(expected,nav.AppliedResults);Assert.AreEqual(expected,nav.ArrivalReservations.Count);
        }
        private System.Collections.IDictionary ProjectionLeases()=>(System.Collections.IDictionary)typeof(NavigationSession).GetField("arrivalProjectionLeases",F).GetValue(nav);
        private void PauseTransferredArrival(int[] ids,NavPoint goal)
        {
            Move(ids,goal);nav.PrepareDeliveryBarrier();host.Continue=()=>ProjectionLeases().Count<ids.Length;
            for(int n=0;n<20000&&ProjectionLeases().Count<ids.Length;n++){Pump(4);nav.ApplyResults();}
            Assert.AreEqual(ids.Length,ProjectionLeases().Count);Assert.AreEqual(0,nav.AppliedResults);Assert.AreEqual(ids.Length,nav.ArrivalReservations.Count);
        }
        [Test] public void CancelAfterTransferRetainsProjectionRecipeAndFreshBytesRetireExactOwner()
        {
            int id=Spawn(-18,-14);PauseTransferredArrival(new[]{id},new NavPoint(-10,-14));
            var leases=ProjectionLeases();var lease=leases.Values.Cast<object>().Single();long token=(long)lease.GetType().GetField("Token",F).GetValue(lease);
            Assert.Greater(nav.RoutePort.CorridorRetainedBytes,0);byte[] active=Bytes();
            Stop(new[]{id});Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.AreEqual(0,nav.PendingCount);Assert.AreEqual(0,nav.RegisteredRouteCount);
            Assert.True((bool)lease.GetType().GetField("Retiring",F).GetValue(lease));Assert.AreEqual(1,ProjectionLeases().Count);
            byte[] retired=Bytes();long owned=nav.RoutePort.CorridorRetainedBytes+nav.HostRetainedBytes;Assert.Greater(owned,0);
            host.Continue=null;Pump(1);nav.ApplyResults();byte[] final=Bytes();Assert.AreEqual(0,ProjectionLeases().Count);Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.AreEqual(0,nav.AppliedResults);Assert.AreEqual(0,nav.RegisteredRouteCount);
            RestoreOnlyBytes(retired);CollectionAssert.AreEqual(retired,Bytes());Assert.AreEqual(owned,nav.RoutePort.CorridorRetainedBytes+nav.HostRetainedBytes,"same physical provider/projection payload capacities before retirement");
            var restored=ProjectionLeases().Values.Cast<object>().Single();Assert.AreEqual(token,restored.GetType().GetField("Token",F).GetValue(restored));
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(final,Bytes(),"same bounded pre-Advance retirement and final owner release");
            Assert.AreEqual(0,nav.AppliedResults);Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.AreEqual(0,ProjectionLeases().Count);Assert.AreEqual(0,nav.RegisteredRouteCount);
        }
        private void AliasInitialWorkingProviders()
        {
            // Explicit fixture recipe seam before the first epoch. Production must
            // retain distinct instances even when their certified bindings agree.
            var attempt=FirstAttempt();var type=attempt.GetType();var input=(NavArrivalInput)type.GetField("Input",F).GetValue(attempt);
            Assert.AreEqual(0,((System.Collections.ICollection)type.GetField("Journal",F).GetValue(attempt)).Count);
            Assert.AreEqual(1,input.WorkingBindings.Select(b=>b.Provider.Binding).Distinct().Count());
            var shared=input.Provider;var bindings=input.WorkingBindings.Select(b=>new NavArrivalWorkingBinding(b.Entity,shared,b.Profile)).ToArray();
            var replacement=new NavArrivalInput(input.TopologyIdentity,input.ProfileIdentity,shared,input.Profile,input.ArrivalProfile,input.Limits,input.Source,input.Members,input.Occupancy,input.MaxLabels,bindings,input.IndependentMembers);
            Assert.AreEqual(input.StableKey,replacement.StableKey,"physical aliases do not alter semantic recipe");
            foreach(var provider in ((ILayeredNavigationProvider[])type.GetField("OwnedProviders",F).GetValue(attempt)))
                typeof(NavigationSession).GetMethod("ReleaseArrivalProvider",F).Invoke(nav,new object[]{provider});
            type.GetField("Input",F).SetValue(attempt,replacement);type.GetField("Work",F).SetValue(attempt,new NavigationArrivalWork(replacement));
            typeof(NavigationSession).GetMethod("OwnArrivalInput",F).Invoke(nav,new[]{attempt});
        }
        private object LeaseField(object lease,string name)=>lease.GetType().GetField(name,F).GetValue(lease);
        [TestCase(false)][TestCase(true)]
        public void PhysicalProviderAliasesAndDistinctEqualBindingsSurviveTwoLeaseRetirement(bool alias)
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(-10,-14));nav.PrepareDeliveryBarrier();
            if(alias)AliasInitialWorkingProviders();
            host.Continue=()=>ProjectionLeases().Count<2;
            for(int n=0;n<20000&&ProjectionLeases().Count<2;n++){Pump(4);nav.ApplyResults();}
            Assert.AreEqual(2,ProjectionLeases().Count);Assert.AreEqual(0,nav.AppliedResults);
            var owners=ProjectionLeases().Values.Cast<object>().OrderBy(l=>(long)LeaseField(l,"Acquisition")).ToArray();
            var p0=(ILayeredNavigationProvider)LeaseField(owners[0],"Provider");var p1=(ILayeredNavigationProvider)LeaseField(owners[1],"Provider");
            Assert.AreEqual(p0.Binding,p1.Binding);Assert.AreEqual(alias,ReferenceEquals(p0,p1));
            Stop(new[]{a,b});byte[] retired=Bytes();long before=nav.ArrivalProviderOwnedBytes;
            Pump(1);nav.ApplyResults();Assert.AreEqual(1,ProjectionLeases().Count);Assert.Less(nav.ArrivalProviderOwnedBytes,before);Assert.Greater(nav.ArrivalProviderOwnedBytes,0);
            long survivor=(long)LeaseField(ProjectionLeases().Values.Cast<object>().Single(),"Token");byte[] first=Bytes();
            Pump(1);nav.ApplyResults();byte[] final=Bytes();Assert.AreEqual(0,ProjectionLeases().Count);Assert.AreEqual(0,nav.ArrivalProviderOwnedBytes);Assert.AreEqual(0,nav.AppliedResults);Assert.AreEqual(0,nav.ArrivalReservations.Count);
            RestoreOnlyBytes(retired);CollectionAssert.AreEqual(retired,Bytes());Assert.AreEqual(before,nav.ArrivalProviderOwnedBytes);
            owners=ProjectionLeases().Values.Cast<object>().OrderBy(l=>(long)LeaseField(l,"Acquisition")).ToArray();
            Assert.AreEqual(alias,ReferenceEquals(LeaseField(owners[0],"Provider"),LeaseField(owners[1],"Provider")),"fresh physical identity is preserved independently of binding equality");
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(first,Bytes());Assert.AreEqual(survivor,LeaseField(ProjectionLeases().Values.Cast<object>().Single(),"Token"));
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(final,Bytes());Assert.AreEqual(0,nav.ArrivalProviderOwnedBytes);
        }
        [TestCase("unknown-token")][TestCase("missing-recipe")][TestCase("inconsistent-alias")]
        public void InvalidTransferredOwnerIsRejectedByFreshWorldWire(string defect)
        {
            int id=Spawn(-18,-14);PauseTransferredArrival(new[]{id},new NavPoint(-10,-14));var lease=ProjectionLeases().Values.Cast<object>().Single();var type=lease.GetType();
            if(defect=="unknown-token")type.GetField("Token",F).SetValue(lease,0L);
            else if(defect=="missing-recipe")type.GetField("Attempt",F).SetValue(lease,long.MaxValue);
            else type.GetField("ProviderId",F).SetValue(lease,long.MaxValue);
            RejectFreshBytes(Bytes());
            // Restore metadata for ordinary exact-once teardown of this fixture.
            if(defect=="unknown-token")type.GetField("Token",F).SetValue(lease,1L);
            else if(defect=="missing-recipe")type.GetField("Attempt",F).SetValue(lease,1L);
            else type.GetField("ProviderId",F).SetValue(lease,1L);
        }
        [Test] public void OriginalSessionTokenReleaseIsExactOnceAfterHostReferencesAreCleared()
        {
            int id=Spawn(-18,-14);PauseTransferredArrival(new[]{id},new NavPoint(-10,-14));var lease=ProjectionLeases().Values.Cast<object>().Single();host.Clear();
            Assert.AreEqual(0,ProjectionLeases().Count);Assert.Null(LeaseField(lease,"Projection"));Assert.Null(LeaseField(lease,"Provider"));
            var release=typeof(NavigationSession).GetMethod("ReleaseArrivalProjection",F);
            var duplicate=Assert.Throws<TargetInvocationException>(()=>release.Invoke(nav,new[]{lease,"duplicate"}));Assert.IsInstanceOf<InvalidOperationException>(duplicate.InnerException);
        }
        [TestCase(false)][TestCase(true)]
        public void CaptureWhileProjectionQualificationIsPausedSeesOldAtomicPrefix(bool cancel)
        {
            int id=Spawn(-18,-14);PauseTransferredArrival(new[]{id},new NavPoint(-10,-14));byte[] old=Bytes();
            using(var qualified=new System.Threading.ManualResetEventSlim())using(var resume=new System.Threading.ManualResetEventSlim()){
                host.Continue=null;host.BeforeArrivalProjectionCommit=(_,__)=>{qualified.Set();Assert.True(resume.Wait(5000));};
                var task=System.Threading.Tasks.Task.Run(()=>Pump(4));
                try{
                    Assert.True(qualified.Wait(5000));CollectionAssert.AreEqual(old,Bytes(),"capture completes while provider compute is paused outside gate; immutable prefix/capacities agree");
                    var lease=ProjectionLeases().Values.Cast<object>().Single();Assert.AreEqual(0,LeaseField(lease,"PointsCapacity"));
                    var premature=Assert.Throws<TargetInvocationException>(()=>typeof(NavigationSession).GetMethod("ReleaseArrivalProjection",F).Invoke(nav,new[]{lease,"before-delta-disposal"}));
                    Assert.IsInstanceOf<InvalidOperationException>(premature.InnerException);Assert.NotNull(LeaseField(lease,"Projection"));Assert.NotNull(LeaseField(lease,"Provider"));CollectionAssert.AreEqual(old,Bytes());
                    if(cancel){Stop(new[]{id});old=Bytes();Assert.AreEqual(0,nav.PendingCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);}
                }finally{resume.Set();}
                Assert.True(task.Wait(5000));host.BeforeArrivalProjectionCommit=null;nav.ApplyResults();
            }
            if(cancel){Assert.AreEqual(0,LeaseField(ProjectionLeases().Values.Cast<object>().Single(),"PointsCapacity"));Pump(1);nav.ApplyResults();}
            byte[] next=Bytes();Assert.AreEqual(cancel?0:1,nav.AppliedResults);
            RestoreOnlyBytes(old);host.Continue=null;Pump(cancel?1:4);nav.ApplyResults();CollectionAssert.AreEqual(next,Bytes(),"same next publication/retirement from fresh-only committed prefix");
        }
        [Test] public void ProviderBirthBlockedBeforeConstructionKeepsAcceptedIntentAndBytes()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));byte[] accepted=Bytes();int constructions=0;
            nav.BeforeArrivalProviderConstruction=()=>constructions++;
            typeof(NavigationSession).GetField("hostRetainedBytes",F).SetValue(nav,LayeredNavigationScheduler.MaxRetainedBytes-1L);
            nav.PrepareDeliveryBarrier();Assert.AreEqual(0,constructions);Assert.AreEqual(1,nav.AllocationCount);Assert.AreEqual(0,nav.RejectedResults);CollectionAssert.AreEqual(accepted,Bytes());
            typeof(NavigationSession).GetField("hostRetainedBytes",F).SetValue(nav,0L);nav.PrepareDeliveryBarrier();Assert.AreEqual(1,constructions);byte[] next=Bytes();
            RestoreOnlyBytes(accepted);nav.BeforeArrivalProviderConstruction=()=>constructions++;
            typeof(NavigationSession).GetField("hostRetainedBytes",F).SetValue(nav,LayeredNavigationScheduler.MaxRetainedBytes-1L);nav.PrepareDeliveryBarrier();CollectionAssert.AreEqual(accepted,Bytes());Assert.AreEqual(1,constructions);
            typeof(NavigationSession).GetField("hostRetainedBytes",F).SetValue(nav,0L);nav.PrepareDeliveryBarrier();CollectionAssert.AreEqual(next,Bytes());Assert.AreEqual(2,constructions);
        }
        [Test] public void NearCapNoListGrowthProjectionAppendDefersSnapshotAndCursorUntilPressureRelease()
        {
            int id=Spawn(-18,-14);PauseTransferredArrival(new[]{id},new NavPoint(10,14));
            var lease=ProjectionLeases().Values.Cast<object>().Single();var row=LeaseField(lease,"Projection");var rowType=row.GetType();
            var solved=(NavScheduledCompletion)rowType.GetField("Solved",F).GetValue(row);Assert.Greater(solved.Route.Legs.Count,1,"actual route must offer a second surface append");
            host.Continue=()=>((System.Collections.IList)rowType.GetField("Points",F).GetValue(row)).Count<1;Pump(4);nav.ApplyResults();
            Assert.AreEqual(1,((System.Collections.IList)rowType.GetField("Points",F).GetValue(row)).Count);Assert.AreEqual(4,LeaseField(lease,"PointsCapacity"));
            Assert.AreEqual(NavTypedLegKind.Surface,solved.Route.Legs[(int)rowType.GetField("LegCursor",F).GetValue(row)].Kind);
            byte[] prefix=Bytes();long priorHost=nav.HostRetainedBytes;host.Continue=null;
            typeof(CertifiedRouteKernel).GetField("hostAnswerBytes",F).SetValue(host,LayeredNavigationScheduler.MaxRetainedBytes-nav.RoutePort.CorridorRetainedBytes-1);
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(prefix,Bytes(),"snapshot growth alone is admitted before no-capacity-growth append");
            Assert.AreEqual(1,((System.Collections.IList)rowType.GetField("Points",F).GetValue(row)).Count);Assert.AreEqual(4,LeaseField(lease,"PointsCapacity"));
            typeof(CertifiedRouteKernel).GetField("hostAnswerBytes",F).SetValue(host,priorHost);Pump(1);nav.ApplyResults();byte[] next=Bytes();
            RestoreOnlyBytes(prefix);typeof(CertifiedRouteKernel).GetField("currentPort",F).SetValue(host,nav.RoutePort);host.Continue=null;typeof(CertifiedRouteKernel).GetField("hostAnswerBytes",F).SetValue(host,LayeredNavigationScheduler.MaxRetainedBytes-nav.RoutePort.CorridorRetainedBytes-1);
            Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(prefix,Bytes());typeof(CertifiedRouteKernel).GetField("hostAnswerBytes",F).SetValue(host,priorHost);Pump(1);nav.ApplyResults();CollectionAssert.AreEqual(next,Bytes());
        }
        [TestCase("points")][TestCase("typed")][TestCase("observed")]
        public void ActualPartitionProjectionCapacityGrowthCapturesAndRestoresOneCoherentStep(string component)
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;host.Clear();domain=Activator.CreateInstance(Domain,F,null,new object[]{profile,1L,false},null);
            nav=(NavigationSession)Domain.GetProperty("Navigation",F).GetValue(domain);host=new CertifiedRouteKernel();
            int id=Spawn(-16,0);PauseTransferredArrival(new[]{id},new NavPoint(16,0));host.Continue=null;byte[] prior=null;int nextLeg=-1,nextReplay=-1;long retained=0;
            Func<bool> beforeNext=()=>{var current=LeaseField(ProjectionLeases().Values.Cast<object>().Single(),"Projection");return (int)current.GetType().GetField("LegCursor",F).GetValue(current)<nextLeg||(int)current.GetType().GetField("ReplayCursor",F).GetValue(current)<nextReplay;};
            using(var qualified=new System.Threading.ManualResetEventSlim())using(var resume=new System.Threading.ManualResetEventSlim()){
                host.BeforeArrivalProjectionCommit=(delta,next)=>{
                    var owner=delta.Lease;bool growth=component=="points"?next.Points.Length>owner.PointsCapacity:component=="typed"?next.Typed.Length>owner.TypedCapacity:next.Observed.Length>owner.ObservedCapacity;
                    if(!growth||qualified.IsSet)return;
                    prior=Bytes();retained=nav.ArrivalProviderOwnedBytes-owner.StagingBytes;nextLeg=next.Leg;nextReplay=next.Replay;host.Continue=beforeNext;qualified.Set();Assert.True(resume.Wait(5000));
                };
                var task=System.Threading.Tasks.Task.Run(()=>Pump(4));
                try{Assert.True(qualified.Wait(5000),component+" actual partition growth was not reached");CollectionAssert.AreEqual(prior,Bytes(),"capture can run concurrently with paused qualification");}
                finally{resume.Set();}
                Assert.True(task.Wait(5000));host.BeforeArrivalProjectionCommit=null;nav.ApplyResults();
            }
            Assert.AreEqual(0,nav.AppliedResults);byte[] nextBytes=Bytes();long nextRetained=nav.ArrivalProviderOwnedBytes;
            RestoreOnlyBytes(prior,profile);CollectionAssert.AreEqual(prior,Bytes());Assert.AreEqual(retained,nav.ArrivalProviderOwnedBytes);
            host.Continue=beforeNext;Pump(4);nav.ApplyResults();CollectionAssert.AreEqual(nextBytes,Bytes(),"same next prefix/capacities/aliases and logical quota for actual "+component+" growth");Assert.AreEqual(nextRetained,nav.ArrivalProviderOwnedBytes);
        }
        private static byte[] InitialArrivalV3AsLegacyV2(byte[] bytes)
        {
            // Preserve the actual legacy primitive sections byte-for-byte; remove
            // only the v3 owner fields. This fixture refuses journals/projections.
            using(var input=new System.IO.MemoryStream(bytes))using(var r=new System.IO.BinaryReader(input))
            using(var output=new System.IO.MemoryStream())using(var w=new System.IO.BinaryWriter(output)){
                void Copy(Action read){int start=(int)input.Position;read();w.Write(bytes,start,(int)input.Position-start);}
                void Strings(int n){for(int i=0;i<n;i++)WorldWire.String(r);}
                void Longs(int n){for(int i=0;i<n;i++)r.ReadInt64();}
                void Ints(int n){for(int i=0;i<n;i++)r.ReadInt32();}
                void Locations(int n){for(int i=0;i<n;i++)GroupOrderWire.ReadLocation(r);}
                void Array(Action read){int n=r.ReadInt32();for(int i=0;i<n;i++)read();}
                Assert.AreEqual(0x41525231,r.ReadInt32());Assert.AreEqual(3,r.ReadInt32());w.Write(0x41525231);w.Write(2);
                Copy(()=>Longs(2));Longs(5);Ints(3);int count=r.ReadInt32();w.Write(count);
                for(int n=0;n<count;n++){
                    Copy(()=>{Longs(3);r.ReadBoolean();r.ReadInt32();Strings(2);Ints(3);});
                    Assert.False(r.ReadBoolean());Strings(1);r.ReadBoolean();r.ReadInt64();Assert.False(r.ReadBoolean());
                    Copy(()=>Array(()=>{WorldWire.ReadNavigationRequestState(r);Longs(5);Locations(2);}));
                    Copy(()=>{Strings(4);r.ReadInt32();Locations(1);Array(()=>Locations(1));});
                    Copy(()=>Array(()=>{Longs(4);Locations(1);Strings(1);r.ReadInt32();Strings(1);r.ReadInt32();r.ReadDouble();Longs(8);Strings(1);}));
                    Copy(()=>{Strings(2);r.ReadDouble();});r.ReadInt64();Array(()=>r.ReadInt64());Strings(1);
                    Copy(()=>{Array(()=>r.ReadDouble());Strings(1);r.ReadInt32();Ints(4);
                        Array(()=>{r.ReadInt32();Longs(2);Locations(1);r.ReadDouble();r.ReadBoolean();Strings(1);if(r.ReadBoolean())r.ReadInt32();Longs(2);Locations(1);});
                        Assert.AreEqual(0,r.ReadInt32(),"legacy fixture starts before the first quota journal");Assert.False(r.ReadBoolean());Assert.AreEqual(0,r.ReadInt32());
                    });
                }
                Assert.AreEqual(0,r.ReadInt32(),"legacy v2 carries no projection leases");Copy(()=>Assert.AreEqual(0,r.ReadInt32()));Assert.AreEqual(input.Length,input.Position);w.Flush();return output.ToArray();
            }
        }
        [TestCase(false)][TestCase(true)]
        public void LegacyArrivalV2ActualV9WorldHydratesAcceptedAndInitialPrimitiveAttempt(bool attempt)
        {
            int a=Spawn(-18,-14),b=Spawn(-22,-14);Move(new[]{a,b},new NavPoint(-10,-14));if(attempt)nav.PrepareDeliveryBarrier();
            var world=(PlayableWorldState)Call("CaptureWorldState",7,"a4b-authority-tests");var state=nav.CaptureState();
            state.ArrivalAuthority=InitialArrivalV3AsLegacyV2(state.ArrivalAuthority);world.Navigation=WorldWire.Pack(w=>WorldWire.Write(w,state));byte[] legacy=world.Encode();
            RestoreOnlyBytes(legacy);Assert.AreEqual(2,nav.AllocationCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);
            if(attempt)Assert.True((bool)FirstAttempt().GetType().GetField("LegacyProviderAccounting",F).GetValue(FirstAttempt()));
            var fresh=PlayableWorldState.Decode(Bytes());Assert.AreEqual(9,fresh.Version);CollectionAssert.AreEqual(world.Domain,fresh.Domain,"v9 domain/history/stats untouched by arrival reader");
            Complete();Assert.AreEqual(2,nav.AppliedResults);Assert.AreEqual(2,nav.ArrivalReservations.Count);
        }
        [Test] public void StopBeforeAllocationCancelsWithoutReservationOrReceipt()
        {
            int id=Spawn(-18,-14);Move(new[]{id},new NavPoint(-10,-14));Pump();
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Stop,new[]{id}),null));
            for(int n=0;n<5;n++)Pump();Assert.AreEqual(0,nav.PendingCount);Assert.AreEqual(0,nav.ArrivalReservations.Count);Assert.AreEqual(0,nav.AppliedResults);
            Assert.Null(Call("SpatialCompletion",id,PlayableOwner.Player));RestoreOnlyBytes(Bytes());
        }
    }
}
