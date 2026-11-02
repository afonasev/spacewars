using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedRebind
        {
            internal NavigationCommandAuthority PreparedBacking=>Backing??Bootstrap?.Committed;
            internal int[] RemovalIds;internal NavCrowdSpatialStorage[] RemovalSpatials;
            internal NavigationCommandSlots<long,GroupOrderState> RemovalSourceGroups,RemovalGroups,RetiredRemovalGroups;
            internal NavigationCommandSlots<int,long> RemovalSourceMembers,RemovalMembers,RetiredRemovalMembers;
            internal NavigationCrowdNameBacking RemovalNames,RetiredRemovalNames;
            internal int RemovalGroupRevision,RemovalMemberRevision;private bool removalDestinations;
            internal bool Removes(int id)=>RemovalIds!=null&&Array.BinarySearch(RemovalIds,id)>=0;
            internal void PrepareRemovalIds(int[] ids,INavStorageReservation r)
            {
                if(ids==null)return;if(ids.Length==0)throw new ArgumentException("Missing lethal actors.");
                if(RemovalIds==null||RemovalIds.Length!=ids.Length)throw new InvalidOperationException("Lethal removal layout mismatch.");
                Array.Copy(ids,RemovalIds,ids.Length);Array.Sort(RemovalIds);
                for(int i=0;i<RemovalIds.Length;i++)if(RemovalIds[i]<=0||i>0&&RemovalIds[i]==RemovalIds[i-1])throw new ArgumentException("Invalid lethal actors.");
            }
            internal void SealRemovalDestinations(){if(RemovalIds==null)return;RemovalGroups.Retain();RemovalMembers.Retain();RemovalNames.Retain();removalDestinations=true;}
            internal void TransferRemovalDestinations(){removalDestinations=false;}
            internal void ReleaseRemovalEdges()
            {
                RemovalSourceGroups?.Release();RemovalSourceMembers?.Release();
                if(removalDestinations){RemovalGroups?.Release();RemovalMembers?.Release();RemovalNames?.Release();}
                RemovalGroups?.ReleaseStorage();RemovalMembers?.ReleaseStorage();RemovalNames?.Release();
                RetiredRemovalGroups?.Release();RetiredRemovalMembers?.Release();RetiredRemovalNames?.Release();
                if(RemovalSpatials!=null)foreach(var spatial in RemovalSpatials)spatial?.Release();
            }
        }
        private void ObserveRemovalSource(PreparedRebind c)
        {
            if(c.RemovalIds==null)return;
            c.RemovalSourceGroups=NavPhysicalMacro.Acquire(c,(c.Bootstrap?.Groups??groups) as NavigationCommandSlots<long,GroupOrderState>);
            c.RemovalSourceMembers=NavPhysicalMacro.Acquire(c,(c.Bootstrap?.MemberGroups??memberGroups) as NavigationCommandSlots<int,long>);
            if(c.RemovalSourceGroups==null||c.RemovalSourceMembers==null)throw new NavigationCommandRefused("Lethal removal requires owned command tables.");
            c.RemovalGroupRevision=c.RemovalSourceGroups.Revision;c.RemovalMemberRevision=c.RemovalSourceMembers.Revision;
            foreach(int id in c.RemovalIds)if(!Crowd.TryGet(id,out _))throw new NavigationCommandRefused("Lethal actor changed.");
        }
        private bool RemovalSourceCurrent(PreparedRebind c)=>c.RemovalIds==null||c.RemovalSourceGroups!=null&&
            ReferenceEquals(c.Bootstrap?.Groups??groups,c.RemovalSourceGroups)&&c.RemovalSourceGroups.Revision==c.RemovalGroupRevision&&
            ReferenceEquals(c.Bootstrap?.MemberGroups??memberGroups,c.RemovalSourceMembers)&&c.RemovalSourceMembers.Revision==c.RemovalMemberRevision;
        private static long RemovalMembership(PreparedRebind c,long group,long revision)
        {
            if(c.RemovalIds!=null)foreach(int id in c.RemovalIds)if(c.RemovalSourceMembers.TryGetValue(id,out long source)&&source==group)return checked(revision+1);
            return revision;
        }
#if UNITY_INCLUDE_TESTS
        internal Action AfterGeometryRemovalPreparationForQa;
#endif
        private void PrepareGeometryRemovals(PreparedRebind c,NavigationRetentionBudget.Scope scope)
        {
            var r=scope.OwnedReservation();
            // All removals, group replacements, names and spatial indexes are private
            // members of the same geometry candidate. No second admission follows it.
            c.RemovalGroups=CopyOrdinaryRebindMap(c,c.RemovalSourceGroups,r);
            c.RemovalMembers=CopyOrdinaryRebindMap(c,c.RemovalSourceMembers,r);
            for(int i=0;i<c.RemovalIds.Length;i++){
                int id=c.RemovalIds[i];int count=c.Actors.Length-i-1;
                c.RemovalSpatials[i]=count==0?null:NavCrowdSpatialStorage.Create(count,scope.TryReserve);
                if(c.NextOrders.TryGetValue(id,out long order))c.NextOrders.SetPreparing(id,checked(order+1));
                c.NextPending.Remove(id);c.Allocating.Remove(id);c.NextReservations.Remove(id);c.NextGoals.Remove(id);c.NextFailures.Remove(id);c.Identities.Remove(id);c.RemovalMembers.Remove(id);
                for(int slot=0;slot<c.NextLedger.Capacity;slot++)if(c.NextLedger.TryReadSlot(slot,out long key,out var row)&&row.Entity==id)c.NextLedger.Remove(key);
            }
            using(NavStorageScratch.Reserve(r,RebindStagingScratchBound(c.Actors.Length,0,0))){
                foreach(var row in c.RemovalSourceGroups){
                    NavigationAuthorityGroup version=null;for(int g=0;g<c.GroupCount;g++)if(c.RebindGroups[g].Source.Version.Data.GroupId==row.Key)version=c.RebindGroups[g].Next;
                    bool removed=row.Value.Members.Any(m=>c.Removes(m.Entity));if(version==null&&!removed)continue;
                    GroupOrderState record;
                    if(version==null)record=row.Value.Copy();
                    else{var view=NavigationLiveGroup.CreateOwned(version,scope);try{view.SealForInstall(scope);record=view.ObserveDetached();}finally{view.ReleaseStorage();}}
                    if(removed){record.Members=record.Members.Where(m=>!c.Removes(m.Entity)).ToArray();record.MembershipRevision=checked(record.MembershipRevision+1);record.MarchAnchor=0;record.MarchInitialized=false;}
                    if(record.Members.Length==0)c.RemovalGroups.Remove(row.Key);
                    else{var live=CopyCommandGroup(record,scope);try{c.RemovalGroups.SetPreparing(row.Key,live.PreparedRecord(scope),live);}finally{live.ReleaseStorage();}}
                }
            }
            if(!PrepareRebindRetainedPrune(c,scope))throw new NavigationCommandRefused("Lethal arrival retirement refused.");
            c.RemovalNames=CaptureCrowdNameReferences(r,excludedEntities:c.RemovalIds);
            foreach(var retry in c.Retries)if(!retry.Assigned&&!retry.Removed&&!retry.GeometryRejected)c.RemovalNames.ClearFailedPreparing(retry.Entity);
            for(int i=0;i<c.Retries.Length;i++)if(c.Retries[i].GeometryRejected&&c.Retries[i].Source!=null){
                ref var retry=ref c.Retries[i];retry.ArrivalFailureNames?.Release();retry.ArrivalFailureNames=null;
                retry.ArrivalFailureNames=NavigationPreparedCrowdNameWrite.CreateOwned(c.RemovalNames,c.RemovalNames.Find(retry.Entity),retry.Source.Request,r);
            }
            c.RemovalGroups.Seal();c.RemovalMembers.Seal();
#if UNITY_INCLUDE_TESTS
            AfterGeometryRemovalPreparationForQa?.Invoke();
#endif
        }
        private void ValidateGeometryRemovals(PreparedRebind c,NavigationRetentionBudget.Scope scope)
        {
            if(c.RemovalIds==null)return;
            if(!RemovalSourceCurrent(c))throw new NavigationCommandRefused("Lethal group source changed.");
            for(int i=0;i<c.RemovalIds.Length;i++)if(!Crowd.TryGet(c.RemovalIds[i],out _)||(c.Actors.Length-i-1==0?c.RemovalSpatials[i]!=null:c.RemovalSpatials[i]?.ActorCount!=c.Actors.Length-i-1))throw new NavigationCommandRefused("Lethal spatial preparation changed.");
            foreach(var row in c.RemovalGroups){var owner=(NavigationLiveGroup)c.RemovalGroups.OwnerFor(row.Key);if(owner.Phase==NavigationLiveStoragePhase.SealedForInstall)owner.ValidateInstall(scope);}
        }
        private void ApplyGeometryRemovalsNoThrow(PreparedRebind c)
        {
            foreach(var row in c.RemovalGroups){var owner=(NavigationLiveGroup)c.RemovalGroups.OwnerFor(row.Key);if(owner.Phase==NavigationLiveStoragePhase.SealedForInstall)owner.ActivateNoThrow(this);}
            c.RetiredRemovalGroups=c.PreparedBacking.Groups;c.PreparedBacking.Groups=c.RemovalGroups;groups=c.RemovalGroups;
            c.RetiredRemovalMembers=c.PreparedBacking.MemberGroups;c.PreparedBacking.MemberGroups=c.RemovalMembers;memberGroups=c.RemovalMembers;
            c.RetiredRemovalNames=c.PreparedBacking.CrowdNames;c.PreparedBacking.CrowdNames=c.RemovalNames;
            for(int i=0;i<c.RemovalIds.Length;i++){Crowd.RemovePrepared(c.RemovalIds[i],c.RemovalSpatials[i]);c.RemovalSpatials[i]=null;}
        }
    }
}
