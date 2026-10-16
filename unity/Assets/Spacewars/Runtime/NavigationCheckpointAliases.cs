using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class NavigationReferenceComparer<T> : IEqualityComparer<T> where T:class
    {
        internal static readonly NavigationReferenceComparer<T> Instance=new NavigationReferenceComparer<T>();
        public bool Equals(T left,T right)=>ReferenceEquals(left,right);
        public int GetHashCode(T value)=>RuntimeHelpers.GetHashCode(value);
    }
    internal static class NavigationAliasMetadata
    {
        internal static void CheckScope(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation)
        {if(scope==null||!(allocation is INavStorageLedgerIdentity identity)||!ReferenceEquals(scope.Identity,identity.Identity))throw new ArgumentException("Alias factory must use its own allocation ledger.");scope.AssertOwnerLive();}
        internal static void Grow<K,V>(Dictionary<K,V> map,INavStorageReservation allocation)
        {
            int capacity=map.EnsureCapacity(0);if(map.Count<capacity)return;
            // Reserve the entire replacement hash arrays alongside the old ones.
            // The next prime capacity is bounded by four times the current size;
            // 64 bytes/slot covers entries, buckets and alignment of these keys.
            long replacement=checked(64L+64L*(4L*(capacity+1)+32L));
            if(!allocation.TryReserve(NavStorageLayout.Charge(replacement)))throw new ArgumentException("Navigation alias metadata exceeds retention.");
            map.EnsureCapacity(map.Count+1);
        }
    }
    // One frozen hash backing unit; the existing growth credits admitted its
    // private arrays before EnsureCapacity. Registration transfers those credits
    // plus the wrapper header, without a second reservation for the arrays.
    internal sealed class NavigationAliasHashStorage : NavOwnedStorage
    {
        private object map;
        private NavigationAliasHashStorage(object map,int capacity):base(256L+64L*capacity){this.map=map;}
        internal static NavigationAliasHashStorage Seal<K,V>(Dictionary<K,V> map,INavStorageReservation reservation)
        {
            int capacity=map.EnsureCapacity(0);if(capacity==0)return null;
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(256);var storage=allocation.Register(new NavigationAliasHashStorage(map,capacity));storage.Retain();return storage;}
        }
        internal void ReleaseStorage()=>Release();
        protected override void ReleaseEdges(){map=null;}
    }
    // Snapshot-local dense IDs are assigned by deterministic owner traversal.
    // Binding equality never merges independently allocated physical objects.
    internal sealed class NavigationReferenceTable<T> : NavOwnedStorage where T:class
    {
        private const long RootBytes=512;
        private NavigationRetentionBudget.Scope scope;
        private INavStorageReservation allocation;
        private Dictionary<T,int> ids=new Dictionary<T,int>(NavigationReferenceComparer<T>.Instance);
        private readonly NavPagedList<T> values;
        private bool frozen;
        private NavigationAliasHashStorage hashStorage;private bool released;
        private NavigationReferenceTable(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation):base(RootBytes){this.scope=scope;this.allocation=allocation;allocation.Register(this);values=NavPagedList<T>.Create(allocation.TryReserve);Retain();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){scope=null;allocation=null;ids=null;hashStorage?.ReleaseStorage();values?.ReleaseStorage();}
        internal static NavigationReferenceTable<T> Create(NavigationRetentionBudget.Scope scope)=>CreateOwned(scope,scope?.OwnedReservation());
        internal static NavigationReferenceTable<T> CreateOwned(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation)
        {NavigationAliasMetadata.CheckScope(scope,allocation);if(!allocation.TryReserve(NavStorageLayout.Charge(RootBytes)))throw new ArgumentException("Navigation alias root exceeds retention.");return new NavigationReferenceTable<T>(scope,allocation);}
        internal int Count=>values.Count;
        internal IReadOnlyList<T> Values=>values;
        internal long RetainedMetadataBytes=>RootBytes+(hashStorage?.StorageBytes??(ids.EnsureCapacity(0)==0?0:256L+64L*ids.EnsureCapacity(0)))+values.CommittedStorageBytes(8);
        internal int Include(T value)
        {
            if(References<=0)throw new InvalidOperationException("Navigation alias owner is released.");
            if(value==null)throw new ArgumentNullException(nameof(value));
            if(!frozen)scope.AssertOwnerLive();
            if(ids.TryGetValue(value,out int existing))return existing;
            if(frozen||scope.IsResolved)throw new InvalidOperationException("Navigation aliases are sealed.");
            if(Count>=WorldWire.MaxItems)throw new ArgumentException("Too many physical navigation aliases.");
            NavigationAliasMetadata.Grow(ids,allocation);int id=Count;
            // Failure after a successful reservation leaves an uncommitted table.
            // The enclosing owner discards the entire candidate and its scope.
            values.Add(value);ids.Add(value,id);return id;
        }
        internal void BindRestored(int id,T value)
        {
            if(id!=Count||value==null||ids.ContainsKey(value))throw new ArgumentException("Duplicate or unordered physical navigation alias.");
            if(Include(value)!=id)throw new ArgumentException("Physical navigation alias mismatch.");
        }
        internal T Resolve(int id)
        {if(id<0||id>=Count)throw new ArgumentException("Unknown physical navigation alias.");return values[id];}
        internal void Freeze(){if(frozen)return;hashStorage=NavigationAliasHashStorage.Seal(ids,allocation);values.Seal();frozen=true;scope=null;allocation=null;}
    }
    [Flags] internal enum NavigationCheckpointRequestRole { Canonical=1, ArrivalIntent=2, AssignedEndpoint=4 }
    internal sealed class NavigationRequestAliases : NavOwnedStorage
    {
        private readonly struct RoleKey : IEquatable<RoleKey>
        {
            private readonly long session,request;private readonly NavigationCheckpointRequestRole role;
            internal RoleKey(NavigationRequest value,NavigationCheckpointRequestRole role){session=value.Session;request=value.Request;this.role=role;}
            public bool Equals(RoleKey other)=>session==other.session&&request==other.request&&role==other.role;
            public override bool Equals(object value)=>value is RoleKey other&&Equals(other);
            public override int GetHashCode()=>unchecked(((session.GetHashCode()*397)^request.GetHashCode())*397^(int)role);
        }
        private NavigationRetentionBudget.Scope scope;
        private INavStorageReservation allocation;
        private Dictionary<RoleKey,int> logical=new Dictionary<RoleKey,int>();
        private readonly NavPagedList<int> roles;
        internal readonly NavigationReferenceTable<NavigationRequest> Physical;
        private bool frozen;
        private NavigationAliasHashStorage hashStorage;private bool released;
        private NavigationRequestAliases(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation):base(512)
        {this.scope=scope;this.allocation=allocation;allocation.Register(this);roles=NavPagedList<int>.Create(allocation.TryReserve);Physical=NavigationReferenceTable<NavigationRequest>.CreateOwned(scope,allocation);Retain();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){scope=null;allocation=null;logical=null;hashStorage?.ReleaseStorage();roles?.ReleaseStorage();Physical?.ReleaseStorage();}
        internal static NavigationRequestAliases Create(NavigationRetentionBudget.Scope scope)=>CreateOwned(scope,scope?.OwnedReservation());
        internal static NavigationRequestAliases CreateOwned(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation)
        {NavigationAliasMetadata.CheckScope(scope,allocation);if(!allocation.TryReserve(NavStorageLayout.Charge(512)))throw new ArgumentException("Navigation request role root exceeds retention.");return new NavigationRequestAliases(scope,allocation);}
        internal int Count=>Physical.Count;
        internal long RetainedMetadataBytes=>512L+(hashStorage?.StorageBytes??(logical.EnsureCapacity(0)==0?0:256L+64L*logical.EnsureCapacity(0)))+roles.CommittedStorageBytes(4)+Physical.RetainedMetadataBytes;
        private void BindRole(NavigationRequest value,NavigationCheckpointRequestRole role,int id)
        {
            var key=new RoleKey(value,role);
            if(logical.TryGetValue(key,out int previous)){if(previous!=id)throw new ArgumentException("Noncanonical navigation request role alias.");return;}
            NavigationAliasMetadata.Grow(logical,allocation);logical.Add(key,id);
        }
        internal int Include(NavigationRequest value,NavigationCheckpointRequestRole role)
        {
            if(value==null||value.Session<1||value.Request<1||value.Profile==null||value.Geometry==null||
                (value.Entity<0?value.Order<0:value.Order<1)||(int)role<1||((int)role&~7)!=0||
                (role&NavigationCheckpointRequestRole.AssignedEndpoint)!=0&&((role&NavigationCheckpointRequestRole.Canonical)==0||(role&NavigationCheckpointRequestRole.ArrivalIntent)!=0)||
                (role&(NavigationCheckpointRequestRole.ArrivalIntent|NavigationCheckpointRequestRole.AssignedEndpoint))!=0&&
                    (value.Entity<0||value.MemberIdentity==null||value.MemberIdentity.Incarnation<1||value.MemberIdentity.MobilityRevision<0||
                        value.MemberIdentity.GroupId<1||value.MemberIdentity.OrderRevision<1||value.MemberIdentity.MembershipRevision<1||
                        !value.StartLocation.HasValue||!value.GoalLocation.HasValue||!value.StartLocation.Value.Position.Equals(value.Start)||!value.GoalLocation.Value.Position.Equals(value.Goal)))
                throw new ArgumentException("Invalid navigation request role.");
            if(frozen||scope.IsResolved)throw new InvalidOperationException("Navigation request roles are sealed.");
            scope.AssertOwnerLive();
            int id=Physical.Include(value);
            int merged=(int)role|(id<roles.Count?roles[id]:0);
            if((merged&6)==6)throw new ArgumentException("Intent and assigned endpoint cannot share a physical request.");
            if((role&NavigationCheckpointRequestRole.Canonical)!=0)BindRole(value,NavigationCheckpointRequestRole.Canonical,id);
            if((role&NavigationCheckpointRequestRole.ArrivalIntent)!=0)BindRole(value,NavigationCheckpointRequestRole.ArrivalIntent,id);
            if(id==roles.Count)roles.Add(merged);else roles[id]=merged;
            return id;
        }
        internal NavigationCheckpointRequestRole Role(int id){Physical.Resolve(id);return (NavigationCheckpointRequestRole)roles[id];}
        internal void BindRestored(int id,NavigationRequest value,NavigationCheckpointRequestRole role)
        {if(id!=Count||Include(value,role)!=id)throw new ArgumentException("Duplicate or unordered request role alias.");}
        internal void Freeze(){if(frozen)return;if(roles.Count!=Physical.Count)throw new ArgumentException("Incomplete request role aliases.");Physical.Freeze();hashStorage=NavigationAliasHashStorage.Seal(logical,allocation);roles.Seal();frozen=true;scope=null;allocation=null;}
    }
    // One catalog is shared by ordinary scheduler, arrival allocator, caches,
    // host rows and transport. Factories/recipes are supplied by the containing
    // world section; no key-string fallback is allowed for ready completions.
    internal sealed class NavigationCheckpointAliases : NavOwnedStorage
    {
        private NavigationRetentionBudget.Scope scope;
        private INavStorageReservation allocation;
        private Dictionary<NavScheduledIdentity,NavScheduledRequest> recipes=new Dictionary<NavScheduledIdentity,NavScheduledRequest>(NavigationReferenceComparer<NavScheduledIdentity>.Instance);
        internal readonly NavigationRequestAliases Requests;
        internal readonly NavigationReferenceTable<NavGeometry> Geometries;
        internal readonly NavigationReferenceTable<ILayeredNavigationProvider> Providers;
        internal readonly NavigationReferenceTable<LayeredNavigationGraph> Graphs;
        internal readonly NavigationReferenceTable<NavSharedTerminalField> Fields;
        internal readonly NavigationReferenceTable<NavTerminalRegion> Regions;
        internal readonly NavigationReferenceTable<NavScheduledRequest> Scheduled;
        internal readonly NavigationReferenceTable<NavTypedRoute> Routes;
        internal readonly NavigationReferenceTable<NavArrivalProposal> Proposals;
        private bool frozen;
        private NavigationCheckpointAliases(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation):base(512)
        {
            this.scope=scope;this.allocation=allocation;allocation.Register(this);Requests=NavigationRequestAliases.CreateOwned(scope,allocation);Geometries=NavigationReferenceTable<NavGeometry>.CreateOwned(scope,allocation);
            Providers=NavigationReferenceTable<ILayeredNavigationProvider>.CreateOwned(scope,allocation);Graphs=NavigationReferenceTable<LayeredNavigationGraph>.CreateOwned(scope,allocation);
            Fields=NavigationReferenceTable<NavSharedTerminalField>.CreateOwned(scope,allocation);Regions=NavigationReferenceTable<NavTerminalRegion>.CreateOwned(scope,allocation);
            Scheduled=NavigationReferenceTable<NavScheduledRequest>.CreateOwned(scope,allocation);Routes=NavigationReferenceTable<NavTypedRoute>.CreateOwned(scope,allocation);Proposals=NavigationReferenceTable<NavArrivalProposal>.CreateOwned(scope,allocation);Retain();
        }
        private NavigationAliasHashStorage hashStorage;private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){scope=null;allocation=null;recipes=null;hashStorage?.ReleaseStorage();Requests?.ReleaseStorage();Geometries?.ReleaseStorage();Providers?.ReleaseStorage();Graphs?.ReleaseStorage();Fields?.ReleaseStorage();Regions?.ReleaseStorage();Scheduled?.ReleaseStorage();Routes?.ReleaseStorage();Proposals?.ReleaseStorage();}
        internal static NavigationCheckpointAliases Create(NavigationRetentionBudget.Scope scope)=>CreateOwned(scope,scope?.OwnedReservation());
        internal static NavigationCheckpointAliases CreateOwned(NavigationRetentionBudget.Scope scope,INavStorageReservation allocation)
        {NavigationAliasMetadata.CheckScope(scope,allocation);if(!allocation.TryReserve(NavStorageLayout.Charge(512)))throw new ArgumentException("Navigation catalog root exceeds retention.");return new NavigationCheckpointAliases(scope,allocation);}
        internal long RetainedMetadataBytes=>512L+(hashStorage?.StorageBytes??(recipes.EnsureCapacity(0)==0?0:256L+64L*recipes.EnsureCapacity(0)))+Requests.RetainedMetadataBytes+Geometries.RetainedMetadataBytes+
            Providers.RetainedMetadataBytes+Graphs.RetainedMetadataBytes+Fields.RetainedMetadataBytes+Regions.RetainedMetadataBytes+Scheduled.RetainedMetadataBytes+Routes.RetainedMetadataBytes+Proposals.RetainedMetadataBytes;
        internal int ScheduledId(NavScheduledRequest value)
        {
            int id=Scheduled.Include(value);
            if(!recipes.ContainsKey(value.Identity)){
                if(frozen)throw new InvalidOperationException("Navigation recipes are sealed.");
                NavigationAliasMetadata.Grow(recipes,allocation);recipes.Add(value.Identity,value);
            }
            Providers.Include(value.Provider);if(value.Region!=null)Regions.Include(value.Region);return id;
        }
        internal int GraphId(LayeredNavigationGraph value)
        {int id=Graphs.Include(value);Providers.Include(value.PhysicalProvider);return id;}
        internal int FieldId(NavSharedTerminalField value)
        {int id=Fields.Include(value);GraphId(value.Graph);Regions.Include(value.Region);return id;}
        internal int RouteId(NavTypedRoute value)=>Routes.Include(value);
        internal int ProposalId(NavArrivalProposal value){int id=Proposals.Include(value);RouteId(value.Route);return id;}
        internal int CompletionRecipeId(NavScheduledCompletion value)
        {if(value==null||!recipes.TryGetValue(value.Identity,out var recipe))throw new ArgumentException("Missing physical completion recipe alias.");return Scheduled.Include(recipe);}
        internal void Freeze()
        {if(frozen)return;Requests.Freeze();Geometries.Freeze();Providers.Freeze();Graphs.Freeze();Fields.Freeze();Regions.Freeze();Scheduled.Freeze();Routes.Freeze();Proposals.Freeze();hashStorage=NavigationAliasHashStorage.Seal(recipes,allocation);frozen=true;scope=null;allocation=null;}
    }
}
