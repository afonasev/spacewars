using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class PreparedTacticalCommitTests
    {
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)]
        public void OrdinaryRuntimeOriginalMoveSurvivesRealWorkerProjectionPublication(PlayableCommandKind kind)
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);
            var runtime=new PlayableRuntime(config,71);var domain=(PlayableDomain)Field(runtime,"domain");var nav=domain.Navigation;
            var kernel=new CertifiedRouteKernel();CertifiedRouteWorker worker=null;NavigationHostRoot original=null,advanced=null;
            using var projection=new ManualResetEventSlim();using var releaseProjection=new ManualResetEventSlim();
            using var postFlush=new ManualResetEventSlim();using var releasePostFlush=new ManualResetEventSlim();
            using var cleaned=new ManualResetEventSlim();using var releaseCleanup=new ManualResetEventSlim();
            int closing=0,hits=0;Exception hookError=null;string before=null,after=null;byte[] current=null;
            System.Threading.Tasks.Task controller=null;
            PlayableDomain.PreparedTacticalCommit prepared=null;long preparedTick=-1;int ownId=0;
            var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
            void OutsideGates(){Assert.False(Monitor.IsEntered(Field(nav,"transportGate")));Assert.False(Monitor.IsEntered(Field(nav.PhysicalHostBudget,"gate")));Assert.False(Monitor.IsEntered(Field(runtime,"gate")));}
            kernel.PauseAtPhysicalBoundary=()=>{
                if(Volatile.Read(ref closing)!=0)return true;
                var root=nav.PinCommittedHostRoot();bool first=false,last=false;
                try{first=root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&root.Projection.Count>0&&!projection.IsSet;
                    last=root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.PostFlush&&projection.IsSet&&!postFlush.IsSet;}
                finally{root?.Release();}
                if(first){projection.Set();if(!releaseProjection.Wait(10000))throw new TimeoutException("real Projection release");}
                if(last){postFlush.Set();if(!releasePostFlush.Wait(10000))throw new TimeoutException("real PostFlush release");}
                return Volatile.Read(ref closing)!=0||nav.RoutePort.CaptureRequested;
            };
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));
                string foreign=config.Roster[1].Id;var foreignView=runtime.ParticipantView(foreign);var peer=foreignView.Entities.First(e=>e.Owner==foreignView.Owner);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,foreign,PlayableCommandKind.Move,new[]{peer.Id},new NavPoint(peer.Position.X-3,peer.Position.Z+3))).Status);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000));
                Assert.AreEqual(PlayableCommandStatus.Applied,receipts.Single().Status,receipts.Single().Message);receipts.Clear();
                worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel);
                Assert.True(projection.Wait(10000),"real worker reaches nonempty Projection using ordinary runtime grants: "+worker.Publication.Failure);
                original=nav.PinCommittedHostRoot();Assert.Greater(original.Projection.Count,0);Assert.AreNotEqual(0,Field(nav,"hostAllocationThread"));
                string owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);ownId=actor.Id;
                nav.CommandAdmissionDiagnosticsForQa=true;
                domain.AfterTacticalPreparationForQa=payload=>{
                    OutsideGates();hits++;prepared=payload;preparedTick=domain.Tick;before=UnitState(domain,ownId);
                    Assert.True(nav.RoutePort.CaptureRequested,"acknowledgment precedes the original payload");
                    Assert.Zero((int)Field(nav.RoutePort,"hostOperations"));
                    Assert.False(nav.RoutePort.TryBeginHostOperation(),"no worker operation during original command");
                    var now=nav.PinCommittedHostRoot();try{Assert.AreSame(original,now,"original real worker source stays current through preparation");}finally{now.Release();}
                };
                nav.AfterTacticalCleanupForQa=()=>{
                    try{
                        OutsideGates();Assert.AreEqual(1,hits);Assert.AreEqual(preparedTick,domain.Tick);after=UnitState(domain,ownId);
                        Assert.IsNull(Field(nav,"commandGroup"));Assert.IsNull(Field(nav,"preparedCommandGroup"));
                        Assert.True(nav.RoutePort.CaptureRequested);Assert.Zero((int)Field(nav.RoutePort,"hostOperations"));
                        var now=nav.PinCommittedHostRoot();try{Assert.AreSame(original,now,"command must preserve original worker lineage");}finally{now.Release();}
                        current=domain.CaptureWorldBytes(config.Seed,config.SourceIdentity);
                        CollectionAssert.AreEqual(current,domain.CaptureWorldBytes(config.Seed,config.SourceIdentity),"same-tick atomic CURRENT capture");
                        var cold=PlayableDomain.RestoreConfiguredWorldBytes(current,config.Profile,config.Seed,config.SourceIdentity,config);
                        try{CollectionAssert.AreEqual(current,cold.CaptureWorldBytes(config.Seed,config.SourceIdentity),"exact configured CURRENT cold restore");}finally{cold.DestroyOwnedNavigation();}
                    }catch(Exception e){hookError=e;}finally{cleaned.Set();if(!releaseCleanup.Wait(10000))throw new TimeoutException("cleanup observation release");}
                };
                // On the baseline the preparation callback is reached first; on
                // the repair capture is requested first. Never wait for progress
                // of the excluded worker from inside the command callback.
                controller=System.Threading.Tasks.Task.Run(()=>{Assert.True(SpinWait.SpinUntil(()=>nav.RoutePort.CaptureRequested||Volatile.Read(ref hits)>0||Volatile.Read(ref closing)!=0,10000));releaseProjection.Set();});
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,100,owner,kind,new[]{actor.Id},new NavPoint(actor.Position.X+2,actor.Position.Z+3))).Status);
                Assert.True(cleaned.Wait(10000),"ordinary command reaches final cleanup: "+runtime.Latest.Failure);if(hookError!=null)throw new Exception("atomic CURRENT proof",hookError);
                Assert.AreNotEqual(before,after,"original command installs its own actor/order atomically");Assert.NotNull(current);Assert.IsNull(prepared.Refusal);
                var stamp=(PlayableOrderStamp)Field(Unit(domain,ownId),"LastOrder");Assert.AreEqual(100,stamp.Sequence);Assert.AreEqual(preparedTick,stamp.Tick);Assert.AreEqual(PlayableOrderOrigin.Human,stamp.Origin);
                releaseCleanup.Set();
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000));var receipt=receipts.Single();
                Assert.AreEqual(100,receipt.Sequence);Assert.AreEqual(owner,receipt.OwnerId);Assert.AreEqual(preparedTick,receipt.AppliedTick);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,"Original ordinary command should survive worker-only publication; baseline causal proof above must remain preserved.");
                Assert.AreEqual("Order accepted.",receipt.Message);Assert.AreEqual(1,hits);Assert.True(controller.Wait(10000));
                Assert.True(postFlush.Wait(10000),"real worker resumes after command exclusion release");advanced=nav.PinCommittedHostRoot();
                Assert.AreEqual(CertifiedRouteKernel.PumpStage.PostFlush,advanced.Frontier.Stage);Assert.Greater(advanced.Progress,original.Progress);Assert.Zero((int)Field(nav,"hostAllocationThread"));
                TestContext.Out.WriteLine("CYCLE55_REPAIRED kind="+kind+" status="+receipt.Status+" owner="+owner+" actor="+ownId+" sequence=100 tick="+preparedTick+" bodyOnce="+hits+" currentBytes="+current.Length+" currentExact=true workerResumed=true");
            }finally{
                Volatile.Write(ref closing,1);releaseProjection.Set();releasePostFlush.Set();releaseCleanup.Set();
                domain.AfterTacticalPreparationForQa=null;nav.AfterTacticalCleanupForQa=null;
                runtime.RequestStop();worker?.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),10000),"both actual owners stop before navigation destruction");
                if(controller!=null)Assert.True(controller.Wait(10000));
                runtime.Dispose();original?.Release();advanced?.Release();domain.DestroyOwnedNavigation();
            }
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void OrdinaryMoveCaptureWaitReleasesOnStopOrRealWorkerFault(bool fault,bool ai)
        {
            var config=ai?QueuedAiCompletionConfiguration():OfflineParticipantAuthorityTests.Config(2,false);var runtime=new PlayableRuntime(config,71);var domain=(PlayableDomain)Field(runtime,"domain");var nav=domain.Navigation;
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int closing=0,hits=0;
            var kernel=new CertifiedRouteKernel();CertifiedRouteWorker worker=null;var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
            kernel.PauseAtPhysicalBoundary=()=>{
                if(Volatile.Read(ref closing)!=0)return true;
                var root=nav.PinCommittedHostRoot();bool park;try{park=root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&root.Projection.Count>0&&!reached.IsSet;}finally{root?.Release();}
                if(park){reached.Set();if(!release.Wait(10000))throw new TimeoutException("capture-wait real worker release");if(fault&&Volatile.Read(ref closing)==0)throw new InvalidOperationException("cycle55 real worker fault");}
                return Volatile.Read(ref closing)!=0||nav.RoutePort.CaptureRequested;
            };
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));string foreign=config.Roster[1].Id;var fv=runtime.ParticipantView(foreign);var peer=fv.Entities.First(e=>e.Owner==fv.Owner);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,foreign,PlayableCommandKind.Move,new[]{peer.Id},new NavPoint(peer.Position.X-3,peer.Position.Z+3))).Status);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000));Assert.AreEqual(PlayableCommandStatus.Applied,receipts.Single().Status);receipts.Clear();
                worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,failed:runtime.ReportRouteFailure);Assert.True(reached.Wait(10000),worker.Publication.Failure);
                string owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);
                domain.AfterTacticalPreparationForQa=_=>Interlocked.Increment(ref hits);
                long sequence=ai?PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0):100;
                var command=new PlayableCommand(71,sequence,owner,PlayableCommandKind.Move,new[]{actor.Id},new NavPoint(actor.Position.X+2,actor.Position.Z+3),origin:ai?PlayableOrderOrigin.Ai:PlayableOrderOrigin.Human,source:ai?PlayableRuntime.MovingPerformanceSource:null,jobId:ai?1000000:0,actionId:ai?1000000:0);
                Assert.AreEqual(PlayableCommandStatus.Accepted,(ai?runtime.TrySubmitAi(command):runtime.TrySubmit(command)).Status);
                Assert.True(SpinWait.SpinUntil(()=>nav.RoutePort.CaptureRequested,10000));Assert.Zero(hits);Assert.IsEmpty(runtime.DrainReceipts());
                // The command remains pending while the authority continues idle ticks.
                // This actor has been stationary since creation (timer zero at tick zero).
                // Do not pair two unsynchronised live reads of its timer and domain.Tick.
                string before=UnitState(domain,actor.Id);
                object ownQueue(){var queues=(System.Collections.IDictionary)Field(domain,"tacticalQueues");return queues.Contains(actor.Id)?queues[actor.Id]:null;}
                var queueBefore=ownQueue();var requestBefore=Field(nav,"requestSequence");var groupBefore=Field(nav,"groupSequence");var orderBefore=Field(domain,"nextOrderRevision");
                if(fault)release.Set();else runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000),"capture wait wakes on Stop/fault without requiring a successful worker publication");
                Assert.False(nav.RoutePort.CaptureRequested);Assert.Zero(hits);
                long completedIdleTicks=domain.Tick;Assert.Greater(completedIdleTicks,0);
                var expected=Newtonsoft.Json.Linq.JObject.Parse(before);double stopped=0;for(long i=0;i<completedIdleTicks;i++)stopped+=1d/30;expected["StoppedSeconds"]=stopped;
                Assert.AreEqual(expected.ToString(Newtonsoft.Json.Formatting.None),UnitState(domain,actor.Id),"all original actor/order fields exact; stationary timer accounts for every completed world tick since creation");
                Assert.AreSame(queueBefore,ownQueue());Assert.AreEqual(requestBefore,Field(nav,"requestSequence"));Assert.AreEqual(groupBefore,Field(nav,"groupSequence"));Assert.AreEqual(orderBefore,Field(domain,"nextOrderRevision"));
                Assert.IsNull(Field(nav,"commandGroup"));Assert.IsNull(Field(nav,"preparedCommandGroup"));Assert.IsNull(Field(nav,"preparedCommandScope"));
                receipts.AddRange(runtime.DrainReceipts());var receipt=receipts.Single();Assert.AreEqual(sequence,receipt.Sequence);Assert.AreEqual(owner,receipt.OwnerId);
                Assert.AreEqual(fault?PlayableCommandStatus.Rejected:PlayableCommandStatus.Cancelled,receipt.Status);
                Assert.AreEqual(fault?"Command failed.":"Runtime stopped.",receipt.Message);
                if(fault){StringAssert.Contains("cycle55 real worker fault",runtime.Latest.Failure);Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,10000));}
                Assert.True(nav.RoutePort.TryBeginHostOperation(),"capture exclusion released after stop/fault");nav.RoutePort.EndHostOperation();
                TestContext.Out.WriteLine("CYCLE57_CAPTURE_WAIT ai="+ai+" fault="+fault+" status="+receipt.Status+" bodyCount="+hits+" completedExistingIdleTicks="+completedIdleTicks+" originalTacticalStateExact=true exclusionReleased=true");
            }finally{
                Volatile.Write(ref closing,1);release.Set();domain.AfterTacticalPreparationForQa=null;runtime.RequestStop();worker?.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),10000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
        private static OfflineMatchConfiguration QueuedAiCompletionConfiguration()
        {
            var basis=OfflineParticipantAuthorityTests.Config(2,false,seed:7);
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var roster=basis.Roster.Select((p,i)=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,i==0?OfflineControl.Ai:OfflineControl.Human)).ToArray();
            return new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);
        }
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)]
        public void QueuedAiOriginalMoveSurvivesRealWorkerCompletion(PlayableCommandKind kind)
        {
            var config=QueuedAiCompletionConfiguration();
            var runtime=new PlayableRuntime(config,71);var domain=(PlayableDomain)Field(runtime,"domain");var nav=domain.Navigation;
            var kernel=new CertifiedRouteKernel();CertifiedRouteWorker worker=null;NavigationHostRoot original=null,completed=null;
            using var reached=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var finished=new ManualResetEventSlim();
            using var cleaned=new ManualResetEventSlim();using var releaseCleanup=new ManualResetEventSlim();
            int closing=0,hits=0,cleanups=0;Exception hookError=null;string before=null,after=null,diagnostic=null;byte[] current=null;long bodyTick=-1;
            System.Threading.Tasks.Task controller=null;var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
            void OutsideGates(){Assert.False(Monitor.IsEntered(Field(nav,"transportGate")));Assert.False(Monitor.IsEntered(Field(nav.PhysicalHostBudget,"gate")));Assert.False(Monitor.IsEntered(Field(runtime,"gate")));}
            kernel.PauseAtPhysicalBoundary=()=>{
                if(Volatile.Read(ref closing)!=0)return true;
                var root=nav.PinCommittedHostRoot();bool start;
                try{start=root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.ArrivalAdvance&&root.Progress>0&&!reached.IsSet;}
                finally{root?.Release();}
                if(start){reached.Set();if(!release.Wait(10000))throw new TimeoutException("actual stage2 release");}
                return Volatile.Read(ref closing)!=0||nav.RoutePort.CaptureRequested;
            };
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));
                Assert.AreEqual(OfflineControl.Ai,config.Roster[0].Control);
                Assert.True(((AiAuthorityScheduler)Field(runtime,"aiScheduler")).Owners.Any(),"real configured AI owner remains active");
                string foreign=config.Roster[1].Id;var foreignView=runtime.ParticipantView(foreign);var peer=foreignView.Entities.First(e=>e.Owner==foreignView.Owner);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,foreign,PlayableCommandKind.Move,new[]{peer.Id},new NavPoint(peer.Position.X-3,peer.Position.Z+3))).Status);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000));
                Assert.AreEqual(PlayableCommandStatus.Applied,receipts.Single().Status,receipts.Single().Message);receipts.Clear();
                // PumpCore publishes completed Idle and returns before its next physical
                // boundary callback. Observe it at the real worker's existing stop
                // probe after Pump, before it can promote the next credit. Return
                // false: the fixture neither stops nor rewrites the worker/root.
                bool ObserveWorkerStop(){
                    if(Volatile.Read(ref closing)!=0)return true;
                    var root=nav.PinCommittedHostRoot();bool done;
                    try{done=reached.IsSet&&!finished.IsSet&&root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Idle&&root.QuantumCompleted;}finally{root?.Release();}
                    if(done){finished.Set();if(!releaseCleanup.Wait(10000))throw new TimeoutException("actual completed stage0 release");}
                    return Volatile.Read(ref closing)!=0;
                }
                worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,stopRequested:ObserveWorkerStop,failed:runtime.ReportRouteFailure);
                Assert.True(reached.Wait(10000),"real worker stage2: "+worker.Publication.Failure);
                original=nav.PinCommittedHostRoot();Assert.AreEqual(CertifiedRouteKernel.PumpStage.ArrivalAdvance,original.Frontier.Stage);
                Assert.True(original.QuantumRunnable);Assert.False(original.QuantumCompleted);Assert.Zero(original.Projection.Count);Assert.Zero((int)Field(nav,"hostAllocationThread"));
                string owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);
                long sequence=PlayableRuntime.EightPlayerMovingPerformanceSequence(view.Owner,0),action=1000000+(int)view.Owner;
                var command=new PlayableCommand(71,sequence,owner,kind,new[]{actor.Id},new NavPoint(actor.Position.X+2,actor.Position.Z+3),
                    origin:PlayableOrderOrigin.Ai,source:PlayableRuntime.MovingPerformanceSource,jobId:action,actionId:action);
                nav.CommandAdmissionDiagnosticsForQa=true;
                nav.AfterMovementObservationForQa=()=>{
                    var context=(GroupOrderState)Field(nav,"commandGroup");if(context==null||context.CommandSequence!=sequence)return;
                    Assert.AreEqual(command.Origin,context.Origin);Assert.AreEqual(command.Source,context.Source);Assert.AreEqual(action,context.JobId);Assert.AreEqual(action,context.ActionId);
                    OutsideGates();Interlocked.Increment(ref hits);bodyTick=domain.Tick;before=UnitState(domain,actor.Id);
                    if(nav.RoutePort.CaptureRequested){Assert.Zero((int)Field(nav.RoutePort,"hostOperations"));Assert.False(nav.RoutePort.TryBeginHostOperation());}
                    else{release.Set();Assert.True(finished.Wait(10000),"same actual worker completes before guard");}
                    runtime.RequestPause(true); // Existing pause takes effect only after this original command returns.
                };
                void InspectOutcome(){
                    try{
                        OutsideGates();after=UnitState(domain,actor.Id);Assert.AreEqual(bodyTick,domain.Tick);
                        Assert.IsNull(Field(nav,"commandGroup"));Assert.IsNull(Field(nav,"preparedCommandGroup"));
                        diagnostic=nav.CommandAdmissionForQa(sequence);completed=nav.PinCommittedHostRoot();
                        TestContext.Out.WriteLine("CYCLE57_OBSERVED stage="+(int)original.Frontier.Stage+">"+(int)completed.Frontier.Stage+" progress="+original.Progress+">"+completed.Progress+" diagnostic="+diagnostic);
                        if(!nav.RoutePort.CaptureRequested){
                            Assert.AreEqual(CertifiedRouteKernel.PumpStage.Idle,completed.Frontier.Stage);Assert.True(completed.QuantumCompleted);Assert.False(completed.QuantumRunnable);
                            Assert.AreEqual(original.QuantumSerial,completed.QuantumSerial);Assert.AreEqual(original.QuantumTick,completed.QuantumTick);
                            Assert.AreEqual(original.ConsumedQuantum,completed.ConsumedQuantum);Assert.AreEqual(original.AdmissionPrefix,completed.AdmissionPrefix);
                            Assert.Greater(completed.Progress,original.Progress);Assert.Zero(completed.Projection.Count);Assert.Zero((int)Field(nav,"hostAllocationThread"));
                            var guard=nav.LastGroupGuardObservationForQa;Assert.True(guard.HasExactHostDiagnostics);Assert.AreEqual(2,guard.ExpectedHostStage);Assert.Zero(guard.ActualHostStage);
                            Assert.AreEqual(NavigationSession.GroupGuardMismatch.Host,guard.Mismatch);Assert.True(guard.MovementInputsCurrent);Assert.False(guard.WorkerCompletedAllocation);
                            StringAssert.Contains("movement-prepare:Group command lost its observed authority.;mask=2",diagnostic);
                            StringAssert.Contains(";guard_stage=2>0;",diagnostic);Assert.AreEqual(before,after,"baseline refusal has no partial actor/order effect");
                        }else Assert.AreSame(original,completed,"ACK before original observation excludes worker-only completion");
                        current=domain.CaptureWorldBytes(config.Seed,config.SourceIdentity);
                        CollectionAssert.AreEqual(current,domain.CaptureWorldBytes(config.Seed,config.SourceIdentity));
                        var cold=PlayableDomain.RestoreConfiguredWorldBytes(current,config.Profile,config.Seed,config.SourceIdentity,config);
                        try{CollectionAssert.AreEqual(current,cold.CaptureWorldBytes(config.Seed,config.SourceIdentity));}finally{cold.DestroyOwnedNavigation();}
                        TestContext.Out.WriteLine("CYCLE57_CAUSAL kind="+kind+" origin="+command.Origin+" source="+command.Source+" job="+command.JobId+" action="+command.ActionId+" sequence="+command.Sequence+" owner="+owner+" bodyOnce="+hits+" stage="+(int)original.Frontier.Stage+">"+(int)completed.Frontier.Stage+" currentExact=true diagnostic="+diagnostic);
                    }catch(Exception e){hookError=e;}
                }
                nav.AfterTacticalCleanupForQa=()=>{if(Volatile.Read(ref hits)==0)return;cleanups++;InspectOutcome();cleaned.Set();if(!releaseCleanup.Wait(10000))throw new TimeoutException("actual AI cleanup observation");};
                controller=System.Threading.Tasks.Task.Run(()=>{Assert.True(SpinWait.SpinUntil(()=>nav.RoutePort.CaptureRequested||Volatile.Read(ref hits)>0||Volatile.Read(ref closing)!=0,10000));if(nav.RoutePort.CaptureRequested)release.Set();});
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmitAi(command).Status);
                Assert.True(SpinWait.SpinUntil(()=>{
                    if(cleaned.IsSet)return true;
                    receipts.AddRange(runtime.DrainReceipts());return receipts.Any(r=>r.Sequence==sequence)&&runtime.Latest.Paused;
                },10000),"actual queued AI reaches receipt or successful cleanup: "+runtime.Latest.Failure+" hits="+hits);
                if(!cleaned.IsSet){Assert.Zero(cleanups,"early preparation refusal precedes successful cleanup hook");InspectOutcome();}
                if(hookError!=null)throw new Exception("actual AI causal proof",hookError);
                Assert.NotNull(current);Assert.AreEqual(1,hits);
                releaseCleanup.Set();Assert.True(controller.Wait(10000));
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Any(r=>r.Sequence==sequence);},10000));
                var receipt=receipts.Single(r=>r.Sequence==sequence);Assert.AreEqual(owner,receipt.OwnerId);Assert.AreEqual(bodyTick,receipt.AppliedTick);
                TestContext.Out.WriteLine("CYCLE57_RECEIPT status="+receipt.Status+" message="+receipt.Message+" sequence="+sequence);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,"Original queued AI command must survive worker-only stage2->0 completion; preserve baseline causal RED before repair.");
                Assert.AreNotEqual(before,after);var stamp=(PlayableOrderStamp)Field(Unit(domain,actor.Id),"LastOrder");
                Assert.AreEqual(sequence,stamp.Sequence);Assert.AreEqual(PlayableOrderOrigin.Ai,stamp.Origin);Assert.AreEqual(command.Source,stamp.Source);Assert.AreEqual(action,stamp.JobId);Assert.AreEqual(action,stamp.ActionId);
                Assert.True(finished.Wait(10000),"real worker resumes and completes after original command ACK release");
                Assert.False(nav.RoutePort.CaptureRequested);Assert.True(nav.RoutePort.TryBeginHostOperation());nav.RoutePort.EndHostOperation();
                TestContext.Out.WriteLine("CYCLE57_REPAIRED bodyOnce="+hits+" cleanupOnce="+cleanups+" originalProvenance=true currentExact=true workerResumed=true");
            }finally{
                Volatile.Write(ref closing,1);release.Set();releaseCleanup.Set();nav.AfterMovementObservationForQa=null;nav.AfterTacticalCleanupForQa=null;
                runtime.RequestStop();worker?.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),10000));
                if(controller!=null)Assert.True(controller.Wait(10000));runtime.Dispose();original?.Release();completed?.Release();domain.DestroyOwnedNavigation();
            }
        }
        private sealed class ProgressWorker : IDisposable
        {
            private readonly World world;private readonly Thread thread;private readonly ManualResetEventSlim reached=new ManualResetEventSlim();private readonly AutoResetEvent resume=new AutoResetEvent(false);
            private Func<bool> target;private Exception error;private bool started;private volatile bool closing;
            internal ProgressWorker(World w)
            {
                world=w;w.Kernel.Attach(w.Nav.RoutePort);
                w.Kernel.PauseAtPhysicalBoundary=AtBoundary;
                thread=new Thread(()=>{try{for(int i=0;i<16&&!closing;i++){w.Kernel.Pump(w.Binding(),4);Observe();if(!closing&&Completed(w)){w.Nav.RoutePort.TryPromoteAccruedCredit();Observe();}}if(!closing)throw new Exception("Boundary not reached within16 Pumps");}catch(Exception e){error=e;reached.Set();}finally{w.Kernel.Clear();}}){IsBackground=true};
            }
            // Fixture-only split of the real allocation/release operations at the
            // existing boundary seam. No runtime observation hook or fabricated
            // root/token/floor. BeginAllocation and PublishPhysicalFrame are the
            // same private methods used by PumpCore; reflection stays in tests.
            private bool AtBoundary()
            {
                Observe();if(closing)return true;
                var kernel=world.Kernel;var port=world.Nav.RoutePort;
                if(world.At(CertifiedRouteKernel.PumpStage.Allocation)&&Token(world)==0){
                    long floor=(long)typeof(CertifiedRouteKernel).GetMethod("BeginAllocation",F).Invoke(kernel,new object[]{port});
                    Set(kernel,"reservedCorridorBytes",floor);Set(kernel,"allocating",true);
                    Observe(); // actual token acquired, Allocation root still published
                }
                if(world.At(CertifiedRouteKernel.PumpStage.EndAllocation)&&Token(world)!=0){
                    var f=kernel.CapturePumpFrontier();var retained=port.PhysicalRetainedBytes??0;
                    Set(kernel,"allocating",false);Set(kernel,"reservedCorridorBytes",0L);port.EndHostAllocation(retained);
                    Observe(); // actual token released, EndAllocation root still published
                    if(closing)return true;
                    var next=new CertifiedRouteKernel.PumpFrontier(CertifiedRouteKernel.PumpStage.PostFlush,f.OriginalBudget,f.ScansUsed,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,f.ProjectionAllowance,f.SolveWorkLimit,f.SolveConsumed,f.AllocationFloor,0,f.ScanAllowance);
                    Assert.True((bool)typeof(CertifiedRouteKernel).GetMethod("PublishPhysicalFrame",F).Invoke(kernel,new object[]{port,next}));
                    return true; // resume from the real published frame on next Pump
                }
                return closing;
            }
            private void Observe(){if(closing||target==null||!target())return;reached.Set();if(!resume.WaitOne(3000))throw new TimeoutException("Boundary release");}
            internal void Until(Func<bool> predicate){target=predicate;reached.Reset();if(started)resume.Set();else{started=true;thread.Start();}Assert.True(reached.Wait(3000),"worker boundary");if(error!=null)throw new Exception("Worker boundary failed",error);}
            public void Dispose(){closing=true;world.Kernel.PauseAtPhysicalBoundary=null;resume.Set();if(started)Assert.True(thread.Join(3000),"worker exit");else world.Kernel.Clear();reached.Dispose();resume.Dispose();if(error!=null)throw new Exception("Worker cleanup failed",error);}
        }
        private static int Token(World w)=>(int)Field(w.Nav,"hostAllocationThread");
        private static long Serial(World w){var r=w.Nav.PinCommittedHostRoot();try{return r.QuantumSerial;}finally{r.Release();}}
        private static bool Completed(World w){var r=w.Nav.PinCommittedHostRoot();try{return r.Frontier.Stage==CertifiedRouteKernel.PumpStage.Idle&&r.QuantumCompleted;}finally{r.Release();}}
        private static int PrepareRemainingProgress(World w)
        {
            var p=w.Position(3);int enemy=w.Spawn(PlayableOwner.Enemy,new NavPoint(p.X+4,p.Z));int peer=w.Spawn(PlayableOwner.Player,new NavPoint(p.X,p.Z+4));
            var authority=new PlayableAuthorityTick(w.Domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,asynchronousRoutes:true);authority.EnableAccruedCredits();
            Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move,new[]{peer}),out _));
            var routing=w.Domain.RoutingProfile;
            for(int i=0;i<3;i++){Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));w.Domain.Step(1d/30);}
            return enemy;
        }
        private static Action PrimeRemainingProgress(World w,ProgressWorker worker,string family)
        {
            var source=family=="retire"||family=="start-gap"?CertifiedRouteKernel.PumpStage.Retirement:
                family=="active-gap"?CertifiedRouteKernel.PumpStage.Allocation:family=="complete-gap"?CertifiedRouteKernel.PumpStage.Solve:
                family=="empty-complete"?CertifiedRouteKernel.PumpStage.Ready:family=="close"||family=="close-next"?CertifiedRouteKernel.PumpStage.PostFlush:
                family=="projection-next"?CertifiedRouteKernel.PumpStage.Projection:CertifiedRouteKernel.PumpStage.Idle;
            bool sourceBusy=family=="active-gap"||family=="complete-gap"||family=="empty-complete"||family=="projection-next";
            worker.Until(()=>w.At(source)&&(Token(w)!=0)==sourceBusy&&(source!=CertifiedRouteKernel.PumpStage.Idle||Completed(w)));
            long serial=Serial(w);
            var destination=family=="retire"?CertifiedRouteKernel.PumpStage.ArrivalAdvance:family=="start-gap"?CertifiedRouteKernel.PumpStage.Allocation:
                family=="active-gap"?CertifiedRouteKernel.PumpStage.Solve:family=="complete-gap"?CertifiedRouteKernel.PumpStage.EndAllocation:
                family=="empty-complete"?CertifiedRouteKernel.PumpStage.PostFlush:family=="next"?CertifiedRouteKernel.PumpStage.PreFlush:
                family=="close-next"||family=="attack"?CertifiedRouteKernel.PumpStage.Retirement:CertifiedRouteKernel.PumpStage.Idle;
            bool busy=family=="start-gap"||family=="active-gap";bool successor=family=="next"||family=="close-next"||family=="projection-next"||family=="attack";
            return ()=>worker.Until(()=>w.At(destination)&&(Token(w)!=0)==busy&&(!successor||Serial(w)==serial+1));
        }
        [Test] public void GuardDiagnosticsKeepValidationSnapshotAcrossLaterWorkerProgress()
        {
            using(var w=new World()){
                var authority=new PlayableAuthorityTick(w.Domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7,asynchronousRoutes:true);
                authority.EnableAccruedCredits();var at=w.Position(4);Assert.True(w.Nav.Move(4,new NavPoint(at.X-3,at.Z+3)));
                var routing=w.Domain.RoutingProfile;
                Assert.True(w.Nav.AccountAccruedCredit(w.Domain.Tick+1,routing.NavigationRequestsPerFrame,routing.Revision,routing.MaxAccruedCredits,w.Domain));
                NavigationHostRoot source=null;int token=0;int hits=0;PlayableDomain.PreparedTacticalCommit payloadForQa=null;
                using(var worker=new ProgressWorker(w)){
                    worker.Until(()=>{var root=w.Nav.PinCommittedHostRoot();try{return root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&root.Projection.Count>0&&Token(w)!=0;}finally{root.Release();}});
                    source=w.Nav.PinCommittedHostRoot();token=Token(w);
                    w.Nav.CommandAdmissionDiagnosticsForQa=true;w.Nav.BeginCommandAdmissionForQa(1);
                    w.Domain.AfterTacticalPreparationForQa=payload=>{payloadForQa=payload;hits++;worker.Until(()=>w.At(CertifiedRouteKernel.PumpStage.EndAllocation)&&Token(w)==0);};
                    try{
                        // The existing nonempty-projection refusal contract stays intact.
                        Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(1,PlayableCommandKind.Move),out _));
                        Assert.AreEqual(1,hits);var observed=w.Nav.LastGroupGuardObservationForQa;
                        Assert.True(observed.HasExactHostDiagnostics);Assert.AreEqual(9,observed.ExpectedHostStage);Assert.AreEqual(10,observed.ActualHostStage);
                        Assert.AreEqual(token,observed.ExpectedAllocationThread);Assert.Zero(observed.ActualAllocationThread);
                        Assert.Greater(observed.ExpectedHostDiagnostic.ProjectionCount,0);Assert.Greater(observed.ActualHostDiagnostic.Progress,observed.ExpectedHostDiagnostic.Progress);
                        Assert.AreEqual(source.Frontier,observed.ExpectedHostDiagnostic.Frontier);Assert.True(observed.WorkerCompletedAllocation);
                        Assert.NotNull(payloadForQa.Refusal);var actualRefusal=w.Nav.CommandAdmissionForQa(1);
                        StringAssert.StartsWith("tactical-final:"+payloadForQa.Refusal.Reason+":"+payloadForQa.Refusal.Message+";",actualRefusal);
                        StringAssert.Contains(";guard_stage=9>10;",actualRefusal);
                        var candidate=w.Nav.LastGroupGuardCandidateForQa;
                        worker.Until(()=>w.At(CertifiedRouteKernel.PumpStage.PostFlush)&&Token(w)==0);
                        w.Nav.RefuseCommandAdmissionForQa("later-catch",source,token,candidate);
                        var diagnostic=w.Nav.CommandAdmissionForQa(1);
                        StringAssert.Contains(";stage=9>11;",diagnostic);StringAssert.Contains(";guard_stage=9>10;",diagnostic);
                        StringAssert.Contains(";guard_token="+token+">0;",diagnostic);StringAssert.Contains(";guard_completed=True",diagnostic);
                        Assert.IsNull(w.Nav.CommandAdmissionForQa(2));Assert.IsNull(Field(Unit(w.Domain,3),"LastOrder"));
                    }finally{w.Domain.AfterTacticalPreparationForQa=null;source?.Release();}
                }
                var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes(),"diagnostics do not enter CURRENT save state");
            }
        }
        [TestCase("retire","stage")][TestCase("retire","commit")]
        [TestCase("start-gap","stage")][TestCase("start-gap","commit")]
        [TestCase("active-gap","stage")][TestCase("active-gap","commit")]
        [TestCase("complete-gap","stage")][TestCase("complete-gap","commit")]
        [TestCase("empty-complete","stage")][TestCase("empty-complete","commit")]
        [TestCase("close","stage")][TestCase("close","commit")]
        [TestCase("next","stage")][TestCase("next","commit")]
        [TestCase("close-next","stage")][TestCase("close-next","commit")]
        [TestCase("projection-next","stage")][TestCase("projection-next","commit")]
        [TestCase("attack","stage")][TestCase("attack","commit")]
        public void RemainingWorkerProgressPreservesHostBankAndExactCold(string family,string boundary)
        {
            using(var w=new World()){
                int enemy=PrepareRemainingProgress(w);NavigationHostRoot advanced=null;NavPagedList<long> bank=null;byte[] body=null;long[] values=null;int hits=0;
                using(var worker=new ProgressWorker(w)){
                    var advance=PrimeRemainingProgress(w,worker,family);
                    void Advance(){hits++;advance();advanced=w.Nav.PinCommittedHostRoot();bank=w.Nav.PinCreditBank();body=HostBody(w.Nav,advanced);values=BankValues(bank);}
                    if(boundary=="stage")w.Nav.BeforeCommandMailboxSnapshotForQa=_=>Advance();else w.Domain.AfterTacticalPreparationForQa=_=>Advance();
                    try{
                        Assert.AreEqual(PlayableCommandStatus.Applied,w.Domain.Apply(w.Command(2,family=="attack"?PlayableCommandKind.Attack:family=="close"?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,target:enemy),out var message),message);
                        Assert.AreEqual(1,hits);Assert.Zero(w.Domain.LastTacticalCommitAllocatedBytesForQa);
                        var current=w.Nav.PinCommittedHostRoot();var currentBank=w.Nav.PinCreditBank();
                        try{Assert.AreSame(advanced,current);Assert.AreSame(bank,currentBank);CollectionAssert.AreEqual(body,HostBody(w.Nav,current));CollectionAssert.AreEqual(values,BankValues(currentBank));}
                        finally{current.Release();currentBank.Release();}
                    }finally{w.Nav.BeforeCommandMailboxSnapshotForQa=null;w.Domain.AfterTacticalPreparationForQa=null;advanced?.Release();bank?.Release();}
                }
                Assert.Zero(w.Nav.PhysicalHostBudget.HeldBytes);Assert.Zero(w.Nav.PhysicalHostBudget.ActiveScopes);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(w.Nav),w.Nav.PhysicalHostBudget.PhysicalBytes);
                var bytes=w.Bytes();w.Cold(bytes);CollectionAssert.AreEqual(bytes,w.Bytes(),"exact CURRENT save/restore after certified worker progress");
            }
        }
        [TestCase("next","worker")][TestCase("next","attachment")][TestCase("next","serial")][TestCase("next","budget")]
        [TestCase("next","prefix")][TestCase("next","consumed")][TestCase("next","token")][TestCase("next","actor")]
        [TestCase("next","profile")][TestCase("next","cancel")][TestCase("next","generation")][TestCase("next","policy")]
        [TestCase("next","immutable")][TestCase("next","counter")][TestCase("next","tick")]
        [TestCase("active-gap","floor")][TestCase("active-gap","token")][TestCase("active-gap","source-token")]
        [TestCase("complete-gap","source-token")][TestCase("complete-gap","counter")]
        [TestCase("projection-next","serial")][TestCase("projection-next","source-token")]
        [TestCase("attack","actor")][TestCase("attack","token")][TestCase("attack","serial")][TestCase("attack","cancel")]
        public void RemainingWorkerProgressRejectsConflictWithoutDomainEffects(string family,string defect)
        {
            using(var w=new World()){
                int enemy=PrepareRemainingProgress(w);Action restore=null;NavigationHostRoot changed=null;int hits=0;
                using(var worker=new ProgressWorker(w)){
                    var advance=PrimeRemainingProgress(w,worker,family);string before=UnitState(w.Domain,3);
                    w.Domain.AfterTacticalPreparationForQa=_=>{
                        hits++;advance();changed=w.Nav.PinCommittedHostRoot();
                        if(defect=="worker"||defect=="attachment"||defect=="token"){
                            string name=defect=="worker"?"certifiedHost":defect=="attachment"?"certifiedHostAttachmentIdentity":"hostAllocationThread";var old=Field(w.Nav,name);Set(w.Nav,name,defect=="worker"?(object)new CertifiedRouteKernel():defect=="attachment"?(object)((long)old+1):(int)old+1);restore=()=>Set(w.Nav,name,old);
                        }
                        if(defect=="serial"||defect=="budget"||defect=="prefix"||defect=="consumed"||defect=="generation"||defect=="tick"){
                            string name=defect=="serial"?"QuantumSerial":defect=="budget"?"QuantumBudget":defect=="prefix"?"AdmissionPrefix":defect=="consumed"?"ConsumedQuantum":defect=="tick"?"QuantumTick":"QuantumGeneration";
                            var f=typeof(NavigationHostRoot).GetField("<"+name+">k__BackingField",F);var old=f.GetValue(changed);object next=old is int?(object)((int)old+1):defect=="prefix"?(object)long.MaxValue:defect=="tick"?(object)0L:(long)old+1;f.SetValue(changed,next);restore=()=>f.SetValue(changed,old);
                        }
                        if(defect=="counter"||defect=="floor"){
                            var f=typeof(NavigationHostRoot).GetField("<Frontier>k__BackingField",F);var a=changed.Frontier;f.SetValue(changed,new CertifiedRouteKernel.PumpFrontier(a.Stage,a.OriginalBudget,a.ScansUsed,a.TransfersUsed,a.ReadyUsed,a.ProjectionUsed,a.ProjectionAllowance,a.SolveWorkLimit,a.SolveConsumed,defect=="floor"?a.AllocationFloor+1:a.AllocationFloor,defect=="counter"?a.PhaseUsed+1:a.PhaseUsed,a.ScanAllowance));restore=()=>f.SetValue(changed,a);
                        }
                        if(defect=="immutable"||defect=="policy"){byte old=(byte)Field(changed,"lifecycle");Set(changed,"lifecycle",defect=="immutable"?(byte)(old&~1):(byte)(old^16));restore=()=>Set(changed,"lifecycle",old);}
                        if(defect=="source-token"){var c=Field(w.Nav,"preparedCommandGroup");var old=Field(c,"AllocationThread");Set(c,"AllocationThread",(int)old+1);restore=()=>Set(c,"AllocationThread",old);}
                        if(defect=="actor"){w.Nav.Crowd.TryGet(3,out var actor);var old=actor.Position;actor.Position=new NavPoint(old.X+.5,old.Z);restore=()=>actor.Position=old;}
                        if(defect=="profile")restore=w.ChangeOwnedProfile();
                        if(defect=="cancel")((NavigationRetentionBudget.Scope)Field(w.Nav,"preparedCommandScope")).RequestCancel();
                    };
                    try{Assert.AreEqual(PlayableCommandStatus.Rejected,w.Domain.Apply(w.Command(2,family=="attack"?PlayableCommandKind.Attack:PlayableCommandKind.Move,target:enemy),out _),defect);Assert.AreEqual(1,hits);Assert.AreEqual(before,UnitState(w.Domain,3),"no partial domain effect");}
                    finally{restore?.Invoke();changed?.Release();w.Domain.AfterTacticalPreparationForQa=null;}
                }
            }
        }
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)]
        public void AttackSuccessorDoesNotRelaxStopOrHold(PlayableCommandKind kind)
        {
            using(var w=new World()){
                PrepareRemainingProgress(w);
                using(var worker=new ProgressWorker(w)){
                    var advance=PrimeRemainingProgress(w,worker,"attack");w.Nav.BeforeCommandMailboxSnapshotForQa=_=>advance();
                    try{Assert.AreEqual(PlayableCommandStatus.Overflow,w.Domain.Apply(w.Command(2,kind),out _));}
                    finally{w.Nav.BeforeCommandMailboxSnapshotForQa=null;}
                }
            }
        }
    }
}
