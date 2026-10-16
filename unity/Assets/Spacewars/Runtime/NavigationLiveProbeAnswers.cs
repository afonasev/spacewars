using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationProbeReceipt : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationProbeReceipt));
        internal NavigationHostAnswer Answer;internal NavigationHostRequest RetiredTransport;internal NavOwnedStorage RetiredTracking;
        internal NavigationLiveProbeAnswers RetiredReset;internal NavigationProbeReceipt RetiredNext;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationProbeReceipt():base(Layout){Retain();}
        internal static NavigationProbeReceipt CreateOwned(NavigationHostAnswer answer,INavStorageReservation r)
        {if(answer==null||!answer.IsImmutableCaptureRoot)throw new ArgumentException("Missing producer receipt payload.");NavStorageAdmission.RequireOwned(r,answer);using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);var value=a.Register(new NavigationProbeReceipt());value.Answer=NavPhysicalMacro.Acquire(value,answer);return value;}}
        protected override void ReleaseEdges(){RetiredTracking?.Release();RetiredTracking=null;Answer?.Release();Answer=null;RetiredTransport?.Release();RetiredTransport=null;RetiredReset?.Release();RetiredReset=null;}
    }
    // Producer receipts have no gameplay cap. A paid compact FIFO keeps its
    // spare capacity after consumption; growth is admitted before publication.
    // Occupied order plus capacity reproduces the concrete cold backing.
    internal sealed class NavigationLiveProbeAnswers : NavOwnedStorage,IReadOnlyCollection<NavigationAnswer>
    {
        private static readonly long Header=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveProbeAnswers));
        private static long Layout(int capacity)=>checked(Header+32L+8L*capacity);
        private readonly NavigationProbeReceipt[] owners;
        public int Count{get;private set;}
        internal int Revision{get;private set;}
        internal int Capacity=>owners.Length;
        internal NavigationLiveProbeAnswers EmptyReset,RetiredNext;
        private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationLiveProbeAnswers(int capacity):base(Layout(capacity)){owners=new NavigationProbeReceipt[capacity];Retain();}
        internal static bool ValidCapacity(int capacity)=>capacity>=0&&Layout(capacity)<=LayeredNavigationScheduler.MaxRetainedBytes;
        internal static NavigationLiveProbeAnswers CreateOwned(int capacity,INavStorageReservation r)
        {if(!ValidCapacity(capacity))throw new ArgumentException("Invalid producer receipt capacity.");using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(capacity));return a.Register(new NavigationLiveProbeAnswers(capacity));}}
        internal static int GrowthCapacity(int capacity,int count)
        {if(!ValidCapacity(capacity)||count<0||count>capacity)throw new ArgumentException("Invalid producer receipt growth source.");return count<capacity?capacity:capacity==0?4:checked(capacity*2);}
        internal NavigationHostAnswer OwnerAt(int index)=>owners[index].Answer;
        internal NavigationProbeReceipt ReceiptAt(int index)=>owners[index];
        internal NavigationAnswer AnswerAt(int index)=>owners[index].Answer.Answer;
        internal bool Contains(NavigationAnswer answer){for(int i=0;i<Count;i++)if(ReferenceEquals(owners[i].Answer.Answer,answer))return true;return false;}
        internal void CopyObserved(NavigationLiveProbeAnswers source)
        {if(source==null||Count!=0||Capacity<source.Count)throw new ArgumentException("Invalid producer receipt copy.");for(int i=0;i<source.Count;i++){RequireEdge(source.owners[i]);source.owners[i].Retain();owners[i]=source.owners[i];}Count=source.Count;Revision=source.Revision;}
        internal void AppendPreparing(NavigationHostAnswer answer,INavStorageReservation r)
        {var receipt=NavigationProbeReceipt.CreateOwned(answer,r);try{AppendReceiptPreparing(receipt);}finally{receipt.Release();}}
        internal void AppendReceiptPreparing(NavigationProbeReceipt receipt)
        {if(receipt==null||Count==Capacity||Revision==int.MaxValue)throw new ArgumentException("Invalid producer receipt node.");RequireEdge(receipt);receipt.Retain();owners[Count++]=receipt;Revision++;}
        internal bool CanTakeFirst(NavigationProbeReceipt expected,int revision)=>Revision==revision&&Count>0&&ReferenceEquals(owners[0],expected)&&Revision<int.MaxValue&&expected.RetiredNext==null&&expected.RetiredReset==null&&expected.RetiredTransport==null&&expected.RetiredTracking==null;
        internal void SealForUse(INavStorageReservation r){if(Count>0&&EmptyReset==null)EmptyReset=CreateOwned(Capacity,r);}
        internal NavigationLiveProbeAnswers TakeResetNoThrow(){var value=EmptyReset;EmptyReset=null;return value;}
        internal NavigationProbeReceipt TakeFirstNoThrow()
        {var receipt=owners[0];for(int i=1;i<Count;i++)owners[i-1]=owners[i];owners[--Count]=null;receipt.RetiredReset=Count==0?TakeResetNoThrow():null;Revision++;return receipt;}
        public IEnumerator<NavigationAnswer> GetEnumerator(){for(int i=0;i<Count;i++)yield return owners[i].Answer.Answer;}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){EmptyReset?.Release();EmptyReset=null;for(int i=0;i<Count;i++){owners[i].Release();owners[i]=null;}Count=0;}
    }
}
