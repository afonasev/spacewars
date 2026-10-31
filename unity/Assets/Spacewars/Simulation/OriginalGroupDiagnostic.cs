using System;
using System.IO;
using System.Threading;
namespace Spacewars.Simulation
{
    internal enum OriginalGroupEvent { Accepted=1,Receipt,Recipient,Birth,Service,Publication,ChildRegistered,Endpoint,Installed,Terminal,Replacement,Barrier }
    // Diagnostic DTO only. No instance field or retained edge in authoritative state.
    internal struct OriginalGroupRecord
    {
        internal OriginalGroupEvent Kind;
        internal int Root,Committed,Status,Members,Member,Page,Raw,PreparationPage,Assignments,PreparedKeys,HistoricalGate,HistoricalCursor,CurrentPhase,PendingCount,RegisteredCount,InstalledCount,Grant,Reason,Remaining,PreparationMember;
        internal long Tick,Sequence,Replacement,Group,Attempt,Entity,Request,Epoch,Quantum,Operation,Geometry,Profile,Total,Candidate,Occupancy,Direct,ProposedTotal,PendingSequence,CurrentSequence,ActiveSequence,OracleGroup;
        internal string Input,Source,Oracle,Preparing;
    }
    // Append-only fixed admitted bytes. Overflow fails the diagnostic explicitly;
    // no overwrites, authority pins, new digests, or output I/O in a hot callback.
    internal sealed class OriginalGroupBuffer : NavOwnedStorage
    {
        internal const int SlotBytes=4096,StageCapacity=64,MapCapacity=1024;
        private readonly byte[] bytes,staging;
        private readonly int[] entities,roots;
        private readonly object gate=new object();
        private readonly int capacity;
        private int count,dropped,staged,delivered;
        private long stagedSequence,lastBarrier,stopAfter=-1;
        private bool armed,stop;
        private byte[] target;private int cursor,end;
        internal static long Layout(int n)=>checked(NavStorageLayout.ObjectBytesBound(typeof(OriginalGroupBuffer))+4*32L+(n+StageCapacity)*(long)SlotBytes+MapCapacity*8L+32);
        private OriginalGroupBuffer(int n):base(Layout(n)){capacity=n;bytes=new byte[n*SlotBytes];staging=new byte[StageCapacity*SlotBytes];entities=new int[MapCapacity];roots=new int[MapCapacity];Retain();}
        internal static OriginalGroupBuffer Create(int n,INavStorageReservation r){if(n<1||n>16384)throw new ArgumentOutOfRangeException(nameof(n));using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(n));return a.Register(new OriginalGroupBuffer(n));}}
        internal int Count=>Volatile.Read(ref count);internal int Dropped=>Volatile.Read(ref dropped);internal bool ShouldStop=>Volatile.Read(ref stop);
        internal static int RootOf(long sequence){if(sequence<(1L<<62)+1)return -1;long delta=sequence-(1L<<62)-1;if(delta<0||(delta&((1L<<50)-1))!=0)return -1;long root=delta>>50;return root<8?(int)root:-1;}
        private int Find(int entity,bool insert){int at=unchecked(entity*265443576)&(MapCapacity-1);for(int n=0;n<MapCapacity;n++,at=(at+1)&(MapCapacity-1)){if(entities[at]==entity)return at;if(entities[at]==0){if(insert)entities[at]=entity;return insert?at:-1;}}Overflow();return -1;}
        private void Overflow(){dropped++;stop=true;}
        internal void Arm(){lock(gate){armed=true;if(stopAfter>=0&&lastBarrier>=stopAfter)stop=true;}}
        internal void Stage(OriginalGroupRecord r)
        {
            lock(gate){int root=RootOf(r.Sequence);if(root<0){int at=Find((int)r.Entity,false);if(at<0||roots[at]==0)return;root=roots[at]-1;r.Replacement=r.Sequence;r.Kind=OriginalGroupEvent.Replacement;}
                if(staged!=0&&stagedSequence!=r.Sequence){Overflow();return;}if(staged>=StageCapacity){Overflow();return;}
                stagedSequence=r.Sequence;r.Root=root;r.Committed=0;Encode(staging,staged++*SlotBytes,r);
            }
        }
        internal void CompleteStaged(long sequence,bool committed,int status,long operation=0,long quantum=0)
        {
            lock(gate){if(staged==0||sequence!=stagedSequence)return;
                for(int n=0;n<staged;n++){int from=n*SlotBytes;var kind=(OriginalGroupEvent)ReadInt(staging,from);int root=ReadInt(staging,from+4);long entity=ReadLong(staging,from+72); // Entity follows five longs after the 32-byte header.
                    if(committed&&entity>0){int at=Find((int)entity,true);if(at>=0)roots[at]=kind==OriginalGroupEvent.Replacement||kind==OriginalGroupEvent.Terminal?0:root+1;}
                    if(count>=capacity){Overflow();continue;}int to=count++*SlotBytes;Buffer.BlockCopy(staging,from,bytes,to,SlotBytes);PutInt(bytes,to+8,committed?1:0);PutInt(bytes,to+12,status);if(operation!=0)PutLong(bytes,to+104,operation);if(quantum!=0)PutLong(bytes,to+96,quantum);
                    if(committed&&(kind==OriginalGroupEvent.Replacement||kind==OriginalGroupEvent.Terminal))Decisive(quantum!=0?quantum:ReadLong(staging,from+96));
                }staged=0;stagedSequence=0;
            }
        }
        internal void Append(OriginalGroupRecord r)
        {lock(gate){int root=RootOf(r.Sequence);if(root<0)return;r.Root=root;AppendLocked(r);}}
        private void AppendLocked(OriginalGroupRecord r)
        {
            if(count>=capacity){Overflow();return;}Encode(bytes,count++*SlotBytes,r);
            if(r.Kind==OriginalGroupEvent.Receipt&&r.Committed==0||r.Committed!=0&&(r.Kind==OriginalGroupEvent.Terminal||r.Kind==OriginalGroupEvent.Replacement||r.Kind==OriginalGroupEvent.Publication||r.Kind==OriginalGroupEvent.Installed||r.Kind==OriginalGroupEvent.Service&&r.Status==(int)NavArrivalStatus.Ready))Decisive(r.Quantum);
        }
        private void Decisive(long quantum){if(stopAfter<0){long value=Math.Max(lastBarrier,quantum);if(value==long.MaxValue)Overflow();else stopAfter=value+1;}}
        internal bool Barrier(long serial,long tick)
        {
            lock(gate){if(serial>lastBarrier)lastBarrier=serial;if(armed&&stopAfter>=0&&lastBarrier>=stopAfter)stop=true;
                if(stop&&Interlocked.Exchange(ref delivered,1)==0){var r=new OriginalGroupRecord{Kind=OriginalGroupEvent.Barrier,Root=-1,Quantum=serial,Tick=tick,Committed=1};if(count<capacity)Encode(bytes,count++*SlotBytes,r);return true;}return false;}
        }
        private static int ReadInt(byte[] b,int at)=>b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;
        private static long ReadLong(byte[] b,int at)=>unchecked((long)((uint)ReadInt(b,at)|(ulong)(uint)ReadInt(b,at+4)<<32));
        private static void PutLong(byte[] b,int at,long value){PutInt(b,at,(int)value);PutInt(b,at+4,(int)(value>>32));}
        private static void PutInt(byte[] b,int at,int value){for(int n=0;n<4;n++)b[at+n]=(byte)(value>>(8*n));}
        private void I(int v){if(cursor>end-4){Overflow();return;}PutInt(target,cursor,v);cursor+=4;}
        private void L(long v){I((int)v);I((int)(v>>32));}
        private void T(string s){int n=s?.Length??0;if(n>256){Overflow();n=256;}I(n);for(int i=0;i<n;i++)I(s[i]);}
        private void Encode(byte[] output,int at,OriginalGroupRecord r)
        {
            Array.Clear(output,at,SlotBytes);target=output;cursor=at;end=at+SlotBytes;
            I((int)r.Kind);I(r.Root);I(r.Committed);I(r.Status);L(System.Diagnostics.Stopwatch.GetTimestamp());L(Thread.CurrentThread.ManagedThreadId);
            L(r.Tick);L(r.Sequence);L(r.Replacement);L(r.Group);L(r.Attempt);L(r.Entity);L(r.Request);L(r.Epoch);L(r.Quantum);L(r.Operation);L(r.Geometry);L(r.Profile);L(r.Total);L(r.Candidate);L(r.Occupancy);L(r.Direct);L(r.ProposedTotal);L(r.PendingSequence);L(r.CurrentSequence);L(r.ActiveSequence);L(r.OracleGroup);
            I(r.Members);I(r.Member);I(r.Page);I(r.Raw);I(r.PreparationPage);I(r.Assignments);I(r.PreparedKeys);I(r.HistoricalGate);I(r.HistoricalCursor);I(r.CurrentPhase);I(r.PendingCount);I(r.RegisteredCount);I(r.InstalledCount);I(r.Grant);I(r.Reason);I(r.Remaining);I(r.PreparationMember);
            T(r.Input);T(r.Source);T(r.Oracle);T(r.Preparing);target=null;
        }
        internal void Save(Stream stream){lock(gate){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){w.Write(0x3144474f);w.Write(1);w.Write(SlotBytes);w.Write(count);w.Write(dropped);w.Write(lastBarrier);w.Write(stopAfter);w.Write(stop);w.Write(System.Diagnostics.Stopwatch.Frequency);w.Write(bytes,0,count*SlotBytes);}}}
        protected override void ReleaseEdges(){}
    }
    internal static class OriginalGroupTrace
    {
        internal static OriginalGroupBuffer Buffer;
        internal static void Append(OriginalGroupRecord r)=>Buffer?.Append(r);
    }
    public sealed partial class NavigationArrivalWork
    {
        internal OriginalGroupRecord OriginalDiagnostic(OriginalGroupEvent kind)
        {
            var first=input?.Members[0];return new OriginalGroupRecord{Kind=kind,Sequence=first?.CommandSequence??0,Group=first?.GroupId??0,Members=members?.Length??0,Input=input?.StableKey,Source=input?.Source.StableKey,Oracle=historicalOracle?.input?.StableKey,Preparing=preparingClearanceKey,Epoch=lastEpoch,Total=AllocationWorkConsumed,Candidate=CandidateWorkConsumed,Occupancy=OccupancyWorkConsumed,Direct=DirectWorkConsumed,Member=memberIndex,Page=pageIndex,Raw=preparationCursor,PreparationPage=preparationPageIndex,Assignments=assignments?.Count??0,PreparedKeys=preparedClearances?.Count??0,HistoricalGate=historicalGate,HistoricalCursor=historicalCursor,OracleGroup=historicalOracle?.input?.Members[0].GroupId??0,CurrentPhase=(int)phase,Status=(int)Status,Remaining=allocationRemaining,PreparationMember=preparationMember};
        }
    }
}
