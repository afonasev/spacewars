using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Live count-derived ownership only. BODY already records ordered barrier
    // payload roles; this chain has no spare capacity or empty-root history.
    internal sealed class NavigationBarrierReceipt:NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationBarrierReceipt));
        internal NavigationHostAnswer Answer;internal NavigationAnswer LegacyAnswer;internal NavigationAnswer Value=>Answer?.Answer??LegacyAnswer;internal NavigationBarrierReceipt Next;
        internal override bool IsImmutableCaptureRoot=>false;
        private bool released;
        private NavigationBarrierReceipt():base(Layout){Retain();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        internal static NavigationBarrierReceipt CreateOwned(NavigationHostAnswer answer,INavStorageReservation r)
        {
            NavStorageAdmission.RequireOwned(r,answer);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var receipt=a.Register(new NavigationBarrierReceipt());try{receipt.Answer=NavPhysicalMacro.Acquire(receipt,answer);return receipt;}catch{receipt.ReleaseStorage();throw;}}
        }
        internal static NavigationBarrierReceipt CreateCompatibilityOwned(NavigationAnswer answer,INavStorageReservation r)
        {
            if(answer.StorageOwner!=null)return CreateOwned(answer.StorageOwner,r);
            using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var receipt=a.Register(new NavigationBarrierReceipt());receipt.LegacyAnswer=answer;return receipt;}
        }
        protected override void ReleaseEdges()
        {
            Answer?.Release();Answer=null;LegacyAnswer=null;var next=Next;Next=null;
            while(next!=null){
                if(next.References!=1){next.Release();break;}
                var tail=next.Next;next.Next=null;next.Release();next=tail;
            }
        }
    }
    // Transient paid publication root. It owns the mailbox retirement slots
    // until they can be released outside the gate, without live scratch.
    internal sealed class NavigationPreparedBarrierHandoff:NavOwnedStorage,IDisposable
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedBarrierHandoff))+48;
        internal readonly NavOwnedStorage[] Retired;internal NavigationBarrierReceipt Receipt;internal NavigationCommandSlots<int,NavigationRequest> Probes;internal NavigationLiveProbeAnswers ProbeQueue;internal bool Published;private bool released;
        private NavigationPreparedBarrierHandoff():base(Layout){Retired=new NavOwnedStorage[2];Retain();}
        internal static NavigationPreparedBarrierHandoff CreateOwned(NavigationAnswer answer,bool bootstrap,INavStorageReservation r)
        {using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var value=a.Register(new NavigationPreparedBarrierHandoff());try{value.Receipt=NavigationBarrierReceipt.CreateCompatibilityOwned(answer,r);if(bootstrap){value.Probes=NavigationCommandSlots<int,NavigationRequest>.CreateEmptyOwned(r);value.Probes.Seal();value.ProbeQueue=NavigationLiveProbeAnswers.CreateOwned(0,r);}return value;}catch{value.Dispose();throw;}}}
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){Receipt?.Release();Probes?.Release();ProbeQueue?.Release();foreach(var edge in Retired)edge?.Release();}
    }
    public sealed partial class NavigationSession
    {
        // Zero-backing view of the paid count-derived receipt container. No
        // duplicate List/array capacity exists beside the authoritative chain.
        internal readonly struct NavigationBarrierAnswers:System.Collections.Generic.IReadOnlyList<NavigationAnswer>,System.Collections.IList
        {
            private readonly NavigationSession session;
            internal NavigationBarrierAnswers(NavigationSession session){this.session=session;}
            public int Count{get{int n=0;for(var row=session.barrierPayloads;row!=null;row=row.Next)n++;return n;}}
            public NavigationAnswer this[int index]{get{if(index<0)throw new ArgumentOutOfRangeException(nameof(index));for(var row=session.barrierPayloads;row!=null;row=row.Next)if(index--==0)return row.Value;throw new ArgumentOutOfRangeException(nameof(index));}}
            internal bool Contains(NavigationAnswer answer){for(var row=session.barrierPayloads;row!=null;row=row.Next)if(ReferenceEquals(row.Value,answer))return true;return false;}
            internal bool ContainsRequest(NavigationRequest request){for(var row=session.barrierPayloads;row!=null;row=row.Next)if(ReferenceEquals(row.Value.Request,request))return true;return false;}
            internal void Reverse(){NavigationBarrierReceipt previous=null,current=session.barrierPayloads;while(current!=null){var next=current.Next;current.Next=previous;previous=current;current=next;}session.barrierPayloads=previous;session.barrierPayloadRevision++;}
            internal void Sort(Comparison<NavigationAnswer> compare){
                NavigationBarrierReceipt sorted=null,current=session.barrierPayloads;
                while(current!=null){var next=current.Next;NavigationBarrierReceipt previous=null,at=sorted;while(at!=null&&compare(at.Value,current.Value)<=0){previous=at;at=at.Next;}current.Next=at;if(previous==null)sorted=current;else previous.Next=current;current=next;}
                session.barrierPayloads=sorted;session.barrierPayloadRevision++;
            }
            public struct Enumerator:System.Collections.Generic.IEnumerator<NavigationAnswer>
            {
                private NavigationBarrierReceipt next,current;internal Enumerator(NavigationBarrierReceipt head){next=head;current=null;}
                public NavigationAnswer Current=>current.Value;object System.Collections.IEnumerator.Current=>Current;
                public bool MoveNext(){current=next;if(current==null)return false;next=current.Next;return true;}public void Dispose(){}public void Reset()=>throw new NotSupportedException();
            }
            public Enumerator GetEnumerator()=>new Enumerator(session.barrierPayloads);
            System.Collections.Generic.IEnumerator<NavigationAnswer> System.Collections.Generic.IEnumerable<NavigationAnswer>.GetEnumerator()=>GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
            bool System.Collections.IList.IsFixedSize=>true;bool System.Collections.IList.IsReadOnly=>true;bool System.Collections.ICollection.IsSynchronized=>false;object System.Collections.ICollection.SyncRoot=>session.transportGate;
            object System.Collections.IList.this[int index]{get=>this[index];set=>throw new NotSupportedException();}
            int System.Collections.IList.Add(object value)=>throw new NotSupportedException();void System.Collections.IList.Clear()=>throw new NotSupportedException();bool System.Collections.IList.Contains(object value)=>value is NavigationAnswer answer&&Contains(answer);
            int System.Collections.IList.IndexOf(object value){int i=0;foreach(var answer in this){if(ReferenceEquals(answer,value))return i;i++;}return -1;}
            void System.Collections.IList.Insert(int index,object value)=>throw new NotSupportedException();void System.Collections.IList.Remove(object value)=>throw new NotSupportedException();void System.Collections.IList.RemoveAt(int index)=>throw new NotSupportedException();
            void System.Collections.ICollection.CopyTo(Array array,int index){foreach(var answer in this)array.SetValue(answer,index++);}
        }
        private NavigationBarrierReceipt legacyProbePayloads,retiredLegacyProbePayloads;
        private NavigationBarrierReceipt barrierPayloads;private long barrierPayloadRevision;
        private bool IsBarrierCandidate(NavigationAnswer answer)
        {
            var r=answer.Request;var active=r.Entity<0?probes:pending;
            return active.TryGetValue(r.Entity,out var expected)&&ReferenceEquals(expected,r)&&!barrierAnswers.ContainsRequest(r);
        }
        private void AppendBarrierReceiptNoThrow(NavigationBarrierReceipt receipt)
        {if(barrierPayloads==null)barrierPayloads=receipt;else{var tail=barrierPayloads;while(tail.Next!=null)tail=tail.Next;tail.Next=receipt;}barrierPayloadRevision++;}
        private NavigationBarrierReceipt TakeBarrierAnswerNoThrow(int index)
        {
            var answer=barrierAnswers[index];barrierPayloadRevision++;
            return ExtractPayloadReceiptNoThrow(ref barrierPayloads,answer);
        }
        private static NavigationBarrierReceipt ExtractPayloadReceiptNoThrow(ref NavigationBarrierReceipt head,NavigationAnswer answer)
        {
            NavigationBarrierReceipt previous=null;
            for(var row=head;row!=null;row=row.Next){
                if(ReferenceEquals(row.Value,answer)){
                    if(previous==null)head=row.Next;else previous.Next=row.Next;
                    row.Next=null;return row;
                }
                previous=row;
            }
            return null; // Explicit ownerless compatibility role.
        }
        private void AppendLegacyProbeReceiptNoThrow(NavigationBarrierReceipt receipt)
        {if(receipt==null)return;receipt.Next=legacyProbePayloads;legacyProbePayloads=receipt;}
        private void ClearLegacyProbePayloads(){var root=legacyProbePayloads;legacyProbePayloads=null;root?.Release();}
        private NavigationBarrierReceipt TakeAllBarrierAnswersNoThrow()
        {var rows=barrierPayloads;barrierPayloads=null;if(rows!=null)barrierPayloadRevision++;return rows;}
        private void ClearBarrierAnswers(){TakeAllBarrierAnswersNoThrow()?.Release();}
        private bool CanBootstrapBarrierCatalog()
            =>ownedProbes==null&&liveProbeAnswers==null&&probes.Count==0&&NavigationCommandLayout.Length(probes)==1&&probeAnswers.Count==0&&legacyProbePayloads==null&&retiredLegacyProbePayloads==null&&liveArrivalTables!=null;
        internal long LastOwnedBarrierHandoffAllocatedBytes{get;private set;}=-1;
        private bool DrainBarrierMailbox(out bool drained)
        {
            drained=false;
            while(true){
                NavigationAnswer answer;long revision,barrierRevision,operation,publication;int count;bool bootstrap;System.Collections.Generic.IDictionary<int,NavigationRequest> sourceProbes;NavigationHostAnswer pin;
                using(EnterTransport()){
                    if(!Answers.TryPeek(out answer))return true;
                    if(!IsBarrierCandidate(answer)){Answers.TryDequeue(out _);ReleaseCorridorAnswer(answer);RejectedResults++;drained=true;continue;}
                    bootstrap=CanBootstrapBarrierCatalog();sourceProbes=probes;count=Answers.Count;revision=Answers.Revision;barrierRevision=barrierPayloadRevision;operation=authorityOperationIdentity;publication=admissionPublicationIdentity;pin=answer.StorageOwner;pin?.Retain();
                }
                try{
                    if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
                    using(scope){NavigationPreparedBarrierHandoff c=null;
                        try{
                            c=NavigationPreparedBarrierHandoff.CreateOwned(answer,bootstrap,scope.OwnedReservation());scope.Attach(c);
                            using(EnterTransport())scope.Publish(c,ignored=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Answers.EnterPreparedWrite()){
                                if(bootstrap!=CanBootstrapBarrierCatalog()||bootstrap&&!ReferenceEquals(probes,sourceProbes)||scope.IsCancelled||operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||revision!=Answers.Revision||barrierRevision!=barrierPayloadRevision||barrierPayloadRevision==long.MaxValue||!Answers.TryPeek(out var head)||!ReferenceEquals(head,answer)||!IsBarrierCandidate(answer))throw new InvalidOperationException("Barrier handoff lost its source.");
                                mailbox.ValidateTakeOne(revision,count,c.Retired);
                                if(bootstrap){ownedProbes=c.Probes;probes=c.Probes;c.Probes=null;liveProbeAnswers=c.ProbeQueue;c.ProbeQueue=null;}AppendBarrierReceiptNoThrow(c.Receipt);c.Receipt=null;mailbox.TakeOneNoThrow(c.Retired);c.Published=true;LastOwnedBarrierHandoffAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                            }});
                            scope.ResolveCommitted();drained=true;
                        }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}
                        finally{c?.Dispose();}
                    }
                }finally{pin?.Release();}
            }
        }
        private void RestoreBarrierPayload(NavigationAnswer answer,bool barrier=true)
        {
            if(answer.StorageOwner==null&&!barrier)return;
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Barrier hydration admission refused.");
            using(scope){NavigationBarrierReceipt receipt=null;bool installed=false;try{receipt=NavigationBarrierReceipt.CreateCompatibilityOwned(answer,scope.OwnedReservation());scope.Attach(receipt);scope.Commit(receipt,_=>{if(barrier)AppendBarrierReceiptNoThrow(receipt);else AppendLegacyProbeReceiptNoThrow(receipt);installed=true;});}finally{if(!installed)receipt?.ReleaseStorage();}}
        }
    }
}
