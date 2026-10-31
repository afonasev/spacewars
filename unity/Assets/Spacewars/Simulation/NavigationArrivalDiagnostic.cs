using System;
using System.IO;
using System.Threading;
namespace Spacewars.Simulation
{
    internal enum NavCandidateCharge { PrepareValidity,PlacementStart,RequestCreate,Completion,HistoricalHeader,HistoricalMember,HistoricalSource,HistoricalGeometry,Page0Control,Page0RegionCompare,Page0PredicateCompare,HistoricalEndpoint,HistoricalRoute,Count }
    // Opt-in diagnostic owner, never an edge/field of authoritative Work.
    // All text is copied into fixed admitted bytes; no retained facade/string pins.
    internal sealed class NavArrivalDiagnosticBuffer : NavOwnedStorage
    {
        internal const int SlotBytes=4096;
        private readonly byte[] bytes;
        private readonly long[] reasons,seenRequests;
        private readonly object gate=new object();
        private readonly int capacity;
        private int count,dropped,position,end,activeThread,stopDelivered;
        private long service,beginSerial,relevantSerial=-1,copiedTotal,copiedCandidate,copiedOccupancy,copiedDirect;
        private bool armed,stop;
        internal static long Layout(int capacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavArrivalDiagnosticBuffer))+32L+capacity*(long)SlotBytes+32+8L*(int)NavCandidateCharge.Count+32+8L*8192+32);
        private NavArrivalDiagnosticBuffer(int capacity):base(Layout(capacity)){this.capacity=capacity;bytes=new byte[capacity*SlotBytes];reasons=new long[(int)NavCandidateCharge.Count];seenRequests=new long[8192];Retain();}
        internal static NavArrivalDiagnosticBuffer Create(int capacity,INavStorageReservation reservation)
        {if(capacity<1||capacity>16384)throw new ArgumentOutOfRangeException();using(var a=new NavStorageAllocation(reservation.TryReserve)){a.Reserve(Layout(capacity));return a.Register(new NavArrivalDiagnosticBuffer(capacity));}}
        internal int Count=>Volatile.Read(ref count);
        internal int Dropped=>Volatile.Read(ref dropped);
        internal bool ShouldStop=>Volatile.Read(ref stop)||Dropped!=0;
        internal void Arm(){lock(gate)armed=true;}
        internal void Charge(NavCandidateCharge reason){if(Thread.CurrentThread.ManagedThreadId==Volatile.Read(ref activeThread))reasons[(int)reason]++;}
        internal void Begin(NavigationArrivalWork work,long attempt,long epoch,long quantum,int grant)
        {lock(gate){if(activeThread!=0){dropped++;stop=true;return;}activeThread=Thread.CurrentThread.ManagedThreadId;Array.Clear(reasons,0,reasons.Length);copiedTotal=copiedCandidate=copiedOccupancy=copiedDirect=0;service++;beginSerial=count;Write(1,work,attempt,epoch,quantum,grant,"begin");}}
        internal void Proposed(NavigationArrivalWork work,long attempt,long epoch,long quantum,int grant)
        {lock(gate)Write(2,work,attempt,epoch,quantum,grant,"proposed");}
        internal void Finish(NavigationArrivalWork work,long attempt,long epoch,long quantum,int grant,string outcome)
        {bool deliver;lock(gate){Write(3,work,attempt,epoch,quantum,grant,outcome);activeThread=0;if(relevantSerial>=0&&beginSerial>relevantSerial)stop=true;deliver=ShouldStop&&Interlocked.Exchange(ref stopDelivered,1)==0;}if(deliver)NavArrivalDiagnostic.RequestStop?.Invoke();}
        internal void Key(NavigationArrivalWork work,string key,long entity)
        {lock(gate)Write(10,work,entity,0,0,0,key);}
        internal void Birth(NavigationArrivalWork work,long attempt)
        {lock(gate){Write(11,work,attempt,0,0,0,"birth-committed");var m=work.PhysicalInput.Members[0];if(m.GroupId<0||m.GroupId>=seenRequests.Length){dropped++;stop=true;return;}if(armed&&seenRequests[m.GroupId]!=0&&seenRequests[m.GroupId]!=m.RequestSequence&&relevantSerial<0)relevantSerial=count-1;seenRequests[m.GroupId]=m.RequestSequence;}}
        internal void Copy(long total,long candidate,long occupancy,long direct){if(Thread.CurrentThread.ManagedThreadId!=Volatile.Read(ref activeThread))return;copiedTotal+=total;copiedCandidate+=candidate;copiedOccupancy+=occupancy;copiedDirect+=direct;}
        internal void Transition(string cause,long oldGeometry,long newGeometry,long operation,long requests,int allocating,long oldRequests=0,long profiles=0)
        {lock(gate){Write(20,null,oldGeometry,newGeometry,operation,allocating,cause,requests,oldRequests,profiles);if(armed&&allocating>0&&relevantSerial<0)relevantSerial=count-1;}}
        internal void Mark(string reason,NavigationArrivalWork work){lock(gate)Write(21,work,0,0,0,0,reason);}
        private void Write(int kind,NavigationArrivalWork work,long attempt,long epoch,long quantum,int grant,string label,long extra=0,long oldExtra=0,long profiles=0)
        {
            if(count>=capacity){dropped++;stop=true;return;}position=count*SlotBytes;end=position+SlotBytes;int start=position;I(0);I(kind);L(System.Diagnostics.Stopwatch.GetTimestamp());I(Thread.CurrentThread.ManagedThreadId);L(service);L(attempt);L(epoch);L(quantum);I(grant);L(extra);L(oldExtra);L(profiles);T(label);work?.WriteDiagnostic(this);if(work==null)I(0);
            L(copiedTotal);L(copiedCandidate);L(copiedOccupancy);L(copiedDirect);for(int n=0;n<reasons.Length;n++)L(reasons[n]);int length=position-start;for(int n=0;n<4;n++)bytes[start+n]=(byte)(length>>(8*n));count++;
        }
        internal void I(int v){if(position>end-4){dropped++;stop=true;return;}for(int n=0;n<4;n++)bytes[position++]=(byte)(v>>(8*n));}
        internal void L(long v){if(position>end-8){dropped++;stop=true;return;}for(int n=0;n<8;n++)bytes[position++]=(byte)(v>>(8*n));}
        internal void T(string text){int n=text?.Length??0;if(n>256){dropped++;stop=true;n=256;}I(n);for(int i=0;i<n;i++){I(text[i]);}}
        internal void WriteTo(Stream output)
        {lock(gate){using(var w=new BinaryWriter(output,System.Text.Encoding.UTF8,true)){w.Write(0x3144414e);w.Write(2);w.Write(capacity);w.Write(count);w.Write(dropped);w.Write(relevantSerial);w.Write(stop);w.Write((int)NavCandidateCharge.Count);for(int i=0;i<count;i++){int start=i*SlotBytes;int n=bytes[start]|bytes[start+1]<<8|bytes[start+2]<<16|bytes[start+3]<<24;w.Write(bytes,start,n);}}}}
        protected override void ReleaseEdges(){}
    }
    internal static class NavArrivalDiagnostic
    {
        internal static NavArrivalDiagnosticBuffer Buffer;
        internal static Action RequestStop;
        internal static void Charge(NavCandidateCharge reason)=>Buffer?.Charge(reason);
        internal static void Mark(string reason,NavigationArrivalWork work)=>Buffer?.Mark(reason,work);
    }
    public sealed partial class NavigationArrivalWork
    {
        internal void WriteDiagnostic(NavArrivalDiagnosticBuffer w)
        {
            w.I(1);w.T(input?.StableKey);w.T(input?.Source.StableKey);w.T(input?.Occupancy.StableKey);w.T(input?.Provider.Binding);w.T(input?.Profile.ExactProfileBinding);w.T(preparingClearanceKey);
            var m=input?.Members[0];w.L(m?.GroupId??0);w.L(m?.CommandSequence??0);w.L(m?.RequestSequence??0);w.I(input?.Source.Candidates.Count??0);w.I(members?.Length??0);
            w.L(lastEpoch);w.I(allocationRemaining);w.L(AllocationWorkConsumed);w.L(CandidateWorkConsumed);w.L(OccupancyWorkConsumed);w.L(DirectWorkConsumed);w.I((int)Status);w.I((int)phase);w.I(memberIndex);w.I(pageIndex);w.I(candidateCursor);w.I(occupantCursor);w.I(preparationCursor);w.I(preparationPageIndex);w.I(preparedClearances?.Count??0);w.I(assignments?.Count??0);w.I(spatialBuilt);w.I(spatialPhase);w.I(spatialX);w.I(spatialZ);w.I(spatialNext);w.I(spatialMinZ);w.I(spatialMaxX);w.I(spatialMaxZ);w.I(spatialFallback?1:0);w.L(BitConverter.DoubleToInt64Bits(spatialRadius));w.I(LastAdvanceRetentionBlocked?1:0);
            w.I(historicalOracle==null?0:1);w.I(historicalGate);w.I(historicalCursor);w.I(historicalMember);w.I(historicalLeg);w.I(historicalState?.SourceOld??0);w.I(historicalState?.PreparationMember??0);w.I(historicalState?.RegionCursor??0);w.I(historicalState?.RemovalStage??0);w.I(historicalState?.OccupancyStage??0);
            // One leaf oracle only; AttachHistorical flattens and validation forbids nesting.
            if(historicalOracle!=null){var o=historicalOracle;w.T(o.input?.StableKey);w.I((int)o.Status);w.I((int)o.phase);w.I(o.memberIndex);w.I(o.pageIndex);w.I(o.preparationCursor);w.I(o.preparationPageIndex);w.T(o.preparingClearanceKey);}
        }
    }
}
