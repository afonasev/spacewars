using System;
using System.Collections;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A worker acquires this under the Session gate and keeps it for the full
    // off-gate read/preparation. The facade alone is a borrowed observation.
    internal readonly struct NavigationAdmissionPin : IDisposable
    {
        private readonly NavigationAdmissionStorage owner;
        internal NavigationAdmissionStorage StorageOwner=>owner;
        internal NavigationAdmission Value{get;}
        internal NavigationAdmissionPin(NavigationAdmissionStorage owner,NavigationAdmission value)
        {this.owner=owner;Value=value;owner?.Retain();}
        public void Dispose()=>owner?.Release();
    }
    // Runtime publication records have their own lifetime. They retain exactly
    // the named/request payload they expose, not a historical authority catalog.
    internal sealed class NavigationAdmissionMemberStorage : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionMemberStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionMember));
        internal readonly NavigationAdmissionMember Value;
        private NavigationHostRequest request;private NavNameReference endpoint;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationAdmissionMemberStorage(NavigationAuthorityMember member,NavigationRequest pending):base(Layout)
        {Value=new NavigationAdmissionMember(member,pending);Retain();}
        internal static bool Matches(NavigationAdmissionMember value,NavigationAuthorityMember member,NavigationRequest pending)
        {
            var data=member.Data;
            return value.Entity==data.Entity&&value.Slot==data.Slot&&value.Incarnation==data.Incarnation&&value.ActivationRevision==data.OrderRevision&&value.MobilityRevision==data.MobilityRevision&&value.HasGoal==data.HasGoal&&value.Phase==data.Phase&&value.FormationReleased==data.FormationReleased&&SameLocation(value.AssignedEndpoint,member.GoalLocation)&&ReferenceEquals(value.PendingRequest,pending);
        }
        internal static bool SameName(NavNameReference a,NavNameReference b)=>ReferenceEquals(a.Owner,b.Owner)&&a.Role==b.Role&&a.Index==b.Index;
        internal static bool SamePoint(NavPoint a,NavPoint b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X)&&BitConverter.DoubleToInt64Bits(a.Z)==BitConverter.DoubleToInt64Bits(b.Z);
        internal static bool SameLocation(NavLocation? a,NavLocation? b)=>a.HasValue==b.HasValue&&(!a.HasValue||SamePoint(a.Value.Position,b.Value.Position)&&SameName(a.Value.NameReference,b.Value.NameReference));
        internal static NavigationAdmissionMemberStorage CreateOwned(NavigationAuthorityMember member,NavigationRequest pending,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,member);
            if(pending!=null)NavStorageAdmission.RequireOwned(reservation,pending.StorageOwner);
            var name=member.GoalSelector;if(name.Owner!=null)NavStorageAdmission.RequireOwned(reservation,name.Owner);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var result=allocation.Register(new NavigationAdmissionMemberStorage(member,pending));
                try{result.request=NavPhysicalMacro.Acquire(result,pending?.StorageOwner);if(name.Owner!=null)name.Owner.Retain();result.endpoint=name;return result;}
                catch{result.Release();throw;}
            }
        }
        protected override void ReleaseEdges(){request?.Release();endpoint.Owner?.Release();request=null;endpoint=default;}
    }
    internal sealed class NavigationAdmissionMemberView : IReadOnlyList<NavigationAdmissionMember>
    {
        private readonly NavPagedList<NavOwnedStorage> values;
        internal NavigationAdmissionMemberView(NavPagedList<NavOwnedStorage> values){this.values=values;}
        public int Count=>values.Count;
        public NavigationAdmissionMember this[int index]=>((NavigationAdmissionMemberStorage)values[index]).Value;
        public IEnumerator<NavigationAdmissionMember> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed class NavigationAdmissionGroupStorage : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionGroupStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionGroup))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionMemberView));
        internal readonly NavigationAdmissionGroup Value;
        internal NavPagedList<NavOwnedStorage> Members{get;private set;}
        private NavOwnedText ownerId,source;private NavNameReference terminal;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationAdmissionGroupStorage(NavigationAuthorityGroup version,NavPagedList<NavOwnedStorage> members):base(Layout)
        {Value=new NavigationAdmissionGroup(version,new NavigationAdmissionMemberView(members));Retain();}
        private bool SameHeader(NavigationAuthorityGroup version)
        {
            var data=version.Data;var value=Value;
            return value.GroupId==data.GroupId&&value.WorldGeneration==data.WorldGeneration&&value.RootOrderRevision==data.OrderRevision&&value.CommandSequence==data.CommandSequence&&value.IssuedTick==data.IssuedTick&&value.MembershipRevision==data.MembershipRevision&&ReferenceEquals(ownerId,version.OwnerIdOwner)&&value.TeamId==data.TeamId&&value.TargetId==data.TargetId&&ReferenceEquals(source,version.SourceOwner)&&value.JobId==data.JobId&&value.ActionId==data.ActionId&&value.Origin==data.Origin&&value.Kind==data.Kind&&NavigationAdmissionMemberStorage.SamePoint(value.OriginalGoal,data.OriginalGoal)&&NavigationAdmissionMemberStorage.SameLocation(value.OriginalTerminal,version.OriginalLocation);
        }
        private static NavigationRequest Pending(IReadOnlyList<NavOwnedStorage> requests,int entity)
        {for(int i=0;i<requests.Count;i++){var request=((NavigationHostRequest)requests[i]).Request;if(request.Entity==entity)return request;}return null;}
        internal bool MatchesProjection(NavigationAuthorityGroup version,IReadOnlyList<NavOwnedStorage> requests)
        {
            if(Members.Count!=version.Members.Count||!SameHeader(version))return false;
            for(int i=0;i<version.Members.Count;i++){var member=(NavigationAuthorityMember)version.Members[i];if(!NavigationAdmissionMemberStorage.Matches(Value.Members[i],member,Pending(requests,member.Data.Entity)))return false;}
            return true;
        }
        internal static NavigationAdmissionGroupStorage PrepareOwned(NavigationAuthorityGroup version,NavRetainedArray<NavOwnedStorage> requests,NavigationAdmissionGroupStorage previous,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,version);
            if(previous!=null)NavStorageAdmission.RequireOwned(reservation,previous);
            NavStorageAdmission.RequireOwned(reservation,requests);
            bool sameOrder=previous!=null&&previous.Members.Count==version.Members.Count;
            if(sameOrder)for(int i=0;i<version.Members.Count;i++)if(previous.Value.Members[i].Entity!=((NavigationAuthorityMember)version.Members[i]).Data.Entity){sameOrder=false;break;}
            bool same=sameOrder&&previous.MatchesProjection(version,requests);
            if(same){previous.Retain();return previous;}
            NavPagedList<NavOwnedStorage> members=null;NavigationAdmissionGroupStorage result=null;
            try{
                members=sameOrder?previous.Members.ForkOwned(reservation):NavPagedList<NavOwnedStorage>.CreateOwned(reservation,32);
                for(int i=0;i<version.Members.Count;i++){
                    var member=(NavigationAuthorityMember)version.Members[i];var pending=Pending(requests,member.Data.Entity);
                    if(sameOrder&&NavigationAdmissionMemberStorage.Matches(previous.Value.Members[i],member,pending))continue;
                    var record=NavigationAdmissionMemberStorage.CreateOwned(member,pending,reservation);
                    try{if(sameOrder)members.Set(i,record);else members.Add(record);}finally{record.Release();}
                }
                members.Seal();
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(Layout);result=allocation.Register(new NavigationAdmissionGroupStorage(version,members));
                    result.Members=NavPhysicalMacro.Acquire(result,members);result.ownerId=NavPhysicalMacro.Acquire(result,version.OwnerIdOwner);result.source=NavPhysicalMacro.Acquire(result,version.SourceOwner);
                    var name=version.OriginalSelector;if(name.Owner!=null)name.Owner.Retain();result.terminal=name;
                }
                return result;
            }catch{result?.Release();throw;}
            finally{members?.ReleaseStorage();}
        }
        protected override void ReleaseEdges(){Members?.Release();Members=null;ownerId?.Release();source?.Release();terminal.Owner?.Release();ownerId=source=null;terminal=default;}
    }
    internal sealed class NavigationAdmissionGroupView : IReadOnlyList<NavigationAdmissionGroup>
    {
        private readonly NavPagedList<NavOwnedStorage> values;
        internal NavigationAdmissionGroupView(NavPagedList<NavOwnedStorage> values){this.values=values;}
        public int Count=>values.Count;
        public NavigationAdmissionGroup this[int index]=>((NavigationAdmissionGroupStorage)values[index]).Value;
        public IEnumerator<NavigationAdmissionGroup> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed class NavigationAdmissionRequestView : IReadOnlyList<NavigationRequest>
    {
        private readonly NavPagedList<NavOwnedStorage> values;
        internal NavigationAdmissionRequestView(NavPagedList<NavOwnedStorage> values){this.values=values;}
        public int Count=>values.Count;
        public NavigationRequest this[int index]=>((NavigationHostRequest)values[index]).Request;
        public IEnumerator<NavigationRequest> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed class NavigationAdmissionStorage : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmission))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionGroupView))+NavStorageLayout.ObjectBytesBound(typeof(NavigationAdmissionRequestView));
        internal NavigationAdmission Value{get;private set;}
        // Session retirement bookkeeping only; it is cleared before releasing
        // the transferred reference and is never part of a worker projection.
        internal NavigationAdmissionStorage RetiredNext;
        internal NavPagedList<NavOwnedStorage> Groups{get;private set;}
        internal NavPagedList<NavOwnedStorage> Requests{get;private set;}
        private NavGeometry.OwnedBuffers geometry;private NavDeclarationStorage profile;
        private NavOwnedText provider;private NavRetainedArray<NavNameReference> terrainNames;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationAdmissionStorage():base(Layout){Retain();}
        internal bool MatchesGroupReplacement(NavigationAuthorityGroup replacement)
        {
            for(int i=0;i<Groups.Count;i++){var group=(NavigationAdmissionGroupStorage)Groups[i];if(group.Value.GroupId==replacement.Data.GroupId)return group.MatchesProjection(replacement,Requests);}
            return false;
        }
        internal static NavigationAdmissionStorage PrepareOwned(long generation,long sequence,IPlayableTerrain terrain,string providerId,int surfaceVersion,NavGeometry geometry,NavigationProfile profile,NavRetainedArray<NavNameReference> terrainNames,NavRetainedArray<NavOwnedStorage> versions,NavRetainedArray<NavOwnedStorage> requests,long publication,NavigationAdmissionStorage previous,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,geometry.StorageRoot);NavStorageAdmission.RequireOwned(reservation,profile.StorageRoot);NavStorageAdmission.RequireOwned(reservation,terrainNames);
            if(previous!=null)NavStorageAdmission.RequireOwned(reservation,previous);
            NavStorageAdmission.RequireOwned(reservation,versions);NavStorageAdmission.RequireOwned(reservation,requests);
            for(int i=0;i<versions.Count;i++){if(!(versions[i] is NavigationAuthorityGroup version)||!version.IsImmutableCaptureRoot)throw new ArgumentException("Invalid admission group input.");NavStorageAdmission.RequireOwned(reservation,version);if(i>0&&((NavigationAuthorityGroup)versions[i-1]).Data.GroupId>=version.Data.GroupId)throw new ArgumentException("Admission group order changed.");}
            for(int i=0;i<requests.Count;i++){if(!(requests[i] is NavigationHostRequest request)||!request.IsImmutableCaptureRoot)throw new ArgumentException("Invalid admission request input.");NavStorageAdmission.RequireOwned(reservation,request);if(i>0&&((NavigationHostRequest)requests[i-1]).Request.Request>request.Request.Request)throw new ArgumentException("Admission request order changed.");}
            bool same=previous!=null&&previous.Value.Generation==generation&&previous.Value.PublishedThroughRequestSequence==sequence&&ReferenceEquals(previous.geometry,geometry.StorageRoot)&&ReferenceEquals(previous.profile,profile.StorageRoot)&&ReferenceEquals(previous.terrainNames,terrainNames)&&ReferenceEquals(previous.Value.AuthoredTerrain,PartitionNavigationProvider.SupportsTerrain(terrain)?terrain:null)&&previous.Value.SurfaceProviderId==providerId&&previous.Value.SurfaceSemanticsVersion==surfaceVersion&&previous.Groups.Count==versions.Count&&previous.Requests.Count==requests.Count;
            if(same)for(int i=0;i<requests.Count;i++)if(!ReferenceEquals(previous.Requests[i],requests[i])){same=false;break;}
            if(same)for(int i=0;i<versions.Count;i++)if(!((NavigationAdmissionGroupStorage)previous.Groups[i]).MatchesProjection((NavigationAuthorityGroup)versions[i],requests)){same=false;break;}
            if(same){previous.Retain();return previous;}
            NavigationAdmissionStorage result=null;NavPagedList<NavOwnedStorage> groups=null,active=null;NavOwnedText providerName=null;
            // These concrete immutable input roots remain live even if admission
            // invokes a callback which replaces or destroys the source Session.
            var geometryRoot=(NavGeometry.OwnedBuffers)geometry.StorageRoot;var profileRoot=(NavDeclarationStorage)profile.StorageRoot;
            versions.Retain();requests.Retain();geometryRoot.Retain();profileRoot.Retain();terrainNames.Retain();previous?.Retain();
            try{
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout);result=allocation.Register(new NavigationAdmissionStorage());}
                bool sameOrder=previous!=null&&previous.Groups.Count==versions.Count;
                if(sameOrder)for(int i=0;i<versions.Count;i++)if(((NavigationAdmissionGroupStorage)previous.Groups[i]).Value.GroupId!=((NavigationAuthorityGroup)versions[i]).Data.GroupId){sameOrder=false;break;}
                groups=sameOrder?previous.Groups.ForkOwned(reservation):NavPagedList<NavOwnedStorage>.CreateOwned(reservation,32);
                for(int i=0;i<versions.Count;i++){
                    var old=sameOrder?(NavigationAdmissionGroupStorage)previous.Groups[i]:null;
                    var group=NavigationAdmissionGroupStorage.PrepareOwned((NavigationAuthorityGroup)versions[i],requests,old,reservation);
                    try{if(sameOrder){if(!ReferenceEquals(group,old))groups.Set(i,group);}else groups.Add(group);}finally{group.Release();}
                }
                groups.Seal();
                bool sameRequests=previous!=null&&previous.Requests.Count==requests.Count;
                if(sameRequests)for(int i=0;i<requests.Count;i++)if(!ReferenceEquals(previous.Requests[i],requests[i])){sameRequests=false;break;}
                if(sameRequests){active=previous.Requests;active.Retain();}else{
                    active=NavPagedList<NavOwnedStorage>.CreateOwned(reservation,32);for(int i=0;i<requests.Count;i++)active.Add(requests[i]);active.Seal();
                }
                if(previous?.provider?.Value==providerId){providerName=previous.provider;providerName.Retain();}else providerName=NavOwnedText.Copy(providerId,reservation);
                result.Groups=NavPhysicalMacro.Acquire(result,groups);result.Requests=NavPhysicalMacro.Acquire(result,active);
                result.geometry=NavPhysicalMacro.Acquire(result,(NavGeometry.OwnedBuffers)geometry.StorageRoot);result.profile=NavPhysicalMacro.Acquire(result,(NavDeclarationStorage)profile.StorageRoot);result.provider=NavPhysicalMacro.Acquire(result,providerName);result.terrainNames=NavPhysicalMacro.Acquire(result,terrainNames);
                result.Value=new NavigationAdmission(generation,sequence,terrain,providerName.Value,surfaceVersion,geometry,profile,new NavigationAdmissionGroupView(groups),new NavigationAdmissionRequestView(active),publication);
                return result;
            }catch{result?.Release();throw;}
            finally{groups?.Release();active?.Release();providerName?.Release();versions.Release();requests.Release();geometryRoot.Release();profileRoot.Release();terrainNames.Release();previous?.Release();}
        }
        protected override void ReleaseEdges(){Groups?.Release();Requests?.Release();geometry?.Release();profile?.Release();provider?.Release();terrainNames?.Release();Groups=Requests=null;geometry=null;profile=null;provider=null;terrainNames=null;Value=null;}
    }
    public sealed partial class NavigationSession
    {
        // Only an authority operation change is expected optimistic staleness.
        // Structural read-set violations under the same operation remain errors.
        private sealed class StaleAdmissionProjection : NavigationCommandRefused
        {internal StaleAdmissionProjection(string message):base(message){}}
        private NavigationAdmissionStorage retiredAdmissions;
        private void RetireAdmissionNoThrow(NavigationAdmissionStorage owner)
        {if(owner!=null){owner.RetiredNext=retiredAdmissions;retiredAdmissions=owner;}}
        // Caller transfers its prepared reference only when this returns true.
        private bool PublishAdmissionOwnerNoThrow(NavigationAdmissionStorage prepared)
        {
            if(ReferenceEquals(prepared,admissionOwner))return false;
            var old=admissionOwner;admissionOwner=prepared;admission=prepared.Value;admissionPublicationIdentity=admission.PublicationIdentity;RetireAdmissionNoThrow(old);return true;
        }
        private void PublishLegacyAdmissionNoThrow(NavigationAdmission snapshot)
        {var old=admissionOwner;admissionOwner=null;admission=snapshot;RetireAdmissionNoThrow(old);}
        private NavigationAdmissionStorage DetachRetiredAdmissionsNoThrow()
        {var result=retiredAdmissions;retiredAdmissions=null;return result;}
        private static void ReleaseRetiredAdmissions(NavigationAdmissionStorage retired)
        {while(retired!=null){var next=retired.RetiredNext;retired.RetiredNext=null;retired.Release();retired=next;}}
        private static int AdmissionSlotCount<TKey,TValue>(IDictionary<TKey,TValue> map)
            =>map is NavigationCommandSlots<TKey,TValue> owned?owned.Capacity:((AuthoritySlotMap<TKey,TValue>)map).SlotCount;
        private static bool ReadAdmissionSlot<TKey,TValue>(IDictionary<TKey,TValue> map,int slot,out TKey key,out TValue value)
            =>map is NavigationCommandSlots<TKey,TValue> owned?owned.TryReadSlot(slot,out key,out value):((AuthoritySlotMap<TKey,TValue>)map).TryReadSlot(slot,out key,out value);
        private bool CanOwnAdmissionProjection()
        {
            if(groups.Count>0&&!(groups is NavigationCommandSlots<long,GroupOrderState>))return false;
            for(int slot=0;slot<AdmissionSlotCount(groups);slot++)if(ReadAdmissionSlot(groups,slot,out long key,out _)&&!(((NavigationCommandSlots<long,GroupOrderState>)groups).OwnerFor(key) is NavigationLiveGroup))return false;
            for(int slot=0;slot<AdmissionSlotCount(pending);slot++)if(ReadAdmissionSlot(pending,slot,out _,out var request)&&request.StorageOwner==null)return false;
            for(int slot=0;slot<AdmissionSlotCount(probes);slot++)if(ReadAdmissionSlot(probes,slot,out _,out var request)&&request.StorageOwner==null)return false;
            return true;
        }
        // Sources are either privately prepared command maps or observed live
        // maps. Only paid scalar/reference copies happen under the read gate;
        // all physical factories and their callbacks run after leaving it.
        private NavigationAdmissionStorage PrepareAdmissionProjection(IDictionary<long,GroupOrderState> sourceGroups,IDictionary<int,NavigationRequest> sourcePending,
            long sequence,long publication,long operation,NavigationRetentionBudget.Scope scope,NavigationAdmissionStorage previous,NavigationAuthorityGroup replacement=null,NavigationAuthorityOwners targetCore=null,IDictionary<int,NavigationRequest> sourceProbes=null,RebindGroup[] rebindGroups=null,int rebindGroupCount=0)
        {
            sourceProbes=sourceProbes??probes;
            if(targetCore!=null)NavStorageAdmission.RequireOwned(scope.OwnedReservation(),targetCore);
            int groupCount,requestCount;NavGeometry observedGeometry;NavigationProfile observedProfile;NavRetainedArray<NavNameReference> observedNames;
            NavOwnedStorage geometryPin=null,profilePin=null;NavRetainedArray<NavNameReference> namesPin=null;bool previousPinned=false;
            try{
            using(EnterTransport()){
                if(operation!=authorityOperationIdentity)throw new StaleAdmissionProjection("Admission read-set changed.");
                groupCount=sourceGroups.Count;requestCount=checked(sourcePending.Count+sourceProbes.Count);
                observedGeometry=targetCore==null?navigationGeometry:(NavGeometry)targetCore.NavigationGeometry.Facade;observedProfile=targetCore==null?profile:(NavigationProfile)targetCore.Profile.Facade;observedNames=terrainNameOwners;
                var geometryRoot=observedGeometry.StorageRoot;NavStorageAdmission.RequireOwned(scope.OwnedReservation(),geometryRoot);geometryRoot.Retain();geometryPin=geometryRoot;
                var profileRoot=observedProfile.StorageRoot;NavStorageAdmission.RequireOwned(scope.OwnedReservation(),profileRoot);profileRoot.Retain();profilePin=profileRoot;
                observedNames.Retain();namesPin=observedNames;previous?.Retain();previousPinned=previous!=null;
            }
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(64L+8L*(groupCount+requestCount)))){
                    var versions=new NavOwnedStorage[groupCount];var requests=new NavOwnedStorage[requestCount];
                    NavRetainedArray<NavOwnedStorage> groupInputs=null,requestInputs=null;int g=0,r=0;
                    try{
                        using(EnterTransport()){
                            if(operation!=authorityOperationIdentity)throw new StaleAdmissionProjection("Admission copy lost read-set.");
                            if(sourceGroups.Count!=groupCount||sourcePending.Count+sourceProbes.Count!=requestCount)throw new InvalidOperationException("Admission copy lost read-set.");
                            for(int slot=0;slot<AdmissionSlotCount(sourceGroups);slot++)if(ReadAdmissionSlot(sourceGroups,slot,out long key,out _)){
                                var version=replacement?.Data.GroupId==key?replacement:(sourceGroups as NavigationCommandSlots<long,GroupOrderState>)?.OwnerFor(key) is NavigationLiveGroup live?live.ImmutableVersion:null;
                                for(int changed=0;changed<rebindGroupCount;changed++)if(rebindGroups[changed].Next.Data.GroupId==key)version=rebindGroups[changed].Next;
                                if(version==null)throw new ArgumentException("Admission requires an owned immutable group version.");version.Retain();versions[g++]=version;
                            }
                            for(int slot=0;slot<AdmissionSlotCount(sourcePending);slot++)if(ReadAdmissionSlot(sourcePending,slot,out _,out var request)){
                                var owner=request.StorageOwner;if(owner==null)throw new ArgumentException("Admission requires an owned pending request.");owner.Retain();requests[r++]=owner;
                            }
                            for(int slot=0;slot<AdmissionSlotCount(sourceProbes);slot++)if(ReadAdmissionSlot(sourceProbes,slot,out _,out var request)){
                                var owner=request.StorageOwner;if(owner==null)throw new ArgumentException("Admission requires an owned probe request.");owner.Retain();requests[r++]=owner;
                            }
                        }
                        for(int i=1;i<g;i++){var value=versions[i];int j=i;while(j>0&&((NavigationAuthorityGroup)versions[j-1]).Data.GroupId>((NavigationAuthorityGroup)value).Data.GroupId){versions[j]=versions[j-1];j--;}versions[j]=value;}
                        for(int i=1;i<r;i++){var value=requests[i];int j=i;while(j>0&&((NavigationHostRequest)requests[j-1]).Request.Request>((NavigationHostRequest)value).Request.Request){requests[j]=requests[j-1];j--;}requests[j]=value;}
                        groupInputs=NavRetainedArray<NavOwnedStorage>.Copy(versions,scope.OwnedReservation().TryReserve);requestInputs=NavRetainedArray<NavOwnedStorage>.Copy(requests,scope.OwnedReservation().TryReserve);
                        return NavigationAdmissionStorage.PrepareOwned(Generation,sequence,terrain,terrain?.Id??NavLocation.FlatSurface,terrain?.SurfaceSemanticsVersion??1,observedGeometry,observedProfile,observedNames,groupInputs,requestInputs,publication,previous,scope.OwnedReservation());
                    }finally{groupInputs?.Release();requestInputs?.Release();for(int i=0;i<g;i++)versions[i].Release();for(int i=0;i<r;i++)requests[i].Release();}
                }
            }finally{geometryPin?.Release();profilePin?.Release();namesPin?.Release();if(previousPinned)previous.Release();}
        }
    }
}
