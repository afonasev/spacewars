using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class NavigationAuthorityOwners
    {
        // [capacity, layout length, slot length/free LIFO..., (answers,external)*].
        // The direct payload edges also cover an answer removed through the
        // public mailbox API while its transport ownership is still outstanding.
        internal void ValidateAnswerTracking(INavStorageReservation r)
        {
            var data=AnswerTrackingLayout;var owners=AnswerTrackingOwners;
            if(!HasAnswerTracking||data==null||owners==null||data.Count<3||data[0]<1||data[0]>AuthoritySlotMap<NavigationAnswer,NavigationHostAnswer>.MaximumSlots||data[1]<1||data[1]>data.Count-2||data.Count!=checked(2+data[1]+2*owners.Count))throw new ArgumentException("Invalid tracked answer catalog shape.");
            int length=data[2],free=data[1]-1;if(length<0||length>data[0]||free>length||owners.Count!=length-free)throw new ArgumentException("Invalid tracked answer occupancy.");
            using(NavStorageScratch.Reserve(r,checked(32L+length))){
                var holes=new bool[length];for(int i=0;i<free;i++){int slot=data[3+i];if(slot<0||slot>=length||holes[slot])throw new ArgumentException("Invalid tracked answer free LIFO.");holes[slot]=true;}
                long bytes=0;for(int i=0;i<owners.Count;i++){
                    if(!(owners[i] is NavigationHostAnswer owner)||owner.Answer.Corridor==null||!ReferenceEquals(owner.Answer.StorageOwner,owner))throw new ArgumentException("Invalid tracked answer payload.");
                    int at=2+data[1]+2*i;if(data[at]<1||(uint)data[at+1]>1)throw new ArgumentException("Invalid tracked answer transport multiplicity.");
                    for(int j=0;j<i;j++){var other=(NavigationHostAnswer)owners[j];if(ReferenceEquals(other.Answer,owner.Answer))throw new ArgumentException("Duplicate tracked answer identity.");if(ReferenceEquals(other.RequestOwner,owner.RequestOwner)&&(data[2+data[1]+2*j]!=data[at]||data[3+data[1]+2*j]!=data[at+1]))throw new ArgumentException("Inconsistent tracked request multiplicity.");}
                    bytes=checked(bytes+owner.Answer.Corridor.EstimatedRetainedBytes);
                }
                if(bytes>LayeredNavigationScheduler.MaxRetainedBytes)throw new ArgumentException("Tracked corridors exceed retained budget.");
            }
            for(int i=0;i<owners.Count;i++){
                var owner=(NavigationHostAnswer)owners[i];int minimum=TrackedRequestOccurrences(ProbeAnswerCatalog,owner.RequestOwner)+TrackedRequestOccurrences(MailboxAnswerCatalog,owner.RequestOwner)+TrackedRequestOccurrences(BarrierAnswerCatalog,owner.RequestOwner);
                for(int j=0;j<owners.Count;j++){var tracked=(NavigationHostAnswer)owners[j];if(ReferenceEquals(tracked.RequestOwner,owner.RequestOwner)&&!HasTrackedRole(ProbeAnswerCatalog,tracked)&&!HasTrackedRole(MailboxAnswerCatalog,tracked)&&!HasTrackedRole(BarrierAnswerCatalog,tracked))minimum=checked(minimum+1);}
                if(data[2+data[1]+2*i]<minimum)throw new ArgumentException("Tracked transport count understates exact answer roles.");
            }
            ValidateTrackedRoles(ProbeAnswerCatalog);ValidateTrackedRoles(MailboxAnswerCatalog);ValidateTrackedRoles(BarrierAnswerCatalog);
        }
        private static bool HasTrackedRole(NavRetainedArray<NavOwnedStorage> roles,NavigationHostAnswer owner)
        {if(roles!=null)foreach(var value in roles)if(ReferenceEquals(value,owner))return true;return false;}
        private static int TrackedRequestOccurrences(NavRetainedArray<NavOwnedStorage> roles,NavigationHostRequest request)
        {int count=0;if(roles!=null)foreach(var value in roles)if(value is NavigationHostAnswer answer&&ReferenceEquals(answer.RequestOwner,request))count++;return count;}
        private void ValidateTrackedRoles(NavRetainedArray<NavOwnedStorage> roles)
        {
            if(roles==null)return;foreach(var value in roles){if(!(value is NavigationHostAnswer owner)||owner.Answer.Corridor==null)continue;bool found=false;foreach(var tracked in AnswerTrackingOwners)if(ReferenceEquals(tracked,owner)){found=true;break;}if(!found)throw new ArgumentException("Answer role has no exact tracked payload edge.");}
        }
    }
    public sealed partial class NavigationSession
    {
        private void CaptureAnswerTracking(NavigationSessionState state)
        {
            if(ownedAnswerTracking==null)return;
            if(countedCorridorAnswers.Count!=0)throw new InvalidOperationException("Mixed legacy corridor tracking cannot be captured as an owned catalog.");
            var map=ownedAnswerTracking;state.CaptureAnswerTrackingOwners=new NavOwnedStorage[map.Count];
            var data=new int[checked(2+map.LayoutLength+2*map.Count)];state.CaptureAnswerTrackingLayout=data;data[0]=map.Capacity;data[1]=map.LayoutLength;map.CopyLayoutTo(data,2);
            long bytes=0;int row=0;for(int slot=0;slot<map.Capacity;slot++)if(map.TryReadSlot(slot,out var answer,out var owner)){
                if(owner==null||!ReferenceEquals(answer,owner.Answer)||answer.Corridor==null||!ReferenceEquals(owner.RequestOwner.TransportOwner,transportOwner)||owner.RequestOwner.TransportAnswers<1)throw new InvalidOperationException("Tracked answer lost its transport owner.");
                owner.Retain();state.CaptureAnswerTrackingOwners[row]=owner;int at=2+data[1]+2*row++;data[at]=owner.RequestOwner.TransportAnswers;data[at+1]=owner.RequestOwner.TransportExternal?1:0;bytes=checked(bytes+answer.Corridor.EstimatedRetainedBytes);
            }
            if(bytes!=answerCorridorBytes)throw new InvalidOperationException("Tracked answer charge differs from its exact membership.");
        }
        private void RestoreOwnedAnswerTracking(NavigationAuthorityOwners source)
        {
            if(ownedAnswerTracking!=null||countedCorridorAnswers.Count!=0||answerCorridorBytes!=0)throw new ArgumentException("Nonempty tracked answer restore destination.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Answer tracking hydration refused.");
            using(scope){NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> map=null;bool installed=false;try{
                var r=scope.OwnedReservation();source.ValidateAnswerTracking(r);var data=source.AnswerTrackingLayout;map=NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>.CreateOwned(data[0],r,true);
                using(NavStorageScratch.Reserve(r,checked(32L+4L*data[1]))){var layout=new int[data[1]];for(int i=0;i<layout.Length;i++)layout[i]=data[2+i];map.InitializeLayoutPreparing(layout);}
                int row=0;long bytes=0;for(int slot=0;slot<data[2];slot++)if(!map.IsPreparedHole(slot)){
                    var owner=(NavigationHostAnswer)source.AnswerTrackingOwners[row++];if(owner.Answer.Request.Session!=Generation)throw new ArgumentException("Tracked answer belongs to another world.");
                    map.AddAtPreparedSlot(slot,owner.Answer,owner,owner);bytes=checked(bytes+owner.Answer.Corridor.EstimatedRetainedBytes);
                }
                map.Seal();scope.Attach(map);scope.Commit(map,_=>{ownedAnswerTracking=map;answerCorridorBytes=bytes;installed=true;});
            }finally{if(!installed)map?.ReleaseStorage();}}
        }
        private void RestoreTrackedTransport(NavigationAuthorityOwners source)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Tracked transport validation refused.");
            using(scope)using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+8L*Answers.Count))){
                var queued=Answers.CopyItems();var data=source.AnswerTrackingLayout;
                bool IsQueued(NavigationAnswer answer){for(int i=0;i<queued.Length;i++)if(ReferenceEquals(queued[i],answer))return true;return barrierAnswers.Contains(answer)||(liveProbeAnswers?.Contains(answer)??probeAnswers.Contains(answer));}
                for(int i=0;i<source.AnswerTrackingOwners.Count;i++){
                    var owner=(NavigationHostAnswer)source.AnswerTrackingOwners[i];bool first=true;for(int j=0;j<i;j++)if(ReferenceEquals(((NavigationHostAnswer)source.AnswerTrackingOwners[j]).RequestOwner,owner.RequestOwner)){first=false;break;}
                    if(first){int at=2+data[1]+2*i;int minimum=owner.RequestOwner.TransportAnswers;
                        for(int j=0;j<source.AnswerTrackingOwners.Count;j++){var answer=(NavigationHostAnswer)source.AnswerTrackingOwners[j];if(ReferenceEquals(answer.RequestOwner,owner.RequestOwner)&&!IsQueued(answer.Answer))minimum=checked(minimum+1);}
                        if(data[at]<minimum)throw new ArgumentException("Tracked transport understates hydrated and detached answer ownership.");
                        transportOwner.RestoreAnswerCount(owner.Answer.Request,data[at],data[at+1]!=0);
                    }
                }
            }
        }
    }
}
