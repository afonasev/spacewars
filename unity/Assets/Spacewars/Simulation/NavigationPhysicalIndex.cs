using System;

namespace Spacewars.Simulation
{
    internal enum NavPhysicalStorageKind
    {
        Scheduler=1,Job,Completion,Queue,QueueRecord,Index,IndexSlot,Map,AvlNode,
        List,Directory,DirectoryBlock,Page,Array,Leg,Metadata,Graph,Compiler,Field,
        Connector,Singleton,FieldFrontier,PackedField,PackedSlots,ProviderReferences,ProviderReference,Polygon,Geometry,TraversalArea,LayerSurface,LayerPortal,LayeredProvider,GraphBinding,MapSupport,SurfaceTransition,PartitionSeam,TerminalRegion,Recipe,PartitionEdges,PrivatePartitionSeam,TerrainLocations,PartitionProvider,PartitionCompiler,SubscriptionMetadata,TypedRoute,SharedConnector,PersistentQueue,ArrivalMember,ArrivalProfile,ArrivalWorkingBinding,ArrivalOccupant,ArrivalSource,ArrivalOccupancy,ArrivalInput,ArrivalProof,ArrivalProposal,ArrivalWork,ArrivalPreparedClearance,ArrivalRouteRow,HostRoot,HostRequest,HostProvider,HostRow,HostAnswer,HostCorridor,HostProjection,HostArrivalIntents,HostArrivalHistory,HostAuthority,HostEnvelope,AuthorityMember,AuthorityGroup,AuthorityCatalog,AuthorityCatalogTerrain,InstalledExecution,AuthorityCatalogRoutes,ArrivalAttemptSnapshot,ArrivalProviderSnapshot,ArrivalCatalog,AuthorityCatalogArrival,ArrivalLeaseSnapshot,ArrivalLeaseCatalog,AuthorityCatalogLeases,AuthorityCatalogProbes,LegacyProbeAnswer,AuthorityCatalogAnswers,HostEnvelopePublication,HostRootQuantum,AuthorityCatalogLedgerLayout,ArrivalProfileCrowdV1,TrafficAuthority,HostEnvelopeTraffic,QueryRecipeV1,QuerySingletonV1,QueryArrivalWorkV1,GroupQueryRecipeV1,GroupQueryArrivalWorkV1,HostRootFairArrivalV1,DemandQueryRecipeV1,GroupDemandQueryRecipeV1,DemandQueryArrivalWorkV1,GroupDemandQueryArrivalWorkV1,DemandQueryWorkV1,DemandQueryJobV1,DemandQueryRecipeV2,GroupDemandQueryRecipeV2,DemandCatalogV2,DemandQueryWorkV2,DemandSchedulerV2,DemandArrivalWorkV2,GroupDemandArrivalWorkV2,HistoricalArrivalStateV2,DemandQueryRecipeV3,GroupDemandQueryRecipeV3,DemandCatalogV3,DemandQueryWorkV3,DemandSchedulerV3,DemandArrivalWorkV3,GroupDemandArrivalWorkV3,HistoricalArrivalStateV3,DemandArrivalWorkV4,GroupDemandArrivalWorkV4,HistoricalArrivalStateV4,LazyPartitionStoreV1,LazyPartitionStoreV2,ExclusionStateV1,CertifiedDemandQueryJobV2
    }
    internal enum NavPhysicalValueKind
    {
        None=0,Boolean,Integer,Long,Unsigned,Double,Text,Point,Location,GridCoordinate,
        PortalEndpoints,GraphNode,GraphArc,QueueEntry,FieldCell,TypedLeg,Storage,
        Request,Job,Completion,CacheField,SubscriberLink,ProviderReference,PhysicalReference,LegTrace,Obstacle,SurfaceDeclaration,PortalDeclaration,MetadataReference,ConnectorResult,NameReference,NameKey,SupportDeclaration,TransitionDeclaration,PartitionSeamDeclaration,PrivatePartitionSeamDeclaration,ReadOnlyArcList,PagedArcList,IntegerQueueRecord,RequestQueueRecord,LegQueueRecord,NameLongIndexSlot,NameSubscriberIndexSlot,JobQueueRecord,CompletionQueueRecord,CacheFieldQueueRecord,ProviderReferenceQueueRecord,ProviderReferenceQueue,ArrivalOccupantDeclaration,ArrivalMemberDeclaration,ArrivalWorkingBindingDeclaration,ArrivalProposalDeclaration,TerminalRegionDeclaration,RetainedIntegerArray,OwnedText,ArrivalRouteRow,ArrivalPreparedClearance,ArrivalStatus,NamePreparedIndexSlot,IntegerArrivalRowIndexSlot,LongArrivalStatusIndexSlot,NameQueueRecord,IncrementalCacheField,SeamBoundPolicyV4,RemovalPolicyV6,ExactCostPolicyV5,ExactArrivalPolicyV7,ExactCost,ExactQueueEntry,ExactAstarPolicyV6,ExactAstarArrivalPolicyV8,LazyPartitionPolicyV7,LazyArrivalPolicyV9,CertifiedLazyPartitionPolicyV8,CertifiedLazyArrivalPolicyV10,OriginRectangleWorkV1,RectangleOriginsPolicyV9,RectangleOriginsArrivalPolicyV11,TerminalRectanglePolicyV10,TerminalRectangleArrivalPolicyV12,TerminalCornerPolicyV11,TerminalCornerArrivalPolicyV13,OriginShadowPolicyV12,OriginShadowArrivalPolicyV14,OwnedArrivalAnchorPolicyV15,OwnedArrivalAnchorV1,TerminalCostPolicyV13,TerminalCostArrivalPolicyV16, ReplayOnlyProjectionV1 = 89, AccruedAuthorityCreditsV1 = 90, Page0OccupancyArrivalPolicyV17 = 91, GroupMarchProgressV2 = 92, InstalledGeometryMemberV2 = 93, InstalledGeometryCertificateV2 = 94, InstalledExecutionV2 = 95, CertifiedGroupCorridor115V1 = 96, CertifiedGroupArrival115V1 = 97, ArrivalProfileCorridor115V1 = 98
    }
    internal readonly struct NavPhysicalTag : IEquatable<NavPhysicalTag>
    {
        internal readonly int Encoded;
        internal NavPhysicalTag(NavPhysicalStorageKind storage,NavPhysicalValueKind key=NavPhysicalValueKind.None,NavPhysicalValueKind value=NavPhysicalValueKind.None)
        {
            if(!Enum.IsDefined(typeof(NavPhysicalStorageKind),storage)||!Enum.IsDefined(typeof(NavPhysicalValueKind),key)||!Enum.IsDefined(typeof(NavPhysicalValueKind),value))throw new ArgumentException("Unknown physical storage tag.");
            Encoded=((int)storage<<24)|((int)key<<12)|(int)value;
        }
        internal NavPhysicalStorageKind Storage=>(NavPhysicalStorageKind)(Encoded>>24);
        internal NavPhysicalValueKind Key=>(NavPhysicalValueKind)((Encoded>>12)&4095);
        internal NavPhysicalValueKind Value=>(NavPhysicalValueKind)(Encoded&4095);
        internal static NavPhysicalTag Decode(int encoded)
        {var tag=new NavPhysicalTag((NavPhysicalStorageKind)(encoded>>24),(NavPhysicalValueKind)((encoded>>12)&4095),(NavPhysicalValueKind)(encoded&4095));if(tag.Encoded!=encoded)throw new ArgumentException("Invalid physical tag encoding.");return tag;}
        // Factories must expose their exact concrete tag.
        internal bool AdmitsDefinition(NavOwnedStorage storage)=>
            storage is INavPhysicalBody body&&body.PhysicalTag.Encoded==Encoded||
            storage is NavTypedLeg&&Encoded==new NavPhysicalTag(NavPhysicalStorageKind.Leg).Encoded||
            storage is NavOwnedText&&Encoded==new NavPhysicalTag(NavPhysicalStorageKind.Metadata,value:NavPhysicalValueKind.Text).Encoded;
        internal bool Valid=>Encoded>0&&Enum.IsDefined(typeof(NavPhysicalStorageKind),(NavPhysicalStorageKind)(Encoded>>24))&&Enum.IsDefined(typeof(NavPhysicalValueKind),(NavPhysicalValueKind)((Encoded>>12)&4095))&&Enum.IsDefined(typeof(NavPhysicalValueKind),(NavPhysicalValueKind)(Encoded&4095));
        public bool Equals(NavPhysicalTag other)=>Encoded==other.Encoded;
        public override bool Equals(object other)=>other is NavPhysicalTag tag&&Equals(tag);
        public override int GetHashCode()=>Encoded;
    }
    // These IDs are snapshot traversal IDs. Capture epoch and visitation state
    // stay out of wire bytes; allocation history and addresses supply no IDs.
    // A codec must visit every typed edge and emit/validate each body's shape.
    internal sealed class NavPhysicalCaptureIndex : NavOwnedStorage
    {
        internal const int MaxDefinitions=(int)(LayeredNavigationScheduler.MaxRetainedBytes/(64+CaptureHeaderBytes));
        private const int Visiting=unchecked((int)0x80000000);
        private readonly NavOwnedStorage root;
        private readonly INavStorageCaptureLane lane;
        private readonly long epoch;
        private int nextId,inProgress,completed;
        private bool finished,released,ownsRoot,ownsLane;
        private NavPhysicalCaptureIndex(NavOwnedStorage root,INavStorageCaptureLane lane,long epoch,INavStorageReservation reservation):base(128)
        {
            this.root=root;this.lane=lane;this.epoch=epoch;
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Register(this);root.Retain();ownsRoot=true;Retain();}ownsLane=true;
        }
        internal static NavPhysicalCaptureIndex CreateOwned(NavOwnedStorage immutableRoot,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,immutableRoot);
            if(!immutableRoot.IsImmutableCaptureRoot)throw new ArgumentException("Capture requires a sealed immutable frontier root.");
            if(!(reservation is INavStorageLedgerIdentity identity)||!(identity.Identity is INavStorageCaptureLane lane))throw new ArgumentException("Missing physical capture lane.");
            if(!lane.TryEnterCapture(out long epoch))throw new InvalidOperationException("Physical capture lane is occupied.");
            try{return new NavPhysicalCaptureIndex(immutableRoot,lane,epoch,NavStorageLayout.Before(reservation,128));}catch{lane.ExitCapture(epoch);throw;}
        }
        internal bool Begin(NavOwnedStorage storage,NavPhysicalTag tag,out int id)
        {
            if(finished||released)throw new InvalidOperationException("Physical capture index is closed.");RequireEdge(storage);if(!tag.Valid)throw new ArgumentException("Unknown physical reference kind.");
            if(nextId==0&&!ReferenceEquals(storage,root))throw new ArgumentException("Capture traversal must start at its immutable root.");
            if(storage.CaptureEpoch==epoch){
                if((storage.CaptureTag&~Visiting)!=tag.Encoded)throw new ArgumentException("Contradictory physical reference kind.");
                if((storage.CaptureTag&Visiting)!=0)throw new ArgumentException("Physical ownership cycle.");id=storage.CaptureId;return false;
            }
            if(nextId==MaxDefinitions)throw new ArgumentException("Physical definition capacity exceeded.");
            id=nextId++;storage.CaptureEpoch=epoch;storage.CaptureId=id;storage.CaptureTag=tag.Encoded|Visiting;inProgress++;return true;
        }
        internal void Complete(NavOwnedStorage storage,NavPhysicalTag tag)
        {
            if(finished||released)throw new InvalidOperationException("Physical capture index is closed.");RequireEdge(storage);
            if(storage.CaptureEpoch!=epoch||storage.CaptureTag!=(tag.Encoded|Visiting))throw new ArgumentException("Physical definition was not opened with this kind.");
            storage.CaptureTag=tag.Encoded;inProgress--;completed++;
        }
        internal int Finish()
        {
            if(finished||released||inProgress!=0||nextId==0||completed!=nextId||root.CaptureEpoch!=epoch||(root.CaptureTag&Visiting)!=0)throw new InvalidOperationException("Physical capture definitions are incomplete.");finished=true;return completed;
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){if(ownsRoot)root.Release();if(ownsLane)lane.ExitCapture(epoch);}
    }
    internal readonly struct NavPhysicalReference
    {
        internal readonly NavOwnedStorage Storage;
        internal readonly NavPhysicalTag Tag;
        internal NavPhysicalReference(NavOwnedStorage storage,NavPhysicalTag tag){Storage=storage;Tag=tag;}
    }
    // Temporary resolution is a charged paged slot vector, never an external
    // identity dictionary or a per-definition managed ticket. Definitions may
    // arrive postorder with dense traversal IDs assigned preorder.
    internal sealed class NavPhysicalResolution : NavOwnedStorage
    {
        private NavPagedList<NavPhysicalReference> slots;
        private readonly int maximum;
        private readonly INavStorageCaptureLane lane;private readonly long epoch;private bool ownsLane;
        private int defined;
        private bool finished,released,faulted;
        private NavPhysicalResolution(int count,INavStorageReservation reservation,INavStorageCaptureLane lane,long epoch):base(128)
        {
            maximum=count;this.lane=lane;this.epoch=epoch;using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Register(this);Retain();}
            try{slots=NavPagedList<NavPhysicalReference>.Create(reservation.TryReserve);ownsLane=true;}catch{ReleaseStorage();throw;}
        }
        internal static NavPhysicalResolution CreateOwned(int count,INavStorageReservation reservation)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));if(count<1||count>NavPhysicalCaptureIndex.MaxDefinitions)throw new ArgumentException("Invalid physical definition count.");
            if(!(reservation is INavStorageLedgerIdentity identity)||!(identity.Identity is INavStorageCaptureLane lane))throw new ArgumentException("Missing physical resolution lane.");
            if(!lane.TryEnterCapture(out long epoch))throw new InvalidOperationException("Physical indexing lane is occupied.");
            try{return new NavPhysicalResolution(count,NavStorageLayout.Before(reservation,128),lane,epoch);}catch{lane.ExitCapture(epoch);throw;}
        }
        internal void Define(int id,NavPhysicalTag tag,NavOwnedStorage storage)
        {
            if(finished||released||faulted)throw new InvalidOperationException("Physical resolution is closed.");
            if(id<0||id>=maximum||!tag.Valid||!tag.AdmitsDefinition(storage))throw new ArgumentException("Invalid or unsupported physical definition ID/kind.");RequireEdge(storage);
            if(storage.CaptureEpoch==epoch)throw new ArgumentException("Physical object already has a canonical definition ID.");
            if(id<slots.Count&&slots[id].Storage!=null)throw new ArgumentException("Duplicate physical definition ID.");
            try{while(slots.Count<=id)slots.Add(default);slots[id]=new NavPhysicalReference(storage,tag);storage.CaptureEpoch=epoch;storage.CaptureId=id;storage.CaptureTag=tag.Encoded;defined++;}catch{faulted=true;throw;}
        }
        internal NavOwnedStorage Resolve(int id,NavPhysicalTag expected)
        {
            if(released||faulted)throw new InvalidOperationException("Physical resolution is closed.");
            if(id<0||id>=slots.Count||!expected.Valid||slots[id].Storage==null||!slots[id].Tag.Equals(expected))throw new ArgumentException("Undefined, cyclic or wrong-kind physical reference.");return slots[id].Storage;
        }
        internal void Finish(){if(finished||released||faulted||defined!=maximum)throw new ArgumentException("Missing physical definitions.");finished=true;slots.Seal();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){slots?.ReleaseStorage();if(ownsLane)lane.ExitCapture(epoch);}
    }
}
