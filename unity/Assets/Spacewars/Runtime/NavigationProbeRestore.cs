using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private static NavigationAnswer ExactProbeReceipt(NavigationAuthorityOwners source,NavigationHostRoot host,
            NavigationSessionState state,NavigationRequest[] requests,int index)
        {
            var saved=state.ProbeAnswers[index];
            if(source.ProbeAnswerCatalog==null||source.ProbeAnswerCatalog.Count!=state.ProbeAnswers.Length||
                !(source.ProbeAnswerCatalog[index] is NavigationHostAnswer owner)||
                saved.RequestIndex<0||saved.RequestIndex>=requests.Length||
                !ReferenceEquals(owner.RequestOwner,BodyRequestOwnerAt(host,source,state,saved.RequestIndex))||
                !ReferenceEquals(owner.Answer.Request,requests[saved.RequestIndex])||owner.Answer.Status!=saved.Status||
                !owner.Answer.ReadOnlyRouteBacking.SequenceEqual(saved.Route)||!SameHostCorridor(owner.Answer.Corridor,saved.Corridor))
                throw new ArgumentException("Producer receipt differs from its exact BODY role/order.");
            return owner.Answer;
        }
        private void RestoreOwnedProbeTables(NavigationAuthorityOwners source,NavigationHostRoot host,
            NavigationSessionState state,NavigationRequest[] requests)
        {
            if(!source.HasProbeStorage||ownedProbes!=null||liveProbeAnswers!=null||probes.Count!=0||probeAnswers.Count!=0||
                state.CollectionLayouts==null||state.CollectionLayouts.Length!=6||state.ProbeRequestIndices==null||state.ProbeAnswers==null)
                throw new ArgumentException("Owned producer restore destination/mode differs.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Producer hydration admission refused.");
            using(scope){NavigationCommandSlots<int,NavigationRequest> map=null;NavigationLiveProbeAnswers queue=null;bool installed=false;
                try{
                    var r=scope.OwnedReservation();map=source.ProbeCapacity==0?NavigationCommandSlots<int,NavigationRequest>.CreateEmptyOwned(r):NavigationCommandSlots<int,NavigationRequest>.CreateOwned(source.ProbeCapacity,r);
                    var layout=(NavRetainedArray<int>)source.CollectionCatalog[6];if(!layout.SequenceEqual(state.CollectionLayouts[4]))throw new ArgumentException("Outer producer layout differs from BODY.");map.InitializeLayoutPreparing(layout.ReadOnlyBacking);
                    using(NavStorageScratch.Reserve(r,checked(64L+requests.Length))){
                        var seen=new bool[requests.Length];int row=0;
                        for(int slot=0;slot<layout[0];slot++)if(!map.IsPreparedHole(slot)){
                            if(row==state.ProbeRequestIndices.Length)throw new ArgumentException("Missing producer occupied-slot role.");int index=state.ProbeRequestIndices[row++];
                            if(index<0||index>=requests.Length||seen[index]||(source.RequestRoles[index]&16)==0)throw new ArgumentException("Invalid duplicate producer request index/role.");seen[index]=true;
                            var request=requests[index];var owner=BodyRequestOwnerAt(host,source,state,index);
                            if(request.Entity>=0||request.Session!=Generation||!ReferenceEquals(request,owner.Request))throw new ArgumentException("Invalid producer key/generation/owner.");map.AddAtPreparedSlot(slot,request.Entity,request,owner);
                        }
                        if(row!=state.ProbeRequestIndices.Length)throw new ArgumentException("Extra producer occupied-slot role.");
                    }
                    map.Seal();queue=NavigationLiveProbeAnswers.CreateOwned(source.ProbeAnswerCapacity,r);
                    for(int i=0;i<state.ProbeAnswers.Length;i++){var answer=ExactProbeReceipt(source,host,state,requests,i);queue.AppendPreparing(answer.StorageOwner,r);}
                    queue.SealForUse(r);scope.Attach(map);scope.Commit(map,_=>{ownedProbes=map;probes=map;liveProbeAnswers=queue;installed=true;});
                    for(int i=0;i<queue.Count;i++){var answer=queue.AnswerAt(i);transportOwner.Answer(answer.Request);TrackCorridorAnswer(answer);}
                }finally{if(!installed){map?.ReleaseStorage();queue?.ReleaseStorage();}}
            }
        }
    }
}
