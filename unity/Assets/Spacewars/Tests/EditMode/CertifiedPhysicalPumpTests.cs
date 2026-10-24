using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedPhysicalPumpTests
    {
        private static PlayableRouteBinding Binding(NavigationSession session,NavGeometry geometry)
            =>new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
        private static byte[] Bytes(NavigationSession session)
        {using(var capture=session.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),capture.CaptureBudget))return bytes.TransferPublic();}
        [Test] public void ImmutableArrangementSharesAcrossChangedGeometryAndHoldButRejectsChangedMap()
        {
            var map=ThreeCrossingsMap.Default;var changed=new ThreeCrossingsMap(new ThreeCrossingsProfileData{centralBridgeWidth=16});
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var moved=new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(-4,-4,-3,-3)}).ToArray(),map.Revision+1);
            var source=new PartitionNavigationProvider(map,geometry,null);var dynamicSource=new PartitionNavigationProvider(map,moved,"hold-changed");
            var different=new PartitionNavigationProvider(changed,new NavGeometry(changed.HalfExtent,changed.MovementBlockers.ToArray(),changed.Revision),null);
            Assert.True(source.SameArrangement(dynamicSource));Assert.False(source.SameArrangement(different));
            var budget=new NavigationRetentionBudget();PartitionNavigationProvider first=null,second=null,third=null;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var a));using(a){first=PartitionNavigationProvider.CopyOwned(source,a.OwnedReservation());a.Attach(first);a.Commit(first,_=>{});}
                long before=budget.PhysicalBytes;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var b));using(b){second=PartitionNavigationProvider.CopyOwned(dynamicSource,b.OwnedReservation(),first);b.Attach(second);b.Commit(second,_=>{});}
                Assert.AreSame(first.Seams,second.Seams);Assert.AreSame(first.Surfaces,second.Surfaces);Assert.AreNotEqual(first.Binding,second.Binding);
                Assert.AreEqual("hold-changed",second.HoldBinding);Assert.AreEqual(moved.Obstacles.Count,second.ExclusionObstacles.Count);Assert.AreNotSame(first.ExclusionObstacles,second.ExclusionObstacles);
                Assert.Less(budget.PhysicalBytes-before,before,"Second provider retains only dynamic geometry and view headers.");
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var c));using(c){Assert.Throws<ArgumentException>(()=>PartitionNavigationProvider.CopyOwned(different,c.OwnedReservation(),first));third=PartitionNavigationProvider.CopyOwned(different,c.OwnedReservation());c.Attach(third);c.Commit(third,_=>{});}
                Assert.AreNotSame(first.Seams,third.Seams);Assert.AreNotSame(first.Surfaces,third.Surfaces);
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(first.StorageRoot,second.StorageRoot,third.StorageRoot),budget.PhysicalBytes);
            }finally{first?.StorageRoot.Release();second?.StorageRoot.Release();third?.StorageRoot.Release();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void RepeatedOrdinaryProvidersShareArrangementAndColdContinuationExactly()
        {
            var map=new FoundryMap(new FoundryProfileData());var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,terrain:map);var kernel=new CertifiedRouteKernel();
            try{
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;
                try{kernel.Pump(Binding(session,geometry),4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                for(int i=0;i<2;i++){Assert.NotNull(session.Probe(i+7,1,PlayableProfile.Default.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));Assert.True(kernel.TryRegisterPhysicalOrdinary(Binding(session,geometry)));}
                void Shared(){var root=session.PinCommittedHostRoot();try{var providers=root.Providers.Select(p=>(PartitionNavigationProvider)((NavigationHostProvider)p.Value).Provider).ToArray();Assert.AreEqual(2,providers.Length);Assert.AreSame(providers[0].Seams,providers[1].Seams);Assert.AreSame(providers[0].Surfaces,providers[1].Surfaces);}finally{root.Release();}Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);}
                Shared();var bytes=Bytes(session);kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,terrain:map);session.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));
                CollectionAssert.AreEqual(bytes,Bytes(session));Shared();
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void TentativeInstallationProtectionReleasesOnDiscardWithoutHistory()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryProtectLive(1000));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,1,out var prepared));NavigationRetentionBudget.Scope pressure=null;
            try{
                budget.ProtectInstallation(4000,prepared.OwnedReservation());Assert.AreEqual(4000,budget.LiveHeadroom);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out pressure));Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-budget.LiveHeadroom));Assert.False(pressure.TryReserve(1));
                prepared.Discard();Assert.AreEqual(1000,budget.LiveHeadroom);Assert.True(pressure.TryReserve(3000),"Discard returns the complete tentative priority credit.");
            }finally{prepared.Discard();pressure?.Discard();Assert.Zero(budget.HeldBytes);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void OrdinaryProjectionCapacityRefusalRollsBackAndRetries()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();NavigationRetentionBudget.Scope pressure=null;
            try{
                session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));var binding=Binding(session,geometry);
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Projection;
                try{kernel.Pump(binding,4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                bool refused=false;var budget=session.PhysicalHostBudget;
                for(int i=0;i<100&&!refused;i++){
                    var before=Bytes(session);long physical=budget.PhysicalBytes,progress=kernel.ProgressSequence;var frame=kernel.CapturePumpFrontier();
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out pressure));
                    budget.AllocationAdmission=bytes=>{
                        if(!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationHostAnswer)&&f.GetMethod().Name=="CreateReady"))return true;
                        budget.AllocationAdmission=null;refused=true;Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-bytes+1));return true;
                    };
                    int used;try{used=kernel.ProjectPhysicalOrdinary(binding,64);}finally{budget.AllocationAdmission=null;}
                    if(refused){Assert.Zero(used);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(progress,kernel.ProgressSequence);Assert.AreEqual(frame,kernel.CapturePumpFrontier());Assert.AreEqual(pressure.HeldBytes,budget.HeldBytes);}
                    pressure.Discard();pressure=null;
                    if(refused){CollectionAssert.AreEqual(before,Bytes(session));Assert.AreEqual(1,kernel.ProjectPhysicalOrdinary(binding,64));}
                }
                Assert.True(refused,"Reach actual Ready payload capacity.");for(int i=0;i<20000&&session.Answers.Count==0;i++)kernel.Pump(binding,4);Assert.AreEqual(1,session.Answers.Count);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            }finally{session.PhysicalHostBudget.AllocationAdmission=null;pressure?.Discard();kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);}
        }
        [Test] public void RoutePressureLeavesActualBirthAndReadyInstallationCapacity()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();NavigationRetentionBudget.Scope pressure=null;
            try{
                session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
                for(int i=0;i<20000&&session.Answers.Count==0;i++)kernel.Pump(Binding(session,geometry),4);
                Assert.AreEqual(1,session.Answers.Count);var budget=session.PhysicalHostBudget;Assert.Greater(budget.LiveHeadroom,NavigationSession.BirthHeadroom(1));
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out pressure));
                Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-budget.LiveHeadroom));Assert.False(pressure.TryReserve(1));
                session.Crowd.Add(2,new NavPoint(-5,3));Assert.AreEqual(2,session.Crowd.Units.Count);session.ApplyResults();Assert.AreEqual(1,session.AppliedResults,$"headroom={budget.LiveHeadroom} free={LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes} names={session.LastAnswerNamesFailure} batch={session.LastResultBatchFailure}/{session.LastResultBatchStage}/{session.LastResultBatchDetail}");Assert.NotNull(session.InstalledExecutionFor(1));
                pressure.Discard();pressure=null;Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            }finally{pressure?.Discard();kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);}
        }
        [Test] public void FoundryMemoryBudgetDiagnostic()
        {
            var map=new FoundryMap(new FoundryProfileData());
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var provider=new PartitionNavigationProvider(map,geometry,null);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,terrain:map);
            var kernel=new CertifiedRouteKernel();
            try{
                Assert.AreEqual(512L*1024*1024,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.AreEqual(64L*1024*1024,LayeredNavigationScheduler.MaxCacheBytes);
                TestContext.WriteLine($"FOUNDRY supports={map.Supports.Count} edges={map.Supports.Sum(s=>s.Bounds.Footprint.Vertices.Count)} seams={provider.Seams.Count} traceScratch={provider.TraceScratchBound} construction={NavigationProviderConstructionBound.Estimate(map,geometry)} providerEstimate={provider.EstimatedRetainedBytes}");
                kernel.Attach(session.RoutePort);Snapshot("initial");
                for(int n=0;n<2;n++){
                    Assert.NotNull(session.Probe(7+n,1,PlayableProfile.Default.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
                    Assert.True(kernel.TryRegisterPhysicalOrdinary(Binding(session,geometry)));
                    Snapshot("registered"+(n+1));
                }
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Solve;
                try{kernel.Pump(Binding(session,geometry),4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                int consumed=0;
                for(int n=0;n<16;n++)consumed+=kernel.AdvancePhysicalOrdinary(Binding(session,geometry),4096);
                Assert.Greater(consumed,0,"Diagnostic must exercise solver work, not an uninitialized quota.");
                TestContext.WriteLine("SOLVE consumed="+consumed);
                Snapshot("after16SolveBatches");
                Assert.True(session.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
                using(scope){
                    var actor=NavLiveUnitStorage.CreateOwned(123,new NavPoint(0,0),.25,1,1,scope.OwnedReservation());
                    try{TestContext.WriteLine("ACTOR bareHeader="+actor.StorageBytes);}finally{actor.Release();}
                    var spatial=NavCrowdSpatialStorage.Create(40,scope.TryReserve);
                    try{TestContext.WriteLine("SPATIAL actors=40 bytes="+spatial.StorageBytes);}finally{spatial.Release();}
                }
                void Snapshot(string label){
                    var budget=session.PhysicalHostBudget;
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                    TestContext.WriteLine($"MEMORY {label} retained={budget.RetainedBytes} physical={budget.PhysicalBytes} held={budget.HeldBytes} peak={budget.PeakRetainedBytes}");
                    var rows=NavigationStorageOwnershipTests.OwnerShapeWithSession(session).Split('\n').Select(line=>{
                        int colon=line.LastIndexOf(':'),equal=line.LastIndexOf('=');
                        return new {Type=line.Substring(0,colon),Bytes=long.Parse(line.Substring(colon+1,equal-colon-1))*int.Parse(line.Substring(equal+1)),Count=int.Parse(line.Substring(equal+1))};
                    }).GroupBy(row=>row.Type).Select(group=>new{Type=group.Key,Bytes=group.Sum(row=>row.Bytes),Count=group.Sum(row=>row.Count)}).OrderByDescending(row=>row.Bytes).Take(12);
                    foreach(var row in rows)TestContext.WriteLine($"OWNER {label} {row.Type} bytes={row.Bytes} count={row.Count}");
                }
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        [TestCase(false)][TestCase(true)]
        public void OrdinaryPartitionSeamCapacityRefusalRollsBackAndRetriesAfterCreditRelease(bool foundry)
        {
            IPlayableTerrain map=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData()):ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=PlayableProfile.Default;
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            var kernel=new CertifiedRouteKernel();NavigationRetentionBudget.Scope pressure=null;
            NavigationHostRoot original=null;
            try{
                kernel.Attach(session.RoutePort);
                var request=session.Probe(7,1,profile.Navigation,new NavPoint(-4,2),new NavPoint(4,2));Assert.NotNull(request);
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer;
                try{kernel.Pump(binding,4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.Transfer,kernel.CapturePumpFrontier().Stage);
                var budget=session.PhysicalHostBudget;var before=Bytes(session);long physical=budget.PhysicalBytes,progress=kernel.ProgressSequence;
                original=session.PinCommittedHostRoot();var admission=session.Admission;
                Assert.AreEqual(512L*1024*1024,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out pressure));
                int seams=0;long refusedBytes=0;
                budget.AllocationAdmission=bytes=>{
                    if(!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavPartitionSeam)&&f.GetMethod().Name=="CopyOwned"))return true;
                    budget.AllocationAdmission=null;seams++;refusedBytes=bytes;
                    // Accept the hook, but occupy real sibling credit so the
                    // production capacity check rejects this exact seam birth.
                    Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-bytes+1));
                    return true;
                };
                try{Assert.False(kernel.TryRegisterPhysicalOrdinary(binding));}
                finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,seams);Assert.Greater(refusedBytes,0);
                Assert.AreEqual(physical,budget.PhysicalBytes,"discard all copied provider births");
                Assert.AreEqual(pressure.HeldBytes,budget.HeldBytes,"return construction and copy scratch");
                Assert.AreEqual(1,budget.ActiveScopes);Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                var after=session.PinCommittedHostRoot();try{Assert.AreSame(original,after);}finally{after.Release();}
                Assert.AreSame(admission,session.Admission);Assert.AreEqual(progress,kernel.ProgressSequence);
                Assert.AreEqual(1,session.Requests.Count);Assert.AreSame(request,session.Requests.CopyItems()[0]);Assert.Zero(session.RegisteredRouteCount);
                pressure.Discard();pressure=null;
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes(session));
                Assert.True(kernel.TryRegisterPhysicalOrdinary(binding),"freed capacity must permit registration");
                Assert.Zero(session.Requests.Count);Assert.AreEqual(1,session.RegisteredRouteCount);
                original.Release();original=null;
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
                var registered=Bytes(session);kernel.Pump(binding,4);var next=Bytes(session);
                kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);
                session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
                session.RestoreState(WorldWire.Unpack(registered,WorldWire.ReadNavigationSessionState));
                CollectionAssert.AreEqual(registered,Bytes(session));kernel=new CertifiedRouteKernel();
                kernel.Pump(new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort),4);
                CollectionAssert.AreEqual(next,Bytes(session),"current-format cold next Pump");
            }finally{
                session.PhysicalHostBudget.AllocationAdmission=null;pressure?.Discard();original?.Release();
                kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            }
        }
        [Test] public void OrdinaryRegistrationStillPropagatesInvariantFailure()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            try{
                kernel.Attach(session.RoutePort);Assert.NotNull(session.Probe(7,1,PlayableProfile.Default.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
                var before=Bytes(session);long physical=session.PhysicalHostBudget.PhysicalBytes;
                var invariant=new InvalidOperationException("test invariant failure");kernel.BeforePrepare=()=>throw invariant;
                Assert.AreSame(invariant,Assert.Throws<InvalidOperationException>(()=>kernel.TryRegisterPhysicalOrdinary(Binding(session,geometry))));
                Assert.AreEqual(physical,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                CollectionAssert.AreEqual(before,Bytes(session));
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        [TestCase(false)][TestCase(true)]
        public void OrdinaryGraphTraceScratchCapacityRefusalPreservesCursorQuotaAndColdRetry(bool foundry)
        {
            IPlayableTerrain map=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData()):ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var profile=PlayableProfile.Default;
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);var kernel=new CertifiedRouteKernel();
            NavigationRetentionBudget.Scope pressure=null;NavigationHostRoot original=null;
            try{
                kernel.Attach(session.RoutePort);Assert.NotNull(session.Probe(7,1,profile.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Solve;
                try{kernel.Pump(binding,4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.Solve,kernel.CapturePumpFrontier().Stage);Assert.AreEqual(1,session.RegisteredRouteCount);
                var budget=session.PhysicalHostBudget;Assert.AreEqual(512L*1024*1024,LayeredNavigationScheduler.MaxRetainedBytes);
                bool refused=false;byte[] prefix=null;int resumed=0;
                for(int n=0;n<256&&!refused;n++){
                    prefix=Bytes(session);long physical=budget.PhysicalBytes,progress=kernel.ProgressSequence,epoch=kernel.SolverEpoch;
                    var frame=kernel.CapturePumpFrontier();original=session.PinCommittedHostRoot();
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out pressure));
                    budget.AllocationAdmission=bytes=>{
                        if(!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(TerrainLocations)&&f.GetMethod().Name=="TraceCore"))return true;
                        budget.AllocationAdmission=null;refused=true;
                        Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-bytes+1));
                        return true; // Real ledger pressure, not a false test-hook result.
                    };
                    int used;try{used=kernel.AdvancePhysicalOrdinary(binding,4096);}finally{budget.AllocationAdmission=null;}
                    if(refused){
                        Assert.Zero(used);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(pressure.HeldBytes,budget.HeldBytes);Assert.AreEqual(1,budget.ActiveScopes);
                        Assert.AreEqual(progress,kernel.ProgressSequence);Assert.AreEqual(epoch,kernel.SolverEpoch);Assert.AreEqual(frame,kernel.CapturePumpFrontier());
                        var current=session.PinCommittedHostRoot();try{Assert.AreSame(original,current);}finally{current.Release();}
                        Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                    }
                    pressure.Discard();pressure=null;original.Release();original=null;
                    if(refused){
                        Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes(session),"compiler cursor, quota history and published frontier rollback");
                        resumed=kernel.AdvancePhysicalOrdinary(binding,4096);Assert.Greater(resumed,0,"freed scratch capacity must resume actual work");
                    }
                }
                Assert.True(refused,"must reach real partition graph trace scratch");var next=Bytes(session);
                kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);
                session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);session.RestoreState(WorldWire.Unpack(prefix,WorldWire.ReadNavigationSessionState));
                CollectionAssert.AreEqual(prefix,Bytes(session));kernel=new CertifiedRouteKernel();binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                // Adopt the saved Pump frontier without spending a new operation.
                kernel.PauseAtPhysicalBoundary=()=>true;try{kernel.Pump(binding,4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                CollectionAssert.AreEqual(prefix,Bytes(session));Assert.AreEqual(resumed,kernel.AdvancePhysicalOrdinary(binding,4096));CollectionAssert.AreEqual(next,Bytes(session),"same cold retry and paid work");
                for(int n=0;n<20000&&session.RegisteredRouteCount>0;n++)kernel.Pump(binding,4);
                Assert.Zero(session.RegisteredRouteCount,"resumed graph must finish and deliver its registered route");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
            }finally{
                session.PhysicalHostBudget.AllocationAdmission=null;pressure?.Discard();original?.Release();kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            }
        }
        [Test] public void OrdinaryGraphSolveStillPropagatesAllocatorInvariant()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            try{
                kernel.Attach(session.RoutePort);Assert.NotNull(session.Probe(7,1,PlayableProfile.Default.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
                var binding=Binding(session,geometry);kernel.PauseAtPhysicalBoundary=()=>kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Solve;
                try{kernel.Pump(binding,4);}finally{kernel.PauseAtPhysicalBoundary=null;}
                var before=Bytes(session);var budget=session.PhysicalHostBudget;long physical=budget.PhysicalBytes;var invariant=new InvalidOperationException("test allocator invariant");
                budget.AllocationAdmission=bytes=>{if(bytes!=NavigationRetentionBudget.ScopeBytes)throw invariant;return true;};
                try{Assert.AreSame(invariant,Assert.Throws<InvalidOperationException>(()=>kernel.AdvancePhysicalOrdinary(binding,4096)));}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Bytes(session));
            }finally{session.PhysicalHostBudget.AllocationAdmission=null;kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        // Independent object-identity walk of the concrete newly allocated DTO
        // graph. Immutable borrowed facades and separately owned byte outputs are
        // excluded; neither production's shape formula nor wire size is used.
        internal static void AssertCaptureDtoLayout(NavigationSessionState state)
        {
            var seen=new System.Collections.Generic.HashSet<object>();long bytes=0;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
            bool Record(Type type)=>type==typeof(NavigationSessionState)||type==typeof(NavigationGeometryState)||type==typeof(NavigationRequestState)||type==typeof(NavigationAnswerState)||type==typeof(NavigationTechnicalFailureState)||type==typeof(NavigationEntityPointState)||type==typeof(NavigationMovementIdentityState)||type==typeof(NavigationEntityOrderState)||type==typeof(GroupOrderState)||type==typeof(GroupMemberState)||type==typeof(NavCrowdSaveState)||type==typeof(NavUnitSaveState)||type==typeof(NavigationInstalledExecutionState);
            void Visit(object value){
                if(value==null||value is byte[]||value is NavOwnedStorage||!seen.Add(value))return;
                if(value is Array array){bytes+=32L+((array.LongLength*NavStorageLayout.ValueBytes(array.GetType().GetElementType())+7)/8)*8;foreach(var item in array)Visit(item);return;}
                if(!Record(value.GetType()))return;
                bytes+=NavStorageLayout.ObjectBytesBound(value.GetType());foreach(var field in value.GetType().GetFields(flags))Visit(field.GetValue(value));
            }
            Visit(state);Assert.AreSame(state,state.CaptureDto.Value);
            Assert.AreEqual(NavStorageLayout.Charge(NavStorageLayout.ObjectBytesBound(typeof(NavigationCaptureDtoStorage))+bytes),state.CaptureDto.StorageBytes);
        }
        [Test] public void AuthorityCatalogTerrainUsesAppendOnlyTagAndMandatoryEmptyRoles()
        {
            Assert.AreEqual(73,(int)NavPhysicalStorageKind.AuthorityCatalog);Assert.AreEqual(74,(int)NavPhysicalStorageKind.AuthorityCatalogTerrain);Assert.AreEqual(75,(int)NavPhysicalStorageKind.InstalledExecution);Assert.AreEqual(76,(int)NavPhysicalStorageKind.AuthorityCatalogRoutes);
            var source=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);var decodedBudget=new NavigationRetentionBudget();
            try{using(var state=source.CaptureOwnedState()){
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogRoutes),state.CaptureAuthority.PhysicalTag);Assert.NotNull(state.CaptureAuthority.TerrainNames);Assert.Zero(state.CaptureAuthority.TerrainNames.Count);Assert.Zero(state.CaptureAuthority.ActorGlobalCatalog.Count);Assert.Zero(state.CaptureAuthority.ActorLocalCatalog.Count);Assert.Zero(state.CaptureAuthority.InstalledCatalog.Count);Assert.AreEqual(13,state.CaptureAuthority.CollectionCatalog.Count);
                NavigationSession.DecodeDetachedHostAuthority(state.HostAuthority,decodedBudget,out var host,out var authority);
                try{Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogRoutes),authority.PhysicalTag);Assert.NotNull(authority.TerrainNames);Assert.Zero(authority.TerrainNames.Count);Assert.AreEqual(13,authority.CollectionCatalog.Count);Assert.AreEqual(10,((NavRetainedArray<int>)authority.CollectionCatalog[12]).Count);}
                finally{host.ReleaseStorage();authority.ReleaseStorage();}
            }}finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();}
            Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);Assert.Zero(decodedBudget.PhysicalBytes);Assert.Zero(decodedBudget.HeldBytes);Assert.Zero(decodedBudget.ActiveScopes);
        }
        [Test] public void DurablePublicationBodyTagPreservesIdentityAndReadsLegacyEnvelope()
        {
            Assert.AreEqual(70,(int)NavPhysicalStorageKind.HostEnvelope);Assert.AreEqual(86,(int)NavPhysicalStorageKind.AuthorityCatalogAnswers);Assert.AreEqual(87,(int)NavPhysicalStorageKind.HostEnvelopePublication);Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavigationHostEnvelope)),192);
            var source=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);NavigationSession cold=null;var budget=new NavigationRetentionBudget();
            try{
                source.Crowd.Add(1,new NavPoint(-5,0));Assert.True(source.Move(1,new NavPoint(5,0)));source.Stop(1,false);Assert.Greater(source.Admission.PublicationIdentity,1);
                using(var capture=source.CaptureOwnedState()){
                    NavigationSession.DecodeDetachedHostAuthority(capture.HostAuthority,budget,out var host,out var authority,out long publication);try{Assert.AreEqual(source.Admission.PublicationIdentity,publication);Assert.AreEqual(capture.AdmissionPublicationIdentity,publication);}finally{host.ReleaseStorage();authority.ReleaseStorage();}Assert.Zero(budget.PhysicalBytes);
                    var bytes=Bytes(source);cold=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));Assert.AreEqual(source.Admission.PublicationIdentity,cold.Admission.PublicationIdentity);CollectionAssert.AreEqual(bytes,Bytes(cold));
                    var rejected=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);try{var altered=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);altered.AdmissionPublicationIdentity=source.Admission.PublicationIdentity+1;long initial=rejected.PhysicalHostBudget.PhysicalBytes;Assert.Throws<ArgumentException>(()=>rejected.RestoreState(altered));Assert.AreEqual(initial,rejected.PhysicalHostBudget.PhysicalBytes);Assert.Zero(rejected.PhysicalHostBudget.HeldBytes);Assert.Zero(rejected.PhysicalHostBudget.ActiveScopes);}finally{rejected.DestroyNavigationAuthority();Assert.Zero(rejected.PhysicalHostBudget.PhysicalBytes);}
                    Assert.True(source.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var scope));byte[] legacy;
                    using(scope){var envelope=NavigationHostEnvelope.CreateOwned(capture.CaptureRoot,capture.CaptureAuthority,scope.OwnedReservation());try{Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelope),envelope.PhysicalTag);using(var output=NavigationCaptureBytes.Write(scope.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,envelope,scope.OwnedReservation())))legacy=(byte[])output.Bytes.Clone();scope.Attach(envelope);scope.Commit(envelope,_=>{});}finally{envelope.ReleaseStorage();}}
                    NavigationSession.DecodeDetachedHostAuthority(legacy,budget,out host,out authority,out publication);try{Assert.Zero(publication,"old envelope retains existing default behavior");}finally{host.ReleaseStorage();authority.ReleaseStorage();}
                    var malformed=(byte[])capture.HostAuthority.Clone();using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(malformed,false))){reader.ReadInt32();reader.ReadInt32();Assert.AreEqual(2,reader.ReadByte());reader.ReadInt32();Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication).Encoded,reader.ReadInt32());long scalar=reader.BaseStream.Position;Array.Clear(malformed,(int)scalar,8);}
                    Assert.Throws<ArgumentException>(()=>NavigationSession.DecodeDetachedHostAuthority(malformed,budget,out _,out _,out _));Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var oldReader));using(oldReader)using(var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(capture.HostAuthority,false)))Assert.Throws<ArgumentException>(()=>NavPhysicalBodyWire.ReadFrom(reader,oldReader.OwnedReservation(),new LegacyHostEnvelopeReader()));Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();cold?.ReleaseCommittedHostRoot();cold?.DestroyNavigationAuthority();}Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);if(cold!=null)Assert.Zero(cold.PhysicalHostBudget.PhysicalBytes);Assert.Zero(budget.PhysicalBytes);
        }
        private sealed class LegacyHostEnvelopeReader:INavPhysicalBodyExtension
        {
            public NavOwnedStorage Read(NavPhysicalTag tag,NavPhysicalBodyReader reader)
            {if(tag.Storage==NavPhysicalStorageKind.HostEnvelopePublication)throw new ArgumentException("Unsupported host BODY tag in old reader.");return NavigationHostBodyExtension.Instance.Read(tag,reader);}
        }
        [Test] public void PrivateCaptureOutputsAndPinnedRootsRemainOwnedAfterSessionDestruction()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var budget=session.PhysicalHostBudget;
            using(var capture=session.CaptureOwnedState()){
                Assert.NotNull(capture.CaptureOutput);Assert.NotNull(capture.CaptureArrivalOutput);Assert.NotNull(capture.CaptureDto);AssertCaptureDtoLayout(capture);
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var liveCore=(NavOwnedStorage)typeof(NavigationSession).GetField("authorityOwners",flags).GetValue(session);
                var transport=(NavOwnedStorage)typeof(NavigationSession).GetField("transportOwner",flags).GetValue(session);
                var answers=(NavOwnedStorage)session.Answers.GetType().GetField("retention",flags).GetValue(session.Answers);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,capture.CaptureRoot,capture.CaptureAuthority,capture.CaptureOutput,capture.CaptureArrivalOutput,capture.CaptureDto,liveCore,transport,answers),budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                var body=(byte[])capture.HostAuthority.Clone();
                session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                CollectionAssert.AreEqual(body,capture.HostAuthority);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,capture.CaptureRoot,capture.CaptureAuthority,capture.CaptureOutput,capture.CaptureArrivalOutput,capture.CaptureDto),budget.PhysicalBytes);
                using(var packed=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),budget)){
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,capture.CaptureRoot,capture.CaptureAuthority,capture.CaptureOutput,capture.CaptureArrivalOutput,capture.CaptureDto,packed),budget.PhysicalBytes);
                    Assert.Greater(budget.PhysicalBytes,packed.StorageBytes);Assert.Zero(budget.ActiveScopes);
                }
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PublicCaptureTransferLeavesNoPrivateOutputChargeAndPreservesCallerBytes()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var capture=session.CaptureState();var body=(byte[])capture.HostAuthority.Clone();var arrival=(byte[])capture.ArrivalAuthority.Clone();
            Assert.Null(capture.CaptureOutput);Assert.Null(capture.CaptureArrivalOutput);Assert.Null(capture.CaptureDto);Assert.Null(capture.CaptureRoot);
            var budget=session.PhysicalHostBudget;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            Assert.Zero(budget.PhysicalBytes);capture.Dispose();CollectionAssert.AreEqual(body,capture.HostAuthority);CollectionAssert.AreEqual(arrival,capture.ArrivalAuthority);
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void ActualHostRequestChargeIncludesConstructedFacadeAndOptionalMember(bool member,bool direct)
        {
            var budget=new NavigationRetentionBudget();var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var identity=member?new NavigationMemberIdentity(7,8,9,10,11):null;
            var source=new NavigationRequest(1,2,3,4,PlayableProfile.Default.Navigation,geometry,new NavPoint(-5,0),new NavPoint(5,0),memberIdentity:identity);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
            NavigationHostRequest owner;
            using(scope){owner=direct?NavigationHostRequest.CreateOwned(1,2,3,4,PlayableProfile.Default.Navigation,geometry,new NavPoint(-5,0),new NavPoint(5,0),geometry,null,identity,null,null,null,scope.OwnedReservation()):NavigationHostRequest.CopyOwned(source,scope.OwnedReservation());scope.Attach(owner);scope.Commit(owner,_=>{});}
            try{
                long expected=NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRequest))+NavStorageLayout.ObjectBytesBound(typeof(NavigationRequest))+(member?NavStorageLayout.ObjectBytesBound(typeof(NavigationMemberIdentity)):0);
                Assert.AreEqual(NavStorageLayout.Charge(Math.Max(256,expected)),owner.StorageBytes);
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(owner),budget.PhysicalBytes);
                Assert.AreNotSame(source,owner.Request);Assert.AreSame(owner,owner.Request.StorageOwner);Assert.AreSame(owner,((INavStorageFacade)owner.Request).StorageRoot);
                if(member){Assert.AreNotSame(identity,owner.Request.MemberIdentity);Assert.AreEqual(identity.MembershipRevision,owner.Request.MemberIdentity.MembershipRevision);}
                else Assert.Null(owner.Request.MemberIdentity);
            }finally{owner.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void BodyCatalogRejectsOuterProfileOrMemberMutationBeforeCanonicalFacadeBinding(bool member)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var kernel=new CertifiedRouteKernel();NavigationSessionState captured=null;
            for(int step=0;step<20000&&captured==null;step++){
                kernel.Pump(Binding(session,geometry),1);var root=session.PinCommittedHostRoot();
                try{if(root.Rows.Count!=0)captured=WorldWire.Unpack(Bytes(session),WorldWire.ReadNavigationSessionState);}
                finally{root.Release();}
            }
            Assert.NotNull(captured);Assert.AreEqual(1,captured.RegisteredRequestIndices.Length);
            var saved=captured.Requests[captured.RegisteredRequestIndices[0]];
            if(member){var old=saved.MemberIdentity;Assert.NotNull(old);saved.MemberIdentity=new NavigationMemberIdentity(old.Incarnation+1,old.MobilityRevision,old.GroupId,old.OrderRevision,old.MembershipRevision);}
            else saved.ProfileValues[0]+=.25;
            kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            var cold=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);var budget=cold.PhysicalHostBudget;
            try{Assert.Throws<ArgumentException>(()=>cold.RestoreState(captured));}
            finally{cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void OwnedUnitProfileFactoryPreservesAllFieldsAndAdmitsBeforeFacadeConstruction()
        {
            var source=PlayableProfile.Default.Navigation;var expected=source.ForUnit(.7,3,2);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));NavigationProfile owned;
            using(scope){owned=NavigationProfile.CreateForUnitOwned(source,.7,3,2,scope.OwnedReservation());scope.Attach(owned.StorageRoot);scope.Commit(owned.StorageRoot,_=>{});}
            try{
                Assert.AreNotSame(source,owned);Assert.AreEqual(expected.ProfileId,owned.ProfileId);Assert.AreEqual(expected.Revision,owned.Revision);
                foreach(var field in typeof(NavigationProfile).GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public))
                    if(field.PropertyType==typeof(double))Assert.AreEqual(field.GetValue(expected),field.GetValue(owned),field.Name);
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(owned.StorageRoot),budget.PhysicalBytes);
            }finally{owned.StorageRoot.Release();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)] public void AuthorityCoreRebindRefusalsPreservePublishedBytesAndOwnedRoots(bool withHost)
        {
            void PrepareHost(NavigationSession session){
                if(!withHost)return;
                Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,0,out var scope));
                using(scope){var root=NavigationHostRoot.CreateOwned(scope.OwnedReservation());
                    try{root.SealPrepared();scope.Attach(root);session.PublishPhysicalHostRoot(scope,null,root,()=>{});}
                    finally{root.ReleaseStorage();}
                }
                Assert.True(session.HasPhysicalHostRoot);
            }
            var next=PlayableProfile.Default.Navigation.ForUnit(.7,3,2);
            int sites=0;
            var baseline=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            PrepareHost(baseline);baseline.PhysicalHostBudget.AllocationAdmission=_=>{sites++;return true;};
            baseline.Rebind(next);baseline.ReleaseCommittedHostRoot();baseline.DestroyNavigationAuthority();
            Assert.Greater(sites,0);Assert.Zero(baseline.PhysicalHostBudget.PhysicalBytes);
            for(int fail=1;fail<=sites;fail++){
                var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                PrepareHost(session);var budget=session.PhysicalHostBudget;var before=Bytes(session);var charge=budget.PhysicalBytes;int attempt=0;
                budget.AllocationAdmission=_=>++attempt!=fail;
                Assert.Catch<Exception>(()=>session.Rebind(next));budget.AllocationAdmission=null;
                CollectionAssert.AreEqual(before,Bytes(session));Assert.AreEqual(charge,budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);
            }
        }
        [Test] public void AuthorityCoreRefusesReturnedNestedBatchEvenWithUnchangedDepthAndRoots()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var budget=session.PhysicalHostBudget;var before=Bytes(session);var charge=budget.PhysicalBytes;bool nested=false;
            budget.AllocationAdmission=_=>{
                if(!nested){nested=true;Assert.False(session.Move(999,new NavPoint(0,0)));}
                return true;
            };
            Assert.Throws<InvalidOperationException>(()=>session.Rebind(PlayableProfile.Default.Navigation.ForUnit(.7,3,2)));
            budget.AllocationAdmission=null;Assert.True(nested);CollectionAssert.AreEqual(before,Bytes(session));
            Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void ImmutableMemberBodyFreezesSourceAndPreservesDistinctEqualNames()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var source=session.GroupFor(1).Members[0];source.ContactPhase=source.GoalLocation.Value.SurfaceId;
            source.Branch=7;source.ContactTick=8;source.ProgressRequest=9;source.Progress=-.5;
            var expected=new NavigationAuthorityMember.Values(source);string expectedName=source.ContactPhase;
            session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            var budget=new NavigationRetentionBudget();NavigationAuthorityMember hot,cold;NavigationCaptureBytes bytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var first));
            budget.AllocationAdmission=_=>{source.ContactPhase="changed";source.GoalLocation=new NavLocation(new NavPoint(19,19),"changed");source.MobilityRevision++;return true;};
            using(first){hot=NavigationAuthorityMember.CopyOwned(source,first.OwnedReservation());first.Attach(hot);first.Commit(hot,_=>{});}budget.AllocationAdmission=null;
            Assert.AreEqual(expected.MobilityRevision,hot.Data.MobilityRevision);Assert.AreEqual(expected.Location,hot.GoalLocation.Value.Position);
            Assert.AreEqual(expectedName,hot.ContactPhase);Assert.AreEqual(expectedName,hot.GoalLocation.Value.SurfaceId);Assert.AreNotSame(hot.ContactPhase,hot.GoalLocation.Value.SurfaceId);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var capture));
            using(capture){bytes=NavigationCaptureBytes.Write(capture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,hot,capture.OwnedReservation()));capture.Attach(bytes);capture.Commit(bytes,_=>{});}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,0,out var restore));
            using(restore){using(NavStorageScratch.Reserve(restore.OwnedReservation(),256))
                using(var stream=new System.IO.MemoryStream(bytes.Bytes,false))using(var reader=new System.IO.BinaryReader(stream)){
                    cold=(NavigationAuthorityMember)NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation(),NavigationHostBodyExtension.Instance);Assert.AreEqual(stream.Length,stream.Position);
                }
                restore.Attach(cold);restore.Commit(cold,_=>{});
            }
            Assert.AreEqual(expectedName,cold.ContactPhase);Assert.AreEqual(expectedName,cold.GoalLocation.Value.SurfaceId);Assert.AreNotSame(cold.ContactPhase,cold.GoalLocation.Value.SurfaceId);
            foreach(var field in typeof(NavigationAuthorityMember.Values).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic))
                Assert.AreEqual(field.GetValue(expected),field.GetValue(cold.Data),field.Name);
            Assert.AreEqual(NavigationStorageOwnershipTests.Unique(hot,cold,bytes),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var recapture));
            using(recapture){using(var again=NavigationCaptureBytes.Write(recapture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,cold,recapture.OwnedReservation()))){recapture.Attach(again);recapture.Commit(again,_=>{});CollectionAssert.AreEqual(bytes.Bytes,again.Bytes);}}
            bytes.Dispose();hot.ReleaseStorage();cold.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void MemberBoundsAndForeignLocationRefuseBeforeAnyPayloadAdmission()
        {
            foreach(string field in new[]{"Slot","ContactTick","ProgressRequest","Branch","Reservation"}){
                var source=new GroupMemberState{ContactPhase="owned",GoalLocation=new NavLocation(new NavPoint(1,2),"flat")};
                var member=typeof(GroupMemberState).GetField(field);member.SetValue(source,member.FieldType==typeof(int)?(object)(-1):(object)(-1L));
                var budget=new NavigationRetentionBudget();int attempts=0;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));budget.AllocationAdmission=_=>{attempts++;return true;};
                using(scope)Assert.Throws<ArgumentException>(()=>NavigationAuthorityMember.CopyOwned(source,scope.OwnedReservation()),field);
                Assert.Zero(attempts,field);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
            var values=new NavigationAuthorityMember.Values(0,0,0,0,0,GroupMemberPhase.PendingRoute,default,new NavPoint(17,19),false,false,false,0,0,0,0,0);
            Assert.AreEqual(default(NavPoint),values.Location,"inactive location has one hot/cold representation");
            var foreign=new NavigationRetentionBudget();NavOwnedText text;
            Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var birth));
            using(birth){text=NavOwnedText.Copy("flat",birth.OwnedReservation());birth.Attach(text);birth.Commit(text,_=>{});}
            var target=new NavigationRetentionBudget();int allocations=0;int refs=text.References;
            Assert.True(target.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var destination));target.AllocationAdmission=_=>{allocations++;return true;};
            using(destination)Assert.Throws<ArgumentException>(()=>NavigationAuthorityMember.CopyOwned(new GroupMemberState{ContactPhase="phase",GoalLocation=new NavLocation(default,new NavNameReference(text,NavNameRole.Text))},destination.OwnedReservation()));
            Assert.Zero(allocations);Assert.AreEqual(refs,text.References);Assert.Zero(target.PhysicalBytes);Assert.Zero(target.HeldBytes);Assert.Zero(target.ActiveScopes);
            text.Release();Assert.Zero(foreign.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void MalformedMemberBodyDiscardsPayloadBirthsAndKeepsPrivateInput(bool selector)
        {
            var budget=new NavigationRetentionBudget();byte[] publicBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var capture));
            using(capture){var root=NavigationAuthorityMember.CopyOwned(new GroupMemberState{ContactPhase="flat",GoalLocation=new NavLocation(default,"flat")},capture.OwnedReservation());
                try{using(var bytes=NavigationCaptureBytes.Write(capture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,root,capture.OwnedReservation()))){capture.Attach(bytes);capture.Commit(bytes,_=>{});publicBytes=bytes.TransferPublic();}}
                finally{root.ReleaseStorage();}
            }
            Assert.Zero(budget.PhysicalBytes);
            using(var stream=new System.IO.MemoryStream(publicBytes,true))using(var writer=new System.IO.BinaryWriter(stream)){
                stream.Position=selector?publicBytes.Length-4:21;writer.Write(selector?0:-1);
            }
            using(var input=NavigationCaptureBytes.CreateOwned(publicBytes.Length,budget,_=>(byte[])publicBytes.Clone())){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,0,out var restore));
                using(restore)using(NavStorageScratch.Reserve(restore.OwnedReservation(),256))
                    using(var stream=new System.IO.MemoryStream(input.Bytes,false))using(var reader=new System.IO.BinaryReader(stream))
                        Assert.Throws<ArgumentException>(()=>NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation(),NavigationHostBodyExtension.Instance));
                Assert.AreEqual(NavigationStorageOwnershipTests.Unique(input),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                CollectionAssert.AreEqual(publicBytes,input.Bytes);
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void EveryMemberFactoryAdmissionRefusalLeavesNoBornPayload()
        {
            var source=new GroupMemberState{ContactPhase="phase",GoalLocation=new NavLocation(new NavPoint(1,2),"flat")};int sites=0;
            var count=new NavigationRetentionBudget();
            Assert.True(count.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var initial));count.AllocationAdmission=_=>{sites++;return true;};
            using(initial){var owner=NavigationAuthorityMember.CopyOwned(source,initial.OwnedReservation());initial.Attach(owner);initial.Commit(owner,_=>{});owner.ReleaseStorage();}
            Assert.Greater(sites,0);Assert.Zero(count.PhysicalBytes);
            for(int fail=1;fail<=sites;fail++){
                var budget=new NavigationRetentionBudget();int attempts=0;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));budget.AllocationAdmission=_=>++attempts!=fail;
                using(scope)Assert.Catch<Exception>(()=>NavigationAuthorityMember.CopyOwned(source,scope.OwnedReservation()),"actual member site "+fail);
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        [Test] public void MemberScalarCowKeepsActualDistinctNameOwnersAfterRetiringSource()
        {
            var budget=new NavigationRetentionBudget();NavigationAuthorityMember original,fork;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var first));
            using(first){original=NavigationAuthorityMember.CopyOwned(new GroupMemberState{ContactPhase="flat",GoalLocation=new NavLocation(new NavPoint(1,2),"flat")},first.OwnedReservation());first.Attach(original);first.Commit(original,_=>{});}
            var contact=original.ContactOwner;var goal=original.GoalSelector;Assert.AreNotSame(contact,goal.Owner);long before=budget.PhysicalBytes;
            var data=new NavigationAuthorityMember.Values(1,2,3,4,5,GroupMemberPhase.March,new NavPoint(7,8),new NavPoint(9,10),true,true,false,11,12,13,14,-.5);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var change));
            using(change){fork=NavigationAuthorityMember.CopyWithValuesOwned(original,data,change.OwnedReservation());change.Attach(fork);change.Commit(fork,_=>{});}
            Assert.AreSame(contact,fork.ContactOwner);Assert.AreSame(goal.Owner,fork.GoalSelector.Owner);Assert.AreEqual(goal.Role,fork.GoalSelector.Role);Assert.AreEqual(goal.Index,fork.GoalSelector.Index);
            Assert.AreEqual(before+fork.StorageBytes,budget.PhysicalBytes,"only the affected scalar owner is new");original.ReleaseStorage();
            Assert.AreEqual(NavigationStorageOwnershipTests.Unique(fork),budget.PhysicalBytes);Assert.AreEqual(new NavPoint(9,10),fork.GoalLocation.Value.Position);Assert.AreEqual("flat",fork.ContactPhase);
            fork.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void MemberScalarCowDropsLocationAndRefusesAddingItWithoutSelector()
        {
            var budget=new NavigationRetentionBudget();NavigationAuthorityMember original,without;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var first));
            using(first){original=NavigationAuthorityMember.CopyOwned(new GroupMemberState{ContactPhase="contact",GoalLocation=new NavLocation(new NavPoint(1,2),"flat")},first.OwnedReservation());first.Attach(original);first.Commit(original,_=>{});}
            var contact=original.ContactOwner;var goal=original.GoalSelector.Owner;long before=budget.PhysicalBytes,originalBytes=original.StorageBytes,goalBytes=goal.StorageBytes;
            var absent=new NavigationAuthorityMember.Values(0,0,0,0,0,GroupMemberPhase.PendingRoute,default,new NavPoint(7,8),false,false,false,0,0,0,0,0);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var change));
            using(change){without=NavigationAuthorityMember.CopyWithValuesOwned(original,absent,change.OwnedReservation());change.Attach(without);change.Commit(without,_=>{});}
            Assert.AreSame(contact,without.ContactOwner);Assert.Null(without.GoalLocation);Assert.Null(without.GoalSelector.Owner);Assert.AreEqual(default(NavPoint),without.Data.Location);
            Assert.AreEqual(before+without.StorageBytes,budget.PhysicalBytes);original.ReleaseStorage();
            Assert.Zero(goal.References);Assert.AreEqual(1,contact.References);Assert.AreEqual(before+without.StorageBytes-originalBytes-goalBytes,budget.PhysicalBytes);
            Assert.AreEqual(NavigationStorageOwnershipTests.Unique(without),budget.PhysicalBytes);
            var present=new NavigationAuthorityMember.Values(0,0,0,0,0,GroupMemberPhase.PendingRoute,default,new NavPoint(1,2),true,false,false,0,0,0,0,0);
            long charge=budget.PhysicalBytes;int references=without.References,contactReferences=contact.References,attempts=0;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var rejected));budget.AllocationAdmission=_=>{attempts++;return true;};
            using(rejected)Assert.Throws<ArgumentException>(()=>NavigationAuthorityMember.CopyWithValuesOwned(without,present,rejected.OwnedReservation()));budget.AllocationAdmission=null;
            Assert.Zero(attempts);Assert.AreEqual(references,without.References);Assert.AreEqual(contactReferences,contact.References);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            without.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void TypedGroupBodyKeepsAllScalarAndDistinctMetadataEdges()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var saved=session.GroupFor(1);saved.Origin=PlayableOrderOrigin.Ai;saved.OwnerId="flat";saved.Source="flat";saved.JobId=1;saved.ActionId=2;saved.OriginalLocation=new NavLocation(saved.OriginalGoal,"flat");saved.Members[0].ContactPhase="contact";
            session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();var expected=new NavigationAuthorityGroup.Values(saved);
            var budget=new NavigationRetentionBudget();NavigationAuthorityGroup hot,cold;NavigationCaptureBytes bytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var prepare));
            using(prepare){var member=NavigationAuthorityMember.CopyOwned(saved.Members[0],prepare.OwnedReservation());NavRetainedArray<NavOwnedStorage> members;
                using(NavStorageScratch.Reserve(prepare.OwnedReservation(),64))members=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},prepare.OwnedReservation().TryReserve);
                member.ReleaseStorage();budget.AllocationAdmission=_=>{saved.OwnerId="changed";saved.Source="changed";saved.OriginalLocation=new NavLocation(default,"changed");saved.CommandSequence++;return true;};
                try{hot=NavigationAuthorityGroup.CopyNamesOwned(saved,members,prepare.OwnedReservation());prepare.Attach(hot);prepare.Commit(hot,_=>{});}finally{members.Release();budget.AllocationAdmission=null;}
            }
            Assert.AreEqual("flat",hot.OwnerId);Assert.AreEqual("flat",hot.Source);Assert.AreEqual("flat",hot.OriginalLocation.Value.SurfaceId);Assert.AreNotSame(hot.OwnerIdOwner,hot.SourceOwner);Assert.AreNotSame(hot.SourceOwner,hot.OriginalSelector.Owner);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var capture));using(capture){bytes=NavigationCaptureBytes.Write(capture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,hot,capture.OwnedReservation()));capture.Attach(bytes);capture.Commit(bytes,_=>{});}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,0,out var restore));using(restore){using(NavStorageScratch.Reserve(restore.OwnedReservation(),256))using(var stream=new System.IO.MemoryStream(bytes.Bytes,false))using(var reader=new System.IO.BinaryReader(stream)){cold=(NavigationAuthorityGroup)NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation(),NavigationHostBodyExtension.Instance);Assert.AreEqual(stream.Length,stream.Position);}restore.Attach(cold);restore.Commit(cold,_=>{});}
            foreach(var field in typeof(NavigationAuthorityGroup.Values).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic))Assert.AreEqual(field.GetValue(expected),field.GetValue(cold.Data),field.Name);
            Assert.AreEqual("flat",cold.OwnerId);Assert.AreEqual("flat",cold.Source);Assert.AreNotSame(cold.OwnerIdOwner,cold.SourceOwner);Assert.AreNotSame(cold.SourceOwner,cold.OriginalSelector.Owner);Assert.AreEqual("contact",((NavigationAuthorityMember)cold.Members[0]).ContactPhase);
            Assert.AreEqual(NavigationStorageOwnershipTests.Unique(hot,cold,bytes),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var repeat));using(repeat){using(var again=NavigationCaptureBytes.Write(repeat.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,cold,repeat.OwnedReservation()))){repeat.Attach(again);repeat.Commit(again,_=>{});CollectionAssert.AreEqual(bytes.Bytes,again.Bytes);}}
            hot.ReleaseStorage();bytes.Dispose();Assert.AreEqual(NavigationStorageOwnershipTests.Unique(cold),budget.PhysicalBytes);cold.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void GroupMemberCopyRejectsExternallyMutableListBeforeReservationCallback()
        {
            var budget=new NavigationRetentionBudget();NavigationAuthorityMember member;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var own));
            using(own){member=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=1,Slot=1,OrderRevision=1},own.OwnedReservation());own.Attach(member);own.Commit(member,_=>{});}
            var foreignBudget=new NavigationRetentionBudget();NavigationAuthorityMember foreign;Assert.True(foreignBudget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var other));
            using(other){foreign=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=2,Slot=2,OrderRevision=1},other.OwnedReservation());other.Attach(foreign);other.Commit(foreign,_=>{});}
            var external=new System.Collections.Generic.List<NavOwnedStorage>{member};var data=new NavigationAuthorityGroup.Values(new GroupOrderState{WorldGeneration=1,GroupId=1,OrderRevision=1,MembershipRevision=1,Kind=PlayableCommandKind.Move});
            int attempts=0,refs=member.References,foreignRefs=foreign.References;long charge=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var rejected));budget.AllocationAdmission=_=>{attempts++;external[0]=foreign;return true;};
            using(rejected)Assert.Throws<ArgumentException>(()=>NavigationAuthorityGroup.CopyMembersOwned(data,null,null,default,external,rejected.OwnedReservation()));budget.AllocationAdmission=null;
            Assert.Zero(attempts,"external list rejected before callback can replace member");Assert.AreSame(member,external[0]);Assert.AreEqual(refs,member.References);Assert.AreEqual(foreignRefs,foreign.References);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            member.ReleaseStorage();foreign.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(foreignBudget.PhysicalBytes);
        }
        [Test] public void EveryTypedGroupFactoryRefusalPreservesImmutableMembersAndOldCharge()
        {
            var value=new GroupOrderState{WorldGeneration=1,GroupId=1,OrderRevision=1,MembershipRevision=1,Origin=PlayableOrderOrigin.Ai,Kind=PlayableCommandKind.Move,OwnerId="owner",Source="source",JobId=1,ActionId=2,OriginalLocation=new NavLocation(default,"flat")};
            NavRetainedArray<NavOwnedStorage> Members(NavigationRetentionBudget budget){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){var member=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=1,Slot=1,OrderRevision=1,ContactPhase="contact"},scope.OwnedReservation());NavRetainedArray<NavOwnedStorage> result;using(NavStorageScratch.Reserve(scope.OwnedReservation(),64))result=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},scope.OwnedReservation().TryReserve);member.ReleaseStorage();scope.Attach(result);scope.Commit(result,_=>{});return result;}}
            int sites=0;var count=new NavigationRetentionBudget();var countedMembers=Members(count);Assert.True(count.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var counted));count.AllocationAdmission=_=>{sites++;return true;};using(counted){var group=NavigationAuthorityGroup.CopyNamesOwned(value,countedMembers,counted.OwnedReservation());counted.Attach(group);counted.Commit(group,_=>{});group.ReleaseStorage();}countedMembers.Release();Assert.Greater(sites,0);Assert.Zero(count.PhysicalBytes);
            for(int fail=1;fail<=sites;fail++){
                var budget=new NavigationRetentionBudget();var members=Members(budget);long charge=budget.PhysicalBytes;int refs=members.References,memberRefs=members[0].References,attempts=0;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));budget.AllocationAdmission=_=>++attempts!=fail;
                using(scope)Assert.Catch<Exception>(()=>NavigationAuthorityGroup.CopyNamesOwned(value,members,scope.OwnedReservation()),"actual group site "+fail);budget.AllocationAdmission=null;
                Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreEqual(refs,members.References);Assert.AreEqual(memberRefs,members[0].References);Assert.AreEqual(1,((NavigationAuthorityMember)members[0]).Data.Entity);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                members.Release();Assert.Zero(budget.PhysicalBytes);
            }
        }
        [Test] public void RuntimeLiveGroupSealInstallAndRetirementDoNotBecomeCaptureRoots()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));var value=session.GroupFor(1);var budget=session.PhysicalHostBudget;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));NavigationLiveGroup live;NavigationPreparedSlotAggregate slots;
            using(scope){var member=NavigationAuthorityMember.CopyOwned(value.Members[0],scope.OwnedReservation());NavRetainedArray<NavOwnedStorage> members;using(NavStorageScratch.Reserve(scope.OwnedReservation(),64))members=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},scope.OwnedReservation().TryReserve);member.ReleaseStorage();
                var version=NavigationAuthorityGroup.CopyNamesOwned(value,members,scope.OwnedReservation());members.Release();live=NavigationLiveGroup.CreateOwned(version,scope);version.ReleaseStorage();
                long before=budget.PhysicalBytes;int refs=live.References,attempts=0;budget.AllocationAdmission=_=>{attempts++;return true;};
                Assert.Throws<InvalidOperationException>(()=>NavigationPreparedSlotAggregate.CreateOwned(scope,live));Assert.Zero(attempts);Assert.AreEqual(refs,live.References);Assert.AreEqual(before,budget.PhysicalBytes);budget.AllocationAdmission=null;
                live.SealForInstall(scope);Assert.Throws<InvalidOperationException>(()=>live.SealForInstall(scope));
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var wrong));attempts=0;before=budget.PhysicalBytes;refs=live.References;budget.AllocationAdmission=_=>{attempts++;return true;};
                using(wrong)Assert.Throws<InvalidOperationException>(()=>NavigationPreparedSlotAggregate.CreateOwned(wrong,live));budget.AllocationAdmission=null;Assert.Zero(attempts);Assert.AreEqual(before,budget.PhysicalBytes);Assert.AreEqual(refs,live.References);
                slots=NavigationPreparedSlotAggregate.CreateOwned(scope,live);Assert.False(live.IsImmutableCaptureRoot);Assert.False(slots.IsImmutableCaptureRoot);
                Assert.Throws<ArgumentException>(()=>{using(var bytes=NavigationCaptureBytes.Write(scope.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,live,scope.OwnedReservation()))){} });
                Assert.Throws<ArgumentException>(()=>{using(var bytes=NavigationCaptureBytes.Write(scope.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,slots,scope.OwnedReservation()))){} });
                scope.Attach(slots);scope.Publish(slots,_=>session.RunHostStep(()=>{slots.ValidateInstall(scope);live.ActivateNoThrow(session);}));scope.ResolveCommitted();slots.ReleaseStorage();
            }
            Assert.AreEqual(NavigationLiveStoragePhase.Live,live.Phase);var observation=live.ObserveDetached();observation.Members[0].Entity=999;Assert.AreEqual(1,live.ObserveDetached().Members[0].Entity);
            Assert.Throws<InvalidOperationException>(()=>session.ObserveLiveWriteToken());NavigationLiveWriteToken token=default;session.RunHostStep(()=>{token=session.ObserveLiveWriteToken();live.ValidateLiveTransaction(token);});Assert.Throws<InvalidOperationException>(()=>live.ValidateLiveTransaction(token));
            session.RunHostStep(()=>{live.ValidateLiveTransaction(session.ObserveLiveWriteToken());live.RetireNoThrow();});Assert.AreEqual(NavigationLiveStoragePhase.Retired,live.Phase);Assert.Throws<InvalidOperationException>(()=>live.ValidateLiveTransaction(token));Assert.Throws<InvalidOperationException>(()=>live.ObserveDetached());
            live.ReleaseStorage();Assert.AreEqual(NavigationLiveStoragePhase.Released,live.Phase);session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void NavigationLiveSlotsOwnRealArraysAndPreserveFreeLifoUnderPrivateBuilder()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
            using(scope){var member=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=1,Slot=1,OrderRevision=1},scope.OwnedReservation());NavRetainedArray<NavOwnedStorage> members;using(NavStorageScratch.Reserve(scope.OwnedReservation(),64))members=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},scope.OwnedReservation().TryReserve);member.ReleaseStorage();
                var version=NavigationAuthorityGroup.CopyNamesOwned(new GroupOrderState{WorldGeneration=1,GroupId=1,OrderRevision=1,MembershipRevision=1,Kind=PlayableCommandKind.Move},members,scope.OwnedReservation());members.Release();var live=NavigationLiveGroup.CreateOwned(version,scope);version.ReleaseStorage();live.SealForInstall(scope);
                var backing=NavigationLiveGroupSlots.CreateOwned(4,scope);Assert.AreEqual(4,backing.Capacity);Assert.AreEqual(8,backing.LookupCapacity);
                long arrays=0;int arrayCount=0;foreach(var field in typeof(NavigationLiveGroupSlots).GetFields(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance))if(field.GetValue(backing) is Array array){arrayCount++;int width=field.FieldType.GetElementType()==typeof(bool)?1:field.FieldType.GetElementType()==typeof(int)?4:8;arrays+=32+((array.LongLength*width+7)/8)*8;}
                Assert.AreEqual(9,arrayCount);Assert.AreEqual(NavStorageLayout.Charge(NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveGroupSlots))+arrays),backing.StorageBytes);
                backing.AddPreparing(1,live,scope);backing.AddPreparing(2,live,scope);backing.AddPreparing(3,live,scope);backing.RemovePreparing(1,scope);backing.RemovePreparing(2,scope);Assert.AreEqual(1,backing.FreeAtLifoIndex(0));Assert.AreEqual(0,backing.FreeAtLifoIndex(1));
                backing.AddPreparing(4,live,scope);Assert.AreEqual(4,backing.KeyAtSlot(1));Assert.Null(backing.GroupAtSlot(0));Assert.AreEqual(3,backing.KeyAtSlot(2));backing.AddPreparing(5,live,scope);Assert.AreEqual(5,backing.KeyAtSlot(0));Assert.AreEqual(3,backing.Slots);Assert.AreEqual(3,backing.Count);Assert.Zero(backing.FreeCount);
                backing.SealForInstall(scope);Assert.Throws<InvalidOperationException>(()=>backing.AddPreparing(6,live,scope));Assert.False(backing.IsImmutableCaptureRoot);
                var aggregate=NavigationPreparedSlotAggregate.CreateOwned(scope,slots:backing);backing.ReleaseStorage();scope.Attach(aggregate);scope.Commit(aggregate,_=>{});Assert.AreEqual(NavigationStorageOwnershipTests.Unique(aggregate,live),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                aggregate.ReleaseStorage();Assert.AreEqual(NavigationLiveStoragePhase.Released,backing.Phase);live.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
            }
        }
        [Test] public void AdmissionProjectionSharesUntouchedMembersAndOwnsOnlyExposedPayload()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var budget=session.PhysicalHostBudget;NavigationAdmissionStorage first=null,progress=null,phase=null;bool destroyed=false;
            NavigationAuthorityGroup progressVersion=null,phaseVersion=null;
            try{
                session.Crowd.Add(1,new NavPoint(-5,-1));session.Crowd.Add(2,new NavPoint(-5,1));
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(5,0),new NavPoint(5,0)}));
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                var groups=(NavigationCommandSlots<long,GroupOrderState>)typeof(NavigationSession).GetField("groups",flags).GetValue(session);
                var names=(NavRetainedArray<NavNameReference>)typeof(NavigationSession).GetField("terrainNameOwners",flags).GetValue(session);
                var live=(NavigationLiveGroup)groups.OwnerFor(session.GroupFor(1).GroupId);var version=live.ImmutableVersion;
                var original=session.Admission;var requests=original.Requests;var expectedRequest=requests[0];
                NavigationAdmissionStorage Prepare(NavigationAuthorityGroup current,NavigationAdmissionStorage previous,long publication,bool denyFactory=false)
                {
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,publication,out var scope));
                    using(scope){var versions=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{current},scope.TryReserve);var active=NavRetainedArray<NavOwnedStorage>.Copy(requests.Select(r=>(NavOwnedStorage)r.StorageOwner).ToArray(),scope.TryReserve);
                        try{int attempts=0;if(denyFactory)budget.AllocationAdmission=_=>{attempts++;return false;};var value=NavigationAdmissionStorage.PrepareOwned(1,original.PublishedThroughRequestSequence,null,NavLocation.FlatSurface,1,original.Geometry,original.Profile,names,versions,active,publication,previous,scope.OwnedReservation());Assert.Zero(attempts);scope.Attach(value);scope.Commit(value,_=>{});return value;}finally{budget.AllocationAdmission=null;versions.Release();active.Release();}}
                }
                NavigationAuthorityGroup Change(GroupMemberPhase state,long contact,double progressValue)
                {
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
                    using(scope){var member=(NavigationAuthorityMember)version.Members[0];var m=member.Data;
                        var next=NavigationAuthorityMember.CopyWithValuesOwned(member,new NavigationAuthorityMember.Values(m.Entity,m.Slot,m.Incarnation,m.OrderRevision,m.MobilityRevision,state,m.Goal,m.Location,m.HasLocation,m.HasGoal,m.FormationReleased,m.Branch,m.Reservation,contact,m.ProgressRequest,progressValue),scope.OwnedReservation());
                        try{var value=NavigationAuthorityGroup.CopyWithMemberOwned(version,0,next,scope.OwnedReservation());scope.Attach(value);scope.Commit(value,_=>{});return value;}finally{next.ReleaseStorage();}
                    }
                }
                first=Prepare(version,null,1);Assert.AreEqual(2,first.Value.Groups[0].Members.Count);
                var firstGroup=(NavigationAdmissionGroupStorage)first.Groups[0];var source=(NavigationAuthorityMember)version.Members[0];
                progressVersion=Change(source.Data.Phase,9,.5);progress=Prepare(progressVersion,first,2,true);
                Assert.AreSame(first,progress,"contact/progress preserve the complete publication and its identity");Assert.AreEqual(1,progress.Value.PublicationIdentity);Assert.AreSame(firstGroup,progress.Groups[0]);Assert.AreSame(first.Requests,progress.Requests);Assert.False(first.IsImmutableCaptureRoot);
                phaseVersion=Change(GroupMemberPhase.March,9,.5);phase=Prepare(phaseVersion,progress,3);
                var changed=(NavigationAdmissionGroupStorage)phase.Groups[0];Assert.AreNotSame(firstGroup,changed);
                Assert.AreNotSame(firstGroup.Members[0],changed.Members[0]);Assert.AreSame(firstGroup.Members[1],changed.Members[1]);
                Assert.AreEqual(source.Data.Phase,first.Value.Groups[0].Members[0].Phase);Assert.AreEqual(GroupMemberPhase.March,phase.Value.Groups[0].Members[0].Phase);
                Assert.True(phase.Value.Contains(requests[0]));Assert.AreSame(requests[0],phase.Value.Groups[0].Members[0].PendingRequest);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var body));using(body)using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))Assert.Throws<ArgumentException>(()=>NavPhysicalBodyWire.WriteTo(writer,phase,body.OwnedReservation()),"runtime publication cannot become a BODY root");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session,first,progress,phase,progressVersion,phaseVersion),budget.PhysicalBytes);
                long physical=budget.PhysicalBytes;int denied=0;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,4,out var refusal));using(refusal){
                    var versions=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{version},refusal.TryReserve);var active=NavRetainedArray<NavOwnedStorage>.Copy(requests.Select(r=>(NavOwnedStorage)r.StorageOwner).ToArray(),refusal.TryReserve);
                    try{budget.AllocationAdmission=_=>{denied++;return false;};Assert.Throws<InvalidOperationException>(()=>NavigationAdmissionStorage.PrepareOwned(1,original.PublishedThroughRequestSequence,null,NavLocation.FlatSurface,1,original.Geometry,original.Profile,names,versions,active,4,phase,refusal.OwnedReservation()));}
                    finally{budget.AllocationAdmission=null;versions.Release();active.Release();}
                }
                Assert.AreEqual(1,denied);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                progressVersion.ReleaseStorage();progressVersion=null;phaseVersion.ReleaseStorage();phaseVersion=null;first.Release();first=null;progress.Release();progress=null;
                var pinned=phase;using(var pin=new NavigationAdmissionPin(phase,phase.Value)){
                    phase.Release();phase=null;
                    session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();destroyed=true;
                    Assert.AreEqual(NavigationStorageOwnershipTests.Unique(pinned),budget.PhysicalBytes);
                    Assert.AreEqual(GroupMemberPhase.March,pin.Value.Groups[0].Members[0].Phase);Assert.True(pin.Value.Contains(expectedRequest));
                }
            }finally{budget.AllocationAdmission=null;first?.Release();progress?.Release();phase?.Release();progressVersion?.ReleaseStorage();phaseVersion?.ReleaseStorage();if(!destroyed){session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void TypedGroupMemberUpdatesUseFrozenPagedPathsAndKeepUnchangedOwners()
        {
            var budget=new NavigationRetentionBudget();NavigationAuthorityGroup original,first,second;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var start));using(start){var a=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=1,Slot=1,OrderRevision=1},start.OwnedReservation());var b=NavigationAuthorityMember.CopyOwned(new GroupMemberState{Entity=2,Slot=2,OrderRevision=1},start.OwnedReservation());NavRetainedArray<NavOwnedStorage> array;using(NavStorageScratch.Reserve(start.OwnedReservation(),64))array=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{a,b},start.OwnedReservation().TryReserve);a.ReleaseStorage();b.ReleaseStorage();original=NavigationAuthorityGroup.CopyNamesOwned(new GroupOrderState{WorldGeneration=1,GroupId=1,OrderRevision=1,MembershipRevision=1,Kind=PlayableCommandKind.Move},array,start.OwnedReservation());array.Release();start.Attach(original);start.Commit(original,_=>{});}
            var untouched=original.Members[1];var source=(NavigationAuthorityMember)original.Members[0];
            NavigationAuthorityGroup Update(NavigationAuthorityGroup current,long order){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){var member=NavigationAuthorityMember.CopyWithValuesOwned(source,new NavigationAuthorityMember.Values(1,1,0,order,0,GroupMemberPhase.PendingRoute,default,default,false,false,false,0,0,0,0,0),scope.OwnedReservation());NavigationAuthorityGroup result;try{result=NavigationAuthorityGroup.CopyWithMemberOwned(current,0,member,scope.OwnedReservation());scope.Attach(result);scope.Commit(result,_=>{});}finally{member.ReleaseStorage();}return result;}}
            first=Update(original,2);long visits=NavigationAuthorityGroup.FullValidationVisits;second=Update(first,3);Assert.AreEqual(visits,NavigationAuthorityGroup.FullValidationVisits,"scalar COW inherits sealed slot/order proof without whole-group scan/sort scratch");Assert.IsInstanceOf<NavPagedList<NavOwnedStorage>>(first.Members);Assert.IsInstanceOf<NavPagedList<NavOwnedStorage>>(second.Members);Assert.True(((NavOwnedStorage)second.Members).IsImmutableCaptureRoot);Assert.AreSame(untouched,first.Members[1]);Assert.AreSame(untouched,second.Members[1]);Assert.AreEqual(1,((NavigationAuthorityMember)original.Members[0]).Data.OrderRevision);Assert.AreEqual(2,((NavigationAuthorityMember)first.Members[0]).Data.OrderRevision);Assert.AreEqual(3,((NavigationAuthorityMember)second.Members[0]).Data.OrderRevision);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var capture));NavigationCaptureBytes bytes;using(capture){bytes=NavigationCaptureBytes.Write(capture.OwnedReservation(),w=>NavPhysicalBodyWire.WriteTo(w,second,capture.OwnedReservation()));capture.Attach(bytes);capture.Commit(bytes,_=>{});}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,0,out var restore));NavigationAuthorityGroup cold;using(restore){using(NavStorageScratch.Reserve(restore.OwnedReservation(),256))using(var stream=new System.IO.MemoryStream(bytes.Bytes,false))using(var reader=new System.IO.BinaryReader(stream))cold=(NavigationAuthorityGroup)NavPhysicalBodyWire.ReadFrom(reader,restore.OwnedReservation(),NavigationHostBodyExtension.Instance);restore.Attach(cold);restore.Commit(cold,_=>{});}
            Assert.IsInstanceOf<NavPagedList<NavOwnedStorage>>(cold.Members);Assert.AreEqual(3,((NavigationAuthorityMember)cold.Members[0]).Data.OrderRevision);Assert.AreEqual(2,((NavigationAuthorityMember)cold.Members[1]).Data.Entity);cold.ReleaseStorage();bytes.Dispose();
            original.ReleaseStorage();first.ReleaseStorage();Assert.AreEqual(NavigationStorageOwnershipTests.Unique(second),budget.PhysicalBytes);second.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PreparedMemberWriteUpdatesLiveVersionAndStalesDetachedGrowthWithoutGateAllocation()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));var value=session.GroupFor(1);var budget=session.PhysicalHostBudget;NavigationLiveGroup live;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var initial));using(initial){var member=NavigationAuthorityMember.CopyOwned(value.Members[0],initial.OwnedReservation());NavRetainedArray<NavOwnedStorage> members;using(NavStorageScratch.Reserve(initial.OwnedReservation(),64))members=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{member},initial.OwnedReservation().TryReserve);member.ReleaseStorage();var version=NavigationAuthorityGroup.CopyNamesOwned(value,members,initial.OwnedReservation());members.Release();live=NavigationLiveGroup.CreateOwned(version,initial);version.ReleaseStorage();live.SealForInstall(initial);initial.Attach(live);initial.Publish(live,_=>session.RunHostStep(()=>{live.ValidateInstall(initial);live.ActivateNoThrow(session);}));initial.ResolveCommitted();}
            NavigationLiveGroupObservation observed=default;session.RunHostStep(()=>observed=live.ObserveForCopy(session.ObserveLiveWriteToken()));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var growthScope));using(growthScope){var growth=NavigationLiveGroupSlots.CreateOwned(8,growthScope);growth.AddObservedPreparing(1,observed,growthScope);growth.SealForInstall(growthScope);Assert.AreSame(live,growth.GroupAtSlot(0));growth.ValidateInstall(growthScope,session);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var change));using(change){
                var previous=(NavigationAuthorityMember)observed.Version.Members[0];var m=previous.Data;var changed=NavigationAuthorityMember.CopyWithValuesOwned(previous,new NavigationAuthorityMember.Values(m.Entity,m.Slot,m.Incarnation,m.OrderRevision+1,m.MobilityRevision,m.Phase,m.Goal,m.Location,m.HasLocation,m.HasGoal,m.FormationReleased,m.Branch,m.Reservation,m.ContactTick,m.ProgressRequest,m.Progress),change.OwnedReservation());
                var version=NavigationAuthorityGroup.CopyWithMemberOwned(observed.Version,0,changed,change.OwnedReservation());changed.ReleaseStorage();var write=NavigationPreparedGroupWrite.CreateOwned(observed,version,0,change);version.ReleaseStorage();change.Attach(write);
                int reservations=0;budget.AllocationAdmission=_=>{reservations++;return true;};long managed=0;
                change.Publish(write,_=>session.RunHostStep(()=>{write.ValidateInstall(change,session.ObserveLiveWriteToken());long before=GC.GetAllocatedBytesForCurrentThread();write.ApplyNoThrow();managed=GC.GetAllocatedBytesForCurrentThread()-before;}));budget.AllocationAdmission=null;change.ResolveCommitted();write.ReleaseRetired();Assert.Zero(managed,"prevalidated assignment block allocates nothing");Assert.Zero(reservations);Assert.AreEqual(m.OrderRevision+1,live.ObserveDetached().Members[0].OrderRevision);Assert.AreEqual(m.OrderRevision+1,((NavigationAuthorityMember)live.ImmutableVersion.Members[0]).Data.OrderRevision);
                // Scope resolved: inspect the unchanged old observation through its pins.
                Assert.AreEqual(m.OrderRevision,((NavigationAuthorityMember)observed.Version.Members[0]).Data.OrderRevision);Assert.False(live.MatchesObservation(observed));Assert.Throws<InvalidOperationException>(()=>growth.ValidateInstall(growthScope));write.ReleaseStorage();
            }growth.ReleaseStorage();}
            observed.ReleasePins();NavigationLiveGroupObservation current=default;session.RunHostStep(()=>current=live.ObserveForCopy(session.ObserveLiveWriteToken()));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var install));NavigationLiveGroupSlots installed;using(install){installed=NavigationLiveGroupSlots.CreateOwned(8,install);installed.AddObservedPreparing(1,current,install);installed.SealForInstall(install);
                var wrong=new NavigationSession(2,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation,null,null,budget,null);Assert.Throws<InvalidOperationException>(()=>installed.ValidateInstall(install,wrong));wrong.DestroyNavigationAuthority();
                install.Attach(installed);install.Publish(installed,_=>session.RunHostStep(()=>{installed.ValidateInstall(install,session);installed.ActivateNoThrow();}));install.ResolveCommitted();long before=budget.PhysicalBytes;int versionRefs=current.Version.References;installed.ReleaseObservationPinsAfterInstall();Assert.AreEqual(versionRefs-1,current.Version.References);Assert.AreEqual(before,budget.PhysicalBytes,"array capacity remains owned after clearing guard edges");installed.ReleaseObservationPinsAfterInstall();Assert.AreEqual(versionRefs-1,current.Version.References);
            }current.ReleasePins();installed.ReleaseStorage();live.ReleaseStorage();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void JointCarrierGuardsBothVersionsAndAdmissionBeforePublishing()
        {
            var session=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var budget=session.PhysicalHostBudget;
            using(var captured=session.CaptureOwnedState()){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,session.Admission.PublicationIdentity,out var scope));
                using(scope){
                    var slots=NavigationPreparedSlotAggregate.CreateOwned(scope);
                    var joint=NavigationPreparedPublication.CreateSealedOwned(scope,NavigationPublicationMode.HostSwap,captured.CaptureRoot,captured.CaptureAuthority,captured.CaptureRoot,captured.CaptureAuthority,slots,session.Admission.PublicationIdentity);
                    slots.ReleaseStorage();scope.Attach(joint);
                    try{
                        Assert.Throws<InvalidOperationException>(()=>joint.ValidateSource(null,captured.CaptureAuthority,session.Admission.PublicationIdentity));
                        Assert.Throws<InvalidOperationException>(()=>joint.ValidateSource(captured.CaptureRoot,null,session.Admission.PublicationIdentity));
                        Assert.Throws<InvalidOperationException>(()=>joint.ValidateSource(captured.CaptureRoot,captured.CaptureAuthority,session.Admission.PublicationIdentity+1));
                        Assert.AreEqual(NavigationPublicationPhase.Sealed,joint.Phase);
                        scope.Publish(joint,_=>{joint.ValidateSource(captured.CaptureRoot,captured.CaptureAuthority,session.Admission.PublicationIdentity);joint.BeginPublishing();joint.MarkPublishedNoThrow();});
                        scope.ResolveCommitted();Assert.AreEqual(NavigationPublicationPhase.Published,joint.Phase);
                        Assert.Throws<InvalidOperationException>(()=>joint.AbortBeforeWrites());
                    }finally{joint.ReleaseStorage();}
                }
            }
            session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void EveryFreshAuthorityInitializationAdmissionRefusalLeavesNoBornOwner()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);int sites=0;var counter=new NavigationRetentionBudget();counter.AllocationAdmission=_=>{sites++;return true;};
            var counted=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,null,null,counter,null);counted.DestroyNavigationAuthority();Assert.Greater(sites,3);Assert.Zero(counter.PhysicalBytes);
            for(int refusal=1;refusal<=sites;refusal++){
                int seen=0,target=refusal;var budget=new NavigationRetentionBudget();budget.AllocationAdmission=_=>++seen!=target;
                Assert.Throws<InvalidOperationException>(()=>new NavigationSession(1,geometry,PlayableProfile.Default.Navigation,null,null,budget,null),"initial publication site "+refusal);
                Assert.Zero(budget.PhysicalBytes,"initial publication site "+refusal);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        [Test] public void PreparedReplacementForeignOwnerRefusesBeforeAdmissionOrRetain()
        {
            var sourceSession=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            using(var source=sourceSession.CaptureOwnedState()){
                var foreign=source.CaptureAuthority;int references=foreign.References;var destination=new NavigationRetentionBudget();
                Assert.True(destination.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
                using(scope){int allocations=0;destination.AllocationAdmission=_=>{allocations++;return true;};
                    Assert.Throws<ArgumentException>(()=>NavigationPreparedPublication.CopyReplacementsOwned(new NavOwnedStorage[]{foreign},scope));
                    Assert.Zero(allocations,"no private backing/header attempt before foreign rejection");Assert.AreEqual(references,foreign.References);Assert.Zero(destination.PhysicalBytes);
                }
                Assert.Zero(destination.PhysicalBytes);Assert.Zero(destination.HeldBytes);Assert.Zero(destination.ActiveScopes);
            }
            sourceSession.DestroyNavigationAuthority();Assert.Zero(sourceSession.PhysicalHostBudget.PhysicalBytes);
        }
        private static string Facts(CertifiedRouteKernel kernel,NavigationSession session)
        {
            var root=session.PinCommittedHostRoot();
            try{
                var c=kernel.Counters;var f=root.Frontier;
                return string.Join(":",kernel.SolverEpoch,kernel.ProgressSequence,c.LogicalGraphWork,c.PhysicalGraphWork,
                    c.LogicalFieldWork,c.PhysicalFieldWork,c.ConnectorWork,c.PendingSubscriptions,c.ReadyResults,
                    c.CacheFields,c.CacheBytes,c.RetainedBytes,root.Rows.Count,root.Scan.Count,
                    root.Projection.Count,root.Completion.Count,root.Delivered.Count,
                    f.Stage,f.OriginalBudget,f.ScansUsed,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,
                    f.ProjectionAllowance,f.SolveWorkLimit,f.SolveConsumed,f.AllocationFloor);
            }finally{root?.Release();}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)] public void CommandOnlyGroupCompletionPreservesAdjacentOwnedContactPublication(int transition)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            int rendezvous=0,boundaries=0;NavigationHostRoot newest=null;NavigationSession.GroupGuardObservation observation=default;
            bool At(CertifiedRouteKernel.PumpStage stage){var root=session.PinCommittedHostRoot();try{return root.Frontier.Stage==stage;}finally{root.Release();}}
            try{
                session.Crowd.Add(1,new NavPoint(-5,-3));session.Crowd.Add(2,new NavPoint(-5,3));
                Assert.True(session.Move(1,new NavPoint(5,-3)));session.SetFormationContact(1,false,1);
                var initialStage=transition==2?CertifiedRouteKernel.PumpStage.Allocation:transition==3?CertifiedRouteKernel.PumpStage.EndAllocation:CertifiedRouteKernel.PumpStage.Retirement;
                kernel.PauseAtPhysicalBoundary=()=>At(initialStage);kernel.Pump(Binding(session,geometry),4);Assert.True(At(initialStage));
                var command=new PlayableCommand(1,2,"player-1",PlayableCommandKind.Attack,new[]{2},targetId:99);
                var neighbor=Newtonsoft.Json.JsonConvert.SerializeObject(session.GroupFor(1));session.PrepareCommandGroup(command,2,2,1);
                if(transition!=0)session.AfterMetadataObservationForQa=()=>{
                    rendezvous++;Assert.True(At(initialStage));kernel.PauseAtPhysicalBoundary=()=>++boundaries>1;kernel.Pump(Binding(session,geometry),4);
                    newest=session.PinCommittedHostRoot();var expected=transition==1?CertifiedRouteKernel.PumpStage.ArrivalAdvance:transition==2?CertifiedRouteKernel.PumpStage.Scan:CertifiedRouteKernel.PumpStage.PostFlush;Assert.AreEqual(expected,newest.Frontier.Stage);
                };
                session.FinishCommandGroup(true,new[]{2});observation=session.LastGroupGuardObservationForQa;
                Assert.AreEqual(2,session.GroupOrders.Count);Assert.AreEqual(2,session.GroupFor(2).CommandSequence);Assert.AreEqual(neighbor,Newtonsoft.Json.JsonConvert.SerializeObject(session.GroupFor(1)),"metadata does not replace neighbor/contact authority");
                if(transition!=0){
                    Assert.AreEqual(1,rendezvous);Assert.AreEqual(2,boundaries);
                    var mask=NavigationSession.GroupGuardMismatch.Host;if(transition>=2)mask|=NavigationSession.GroupGuardMismatch.AllocationThread;
                    Assert.AreEqual(mask,observation.Mismatch,"only frontier and actual host allocation token may refresh");
                    if(transition==2){Assert.Zero(observation.ExpectedAllocationThread);Assert.Greater(observation.ActualAllocationThread,0);}
                    if(transition==3){Assert.Greater(observation.ExpectedAllocationThread,0);Assert.Zero(observation.ActualAllocationThread);}
                    var current=session.PinCommittedHostRoot();try{Assert.AreSame(newest,current);Assert.True(newest.Frontier.Same(current.Frontier));}finally{current.Release();}
                }
                Assert.DoesNotThrow(()=>session.SetFormationContact(1,false,3));Assert.AreEqual(3,session.GroupFor(1).Members.Single().ContactTick);
                kernel.PauseAtPhysicalBoundary=null;kernel.Pump(Binding(session,geometry),4);Assert.True(At(CertifiedRouteKernel.PumpStage.Idle),"same real kernel continues its saved suffix");
                Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
            }finally{
                newest?.Release();session.AfterMetadataObservationForQa=null;session.LastGroupGuardCandidateForQa=null;kernel.PauseAtPhysicalBoundary=null;kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
                TestContext.WriteLine("METADATA_REOBSERVATION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{transition,rendezvous,boundaries,observation,Cleanup=new{session.PhysicalHostBudget.PhysicalBytes,session.PhysicalHostBudget.HeldBytes,session.PhysicalHostBudget.ActiveScopes}}));
            }
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void AllocationPublicationRefusalResumesTheAlreadyOwnedToken()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            var kernel=new CertifiedRouteKernel();bool refused=false;
            try{
                kernel.PauseAtPhysicalBoundary=()=>{
                    var root=session.PinCommittedHostRoot();
                    try{if(!refused&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Allocation){
                        refused=true;session.PhysicalHostBudget.AllocationAdmission=_=>false;
                    }}finally{root.Release();}
                    return false;
                };
                kernel.Pump(Binding(session,geometry),4);
                Assert.True(refused,"refusal must happen after allocation ownership is acquired");
                var paused=session.PinCommittedHostRoot();
                try{Assert.AreEqual(CertifiedRouteKernel.PumpStage.Allocation,paused.Frontier.Stage);
                    Assert.AreEqual(4,paused.Frontier.OriginalBudget);
                }finally{paused.Release();}
                session.PhysicalHostBudget.AllocationAdmission=null;
                Assert.DoesNotThrow(()=>kernel.Pump(Binding(session,geometry),7));
                var resumed=session.PinCommittedHostRoot();
                try{Assert.AreEqual(CertifiedRouteKernel.PumpStage.Idle,resumed.Frontier.Stage);
                    Assert.AreEqual(4,resumed.Frontier.OriginalBudget,"resume preserves the saved grant");
                }finally{resumed.Release();}
            }finally{
                session.PhysicalHostBudget.AllocationAdmission=null;kernel.Clear();
                session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            }
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(true,true)] public void DefaultPumpColdLegPrefixResumesSavedSuffixAndNextBudgetWithoutPrepare(bool partitionReplay,bool consumedReplay)
        {
            var map=partitionReplay?ThreeCrossingsMap.Default:null;
            var geometry=map==null?new NavGeometry(20,Array.Empty<NavObstacle>(),1):new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var profile=map==null?PlayableProfile.Default:PlayableProfile.ThreeCrossingsDefault;
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            session.Crowd.Add(1,new NavPoint(map==null?-5:-18,0));Assert.True(session.Move(1,new NavPoint(map==null?5:18,0)));
            var kernel=new CertifiedRouteKernel();byte[] prefix=null;int steps=0,savedReplay=0,savedObserved=0;
            kernel.BeforePhysicalProjectionStep=()=>{
                var root=session.PinCommittedHostRoot();
                try{
                    if(prefix!=null||root.Projection.Count==0)return;
                    var row=(NavigationHostRow)root.Rows[root.Projection.First];
                    if(partitionReplay?row.ProjectionOwner.LegCursor!=row.Solved.Route.Legs.Count||row.ProjectionOwner.ReplayCursor>=row.ProjectionOwner.PointCount:row.ProjectionOwner.LegCursor>=row.Solved.Route.Legs.Count)return;
                    if(consumedReplay&&row.ProjectionOwner.ReplayCursor==0)return;
                    savedReplay=row.ProjectionOwner.ReplayCursor;savedObserved=row.ProjectionOwner.ObservedCount;
                    if(consumedReplay){Assert.Greater(savedReplay,0);Assert.Less(savedReplay,row.ProjectionOwner.PointCount);}
                    Assert.AreEqual(CertifiedRouteKernel.PumpStage.Projection,root.Frontier.Stage);
                    Assert.AreEqual(4,root.Frontier.OriginalBudget);prefix=Bytes(session);steps++;
                }finally{root.Release();}
            };
            for(int n=0;n<20000&&prefix==null;n++)kernel.Pump(Binding(session,geometry),4);
            Assert.NotNull(prefix,"actual default Kernel must reach a nonterminal leg");
            kernel.BeforePhysicalProjectionStep=null;
            var suffix=Bytes(session);string suffixFacts=Facts(kernel,session);
            kernel.Pump(Binding(session,geometry),1);var next=Bytes(session);string nextFacts=Facts(kernel,session);
            kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();kernel=null;session=null;geometry=null;
            geometry=map==null?new NavGeometry(20,Array.Empty<NavObstacle>(),1):new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            session.RestoreState(WorldWire.Unpack(prefix,WorldWire.ReadNavigationSessionState));
            CollectionAssert.AreEqual(prefix,Bytes(session),"cold immediate recapture");
            var restoredRoot=session.PinCommittedHostRoot();
            try{var savedRow=(NavigationHostRow)restoredRoot.Rows[restoredRoot.Projection.First];Assert.AreSame(savedRow.RequestOwner,savedRow.Request.StorageOwner);Assert.AreSame(savedRow.RequestOwner,((INavStorageFacade)savedRow.Request).StorageRoot);Assert.AreEqual(savedReplay,savedRow.ProjectionOwner.ReplayCursor);Assert.AreEqual(savedObserved,savedRow.ProjectionOwner.ObservedCount);}
            finally{restoredRoot.Release();}
            kernel=new CertifiedRouteKernel();int prepares=0;kernel.BeforePrepare=()=>prepares++;
            kernel.Pump(Binding(session,geometry),7);
            Assert.Zero(prepares,"registered B must restore directly from BODY");
            CollectionAssert.AreEqual(suffix,Bytes(session),"saved grant governs the suffix");
            Assert.AreEqual(suffixFacts,Facts(kernel,session));
            kernel.Pump(Binding(session,geometry),1);
            CollectionAssert.AreEqual(next,Bytes(session),"one next full Pump uses its new grant");
            Assert.AreEqual(nextFacts,Facts(kernel,session));Assert.AreEqual(1,steps);
            kernel.Clear();session.ReleaseCommittedHostRoot();Assert.Greater(session.PhysicalHostBudget.PhysicalBytes,0,"live Session authority survives host-only release");session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void ColdDeliveredOwnerSurvivesMailboxAndInstalledUseUntilStop(bool installed)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var kernel=new CertifiedRouteKernel();
            for(int n=0;n<20000&&session.Answers.Count==0;n++)kernel.Pump(Binding(session,geometry),4);
            Assert.AreEqual(1,session.Answers.Count);
            if(installed){session.ApplyResults();Assert.AreEqual(1,session.AppliedResults);}
            var saved=Bytes(session);kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();kernel=null;session=null;
            geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.RestoreState(WorldWire.Unpack(saved,WorldWire.ReadNavigationSessionState));
            CollectionAssert.AreEqual(saved,Bytes(session));
            var root=session.PinCommittedHostRoot();NavigationHostRow delivered;
            try{Assert.AreEqual(1,root.Delivered.Count);delivered=(NavigationHostRow)root.Delivered.First().Value;
                if(installed)Assert.AreSame(delivered.Answer.Corridor,session.InstalledExecutionFor(1).Corridor);
                else {Assert.True(session.Answers.TryPeek(out var answer));Assert.AreSame(delivered.Answer,answer);}
            }finally{root.Release();}
            kernel=new CertifiedRouteKernel();kernel.Pump(Binding(session,geometry),1);
            root=session.PinCommittedHostRoot();try{Assert.AreEqual(1,root.Delivered.Count,"live mailbox/installed use retains its physical owner");}finally{root.Release();}
            if(!installed){session.ApplyResults();Assert.AreEqual(1,session.AppliedResults);}
            session.Stop(1,false);kernel.Pump(Binding(session,geometry),1);
            root=session.PinCommittedHostRoot();try{Assert.Zero(root.Delivered.Count,"Stop releases the last installed user");}finally{root.Release();}
            kernel.Clear();session.ReleaseCommittedHostRoot();Assert.Greater(session.PhysicalHostBudget.PhysicalBytes,0,"live Session authority survives host-only release");session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
        }
    }
}
