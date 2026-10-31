using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static long OriginalSequence(int root)=>(1L<<62)+((long)root<<50)+1;
        private static OriginalGroupRecord[] ReadOriginal(OriginalGroupBuffer b)
        {
            using(var ms=new MemoryStream()){b.Save(ms);ms.Position=0;using(var r=new BinaryReader(ms)){
                Assert.AreEqual(0x3144474f,r.ReadInt32());Assert.AreEqual(1,r.ReadInt32());int slot=r.ReadInt32(),count=r.ReadInt32();r.ReadInt32();r.ReadInt64();r.ReadInt64();r.ReadBoolean();r.ReadInt64();var result=new OriginalGroupRecord[count];
                for(int i=0;i<count;i++){long start=ms.Position;var v=new OriginalGroupRecord{Kind=(OriginalGroupEvent)r.ReadInt32(),Root=r.ReadInt32(),Committed=r.ReadInt32(),Status=r.ReadInt32()};r.ReadInt64();r.ReadInt64();
                    v.Tick=r.ReadInt64();v.Sequence=r.ReadInt64();v.Replacement=r.ReadInt64();v.Group=r.ReadInt64();v.Attempt=r.ReadInt64();v.Entity=r.ReadInt64();v.Request=r.ReadInt64();v.Epoch=r.ReadInt64();v.Quantum=r.ReadInt64();v.Operation=r.ReadInt64();v.Geometry=r.ReadInt64();v.Profile=r.ReadInt64();v.Total=r.ReadInt64();v.Candidate=r.ReadInt64();v.Occupancy=r.ReadInt64();v.Direct=r.ReadInt64();v.ProposedTotal=r.ReadInt64();v.PendingSequence=r.ReadInt64();v.CurrentSequence=r.ReadInt64();v.ActiveSequence=r.ReadInt64();v.OracleGroup=r.ReadInt64();
                    v.Members=r.ReadInt32();v.Member=r.ReadInt32();v.Page=r.ReadInt32();v.Raw=r.ReadInt32();v.PreparationPage=r.ReadInt32();v.Assignments=r.ReadInt32();v.PreparedKeys=r.ReadInt32();v.HistoricalGate=r.ReadInt32();v.HistoricalCursor=r.ReadInt32();v.CurrentPhase=r.ReadInt32();v.PendingCount=r.ReadInt32();v.RegisteredCount=r.ReadInt32();v.InstalledCount=r.ReadInt32();v.Grant=r.ReadInt32();v.Reason=r.ReadInt32();v.Remaining=r.ReadInt32();v.PreparationMember=r.ReadInt32();result[i]=v;ms.Position=start+slot;
                }Assert.AreEqual(ms.Length,ms.Position);return result;
            }}
        }
        private static void WithOriginalBuffer(int capacity,Action<OriginalGroupBuffer> action)
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var b=OriginalGroupBuffer.Create(capacity,scope.OwnedReservation());scope.Attach(b);scope.Commit(b,_=>{});
            try{action(b);}finally{b.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void OriginalEightFinalReceiptsPendingCurrentDescendantsAndNextBarrierStop()
        {
            WithOriginalBuffer(128,b=>{
                for(int i=0;i<8;i++){
                    long seq=OriginalSequence(i);b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=seq,Members=50});
                    b.Stage(new OriginalGroupRecord{Kind=OriginalGroupEvent.Recipient,Sequence=seq,Entity=100+i,Group=10+i,PendingSequence=seq,CurrentSequence=0,Quantum=7});
                    b.CompleteStaged(seq,true,(int)PlayableCommandStatus.Applied);
                    b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Receipt,Sequence=seq,Committed=1,Status=(int)PlayableCommandStatus.Applied});
                    b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Birth,Sequence=seq,Group=10+i,Attempt=20+i,OracleGroup=10+i,Committed=1});
                    b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Service,Sequence=seq,Group=10+i,Attempt=20+i,Raw=12,Member=3,HistoricalGate=2,HistoricalCursor=41,Total=256,ProposedTotal=512,Committed=0,Status=(int)NavArrivalStatus.Ready,Reason=3,Quantum=8,Grant=256});
                }
                b.Arm();Assert.False(b.Barrier(8,40));Assert.False(b.ShouldStop,"refused Ready proposal is not committed and rebind/birth is not decisive");
                b.Stage(new OriginalGroupRecord{Kind=OriginalGroupEvent.Recipient,Sequence=(1L<<61)+200,Entity=100,Group=99,PendingSequence=(1L<<61)+200,Quantum=8});b.CompleteStaged((1L<<61)+200,true,(int)PlayableCommandStatus.Applied,22,9);
                Assert.False(b.Barrier(8,40));Assert.False(b.Barrier(9,41));Assert.True(b.Barrier(10,42));Assert.False(b.Barrier(11,43));Assert.True(b.ShouldStop);Assert.Zero(b.Dropped);
                var rows=ReadOriginal(b);Assert.AreEqual(8,rows.Count(r=>r.Kind==OriginalGroupEvent.Receipt&&r.Committed==1));CollectionAssert.AreEqual(Enumerable.Range(0,8),rows.Where(r=>r.Kind==OriginalGroupEvent.Receipt).Select(r=>r.Root));
                Assert.True(rows.Where(r=>r.Kind==OriginalGroupEvent.Recipient).All(r=>r.PendingSequence==r.Sequence&&r.CurrentSequence==0));Assert.True(rows.Where(r=>r.Kind==OriginalGroupEvent.Service).All(r=>r.Committed==0&&r.Total==256&&r.ProposedTotal==512&&r.Grant==256&&r.HistoricalCursor==41));
                var replacement=rows.Single(r=>r.Kind==OriginalGroupEvent.Replacement);Assert.AreEqual(0,replacement.Root);Assert.AreEqual((1L<<61)+200,replacement.Replacement);Assert.AreEqual(9,replacement.Quantum);Assert.AreEqual(22,replacement.Operation);Assert.AreEqual(8,rows.Count(r=>r.Kind==OriginalGroupEvent.Birth&&r.OracleGroup==r.Group));
                string output=Environment.GetEnvironmentVariable("SPACEWARS_ORIGINAL_COMPONENT_OUTPUT");if(output!=null){Directory.CreateDirectory(output);using(var file=File.Create(Path.Combine(output,"synthetic.bin")))b.Save(file);}
            });
        }
        [Test] public void OriginalRefusalOverflowAndDefaultOptInReleaseExactly()
        {
            Assert.False(OriginalGroupDiagnostics.Active);string previous=Environment.GetEnvironmentVariable("SPACEWARS_ORIGINAL_GROUP_DIAGNOSTIC");try{Environment.SetEnvironmentVariable("SPACEWARS_ORIGINAL_GROUP_DIAGNOSTIC",null);OriginalGroupDiagnostics.StartIfRequested();Assert.False(OriginalGroupDiagnostics.Active);}finally{Environment.SetEnvironmentVariable("SPACEWARS_ORIGINAL_GROUP_DIAGNOSTIC",previous);}
            WithOriginalBuffer(16,b=>{long seq=OriginalSequence(2);b.Stage(new OriginalGroupRecord{Kind=OriginalGroupEvent.Recipient,Sequence=seq,Entity=200});Assert.Zero(b.Count);b.CompleteStaged(seq,false,(int)PlayableCommandStatus.Rejected);b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Receipt,Sequence=seq,Committed=0,Status=(int)PlayableCommandStatus.Rejected,Quantum=5});b.Arm();Assert.False(b.Barrier(5,5));Assert.True(b.Barrier(6,6));Assert.True(ReadOriginal(b).Where(r=>r.Kind==OriginalGroupEvent.Recipient).All(r=>r.Committed==0));});
            WithOriginalBuffer(1,b=>{b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=OriginalSequence(0)});b.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=OriginalSequence(0)});Assert.AreEqual(1,b.Count);Assert.AreEqual(1,b.Dropped);Assert.True(b.ShouldStop);});
        }
        [Test] public void OriginalStopCallbackIsDeliveredOnceAfterBarrierAndOverflowReleases()
        {
            int calls=0;try{
                OriginalGroupDiagnostics.Start(8);OriginalGroupDiagnostics.Receipt(new PlayableCommandReceipt(OriginalSequence(0),1,PlayableCommandStatus.Rejected,"synthetic",0),12,4);OriginalGroupDiagnostics.Arm(()=>calls++);
                OriginalGroupDiagnostics.Barrier(4,1);Assert.Zero(calls);OriginalGroupDiagnostics.Barrier(5,2);OriginalGroupDiagnostics.Barrier(6,3);Assert.AreEqual(1,calls);
            }finally{OriginalGroupDiagnostics.Stop();}
            try{OriginalGroupDiagnostics.Start(1);OriginalGroupDiagnostics.Arm(()=>calls++);OriginalGroupTrace.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=OriginalSequence(1)});OriginalGroupTrace.Append(new OriginalGroupRecord{Kind=OriginalGroupEvent.Accepted,Sequence=OriginalSequence(1)});OriginalGroupDiagnostics.Barrier(1,1);Assert.AreEqual(2,calls);Assert.AreEqual(1,OriginalGroupDiagnostics.Dropped);}finally{OriginalGroupDiagnostics.Stop();}
        }
        [Test] public void OriginalStagedTerminalWaitsForCommitAndNextBarrier()
        {
            WithOriginalBuffer(8,b=>{long seq=OriginalSequence(1);b.Arm();b.Stage(new OriginalGroupRecord{Kind=OriginalGroupEvent.Terminal,Sequence=seq,Entity=99,Quantum=4});Assert.False(b.ShouldStop);Assert.Zero(b.Count);b.CompleteStaged(seq,false,(int)PlayableCommandStatus.Rejected,11,5);Assert.False(b.Barrier(6,6));b.Stage(new OriginalGroupRecord{Kind=OriginalGroupEvent.Terminal,Sequence=seq,Entity=99,Quantum=6});b.CompleteStaged(seq,true,(int)PlayableCommandStatus.Applied,12,7);Assert.False(b.Barrier(7,7));Assert.True(b.Barrier(8,8));var rows=ReadOriginal(b).Where(r=>r.Kind==OriginalGroupEvent.Terminal).ToArray();Assert.AreEqual(2,rows.Length);Assert.AreEqual(0,rows[0].Committed);Assert.AreEqual(1,rows[1].Committed);Assert.AreEqual(12,rows[1].Operation);Assert.AreEqual(7,rows[1].Quantum);});
        }
        [Test] public void OriginalProbeLeavesOwnedBodyAndGrantExact()
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(8),memberCount:4,flatCandidateCount:32)){
                var source=fixture.NewWork();NavigationArrivalWork plain=null,traced=null;
                try{plain=Fork(source,fixture.Budget,20,w=>w.Advance(0,400,new NavEpochQuota(0,0,0,0,0)));OriginalGroupDiagnostics.Start(64);traced=Fork(source,fixture.Budget,21,w=>w.Advance(0,400,new NavEpochQuota(0,0,0,0,0)));OriginalGroupDiagnostics.Service(traced,traced,1,0,1,400,"committed");var snapshot=traced.OriginalDiagnostic(OriginalGroupEvent.Service);snapshot.Sequence=OriginalSequence(0);snapshot.Committed=1;OriginalGroupTrace.Append(snapshot);Assert.AreEqual(1,OriginalGroupTrace.Buffer.Count);var recorded=ReadOriginal(OriginalGroupTrace.Buffer).Single();Assert.AreEqual(traced.CandidateWorkConsumed,recorded.Candidate);Assert.AreEqual(traced.AllocationQuotaRemaining,recorded.Remaining);
                    CollectionAssert.AreEqual(CaptureGraph(fixture.Budget,plain),CaptureGraph(fixture.Budget,traced));Assert.AreEqual(plain.AllocationQuotaRemaining,traced.AllocationQuotaRemaining);Assert.AreEqual(plain.CandidateWorkConsumed,traced.CandidateWorkConsumed);
                }finally{OriginalGroupDiagnostics.Stop();plain?.ReleaseOwner();traced?.ReleaseOwner();source.ReleaseOwner();}
            }
        }
        [Test] public void OriginalActualEightCommandsKeepSaveAndOperationExactOnOff()
        {
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",f).GetValue(runtime);var views=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.OrderBy(v=>(int)v.Owner).ToArray();byte[] baseline=null;long operation=0;
                for(int arm=0;arm<2;arm++){var authority=new PlayableAuthorityTick(config,1);var domain=typeof(PlayableAuthorityTick).GetField("domain",f).GetValue(authority);var type=domain.GetType();var nav=(NavigationSession)type.GetProperty("Navigation",f).GetValue(domain);
                    try{if(arm==1)OriginalGroupDiagnostics.Start(1024);foreach(var view in views)Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(PlayableRuntime.CreateEightPlayerMovingPerformanceCommand(view,OriginalSequence((int)view.Owner))).Status);
                        var bytes=(byte[])type.GetMethod("CaptureWorldBytes",f).Invoke(domain,new object[]{7,"original-probe-control"});long current=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",f).GetValue(nav);
                        if(arm==0){baseline=bytes;operation=current;}else{CollectionAssert.AreEqual(baseline,bytes);Assert.AreEqual(operation,current);var rows=ReadOriginal(OriginalGroupTrace.Buffer);Assert.AreEqual(8,rows.Count(r=>r.Kind==OriginalGroupEvent.Receipt&&r.Committed==1));Assert.AreEqual(400,rows.Count(r=>r.Kind==OriginalGroupEvent.Recipient&&r.PendingSequence==r.Sequence&&r.CurrentSequence==0));Assert.Zero(OriginalGroupDiagnostics.Dropped);TestContext.WriteLine("ORIGINAL_PROBE_ONOFF savebytes="+bytes.Length+" operation="+current+" recipients=400 receipts=8");}
                    }finally{OriginalGroupDiagnostics.Stop();authority.Stop();type.GetMethod("DestroyOwnedNavigation",f).Invoke(domain,null);}
                }
            }
        }
    }
}
