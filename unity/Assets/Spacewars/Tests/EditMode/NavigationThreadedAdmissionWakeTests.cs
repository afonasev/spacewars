using System;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using Spacewars.Presentation;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationThreadedAdmissionWakeTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static long CopyScratchCharge(PartitionNavigationProvider provider)
        {
            var locations=(TerrainLocations)typeof(PartitionNavigationProvider).GetField("locations",Hidden).GetValue(provider);
            long bytes=locations.CopyScratchBytes+4096+256L*provider.Surfaces.Count;
            foreach(var surface in provider.Surfaces)bytes=checked(bytes+4096+256L*surface.Area.Blockers.Count);
            return checked(bytes+NavStorageScratch.LeaseBytes);
        }
        private static bool InPartitionCopy()=>new StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(PartitionNavigationProvider)&&f.GetMethod().Name=="CopyOwned");
        private static byte[] Capture(NavigationSession nav)
        {using(var capture=nav.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),capture.CaptureBudget))return bytes.TransferPublic();}
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void OrdinaryProviderScratchRefusalDiscardsOnlyPreparedFork(int site)
        {
            var map=ThreeCrossingsMap.Default;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);
            var nav=new NavigationSession(71,geometry,PlayableProfile.ThreeCrossingsDefault.Navigation,terrain:map);
            var kernel=new CertifiedRouteKernel();
            try{
                nav.Crowd.Add(1,new NavPoint(-18,0));Assert.True(nav.Move(1,new NavPoint(18,0)));
                var binding=new PlayableRouteBinding(geometry,PlayableProfile.ThreeCrossingsDefault,nav.Admission,nav.RoutePort);
                kernel.Attach(nav.RoutePort);var original=nav.PendingRequestFor(1);Assert.NotNull(original);
                using var pin=nav.RoutePort.PinAdmission();var budget=nav.PhysicalHostBudget;
                long construction=NavigationProviderConstructionBound.Estimate(pin.Value.AuthoredTerrain,original.Geometry)+NavStorageScratch.LeaseBytes;
                long nested=CopyScratchCharge(new PartitionNavigationProvider(pin.Value.AuthoredTerrain,original.Geometry,original.HeldIdentity));
                long target=site==0?construction:site==2?8192+NavStorageScratch.LeaseBytes:nested;
                var before=Capture(nav);long physical=budget.PhysicalBytes,progress=kernel.ProgressSequence,epoch=kernel.SolverEpoch;
                int refs=pin.StorageOwner.References,denied=0;long heldAtRefusal=0;
                var root=nav.PinCommittedHostRoot();
                try{
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav,root),physical);
                    budget.AllocationAdmission=bytes=>{
                        if(bytes!=target||(site!=0&&!InPartitionCopy()))return true;
                        denied++;heldAtRefusal=budget.HeldBytes;
                        if(site==3)throw new InvalidOperationException("adverse invariant sentinel");
                        return false;
                    };
                    if(site==3){var failure=Assert.Throws<InvalidOperationException>(()=>kernel.TryRegisterPhysicalOrdinary(binding));Assert.AreEqual("adverse invariant sentinel",failure.Message);}
                    else Assert.False(kernel.TryRegisterPhysicalOrdinary(binding));
                    budget.AllocationAdmission=null;
                    Assert.AreEqual(1,denied,"must enter selected actual reservation branch");
                    if(site!=0)Assert.Greater(heldAtRefusal,construction,"outer construction scratch remains paid at nested refusal");
                    var after=nav.PinCommittedHostRoot();try{Assert.AreSame(root,after);}finally{after?.Release();}
                    Assert.AreSame(original,nav.PendingRequestFor(1));Assert.AreEqual(1,nav.Requests.Count);
                    Assert.Zero(nav.RegisteredRouteCount);Assert.Zero(nav.AllocationCount);Assert.Zero(nav.Answers.Count);
                    Assert.AreEqual(progress,kernel.ProgressSequence);Assert.AreEqual(epoch,kernel.SolverEpoch);
                    Assert.AreEqual(refs,pin.StorageOwner.References);Assert.AreEqual(physical,budget.PhysicalBytes);
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav,root),budget.PhysicalBytes);
                    Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(before,Capture(nav));
                    Assert.True(kernel.TryRegisterPhysicalOrdinary(binding),"same queued head retries after pressure release");
                    Assert.Zero(nav.Requests.Count);Assert.AreEqual(1,nav.RegisteredRouteCount);Assert.AreEqual(progress+1,kernel.ProgressSequence);
                    TestContext.WriteLine($"PROVIDER_REFUSAL site={site} target={target} denied={denied} outer={construction} heldAtRefusal={heldAtRefusal} physicalBefore={physical} exactCapture=true refsStable=true census=true retry=true");
                }finally{root?.Release();}
            }finally{nav.PhysicalHostBudget.AllocationAdmission=null;kernel.Clear();nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}
        }
        private static void SaveProviderEvidence(string name,object value)
        {
            var directory=Environment.GetEnvironmentVariable("SPACEWARS_PROVIDER_EVIDENCE");
            if(string.IsNullOrEmpty(directory))throw new InvalidOperationException("Set SPACEWARS_PROVIDER_EVIDENCE.");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory,name+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
        }
        [Test,Timeout(90000)]
        public void ActualArrivalCopyScratchRefusalPreservesCapacityRetirementDisposition()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var domain=new PlayableDomain(profile,71,false);var nav=domain.Navigation;var kernel=new CertifiedRouteKernel();var budget=nav.PhysicalHostBudget;
            try{
                int entity=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",Hidden).Invoke(domain,new object[]{new NavPoint(-42,-40),PlayableOwner.Player,PlayableEntityKind.Tank});
                using(var idle=nav.CaptureState()){}
                Assert.AreEqual(PlayableCommandStatus.Applied,typeof(PlayableDomain).GetMethod("Apply",Hidden).Invoke(domain,new object[]{new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,new[]{entity},new NavPoint(-36,-46),origin:PlayableOrderOrigin.Human),null}));
                Assert.True(nav.PreparePhysicalArrivalAttempt());
                NavigationArrivalWork Paid(){var root=nav.PinCommittedHostRoot();try{return root.ArrivalWork.Select(p=>(NavigationArrivalWork)p.Value).FirstOrDefault(w=>w.IsHistoricalCandidate&&w.AllocationWorkConsumed>0);}finally{root.Release();}}
                var binding=new PlayableRouteBinding(domain.Geometry,profile,nav.Admission,nav.RoutePort);
                for(int i=0;i<20000&&Paid()==null;i++)kernel.Pump(binding,1);
                var old=Paid();Assert.NotNull(old,"actual paid authored arrival prerequisite");
                var geometry=domain.Geometry;var next=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1);
                typeof(PlayableDomain).GetProperty("Geometry",Hidden).SetValue(domain,next);nav.ChangeGeometry(next);
                Assert.AreSame(old,Paid());Assert.True((bool)typeof(NavigationSession).GetMethod("HistoricalBirthPending",Hidden).Invoke(nav,new object[]{old}));
                Assert.IsNull(nav.NextRetiringArrivalProjection());var request=nav.PendingRequestFor(entity);Assert.NotNull(request);
                int allocating=nav.AllocationCount;long command=nav.GroupFor(entity).CommandSequence;long quantum;var before=nav.PinCommittedHostRoot();try{quantum=before.QuantumSerial;}finally{before.Release();}
                long target=CopyScratchCharge(new PartitionNavigationProvider(nav.Admission.AuthoredTerrain,request.Geometry,request.HeldIdentity));int denied=0;bool retirement=false;string stack=null;
                budget.AllocationAdmission=bytes=>{
                    var frames=new StackTrace().GetFrames();
                    if(bytes==NavigationRetentionBudget.ScopeBytes&&frames.Any(f=>f.GetMethod().Name=="RetirePhysicalArrivalOwner")&&frames.Any(f=>f.GetMethod().Name=="TryOpenScope"))retirement=true;
                    if(bytes!=target||!frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavigationArrivalPhysicalInput)&&f.GetMethod().Name=="CopyInitial")||!frames.Any(f=>f.GetMethod().DeclaringType==typeof(PartitionNavigationProvider)&&f.GetMethod().Name=="CopyOwned"))return true;
                    denied++;stack=new StackTrace().ToString();SaveProviderEvidence("arrival-copy-refusal",new{target,denied,entity,request=request.Request,command,allocating,quantum,stack});return false;
                };
                bool prepared;try{prepared=nav.PreparePhysicalArrivalAttempt();}finally{budget.AllocationAdmission=null;}
                var after=nav.PinCommittedHostRoot();bool removed;try{removed=!after.ArrivalWork.Any(p=>ReferenceEquals(p.Value,old));Assert.AreEqual(quantum,after.QuantumSerial);}finally{after.Release();}
                SaveProviderEvidence("arrival-copy-disposition",new{prepared,denied,retirement,removed,requestBefore=request.Request,requestAfter=nav.PendingRequestFor(entity)?.Request,allocatingBefore=allocating,allocatingAfter=nav.AllocationCount,physical=budget.PhysicalBytes,census=NavigationStorageOwnershipTests.UniqueWithSession(nav),held=budget.HeldBytes,scopes=budget.ActiveScopes});
                Assert.False(prepared);Assert.AreEqual(1,denied,"actual CopyInitial nested scratch was denied");Assert.True(retirement,"same capacity classification invokes historical retirement");Assert.True(removed,"paid historical owner removed before cold replacement");
                Assert.AreEqual(request.Request,nav.PendingRequestFor(entity)?.Request);Assert.AreEqual(command,nav.GroupFor(entity).CommandSequence);Assert.AreEqual(allocating,nav.AllocationCount);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                Assert.True(nav.PreparePhysicalArrivalAttempt(),"same retained intent cold birth after pressure release");var born=nav.PinCommittedHostRoot();try{Assert.False(born.ArrivalWork.Any(p=>ReferenceEquals(p.Value,old)));Assert.True(born.ArrivalWork.Any());}finally{born.Release();}
                Assert.AreEqual(request.Request,nav.PendingRequestFor(entity)?.Request);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                TestContext.WriteLine($"ACTUAL_ARRIVAL_COPY_REFUSAL target={target} denied={denied} historicalRetired={retirement&&removed} requestSame=true");
            }finally{budget.AllocationAdmission=null;kernel.Clear();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        private static string Digest(byte[] bytes)
        {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static object ProviderBoundaryFacts(NavigationSession nav,NavigationHostRoot root,int entity)
        {
            var pending=nav.PendingRequestFor(entity);var f=root.Frontier;
            return new{schema=PlayableWorldState.CurrentVersion,root=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(root),rootReferences=root.References,root.Epoch,root.Progress,root.NextProviderIdentity,root.QuantumSerial,root.QuantumBudget,counters=root.Scheduler.Counters,stage=f.Stage.ToString(),f.OriginalBudget,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,f.SolveWorkLimit,
                rows=root.Rows.Select(p=>new{key=p.Key.Value,owner=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.Value)}).ToArray(),providers=root.Providers.Select(p=>p.Key).ToArray(),queue=nav.Requests.CopyItems().Select(r=>new{r.Entity,r.Request,r.Order}).ToArray(),pending=pending==null?null:new{pending.Entity,pending.Request,pending.Order,arrival=nav.IsArrivalRequest(pending)},registered=nav.RegisteredRouteCount,allocating=nav.AllocationCount,answers=nav.Answers.Count,physical=nav.PhysicalHostBudget.PhysicalBytes,held=nav.PhysicalHostBudget.HeldBytes,scopes=nav.PhysicalHostBudget.ActiveScopes};
        }
        [Test,Timeout(90000)]
        public void OrdinaryProducedExplorerRetriesExactProviderCopyAndArrivesWithoutReissue()
        {
            var total=Stopwatch.StartNew();var profile=PlayableProfile.ThreeCrossingsDefault;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=1024700465};setup.InitializeParticipants();foreach(var participant in setup.Participants)participant.Human=true;
            var config=NativeLobbyMatch.Create(profile,setup);var runtime=new PlayableRuntime(config,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Hidden).GetValue(runtime);var nav=domain.Navigation;var budget=nav.PhysicalHostBudget;
            CertifiedRouteWorker worker=null;CertifiedRouteKernel kernel=null;NavigationHostRoot before=null,after=null;NavigationAdmissionPin admissionPin=default;
            using var targetBoundary=new ManualResetEventSlim();using var releaseAttempt=new ManualResetEventSlim();using var refusedBoundary=new ManualResetEventSlim();using var releaseRefused=new ManualResetEventSlim();
            int phase=0,denied=0,workerThread=0;long targetCharge=0;NavigationRequest selected=null;NavPoint selectedStart=default;string deniedStack=null;int[] originalIds=null;var goal=new NavPoint(-30,-46);
            var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
            void Service(){host.Service(runtime,profile.NavigationRequestsPerFrame);receipts.AddRange(runtime.DrainReceipts());Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
            void Wait(Func<bool> ready,double phaseSeconds,string name){double limit=Math.Min(80,total.Elapsed.TotalSeconds+phaseSeconds);while(!ready()&&total.Elapsed.TotalSeconds<limit){Service();Thread.Sleep(1);}SaveProviderEvidence("producer-phase",new{name,seconds=total.Elapsed.TotalSeconds,tick=runtime.Latest.Tick,ready=ready(),receipts=receipts.Select(r=>new{r.Sequence,status=r.Status.ToString(),r.AppliedTick}).ToArray()});Assert.True(ready(),name);}
            bool Applied(long sequence)=>receipts.Any(r=>r.Sequence==sequence&&r.Status==PlayableCommandStatus.Applied);
            void Submit(PlayableCommand command){var result=runtime.TrySubmit(command);SaveProviderEvidence("producer-submit-"+command.Sequence,new{command.Sequence,kind=command.Kind.ToString(),command.SiteId,command.SlotId,command.ParentId,entities=command.EntityIds,result.Accepted});Assert.True(result.Accepted);}
            try{
                double startupLimit=Math.Min(80,total.Elapsed.TotalSeconds+15);
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&total.Elapsed.TotalSeconds<startupLimit){Assert.IsNull(runtime.Latest.Failure);Thread.Sleep(1);}
                bool workerAbsent=typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host)==null;
                SaveProviderEvidence("producer-startup",new{prepared=runtime.IsPrepared,paused=runtime.Latest.Paused,workerAbsent,seconds=total.Elapsed.TotalSeconds,tick=runtime.Latest.Tick,failure=runtime.Latest.Failure});
                Assert.True(runtime.IsPrepared&&runtime.Latest.Paused,"prepared and paused ACK before first Host.Service");Assert.True(workerAbsent);Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(11,PlayableWorldState.CurrentVersion);Assert.True(nav.AccruedCreditsEnabled);Assert.NotNull(nav.Admission.AuthoredTerrain);
                string owner=config.Roster[0].Id;var initial=runtime.ParticipantView(owner);originalIds=initial.Entities.Select(e=>e.Id).ToArray();var hq=initial.Buildings.Single(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.Headquarters);int slot=1;
                nav.RunHostStep(()=>{
                    var initialRoot=nav.PinCommittedHostRoot();try{
                        var capture=Capture(nav);
                        SaveProviderEvidence("producer-precommand",new{sourceSchema=PlayableWorldState.CurrentVersion,profile=profile.ProfileId,map=nav.Admission.AuthoredTerrain.Id,seed=1024700465,generation=71,hq=hq.Id,hq.SiteId,slot,goal=new{goal.X,goal.Z},factorySeconds=profile.FactoryBuildSeconds,explorerSeconds=profile.ExplorerProductionDurationSec,initial.Credits,workerAbsent,captureSha256=Digest(capture),root=ProviderBoundaryFacts(nav,initialRoot,0)});
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_PROVIDER_EVIDENCE"),"producer-precommand-current-navigation.bin"),capture);
                        Assert.Zero(budget.HeldBytes,"precommand Held");Assert.Zero(budget.ActiveScopes,"precommand scopes");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav,initialRoot),budget.PhysicalBytes,"precommand census");CollectionAssert.AreEqual(capture,Capture(nav));
                    }finally{initialRoot.Release();}
                });
                Service();worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",Hidden).GetValue(host);Assert.NotNull(worker);kernel=(CertifiedRouteKernel)typeof(CertifiedRouteWorker).GetField("kernel",Hidden).GetValue(worker);
                kernel.PauseAtPhysicalBoundary=()=>{
                    int current=Volatile.Read(ref phase);
                    if(current==0&&kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer){
                        NavigationRequest head=null;nav.RunHostStep(()=>{if(nav.Requests.TryPeek(out var row)&&row.Entity>0&&!originalIds.Contains(row.Entity)&&row.Goal.Equals(goal)&&!nav.IsArrivalRequest(row))head=row;});
                        if(head!=null){selected=head;workerThread=Thread.CurrentThread.ManagedThreadId;targetBoundary.Set();if(!releaseAttempt.Wait(10000))throw new TimeoutException("target boundary release");Volatile.Write(ref phase,2);return false;}
                    }
                    if(current==2&&Volatile.Read(ref denied)>0){refusedBoundary.Set();if(!releaseRefused.Wait(10000))throw new TimeoutException("refused boundary release");Volatile.Write(ref phase,4);}
                    return false;
                };
                runtime.RequestPause(false);
                Submit(new PlayableCommand(71,1,owner,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:hq.SiteId,slotId:slot,parentId:hq.Id,buildingKind:PlayableBuildingKind.Factory));
                Wait(()=>Applied(1)&&runtime.ParticipantView(owner).Buildings.Any(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==hq.SiteId&&b.SlotId==slot&&b.Phase==ConstructionPhase.Ready),23,"factory naturally Ready");
                var factory=runtime.ParticipantView(owner).Buildings.Single(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==hq.SiteId&&b.SlotId==slot);
                Submit(new PlayableCommand(71,2,owner,PlayableCommandKind.SetRally,new[]{factory.Id},goal));Wait(()=>Applied(2),12,"factory SetRally Applied");
                Assert.AreEqual(goal,runtime.ParticipantView(owner).Buildings.Single(b=>b.Id==factory.Id).Rally);
                Submit(new PlayableCommand(71,3,owner,PlayableCommandKind.QueueExplorer,new[]{factory.Id}));Wait(()=>targetBoundary.IsSet,23,"naturally produced positive ordinary head");
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused,5,"target pause ACK");
                var produced=runtime.ParticipantView(owner).Entities.Single(e=>e.Id==selected.Entity);selectedStart=produced.Position;
                before=nav.PinCommittedHostRoot();admissionPin=nav.RoutePort.PinAdmission();var queued=nav.Requests.CopyItems();var counters=before.Scheduler.Counters;int registered=nav.RegisteredRouteCount,allocating=nav.AllocationCount,answers=nav.Answers.Count,admissionRefs=admissionPin.StorageOwner.References,rootRefs=before.References;
                var captureBefore=Capture(nav);targetCharge=CopyScratchCharge(new PartitionNavigationProvider(admissionPin.Value.AuthoredTerrain,selected.Geometry,selected.HeldIdentity));
                SaveProviderEvidence("producer-before-attempt",new{factory=factory.Id,commands=new[]{1,2,3},queueApplied=Applied(3),workerThread,targetCharge,entity=selected.Entity,request=selected.Request,order=selected.Order,producedKind=produced.Kind.ToString(),position=new{selectedStart.X,selectedStart.Z},captureSha256=Digest(captureBefore),admissionRefs,root=ProviderBoundaryFacts(nav,before,selected.Entity)});
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_PROVIDER_EVIDENCE"),"producer-before-current-navigation.bin"),captureBefore);
                Assert.True(Applied(3));Assert.AreEqual(initial.Owner,produced.Owner);Assert.AreEqual(PlayableEntityKind.Explorer,produced.Kind);Assert.AreSame(selected,nav.PendingRequestFor(selected.Entity));Assert.False(nav.IsArrivalRequest(selected));Assert.AreEqual(CertifiedRouteKernel.PumpStage.Transfer,before.Frontier.Stage);
                Assert.Zero(budget.HeldBytes,"before Held");Assert.Zero(budget.ActiveScopes,"before scopes");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav,before,admissionPin.StorageOwner),budget.PhysicalBytes,"before census");CollectionAssert.AreEqual(captureBefore,Capture(nav));
                budget.AllocationAdmission=bytes=>{
                    if(Volatile.Read(ref phase)!=2||bytes!=targetCharge||Thread.CurrentThread.ManagedThreadId!=workerThread)return true;
                    var trace=new StackTrace();var frames=trace.GetFrames();
                    bool Has(Type type,string name)=>frames.Any(f=>f.GetMethod().DeclaringType==type&&f.GetMethod().Name==name);
                    if(!Has(typeof(CertifiedRouteKernel),"TryRegisterPhysicalOrdinary")||!Has(typeof(NavigationHostRoot),"RegisterOrdinary")||!Has(typeof(NavigationHostProvider),"CopyOwned")||!Has(typeof(PartitionNavigationProvider),"CopyOwned"))return true;
                    deniedStack=trace.ToString();Interlocked.Increment(ref denied);
                    SaveProviderEvidence("producer-exact-denial",new{bytes,targetCharge,denied,workerThread,currentThread=Thread.CurrentThread.ManagedThreadId,threadName=Thread.CurrentThread.Name,selected.Entity,selected.Request,selected.Order,construction=NavigationProviderConstructionBound.Estimate(admissionPin.Value.AuthoredTerrain,selected.Geometry)+NavStorageScratch.LeaseBytes,held=budget.HeldBytes,stack=deniedStack});return false;
                };
                releaseAttempt.Set();Wait(()=>refusedBoundary.IsSet,5,"refusal completed outside scope");
                after=nav.PinCommittedHostRoot();var captureAfter=Capture(nav);
                SaveProviderEvidence("producer-after-refusal",new{denied,admissionRefsBefore=admissionRefs,admissionRefsAfter=admissionPin.StorageOwner.References,rootRefsBefore=rootRefs,oldRootRefsAfter=before.References,physicalCensus=NavigationStorageOwnershipTests.UniqueWithSession(nav,before,after,admissionPin.StorageOwner),captureSha256=Digest(captureAfter),root=ProviderBoundaryFacts(nav,after,selected.Entity)});
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_PROVIDER_EVIDENCE"),"producer-after-current-navigation.bin"),captureAfter);
                Assert.AreEqual(1,denied);Assert.IsNotEmpty(deniedStack);Assert.AreSame(selected,nav.PendingRequestFor(selected.Entity));CollectionAssert.AreEqual(queued,nav.Requests.CopyItems());
                Assert.AreEqual(registered,nav.RegisteredRouteCount,"registered baseline");Assert.AreEqual(allocating,nav.AllocationCount,"allocating baseline");Assert.AreEqual(answers,nav.Answers.Count,"answer baseline");
                CollectionAssert.AreEqual(before.Rows.Select(p=>p.Value).ToArray(),after.Rows.Select(p=>p.Value).ToArray());CollectionAssert.AreEqual(before.Requests.Select(p=>p.Value).ToArray(),after.Requests.Select(p=>p.Value).ToArray());CollectionAssert.AreEqual(before.Providers.Select(p=>p.Value).ToArray(),after.Providers.Select(p=>p.Value).ToArray());
                Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(counters),Newtonsoft.Json.JsonConvert.SerializeObject(after.Scheduler.Counters),"scheduler counters");Assert.AreEqual(before.NextProviderIdentity,after.NextProviderIdentity);Assert.AreEqual(before.Progress,after.Progress);Assert.AreEqual(before.Epoch,after.Epoch);Assert.AreEqual(before.QuantumSerial,after.QuantumSerial);Assert.AreEqual(before.QuantumBudget,after.QuantumBudget);
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.Solve,after.Frontier.Stage);Assert.AreEqual(before.Frontier.TransfersUsed,after.Frontier.TransfersUsed);Assert.AreEqual(before.Frontier.OriginalBudget*1024,after.Frontier.SolveWorkLimit);
                Assert.AreEqual(admissionRefs,admissionPin.StorageOwner.References,"admission refs");Assert.AreEqual(rootRefs-1,before.References,"only replaced committed source root reference retired");Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav,before,after,admissionPin.StorageOwner),budget.PhysicalBytes,"after census");Assert.Zero(budget.HeldBytes,"after Held");Assert.Zero(budget.ActiveScopes,"after scopes");CollectionAssert.AreEqual(captureAfter,Capture(nav));
                budget.AllocationAdmission=null;after.Release();after=null;before.Release();before=null;admissionPin.Dispose();admissionPin=default;
                releaseRefused.Set();runtime.RequestPause(false);bool moved=false,arrived=false;long firstMotion=-1,arrival=-1;
                Wait(()=>{var current=runtime.ParticipantView(owner).Entities.Single(e=>e.Id==selected.Entity);if(!moved&&!current.Position.Equals(selectedStart)){moved=true;firstMotion=runtime.Latest.Tick;}if(current.NavigationOutcome==NavigationOutcome.Arrived){arrived=true;arrival=runtime.Latest.Tick;}var pending=nav.PendingRequestFor(selected.Entity);if(pending!=null)Assert.AreEqual(selected.Request,pending.Request,"same naturally retried request");return arrived;},15,"produced Explorer Arrived");
                SaveProviderEvidence("producer-result",new{factory=factory.Id,entity=selected.Entity,request=selected.Request,denied,firstMotion,arrival,moved,arrived,seconds=total.Elapsed.TotalSeconds,workerPumps=worker.Publication.Pumps,commands=receipts.Select(r=>new{r.Sequence,status=r.Status.ToString()}).ToArray(),noReissue=true});Assert.True(moved);Assert.True(arrived);Assert.IsNull(worker.Publication.Failure);
                TestContext.WriteLine($"ORDINARY_PRODUCER_RETRY factory={factory.Id} entity={selected.Entity} request={selected.Request} target={targetCharge} denied={denied} firstMotionTick={firstMotion} arrivalTick={arrival} commands=1,2,3 noReissue=true");
            }finally{
                budget.AllocationAdmission=null;runtime.Dispose();host.Dispose();releaseAttempt.Set();releaseRefused.Set();if(kernel!=null)kernel.PauseAtPhysicalBoundary=null;
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));after?.Release();before?.Release();admissionPin.Dispose();nav.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();
                SaveProviderEvidence("producer-cleanup",new{physical=budget.PhysicalBytes,held=budget.HeldBytes,scopes=budget.ActiveScopes});Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        private sealed class ResolvingBirth : NavOwnedStorage
        {
            private readonly ManualResetEventSlim entered,release;
            internal ResolvingBirth(ManualResetEventSlim entered,ManualResetEventSlim release):base(128){this.entered=entered;this.release=release;}
            protected override void ReleaseEdges(){entered.Set();release.Wait();}
        }
        private static NavigationHostRoot Pin(PlayableAuthorityTick authority)=>authority.Domain.Navigation.PinCommittedHostRoot();
        [Test] public void TransientResolvingAdmissionDoesNotStrandOriginalRunnableQuantum()=>RunTransient(false);
        [Test] public void StoppedWorkerSafelyIgnoresDeferredResolutionWake()=>RunTransient(true);
        private static void RunTransient(bool stopBeforeRelease)
        {
            var authority=new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71,asynchronousRoutes:true);var nav=authority.Domain.Navigation;var kernel=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};CertifiedRouteWorker worker=null;Thread resolver=null;Exception resolverError=null;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            try{
                var actor=nav.Crowd.Units.First();Assert.True(nav.Move(actor.Id,new NavPoint(actor.Position.X+3,actor.Position.Z)));var original=nav.PendingRequestFor(actor.Id);Assert.NotNull(original);Assert.True(authority.TryAdvance());
                int prefix=0;for(;prefix<128;prefix++){var root=Pin(authority);try{if(root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Scan)break;Assert.True(root.QuantumRunnable);}finally{root.Release();}kernel.Pump(authority.NavigationBinding,4);}Assert.Less(prefix,128);
                // End the main-thread reservation before a fresh real worker
                // resumes the durable Scan prefix on its own owner thread.
                kernel.Clear();kernel=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
                long serial,tick=authority.Domain.Tick;int grant;var before=Pin(authority);try{serial=before.QuantumSerial;grant=before.QuantumBudget;Assert.True(before.QuantumRunnable);Assert.False(before.QuantumCompleted);}finally{before.Release();}
                var budget=nav.PhysicalHostBudget;resolver=new Thread(()=>{
                    NavigationRetentionBudget.Scope scope=null;
                    try{if(!budget.TryOpenScope(NavigationPreparationKind.Capture,71,0,out scope))throw new InvalidOperationException("Resolution fixture admission failed.");var r=scope.OwnedReservation();using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(128);a.Register(new ResolvingBirth(entered,release));}scope.Discard();}
                    catch(Exception e){resolverError=e;entered.Set();}finally{scope?.Discard();}
                }){IsBackground=true};resolver.Start();Assert.True(entered.Wait(5000));Assert.IsNull(resolverError);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,4,kernel);
                Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting&&worker.Publication.Pumps>0,5000),"actual worker must reach no-progress wait while scope admission resolves");
                var stalled=Pin(authority);try{Assert.True(stalled.QuantumRunnable);Assert.False(stalled.QuantumCompleted);Assert.AreEqual(serial,stalled.QuantumSerial);Assert.AreEqual(CertifiedRouteKernel.PumpStage.Scan,stalled.Frontier.Stage);}finally{stalled.Release();}
                long stalledPumps=worker.Publication.Pumps;
                if(stopBeforeRelease){worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));Assert.IsNull(worker.Publication.Failure);release.Set();Assert.True(resolver.Join(5000));Assert.IsNull(resolverError);var stopped=Pin(authority);try{Assert.AreEqual(serial,stopped.QuantumSerial);Assert.True(stopped.QuantumRunnable);Assert.False(stopped.QuantumCompleted);}finally{stopped.Release();}return;}
                release.Set();Assert.True(resolver.Join(5000));Assert.IsNull(resolverError);
                Assert.True(SpinWait.SpinUntil(()=>{var root=Pin(authority);try{return root.QuantumCompleted;}finally{root.Release();}},5000),"resolving admission becoming available must wake the original quantum without a new game command or budget signal");
                var after=Pin(authority);try{Assert.AreEqual(serial,after.QuantumSerial);Assert.AreEqual(grant,after.QuantumBudget);Assert.True(after.QuantumCompleted);Assert.False(after.QuantumRunnable);}finally{after.Release();}
                Assert.AreEqual(tick,authority.Domain.Tick);Assert.Greater(worker.Publication.Pumps,stalledPumps);Assert.IsNull(worker.Publication.Failure);Assert.NotNull(nav.PendingRequestFor(actor.Id));Assert.AreEqual(original.Request,nav.PendingRequestFor(actor.Id).Request);
                TestContext.WriteLine("THREADED_RESOLUTION_WAKE serial="+serial+" grant="+grant+" worldTick="+tick+" stalledPumps="+stalledPumps+" completedPumps="+worker.Publication.Pumps);
            }finally{
                release.Set();if(resolver!=null)Assert.True(resolver.Join(5000));worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));kernel.Clear();authority.Stop();nav.DestroyNavigationAuthority();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        private sealed class CallbackBirth : NavOwnedStorage
        {
            private readonly Action action;
            internal CallbackBirth(Action action):base(128){this.action=action;}
            protected override void ReleaseEdges()=>action();
        }
        [Test] public void ResolutionWakeCoalescesClearsBeforeReentryAndRunsOutsideLedgerGate()
        {
            var budget=new NavigationRetentionBudget();Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavigationRetentionBudget)),NavigationRetentionBudget.RootBytes-NavOwnedStorage.CaptureHeaderBytes,"keep original ledger root charge");
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var scope));int calls=0;bool reentered=false;var gate=typeof(NavigationRetentionBudget).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(budget);
            Action wake=()=>{calls++;Assert.False(Monitor.IsEntered(gate));reentered=budget.TryOpenScope(NavigationPreparationKind.Admission,1,2,out var next);Assert.True(reentered);next.Discard();};
            var r=scope.OwnedReservation();using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(128);a.Register(new CallbackBirth(()=>{for(int i=0;i<3;i++){Assert.False(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var refused,wake));Assert.IsNull(refused);}Assert.Zero(calls);}));}
            scope.Discard();Assert.True(scope.IsResolved);Assert.True(reentered);Assert.AreEqual(1,calls);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,3,out var ordinary));ordinary.Discard();Assert.AreEqual(1,calls);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void ThrowingResolutionWakeLeavesCommittedResolutionAndNoStaleWaiter()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var scope));int calls=0;Action wake=()=>{calls++;throw new InvalidOperationException("expected wake fault");};
            var r=scope.OwnedReservation();using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(128);a.Register(new CallbackBirth(()=>Assert.False(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var refused,wake))));}
            Assert.Throws<InvalidOperationException>(()=>scope.Discard());Assert.True(scope.IsResolved);Assert.AreEqual(1,calls);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,2,out var next));next.Discard();Assert.AreEqual(1,calls);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void OwnedReleaseFaultKeepsAdmissionClosedAndDropsDeferredWake()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var scope));int calls=0;Action wake=()=>calls++;
            var r=scope.OwnedReservation();using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(128);a.Register(new CallbackBirth(()=>{Assert.False(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var refused,wake));throw new InvalidOperationException("expected owned release fault");}));}
            Assert.Throws<InvalidOperationException>(()=>scope.Discard());Assert.True(scope.IsResolving);Assert.False(scope.IsResolved);Assert.Zero(calls);
            Assert.False(budget.TryOpenScope(NavigationPreparationKind.Admission,1,2,out var next));Assert.IsNull(next);
            Assert.IsNull(typeof(NavigationRetentionBudget).GetField("pendingResolutionWake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(budget));
            // The deliberately faulted local ledger retains its original charge
            // and resolving scope: reopening it would hide unsafe physical release.
            Assert.AreEqual(1,budget.ActiveScopes);Assert.Greater(budget.HeldBytes,0);
        }
        [Test] public void PermanentScopeRefusalDoesNotBusyRetryOrMintAnotherGrant()
        {
            var authority=new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71,asynchronousRoutes:true);var nav=authority.Domain.Navigation;var kernel=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};CertifiedRouteWorker worker=null;
            try{
                var actor=nav.Crowd.Units.First();Assert.True(nav.Move(actor.Id,new NavPoint(actor.Position.X+3,actor.Position.Z)));Assert.True(authority.TryAdvance());int prefix=0;
                for(;prefix<128;prefix++){var root=Pin(authority);try{if(root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Scan)break;}finally{root.Release();}kernel.Pump(authority.NavigationBinding,4);}Assert.Less(prefix,128);kernel.Clear();kernel=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};
                long serial;var before=Pin(authority);try{serial=before.QuantumSerial;Assert.True(before.QuantumRunnable);}finally{before.Release();}
                nav.PhysicalHostBudget.AllocationAdmission=bytes=>bytes!=NavigationRetentionBudget.ScopeBytes;worker=new CertifiedRouteWorker(authority.NavigationBinding,4,kernel);Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting&&worker.Publication.Pumps>0,5000));long pumps=worker.Publication.Pumps;worker.SetBudget(4);Thread.Sleep(50);Assert.True(worker.IsWaiting);Assert.AreEqual(pumps,worker.Publication.Pumps,"permanent admission refusal must not self-wake/spin");
                var stalled=Pin(authority);try{Assert.AreEqual(serial,stalled.QuantumSerial);Assert.True(stalled.QuantumRunnable);Assert.False(stalled.QuantumCompleted);}finally{stalled.Release();}
                nav.PhysicalHostBudget.AllocationAdmission=null;nav.SignalHost();Assert.True(SpinWait.SpinUntil(()=>{var root=Pin(authority);try{return root.QuantumCompleted;}finally{root.Release();}},5000));var after=Pin(authority);try{Assert.AreEqual(serial,after.QuantumSerial);}finally{after.Release();}Assert.IsNull(worker.Publication.Failure);
            }finally{nav.PhysicalHostBudget.AllocationAdmission=null;worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));kernel.Clear();authority.Stop();nav.DestroyNavigationAuthority();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}
        }
    }
}
