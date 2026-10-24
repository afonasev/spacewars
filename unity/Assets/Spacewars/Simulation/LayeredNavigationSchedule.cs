using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed class NavEpochQuota
    {
        public NavEpochQuota(int graph, int field, int connectors, int? bookkeeping = null, int? maintenance = null)
        {
            if (graph < 0 || field < 0 || connectors < 0 || bookkeeping < 0 || maintenance < 0)
                throw new ArgumentOutOfRangeException("Negative route quota.");
            Graph = graph; Field = field; Connectors = connectors;
            Bookkeeping = bookkeeping ?? (int)Math.Min(int.MaxValue, (long)graph + field + connectors);
            Maintenance = maintenance ?? 256;
        }
        public int Graph { get; } public int Field { get; } public int Connectors { get; }
        public int Bookkeeping { get; } public int Maintenance { get; }
    }
    public sealed partial class NavScheduledRequest
    {
        public NavScheduledRequest(ILayeredNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits, NavTerminalRegion region,
            NavRouteSubscription subscription, int maxLabels, bool singleton = false, bool query = false, bool groupQuery = false, bool demandQuery = false, bool demandQueryV2 = false, bool demandQueryV3 = false, bool demandQueryV4 = false, bool demandQueryV5 = false, bool demandQueryV6 = false, bool lazyPartition = false, bool certifiedLazyPartition = false, bool rectangularOrigins = false, bool terminalRectangleProof = false, bool terminalCornerProof = false, bool originShadowProof = false, bool terminalCostProof = false, bool certifiedGroupCorridor = false)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Clearance = clearance ?? throw new ArgumentNullException(nameof(clearance));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            if (!singleton && region == null) throw new ArgumentNullException(nameof(region));
            if(query&&(singleton||region==null||!(provider is PartitionNavigationProvider)))throw new ArgumentException("Invalid query-mode recipe.");
            if(groupQuery&&!query)throw new ArgumentException("Group query requires explicit query mode.");
            if(terminalCostProof&&!originShadowProof)throw new ArgumentException("Terminal cost requires Origins shadow policy.");
            if(originShadowProof&&!terminalCornerProof)throw new ArgumentException("Origin shadow requires terminal corner policy.");
            if(terminalCornerProof&&!terminalRectangleProof)throw new ArgumentException("Terminal corner proof requires terminal rectangle proof.");
            if(terminalRectangleProof&&!rectangularOrigins)throw new ArgumentException("Terminal rectangle proof requires rectangular origins.");
            Region = region; Singleton = singleton; Query = query; GroupQuery=groupQuery;
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            if (maxLabels < 1 || maxLabels > 1048576) throw new ArgumentOutOfRangeException(nameof(maxLabels));
            MaxLabels = maxLabels;
            bool certified=certifiedLazyPartition&&demandQuery&&(certifiedGroupCorridor||!NavDemandQueryWork.LazyEligible(this))&&NavDemandQueryWork.CertifiedEligible(this);bool lazy=(lazyPartition&&demandQuery&&NavDemandQueryWork.LazyEligible(this))||certified;DemandQuery=demandQuery&&(lazy||NavDemandQueryWork.Eligible(this));DemandQueryV2=(demandQueryV2||lazy)&&DemandQuery;DemandQueryV3=(demandQueryV3||lazy)&&DemandQueryV2;exactMode=(byte)(certified&&certifiedGroupCorridor&&terminalCostProof&&groupQuery?9:certified&&terminalCostProof&&originShadowProof?8:certified&&originShadowProof&&terminalCornerProof&&terminalRectangleProof&&rectangularOrigins?7:certified&&terminalCornerProof&&terminalRectangleProof&&rectangularOrigins?6:certified&&terminalRectangleProof&&rectangularOrigins?5:certified&&rectangularOrigins?4:certified?3:lazy?2:demandQueryV6&&DemandQueryV3?1:0);DemandQueryV5=(demandQueryV5||DemandQueryV6)&&DemandQueryV3;MetricContract=DemandQueryV6?LayeredNavigationProvider.DistanceMetricContract:NavDistanceMetricContract.Unknown;DemandQueryV4=demandQueryV4&&DemandQueryV3&&!DemandQueryV5;
            ProviderBytes = provider.EstimatedRetainedBytes;
            GraphKey = new NavGraphBinding(provider.Binding, clearance, profile).StableKey;
            CompileKey = NavContract.Digest(w => {
                w.Write(GraphKey); w.Write(limits.MaxSamples); w.Write(limits.MaxNodes); w.Write(limits.MaxArcs);if(DemandQuery)w.Write(CertifiedGroupCorridor?"certified-group-corridor115-v1":TerminalCostProof?"partition-query-terminal-cost-v13":OriginShadowProof?"partition-query-origin-shadow-v12":TerminalCornerProof?"partition-query-terminal-corner-v11":TerminalRectangleProof?"partition-query-terminal-rectangle-v10":RectangularOrigins?"partition-query-rectangle-origins-v9":CertifiedLazyPartition?"partition-query-certified-lazy-v8":LazyPartition?"partition-query-lazy-v7":DemandQueryV6?"partition-query-exact-astar-v6":DemandQueryV5?"partition-query-exact-v5":DemandQueryV4?"partition-query-seam-v4":DemandQueryV3?"partition-query-surface-v3":DemandQueryV2?"partition-query-catalog-v2":"partition-query-lattice-v1");if(DemandQueryV6)w.Write((int)MetricContract);
            });
            ArtifactKey = NavContract.Digest(w => {
                w.Write(CompileKey); w.Write(singleton);
                w.Write(singleton ? subscription.StableKey : region.StableKey); w.Write(maxLabels);
                if(query){w.Write("partition-forward-query-v1");w.Write(subscription.StableKey);}
                if(groupQuery)w.Write("partition-group-query-v1");
            });
            Identity = new NavScheduledIdentity(this);
        }
        public static NavScheduledRequest ForSingleton(ILayeredNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits, NavRouteSubscription subscription, int maxStates)
            => new NavScheduledRequest(provider, clearance, profile, limits, null, subscription, maxStates, true);
        public ILayeredNavigationProvider Provider { get; } public NavClearanceProfile Clearance { get; }
        public NavGraphProfile Profile { get; } public NavGraphCompileLimits Limits { get; }
        public NavTerminalRegion Region { get; } public NavRouteSubscription Subscription { get; }
        public bool Singleton { get; }
        public bool Query { get; }
        public bool GroupQuery { get; }
        public bool DemandQuery { get; }
        // Reuse the previous one-byte V6 policy field: old recipe layout/charge stays exact.
        private readonly byte exactMode;public bool LazyPartition=>exactMode>=2;public bool CertifiedLazyPartition=>exactMode>=3;public bool RectangularOrigins=>exactMode>=4;public bool TerminalRectangleProof=>exactMode>=5;public bool TerminalCornerProof=>exactMode>=6;public bool OriginShadowProof=>exactMode>=7;public bool TerminalCostProof=>exactMode>=8;public bool CertifiedGroupCorridor=>exactMode==9;
        public bool DemandQueryV2 { get; } public bool DemandQueryV3 { get; } public bool DemandQueryV4 { get; } public bool DemandQueryV5 { get; } public bool DemandQueryV6=>exactMode!=0;
        internal NavDistanceMetricContract MetricContract { get; }
        public NavScheduledIdentity Identity { get; }
        public int MaxLabels { get; } public string GraphKey { get; } public string CompileKey { get; } public string ArtifactKey { get; }
        internal long ProviderBytes { get; }
    }
    public sealed partial class NavScheduledCompletion : NavOwnedStorage
    {
        internal NavScheduledCompletion(NavScheduledRequest request, NavSolveStatus status, NavTypedRoute route,
            long submittedEpoch, long completedEpoch,Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {
            using(var a=new NavStorageAllocation(reserve)){if(!alreadyReserved)a.Reserve(128);a.Register(this);if(IsRegistered){if(route!=null)RequireEdge(route);RequireEdge(request.StorageRoot);}route?.Retain();recipeStorage=request.StorageRoot;recipeStorage?.Retain();Retain();}
            Identity = request.Identity; Subscription = request.Subscription; Status = status; Route = route;
            SubmittedEpoch = submittedEpoch; CompletedEpoch = completedEpoch;
        }
        private readonly NavOwnedStorage recipeStorage;
        protected override void ReleaseEdges(){Route?.Release();recipeStorage?.Release();}
        public NavScheduledIdentity Identity { get; }
        public NavRouteSubscription Subscription { get; } public NavSolveStatus Status { get; } public NavTypedRoute Route { get; }
        public long SubmittedEpoch { get; } public long CompletedEpoch { get; }
        public long QueueAge => Math.Max(0, CompletedEpoch - SubmittedEpoch);
    }
    public sealed class NavScheduledIdentity
    {
        internal NavScheduledIdentity(NavScheduledRequest request)
        { Subscription = request.Subscription; GraphKey = request.GraphKey; ArtifactKey = request.ArtifactKey; }
        public NavRouteSubscription Subscription { get; }
        public string StableKey => Subscription.StableKey;
        public string GraphKey { get; } public string ArtifactKey { get; }
    }
    public sealed class NavSchedulerCounters
    {
        internal NavSchedulerCounters(long logicalGraph, long physicalGraph, long logicalField, long physicalField,
            long connectors, long cacheHits, long graphBuilds, long fieldBuilds, long cancelled, long evictions,
            long maintenanceWork,
            int pending, int ready, int cacheFields, long cacheBytes, long peakCacheBytes, int peakQueue,
            int peakFieldLabels, int peakFrontier, long retainedBytes, long peakRetainedBytes, long peakQueueAge)
        {
            LogicalGraphWork = logicalGraph; PhysicalGraphWork = physicalGraph;
            LogicalFieldWork = logicalField; PhysicalFieldWork = physicalField;
            ConnectorWork = connectors; CacheHits = cacheHits; GraphBuilds = graphBuilds;
            FieldBuilds = fieldBuilds; Cancelled = cancelled; Evictions = evictions;
            MaintenanceWork = maintenanceWork;
            PendingSubscriptions = pending; ReadyResults = ready; CacheFields = cacheFields;
            CacheBytes = cacheBytes; PeakCacheBytes = peakCacheBytes; PeakQueue = peakQueue;
            PeakFieldLabels = peakFieldLabels; PeakFrontier = peakFrontier;
            RetainedBytes = retainedBytes; PeakRetainedBytes = peakRetainedBytes; PeakQueueAge = peakQueueAge;
        }
        public long LogicalGraphWork { get; } public long PhysicalGraphWork { get; }
        public long LogicalFieldWork { get; } public long PhysicalFieldWork { get; }
        public long ConnectorWork { get; } public long CacheHits { get; }
        public long GraphBuilds { get; } public long FieldBuilds { get; }
        public long Cancelled { get; } public long Evictions { get; }
        public long MaintenanceWork { get; }
        public int PendingSubscriptions { get; } public int ReadyResults { get; }
        public int CacheFields { get; } public long CacheBytes { get; } public long PeakCacheBytes { get; }
        public int PeakQueue { get; } public int PeakFieldLabels { get; } public int PeakFrontier { get; }
        public long RetainedBytes { get; } public long PeakRetainedBytes { get; } public long PeakQueueAge { get; }
    }

    // Pure authority-neutral queue. An authority adapter supplies current exact
    // subscriptions on every Advance and when taking results. It never infers
    // group identity from equal terminal geometry.
    public sealed partial class LayeredNavigationScheduler : NavOwnedStorage
    {
        public const int MaxQueue = 4096, MaxCachedFields = 256;
        public const long MaxCacheBytes = 64L * 1024 * 1024;
        // Temporary session headroom includes live actors, preparation and pinned roots.
        public const long MaxRetainedBytes = 512L * 1024 * 1024;
        internal enum JobPhase { Direct, Graph, Field, Connectors, Origin, PartialFieldProbePaid }
        internal sealed partial class Job : NavOwnedStorage
        {
            private NavScheduledRequest request,graphProviderOwner;
            private NavOwnedStorage requestProvider,graphProvider;
            internal NavScheduledRequest Request {get=>request;set{Set(ref requestProvider,value?.StorageRoot??(value?.Provider as INavStorageFacade)?.StorageRoot);request=value;}}
            internal NavScheduledRequest GraphProviderOwner {get=>graphProviderOwner;set{Set(ref graphProvider,value?.StorageRoot??(value?.Provider as INavStorageFacade)?.StorageRoot);graphProviderOwner=value;}}
            internal NavPersistentQueue<NavScheduledRequest> Subscribers {get;private set;}
            internal long QueueNode;
            internal JobPhase Phase;
            private LayeredNavigationCompileWork compiler,completedCompiler;
            private LayeredNavigationGraph graph;
            private NavSharedTerminalField field;
            private NavIncrementalSharedRoute connector;
            private NavIncrementalSingletonRoute singleton;
            private NavTypedRoute directRoute;
            private NavDemandQueryWork demand;
            internal NavDemandQueryWork Demand {get=>demand;set=>Set(ref demand,value);}
            internal LayeredNavigationCompileWork Compiler {get=>compiler;set=>Set(ref compiler,value);}
            internal LayeredNavigationCompileWork CompletedCompiler {get=>completedCompiler;set=>Set(ref completedCompiler,value);}
            internal LayeredNavigationGraph Graph {get=>graph;set=>Set(ref graph,value);}
            internal NavSharedTerminalField Field {get=>field;set=>Set(ref field,value);}
            internal NavIncrementalSharedRoute Connector {get=>connector;set=>Set(ref connector,value);}
            internal NavIncrementalSingletonRoute Singleton {get=>singleton;set=>Set(ref singleton,value);}
            internal NavTypedRoute DirectRoute {get=>directRoute;set=>Set(ref directRoute,value);}
            private bool frozen;
            private void Set<T>(ref T slot,T value) where T:NavOwnedStorage
            {if(frozen)throw new InvalidOperationException("Committed job cannot be mutated.");if(IsRegistered&&value!=null)RequireEdge(value);value?.Retain();var old=slot;slot=value;old?.Release();}
            internal Job():this((Func<long,bool>)null){}
            internal Job(Func<long,bool> reserve,bool initialize=true,bool alreadyReserved=false):base(512)
            {using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(512);allocation.Register(this);Retain();}if(initialize){try{Subscribers=NavPersistentQueue<NavScheduledRequest>.Create(reserve);}catch{Release();throw;}}}
            internal Job Fork(Func<long,bool> reserve)
            {
                if(!frozen)throw new InvalidOperationException("Seal job before preparing a fork.");
                if(IsRegistered){if(!(reserve?.Target is INavStorageReservation owner))throw new ArgumentException("Missing job ledger.");NavStorageAdmission.RequireOwned(owner,this);}
                var fork=new Job(NavStorageLayout.Before(reserve,512),false,true);try{
                    fork.Subscribers=Subscribers.ForkPrepared(reserve);
                    fork.Request=Request;fork.GraphProviderOwner=GraphProviderOwner;fork.QueueNode=QueueNode;fork.Phase=Phase;
                    fork.Demand=Demand;fork.Compiler=Compiler;fork.CompletedCompiler=CompletedCompiler;fork.Graph=Graph;fork.Field=Field;fork.Connector=Connector;fork.Singleton=Singleton;fork.DirectRoute=DirectRoute;
                    fork.GraphDebt=GraphDebt;fork.FieldDebt=FieldDebt;fork.GraphWork=GraphWork;fork.FieldWork=FieldWork;fork.SetNamed(ref fork.ActiveConnectorKey,ref fork.activeKeyReference,ActiveConnectorKey,activeKeyReference);fork.Failure=Failure;
                    fork.SetOrigin(OriginBinding,originMetadata);fork.SetNamed(ref fork.DirectSubscription,ref fork.directKeyReference,DirectSubscription,directKeyReference);fork.OriginCursor=OriginCursor;fork.OriginValidated=OriginValidated;fork.OriginComplete=OriginComplete;fork.HasIngress=HasIngress;
                    return fork;
                }catch{fork.Release();throw;}
            }
            internal void Seal()
            {if(frozen)return;Demand?.Seal();Compiler?.SealPrepared();CompletedCompiler?.SealPrepared();Field?.SealPrepared();Connector?.SealPrepared();Singleton?.SealPrepared();Subscribers.Seal();frozen=true;}
            internal void PrepareCompiler(Func<long,bool> reserve){var fork=Compiler.ForkPrepared(reserve);AdoptCompiler(fork);}
            internal void PrepareField(Func<long,bool> reserve){var fork=Field.ForkPrepared(reserve);AdoptField(fork);}
            internal void PrepareConnector(Func<long,bool> reserve){var fork=Connector.ForkPrepared(Field,reserve);AdoptConnector(fork);}
            internal void PrepareSingleton(Func<long,bool> reserve){var fork=Singleton.ForkPrepared(reserve);AdoptSingleton(fork);}
            protected override void ReleaseEdges()
            {demand?.Release();Subscribers?.Release();activeKeyReference.Owner?.Release();directKeyReference.Owner?.Release();originMetadata?.Release();requestProvider?.Release();graphProvider?.Release();compiler?.Release();completedCompiler?.Release();graph?.Release();field?.Release();connector?.Release();singleton?.Release();directRoute?.Release();}
            internal void AdoptDemand(NavDemandQueryWork value){Demand=value;value.ReleaseOwner();}
            internal void AdoptCompiler(LayeredNavigationCompileWork value){Compiler=value;value.ReleaseOwner();}
            internal void AdoptField(NavSharedTerminalField value){Field=value;value.ReleaseOwner();}
            internal void AdoptConnector(NavIncrementalSharedRoute value){Connector=value;value.ReleaseOwner();}
            internal void AdoptSingleton(NavIncrementalSingletonRoute value){Singleton=value;value.ReleaseOwner();}
            internal long GraphDebt, FieldDebt;
            internal int GraphWork, FieldWork;
            internal string ActiveConnectorKey;
            private NavNameReference activeKeyReference,directKeyReference;
            private void SetNamed(ref string slot,ref NavNameReference selector,string value,NavNameReference name)
            {
                if(frozen)throw new InvalidOperationException("Committed job cannot be mutated.");if(IsRegistered)name.Validate(value,this);
                name.Owner?.Retain();var old=selector.Owner;slot=value;selector=name;old?.Release();
            }
            internal void SetActiveSubscription(NavRouteSubscription value){var key=value==null?default:NavNameKey.Subscription(value);SetNamed(ref ActiveConnectorKey,ref activeKeyReference,key.Value,key.Reference);}
            internal void SetDirectSubscription(NavRouteSubscription value){var key=value==null?default:NavNameKey.Subscription(value);SetNamed(ref DirectSubscription,ref directKeyReference,key.Value,key.Reference);}
            internal NavSolveStatus? Failure;
            internal string OriginBinding, DirectSubscription;
            private NavOwnedText originMetadata;
            internal void SetOrigin(string binding,NavOwnedText metadata){Set(ref originMetadata,metadata);OriginBinding=binding;}
            internal int OriginCursor; internal bool OriginValidated, OriginComplete, HasIngress;
        }
        internal sealed partial class CacheField : NavOwnedStorage
        {
            internal readonly string Key;
            internal readonly NavSharedTerminalField Field;
            internal readonly NavScheduledRequest Recipe;
            internal readonly int Work;
            internal readonly bool Incremental;
            internal readonly long Bytes;
            private readonly NavOwnedStorage recipeStorage;
            internal CacheField(NavScheduledRequest recipe,NavSharedTerminalField field,int work,long bytes,Func<long,bool> reserve=null,bool alreadyReserved=false,bool incremental=false):base(128)
            {Incremental=incremental;using(var a=new NavStorageAllocation(reserve)){if(!alreadyReserved)a.Reserve(128);a.Register(this);if(IsRegistered){RequireEdge(field);RequireEdge(recipe.StorageRoot);}Key=recipe.ArtifactKey;Recipe=recipe;Field=field;Work=work;Bytes=bytes;field.SealPrepared();field.Retain();recipeStorage=recipe.StorageRoot??(recipe.Provider as INavStorageFacade)?.StorageRoot;recipeStorage?.Retain();Retain();}}
            protected override void ReleaseEdges(){Field?.Release();recipeStorage?.Release();}
        }
        private static readonly Func<long,bool> LocalAdmission=_=>true;
        private NavPersistentQueue<Job> jobs;
        private NavPersistentIndex<NavNameKey,long> jobsByKey;
        private NavPersistentIndex<NavNameKey,NavSubscriberLink> subscribers;
        private NavPersistentIndex<NavNameKey,long> submittedEpoch;
        private NavProviderReferences providerRefs;
        private NavPersistentQueue<NavScheduledCompletion> ready;
        private NavPersistentIndex<NavNameKey,long> cachedFields;
        private NavPersistentQueue<CacheField> lru;
        private string cachedGraphKey;
        private NavNameReference cachedGraphKeyReference;
        private void SetCachedGraphKey(NavScheduledRequest recipe)
        {
            MutableRoot();var key=recipe==null?default:NavNameKey.Compile(recipe);if(IsRegistered)key.Reference.Validate(key.Value,this);
            key.Reference.Owner?.Retain();var old=cachedGraphKeyReference.Owner;cachedGraphKey=key.Value;cachedGraphKeyReference=key.Reference;old?.Release();
        }
        private LayeredNavigationGraph cacheGraphRoot;
        private LayeredNavigationGraph cachedGraph {get=>cacheGraphRoot;set{value?.Retain();var old=cacheGraphRoot;cacheGraphRoot=value;old?.Release();}}
        private LayeredNavigationCompileWork cacheCompilerRoot;
        private LayeredNavigationCompileWork cachedGraphCompiler {get=>cacheCompilerRoot;set{value?.SealPrepared();value?.Retain();var old=cacheCompilerRoot;cacheCompilerRoot=value;old?.Release();}}
        private NavScheduledRequest graphRecipe;
        private NavOwnedStorage graphRecipeStorage;
        private NavScheduledRequest cachedGraphRecipe
        {get=>graphRecipe;set{var root=value?.StorageRoot;root?.Retain();var old=graphRecipeStorage;graphRecipe=value;graphRecipeStorage=root;old?.Release();}}
        private int cachedGraphWork;
        private long cachedGraphBytes, cacheBytes, peakCacheBytes;
        private long lastEpoch = -1, logicalGraph, physicalGraph, logicalField, physicalField;
        private long connectorWork, cacheHits, graphBuilds, fieldBuilds, cancelled, evictions, maintenanceWork;
        private long queuedBytes, readyBytes, providerBytes, peakRetainedBytes, peakQueueAge;
        private long externalReservationBytes;
        private int graphQuota, fieldQuota, connectorQuota, bookkeepingQuota, maintenanceQuota;
        private int peakQueue, peakLabels, peakFrontier;

        private Job preparingJob;
        private long corridorBookkeeping;internal long CertifiedBookkeeping=>corridorBookkeeping;internal void EnableCertifiedAccounting(){boundedMode=true;}
        private bool boundedMode;private NavDemandQueryWork corridorDonor;
        // Count every actual debit through one lane, including completion/cache
        // handoff. Accounting does not select or alter the independent policy.
        private void SpendBookkeeping(){bookkeepingQuota--;if(boundedMode)corridorBookkeeping++;}
        private bool demandV2,demandV3,demandV4,demandV5,demandV6,demandLazy,demandCertified,demandRectangle,demandTerminalRectangle,demandTerminalCorner,demandOriginShadow,demandTerminalCost;
        private NavLazyPartitionStore lazyCatalog;
        private void SetLazyCatalog(NavLazyPartitionStore value){if(IsRegistered&&value!=null)RequireEdge(value);value?.Retain();if(lazyCatalog!=null)ReleaseProviderRef(lazyCatalog.Recipe);var old=lazyCatalog;lazyCatalog=value;if(value!=null)AddProviderRef(value.Recipe);old?.Release();}
        private NavDemandQueryCatalog demandCatalog;
        private void SetDemandCatalog(NavDemandQueryCatalog value)
        {
            if(IsRegistered&&value!=null)RequireEdge(value);value?.Retain();
            if(demandCatalog!=null)ReleaseProviderRef(demandCatalog.Recipe);
            var old=demandCatalog;demandCatalog=value;
            if(value!=null)AddProviderRef(value.Recipe);old?.Release();
        }

        private string attemptKey;
        private bool sessionOwnsProviders;
        internal bool SessionOwnsProviders {get=>sessionOwnsProviders;set{MutableRoot();sessionOwnsProviders=value;}}
        private NavScheduledRequest attemptRecipe;
        private NavOwnedStorage attemptRecipeStorage;
        private NavScheduledRequest artifactOwner
        {get=>attemptRecipe;set{var root=value?.StorageRoot;root?.Retain();var old=attemptRecipeStorage;attemptRecipe=value;attemptRecipeStorage=root;old?.Release();}}
        private const long OwnerMetadataBytes=128;
        internal void BindAttemptArtifacts(string key)
        {
            MutableRoot();
            if(key==null||key.Length!=64||attemptKey!=null||jobs.Count!=0||cachedGraph!=null)throw new ArgumentException("Invalid attempt artifact ownership binding.");
            try{if(rootReservation!=null){var text=NavOwnedText.Copy(key,OwnedRootReservation);attemptBindingStorage=text;attemptKey=text.Value;}else attemptKey=key;}catch{rootFaulted=true;throw;}
        }
        internal bool HasAttemptOwner=>artifactOwner!=null;
        internal string AttemptBinding=>attemptKey;
        private long RetainedGraphBytes(LayeredNavigationGraph graph,NavScheduledRequest request)
        {
            // The published graph borrows the compiler's immutable output pages.
            // Its retained compiler root also holds checkpoint-only intermediate
            // storage. Count that owner once, rather than a MaxNodes projection.
            var compiler=ReferenceEquals(graph,cachedGraph)?cachedGraphCompiler:
                preparingJob!=null&&ReferenceEquals(preparingJob.Graph,graph)?preparingJob.CompletedCompiler:
                jobs.First!=null&&ReferenceEquals(jobs.First.Graph,graph)?jobs.First.CompletedCompiler:null;
            if(compiler!=null)return compiler.EstimatedRetainedBytes+128L+
                (attemptKey==null&&graph.EstimatedRetainedBytes==0?compiler.ProviderRetainedBytes:0);
            return GraphBytes(graph,request)-(attemptKey!=null&&graph.EstimatedRetainedBytes==0?request.ProviderBytes:0);
        }
        private static bool CertifiedFlat(NavScheduledRequest request)=>request.Provider is LayeredNavigationProvider flat&&flat.Id=="flat"&&flat.SemanticsVersion==1&&flat.Surfaces.Count==1&&flat.Surfaces[0].Id==NavLocation.FlatSurface&&flat.Portals.Count==0;
        private static string OriginBinding(NavScheduledRequest request)=>NavContract.Digest(w=>{
            w.Write(request.CompileKey);NavContract.Location(w,request.Subscription.Origin);w.Write(request.Subscription.HoldBinding??"");
            w.Write(request.Subscription.EntityId);w.Write(request.Subscription.Incarnation);w.Write(request.Subscription.MobilityRevision);
        });
        private void BeginOrigin(Job job)
        {
            var request=job.Subscribers.First;NavOwnedText owned=rootReservation==null?null:NavOwnedText.Digest(w=>WriteOriginBinding(w,request),OriginBindingWireBytes(request),OwnedRootReservation);
            try{string binding=owned?.Value??OriginBinding(request);
                if(job.OriginBinding!=binding){job.SetOrigin(binding,owned);job.OriginCursor=0;job.OriginValidated=false;job.OriginComplete=false;job.HasIngress=false;}
                job.Phase=CertifiedFlat(request)&&!job.OriginComplete?JobPhase.Origin:job.Field?.Status==NavSolveStatus.Ready?JobPhase.Connectors:JobPhase.Field;
            }finally{owned?.ReleaseOwner();}
        }
        private void SetEpoch(long epoch,NavEpochQuota quota)
        {
            if(epoch!=lastEpoch){lastEpoch=epoch;graphQuota=quota.Graph;fieldQuota=quota.Field;connectorQuota=quota.Connectors;bookkeepingQuota=quota.Bookkeeping;maintenanceQuota=quota.Maintenance;}
        }
        internal bool EndAttemptArtifacts(long epoch,NavEpochQuota quota)
        {
            MutableRoot();SetEpoch(epoch,quota);if(artifactOwner==null&&demandCatalog==null&&lazyCatalog==null)return true;
            if(jobs.Count!=0||ready.Count!=0)throw new InvalidOperationException("Attempt still has subscribers/results.");
            if(bookkeepingQuota==0)return false;
            SpendBookkeeping();ClearCache();UpdatePeak();return true;
        }
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            internal Checkpoint CopyLogical()=>(Checkpoint)MemberwiseClone();
            internal long LastEpoch, LogicalGraph, PhysicalGraph, LogicalField, PhysicalField;
            internal long ConnectorWork, CacheHits, GraphBuilds, FieldBuilds, Cancelled, Evictions, MaintenanceWork;
            internal long QueuedBytes, ReadyBytes, ProviderBytes, PeakRetainedBytes, PeakQueueAge;
            internal KeyValuePair<string, long>[] SubmittedEpoch;
            internal int GraphQuota, FieldQuota, ConnectorQuota, BookkeepingQuota, MaintenanceQuota;
            internal int PeakQueue, PeakLabels, PeakFrontier;
            internal string AttemptKey;
            internal NavScheduledRequest ArtifactOwner;
            internal LayeredNavigationCompileWork.Checkpoint OwnerGraph;
            internal NavSharedTerminalField.Checkpoint OwnerField;
            internal bool OwnerFieldIncremental;
            internal int OwnerGraphWork,OwnerFieldWork; internal long PeakCacheBytes;
            internal long ExternalReservationBytes;
            internal bool SessionProviders, RetentionBlocked,DemandV2,DemandV3,DemandV4,DemandV5,DemandV6,DemandLazy,DemandCertified,DemandRectangle,DemandTerminalRectangle,DemandTerminalCorner,DemandOriginShadow,DemandTerminalCost;
            internal long CorridorBookkeeping;internal bool BoundedMode;internal NavDemandQueryWork.Checkpoint CorridorDonor;internal NavScheduledRequest CorridorRecipe;
            internal NavDemandQueryCatalog.Checkpoint DemandCatalog;internal NavLazyPartitionStore.Checkpoint LazyCatalog;
            internal NavScheduledRequest CacheGraphRecipe;
            internal LayeredNavigationCompileWork.Checkpoint CacheGraph;
            internal int CacheGraphWork;
            internal CacheFieldState[] CacheFields;
            internal JobState[] Jobs;
            internal NavScheduledCompletion[] Ready;
        }
        internal sealed class CacheFieldState
        {
            internal NavScheduledRequest Recipe;
            internal NavSharedTerminalField.Checkpoint Field;
            internal bool Incremental;
            internal int Work;
        }
        internal sealed class JobState
        {
            internal JobState CopyLogical()=>(JobState)MemberwiseClone();
            internal string ArtifactKey;
            internal string[] SubscriptionKeys;
            internal int Phase, GraphWork, FieldWork;
            internal NavSolveStatus? Failure;
            internal long GraphDebt, FieldDebt;
            internal string ActiveConnectorKey;
            internal LayeredNavigationCompileWork.Checkpoint Compiler, Graph;
            internal NavSharedTerminalField.Checkpoint Field;
            internal NavIncrementalSharedRoute.Checkpoint Connector;
            internal NavIncrementalSingletonRoute.Checkpoint Singleton;
            internal NavDemandQueryWork.Checkpoint Demand;
            internal NavTypedRoute DirectRoute;
            internal NavScheduledRequest GraphProviderOwner;
            internal bool OwnedGraph,OwnedField,OriginValidated,OriginComplete,HasIngress;
            internal string OriginBinding,DirectSubscription; internal int OriginCursor;
        }
        internal NavEpochQuota RemainingQuota => new NavEpochQuota(graphQuota,fieldQuota,connectorQuota,bookkeepingQuota,maintenanceQuota);
        internal static NavTypedRoute DirectRoute(NavScheduledRequest request)
        {
            var from=request.Subscription.Origin;var to=request.Subscription.AssignedEndpoint;
            if(!request.Provider.IsValid(from,request.Clearance)||!request.Provider.IsValid(to,request.Clearance))return null;
            IReadOnlyList<NavTypedLeg> trace=null;
            if(request.Provider is PartitionNavigationProvider partition){
                if(partition.TryTrace(from,to.Position,request.Clearance,out var reached,out var found)&&reached.Equals(to))trace=found;
            }else if(from.SurfaceId==to.SurfaceId&&request.Provider.CanSweep(from,to,request.Clearance))
                trace=new[]{new NavTypedLeg(from,to,NavContract.Number(LayeredNavigationProvider.Distance(from.Position,to.Position)*request.Provider.SurfaceCost(from.SurfaceId)),NavTypedLegKind.Surface,null)};
            if(trace==null)return null;
            double cost=0;foreach(var leg in trace)cost+=leg.Cost;
            try{return new NavTypedRoute(request.Subscription,new List<NavTypedLeg>(trace),cost);}finally{foreach(var leg in trace)leg.ReleaseOwner();}
        }
        // Continuations stream their exact BinaryWriter transcript into SHA256.
        // Reserve the bounded writer/hash workspace and iteration metadata.
        internal long LogicalContinuationScratchBytes=>checked(NavContract.StreamingDigestScratchBytes+8192L+256L*(jobs.Count+ready.Count));
        internal string LogicalContinuationBinding => NavContract.StreamingDigest(w=>{
            if(boundedMode){w.Write("certified-group-corridor115-v1");w.Write(corridorBookkeeping);w.Write(corridorDonor!=null);if(corridorDonor!=null){w.Write(corridorDonor.BoundedRecipe.ArtifactKey);corridorDonor.WriteSeamContinuation(w);}}
            if(attemptKey!=null){w.Write(attemptKey);w.Write(artifactOwner?.ArtifactKey??"");}
            if(lazyCatalog!=null){w.Write("lazy-partial-cache-v1");w.Write(lazyCatalog.Recipe.ArtifactKey);w.Write(lazyCatalog.Cells.Count);foreach(var cell in lazyCatalog.Cells){w.Write(cell.Key);w.Write(cell.Value);}}
            w.Write(lastEpoch);w.Write(graphQuota);w.Write(fieldQuota);w.Write(connectorQuota);w.Write(bookkeepingQuota);w.Write(maintenanceQuota);
            w.Write(logicalGraph);w.Write(physicalGraph);w.Write(logicalField);w.Write(physicalField);w.Write(connectorWork);
            w.Write(cacheHits);w.Write(graphBuilds);w.Write(fieldBuilds);w.Write(cancelled);w.Write(evictions);w.Write(maintenanceWork);
            foreach(var job in jobs){w.Write(job.Request.ArtifactKey);w.Write((int)job.Phase);w.Write(job.GraphWork);w.Write(job.FieldWork);job.Demand?.WriteSeamContinuation(w);
                if(attemptKey!=null){w.Write(job.OriginBinding??"");w.Write(job.OriginCursor);w.Write(job.OriginValidated);w.Write(job.OriginComplete);w.Write(job.HasIngress);w.Write(job.DirectSubscription??"");}
                w.Write(job.GraphDebt);w.Write(job.FieldDebt);w.Write(job.ActiveConnectorKey??"");foreach(var r in job.Subscribers)w.Write(r.Subscription.StableKey);}
            foreach(var item in ready){w.Write(item.Subscription.StableKey);w.Write((int)item.Status);}
        });
        public Checkpoint Save()
        {
            var corridorSaves=new Dictionary<NavDemandQueryWork,NavDemandQueryWork.Checkpoint>();
            var states = new List<JobState>();
            foreach (var job in jobs) {
                var keys = new List<string>();
                foreach (var request in job.Subscribers) keys.Add(request.Subscription.StableKey);
                states.Add(new JobState {
                    ArtifactKey = job.Request.ArtifactKey, SubscriptionKeys = keys.ToArray(), Phase = (int)job.Phase,
                    GraphWork = job.GraphWork, FieldWork = job.FieldWork, GraphDebt = job.GraphDebt,
                    FieldDebt = job.FieldDebt, ActiveConnectorKey = job.ActiveConnectorKey,
                    Failure = job.Failure,
                    Compiler = job.Compiler?.Save(), Graph = ReferenceEquals(job.Graph,cachedGraph)?null:job.CompletedCompiler?.Save(),
                    Field = job.Field!=null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var ownerField)&&ReferenceEquals(job.Field,lru[ownerField].Field)?null:job.Field?.Save(), Connector = job.Connector?.Save(), Singleton = job.Singleton?.Save(),
                    Demand=job.Demand?.SaveWithAliases(corridorSaves),DirectRoute = job.DirectRoute,
                    OwnedGraph=job.Graph!=null&&ReferenceEquals(job.Graph,cachedGraph),
                    OwnedField=job.Field!=null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var fieldOwner)&&ReferenceEquals(job.Field,lru[fieldOwner].Field),
                    GraphProviderOwner=job.GraphProviderOwner,OriginBinding=job.OriginBinding,OriginCursor=job.OriginCursor,OriginValidated=job.OriginValidated,OriginComplete=job.OriginComplete,HasIngress=job.HasIngress,DirectSubscription=job.DirectSubscription
                });
            }
            return new Checkpoint {
                Version = 3, AttemptKey=attemptKey,ArtifactOwner=artifactOwner,
            CorridorBookkeeping=corridorBookkeeping,BoundedMode=boundedMode,CorridorDonor=corridorDonor?.SaveWithAliases(corridorSaves),CorridorRecipe=corridorDonor==null?null:NavDemandQueryWork.DetachSharedRecipe(corridorDonor.BoundedRecipe,corridorDonor.BoundedRecipe.Provider),ExternalReservationBytes=externalReservationBytes,SessionProviders=SessionOwnsProviders,DemandV2=demandV2,DemandV3=demandV3,DemandV4=demandV4,DemandV5=demandV5,DemandV6=demandV6,DemandLazy=demandLazy,DemandCertified=demandCertified,DemandRectangle=demandRectangle,DemandTerminalRectangle=demandTerminalRectangle,DemandTerminalCorner=demandTerminalCorner,DemandOriginShadow=demandOriginShadow,DemandTerminalCost=demandTerminalCost,LazyCatalog=lazyCatalog?.Save(NavDemandQueryWork.DetachSharedRecipe(lazyCatalog.Recipe,lazyCatalog.Recipe.Provider)),DemandCatalog=demandCatalog?.Save(),RetentionBlocked=RetentionBlocked,
                CacheGraphRecipe=cachedGraphRecipe,CacheGraph=attemptKey==null?cachedGraphCompiler?.Save():null,CacheGraphWork=cachedGraphWork,
                CacheFields=attemptKey==null?System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(lru,c=>new CacheFieldState{Recipe=c.Recipe,Field=c.Field.Save(),Work=c.Work,Incremental=c.Incremental})):null,
                OwnerGraph=artifactOwner==null?null:cachedGraphCompiler.Save(),OwnerGraphWork=cachedGraphWork,PeakCacheBytes=peakCacheBytes,
                OwnerField=artifactOwner!=null&&cachedFields.TryGetValue(artifactOwner.ArtifactKey,out var owner)?lru[owner].Field.Save():null,
                OwnerFieldIncremental=artifactOwner!=null&&cachedFields.TryGetValue(artifactOwner.ArtifactKey,out var ownerMode)&&lru[ownerMode].Incremental,
                OwnerFieldWork=artifactOwner!=null&&cachedFields.TryGetValue(artifactOwner.ArtifactKey,out var ownerWork)?lru[ownerWork].Work:0,
                LastEpoch = lastEpoch, LogicalGraph = logicalGraph,
                PhysicalGraph = physicalGraph, LogicalField = logicalField, PhysicalField = physicalField,
                ConnectorWork = connectorWork, CacheHits = cacheHits, GraphBuilds = graphBuilds,
                FieldBuilds = fieldBuilds, Cancelled = cancelled, Evictions = evictions,
                MaintenanceWork = maintenanceWork,
                QueuedBytes = queuedBytes, ReadyBytes = readyBytes, ProviderBytes = providerBytes,
                PeakRetainedBytes = peakRetainedBytes, PeakQueueAge = peakQueueAge,
                SubmittedEpoch = submittedEpoch.Select(pair=>new KeyValuePair<string,long>(pair.Key.Value,pair.Value)).ToArray(),
                GraphQuota = graphQuota, FieldQuota = fieldQuota, ConnectorQuota = connectorQuota,
                BookkeepingQuota = bookkeepingQuota, MaintenanceQuota = maintenanceQuota,
                PeakQueue = peakQueue, PeakLabels = peakLabels, PeakFrontier = peakFrontier,
                Jobs = states.ToArray(), Ready = ready.ToArray()
            };
        }
        public static LayeredNavigationScheduler Restore(Checkpoint state, IEnumerable<NavScheduledRequest> exactPendingInputs,IEnumerable<ILayeredNavigationProvider> canonicalProviders=null)
        {
            if (state == null || (state.Version != 1&&state.Version!=2&&state.Version!=3) || exactPendingInputs == null)
                throw new ArgumentException("Invalid scheduler checkpoint.");
            if(state.DemandTerminalCost&&!state.DemandOriginShadow)throw new ArgumentException("Terminal cost scheduler policy flags are incomplete.");
            if(state.DemandOriginShadow&&!state.DemandTerminalCorner)throw new ArgumentException("Origin shadow scheduler policy flags are incomplete.");
            if(state.DemandTerminalCorner&&!state.DemandTerminalRectangle)throw new ArgumentException("Corner scheduler policy flags are incomplete.");
            if(state.DemandTerminalRectangle&&!(state.DemandRectangle&&state.DemandCertified&&state.DemandLazy&&state.DemandV6&&state.DemandV5&&state.DemandV3&&state.DemandV2))
                throw new ArgumentException("Invalid scheduler terminal rectangle policy flags.");
            if(state.Version==3&&(state.Jobs==null||state.Ready==null||state.SubmittedEpoch==null||
                state.Jobs.Length>MaxQueue||state.Ready.Length>MaxQueue||state.SubmittedEpoch.Length>MaxQueue||
                state.QueuedBytes<0||state.ReadyBytes<0||state.ProviderBytes<0||state.PeakCacheBytes<0||
                state.PeakQueue<0||state.PeakQueue>MaxQueue||state.PeakLabels<0||state.PeakFrontier<0||
                state.LogicalGraph<0||state.PhysicalGraph<0||state.LogicalField<0||state.PhysicalField<0||
                state.ConnectorWork<0||state.CacheHits<0||state.GraphBuilds<0||state.FieldBuilds<0||state.Cancelled<0||state.Evictions<0||state.MaintenanceWork<0))
                throw new ArgumentException("Invalid scheduler checkpoint cardinality/counters.");
            var input = new Dictionary<string, NavScheduledRequest>(StringComparer.Ordinal);
            foreach (var request in exactPendingInputs) input.Add(request.Subscription.StableKey, request);
            // Query-only rejection precedes creation of metadata-pinning indices.
            foreach(var saved in state.Jobs??Array.Empty<JobState>())
                if(saved.DirectRoute!=null&&saved.SubscriptionKeys!=null&&saved.SubscriptionKeys.Length>0&&input.TryGetValue(saved.SubscriptionKeys[0],out var directRequest)&&directRequest.Query&&
                    (saved.Phase!=(int)JobPhase.Connectors||saved.DirectRoute.Subscription.StableKey!=directRequest.Subscription.StableKey))
                    throw new ArgumentException("Retained query direct route does not bind its exact subscriber.");
            var corridorRestores=new Dictionary<NavDemandQueryWork.Checkpoint,NavDemandQueryWork>();
            var scheduler = new LayeredNavigationScheduler {
                lastEpoch = state.LastEpoch, logicalGraph = state.LogicalGraph, physicalGraph = state.PhysicalGraph,
                logicalField = state.LogicalField, physicalField = state.PhysicalField,
                connectorWork = state.ConnectorWork, cacheHits = state.CacheHits,
                graphBuilds = state.GraphBuilds, fieldBuilds = state.FieldBuilds,
                cancelled = state.Cancelled, evictions = state.Evictions,
                maintenanceWork = state.MaintenanceWork,
                queuedBytes = state.QueuedBytes, readyBytes = state.ReadyBytes,
                peakRetainedBytes = state.PeakRetainedBytes,
                peakQueueAge = state.PeakQueueAge,
                graphQuota = state.GraphQuota, fieldQuota = state.FieldQuota,
                connectorQuota = state.ConnectorQuota, bookkeepingQuota = state.BookkeepingQuota,
                maintenanceQuota = state.MaintenanceQuota,
                peakQueue = state.PeakQueue,
                peakLabels = state.PeakLabels, peakFrontier = state.PeakFrontier
            };
            if(state.AttemptKey!=null){scheduler.BindAttemptArtifacts(state.AttemptKey);scheduler.peakCacheBytes=state.PeakCacheBytes;
                if(state.ArtifactOwner!=null){
                    if(state.OwnerGraph==null)throw new ArgumentException("Missing attempt graph owner.");
                    var r=state.ArtifactOwner;var compiler=LayeredNavigationCompileWork.Restore(r.Provider,r.Clearance,r.Profile,r.Limits,state.OwnerGraph);
                    if(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)throw new ArgumentException("Incomplete attempt graph owner.");
                    scheduler.artifactOwner=r;scheduler.AddProviderRef(r);scheduler.cachedGraph=compiler.Graph;scheduler.cachedGraphCompiler=compiler;compiler.ReleaseOwner();
                    scheduler.SetCachedGraphKey(r);scheduler.cachedGraphWork=state.OwnerGraphWork;scheduler.cachedGraphBytes=scheduler.RetainedGraphBytes(compiler.Graph,r);
                    scheduler.cacheBytes=scheduler.cachedGraphBytes+OwnerMetadataBytes;
                    if(state.OwnerField!=null){var field=NavSharedTerminalField.Restore(compiler.Graph,r.Region,r.MaxLabels,state.OwnerField);
                        var entry=new CacheField(r,field,state.OwnerFieldWork,FieldBytes(field),incremental:state.OwnerFieldIncremental);
                        scheduler.cachedFields.Add(NavNameKey.Artifact(entry.Recipe),scheduler.lru.AddLast(entry),entry.Recipe.StorageRoot);entry.Release();field.ReleaseOwner();scheduler.cacheBytes+=entry.Bytes;
                    }
                }else if(state.OwnerGraph!=null||state.OwnerField!=null)throw new ArgumentException("Orphan attempt artifact.");
            }else if(state.ArtifactOwner!=null||state.OwnerGraph!=null||state.OwnerField!=null)throw new ArgumentException("Unbound attempt artifact.");
            if(state.Version==3){
                scheduler.SessionOwnsProviders=state.SessionProviders;
                scheduler.externalReservationBytes=state.ExternalReservationBytes;scheduler.RetentionBlocked=state.RetentionBlocked;
                if(state.ExternalReservationBytes<0||state.ExternalReservationBytes>MaxRetainedBytes||state.GraphQuota<0||state.FieldQuota<0||state.ConnectorQuota<0||state.BookkeepingQuota<0||state.MaintenanceQuota<0)
                    throw new ArgumentException("Invalid scheduler quota/reservation checkpoint.");
                if(state.AttemptKey==null){
                    if((state.CacheGraph==null)!=(state.CacheGraphRecipe==null)||state.CacheFields==null||state.CacheFields.Length>MaxCachedFields)
                        throw new ArgumentException("Missing ordinary cache ownership checkpoint.");
                    if(state.CacheGraph!=null){
                        var recipe=state.CacheGraphRecipe;
                        var compiler=LayeredNavigationCompileWork.Restore(recipe.Provider,recipe.Clearance,recipe.Profile,recipe.Limits,state.CacheGraph);
                        if(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)throw new ArgumentException("Incomplete ordinary cache graph.");
                        scheduler.cachedGraph=compiler.Graph;scheduler.cachedGraphCompiler=compiler;compiler.ReleaseOwner();scheduler.cachedGraphRecipe=recipe;
                        scheduler.SetCachedGraphKey(recipe);scheduler.cachedGraphWork=state.CacheGraphWork;
                        scheduler.cachedGraphBytes=scheduler.RetainedGraphBytes(compiler.Graph,recipe);scheduler.cacheBytes=scheduler.cachedGraphBytes;
                    }
                    foreach(var saved in state.CacheFields){
                        var recipe=saved.Recipe;
                        if(recipe==null||scheduler.cachedGraph==null||recipe.CompileKey!=scheduler.cachedGraphKey||saved.Work<0)
                            throw new ArgumentException("Invalid ordinary cached field recipe.");
                        var field=NavSharedTerminalField.Restore(scheduler.cachedGraph,recipe.Region,recipe.MaxLabels,saved.Field);
                        if(field.Status!=NavSolveStatus.Ready&&!(saved.Incremental&&field.Status==NavSolveStatus.Pending))throw new ArgumentException("Incomplete ordinary cached field.");
                        var entry=new CacheField(recipe,field,saved.Work,FieldBytes(field),incremental:saved.Incremental);
                        scheduler.cachedFields.Add(NavNameKey.Artifact(entry.Recipe),scheduler.lru.AddLast(entry),entry.Recipe.StorageRoot);entry.Release();field.ReleaseOwner();scheduler.cacheBytes+=entry.Bytes;
                    }
                    scheduler.peakCacheBytes=state.PeakCacheBytes;
                }else if(state.CacheGraph!=null||state.CacheFields!=null)throw new ArgumentException("Contradictory cache ownership.");
            }
            if (state.Ready.Length > MaxQueue) throw new ArgumentException("Invalid ready result capacity.");
            foreach (var pair in state.SubmittedEpoch) scheduler.submittedEpoch.Add(NavNameKey.Subscription(input[pair.Key].Subscription), pair.Value,input[pair.Key].StorageRoot);
            foreach (var result in state.Ready) scheduler.ready.AddLast(result);
            foreach (var saved in state.Jobs) {
                if (saved.SubscriptionKeys.Length == 0 || saved.Phase < 0 || saved.Phase > (int)JobPhase.PartialFieldProbePaid)
                    throw new ArgumentException("Invalid scheduler job checkpoint.");
                NavScheduledRequest first = null;
                foreach (var key in saved.SubscriptionKeys) {
                    if (!input.TryGetValue(key, out var request) || request.ArtifactKey != saved.ArtifactKey)
                        throw new ArgumentException("Missing or stale scheduler subscription input.");
                    if (first == null) first = request;
                }
                var job = new Job { Request = first, Phase = (JobPhase)saved.Phase,
                    GraphWork = saved.GraphWork, FieldWork = saved.FieldWork,
                    GraphDebt = saved.GraphDebt, FieldDebt = saved.FieldDebt,
                    ActiveConnectorKey = saved.ActiveConnectorKey, Failure = saved.Failure,
                    DirectRoute = saved.DirectRoute,OriginBinding=saved.OriginBinding,OriginCursor=saved.OriginCursor,
                    OriginValidated=saved.OriginValidated,OriginComplete=saved.OriginComplete,HasIngress=saved.HasIngress,DirectSubscription=saved.DirectSubscription };
                if (saved.Compiler != null)
                    job.AdoptCompiler(LayeredNavigationCompileWork.Restore(first.Provider, first.Clearance,
                        first.Profile, first.Limits, saved.Compiler));
                if (saved.Graph != null) {
                    var graphOwner=saved.GraphProviderOwner??first;
                    if(graphOwner.CompileKey!=first.CompileKey)throw new ArgumentException("Stale graph provider owner.");
                    var restored = LayeredNavigationCompileWork.Restore(graphOwner.Provider, graphOwner.Clearance,
                        graphOwner.Profile, graphOwner.Limits, saved.Graph);
                    if (restored.Phase != LayeredNavigationCompileWork.Stage.Ready)
                        throw new ArgumentException("Incomplete published graph checkpoint.");
                    job.Graph = restored.Graph; job.CompletedCompiler = restored;restored.ReleaseOwner();
                }
                if(saved.OwnedGraph){if(scheduler.cachedGraph==null||scheduler.cachedGraphKey!=first.CompileKey||saved.Graph!=null)throw new ArgumentException("Invalid attempt graph alias.");job.Graph=scheduler.cachedGraph;job.CompletedCompiler=scheduler.cachedGraphCompiler;}
                if(saved.GraphProviderOwner!=null){
                    if(scheduler.attemptKey==null||job.Graph==null||saved.GraphProviderOwner.CompileKey!=first.CompileKey)throw new ArgumentException("Invalid active graph provider owner.");
                    job.GraphProviderOwner=saved.GraphProviderOwner;scheduler.AddProviderRef(job.GraphProviderOwner);
                }
                if(saved.OwnedField){if(!saved.OwnedGraph||saved.Field!=null||!scheduler.cachedFields.TryGetValue(first.ArtifactKey,out var owner))throw new ArgumentException("Invalid attempt field alias.");job.Field=scheduler.lru[owner].Field;}
                if(saved.OriginBinding!=null){
                    if(scheduler.attemptKey==null||job.Graph==null||saved.OriginBinding!=OriginBinding(first)||saved.OriginCursor<0||saved.OriginCursor>job.Graph.Nodes.Count||
                        !saved.OriginValidated&&(saved.OriginCursor!=0||saved.OriginComplete||saved.HasIngress)||saved.HasIngress&&!saved.OriginComplete||
                        saved.OriginComplete&&!saved.HasIngress&&saved.OriginCursor!=job.Graph.Nodes.Count)throw new ArgumentException("Invalid origin scan binding/cursor/result.");
                }else if(saved.OriginCursor!=0||saved.OriginValidated||saved.OriginComplete||saved.HasIngress)throw new ArgumentException("Unbound origin certificate.");
                if (saved.Field != null) job.AdoptField(NavSharedTerminalField.Restore(job.Graph, first.Region,
                    first.MaxLabels, saved.Field));
                foreach (var key in saved.SubscriptionKeys) {
                    var node = job.Subscribers.AddLast(input[key]);
                    scheduler.subscribers.Add(NavNameKey.Subscription(input[key].Subscription), new NavSubscriberLink(scheduler.jobs.NextId, node),input[key].StorageRoot);
                    scheduler.AddProviderRef(input[key]);
                }
                if (saved.Connector != null) {
                    if (saved.ActiveConnectorKey != job.Subscribers.First.Subscription.StableKey)
                        throw new ArgumentException("Stale active connector checkpoint.");
                    job.AdoptConnector(NavIncrementalSharedRoute.Restore(job.Field, job.Subscribers.First.Subscription,
                        saved.Connector));
                }
                if(saved.Demand!=null)job.AdoptDemand(NavDemandQueryWork.RestoreWithAliases(first,saved.Demand,corridorRestores));
                if (saved.Singleton != null)
                    job.AdoptSingleton(NavIncrementalSingletonRoute.Restore(job.Graph,
                        job.Subscribers.First.Subscription, saved.Singleton, first.MaxLabels,first.Query?first.Region:null));
                if(job.Phase==JobPhase.PartialFieldProbePaid&&(!ValidPartialProbeJob(job)||scheduler.connectorWork==0||scheduler.lastEpoch<0))throw new ArgumentException("Invalid paid partial-field checkpoint.");
                job.QueueNode=scheduler.jobs.NextId;job.Seal();scheduler.jobs.AddLast(job); job.Release(); scheduler.jobsByKey.Add(NavNameKey.Artifact(first), job.QueueNode,first.StorageRoot);
            }
            if (scheduler.subscribers.Count + scheduler.ready.Count > MaxQueue)
                throw new ArgumentException("Invalid scheduler queue capacity.");
            scheduler.corridorBookkeeping=state.CorridorBookkeeping;scheduler.boundedMode=state.BoundedMode;if(state.CorridorDonor!=null){if(!state.BoundedMode||state.CorridorRecipe==null)throw new ArgumentException("Missing corridor scheduler policy.");var provider=input.Values.Select(q=>q.Provider).Concat(canonicalProviders??Enumerable.Empty<ILayeredNavigationProvider>()).First(p=>p.Binding==state.CorridorRecipe.Provider.Binding);var restoredDonor=NavDemandQueryWork.RestoreWithAliases(NavDemandQueryWork.DetachSharedRecipe(state.CorridorRecipe,provider),state.CorridorDonor,corridorRestores);scheduler.SetCorridorDonor(restoredDonor);restoredDonor.ReleaseOwner();}
            scheduler.demandV2=state.DemandV2;scheduler.demandV3=state.DemandV3;scheduler.demandV4=state.DemandV4;scheduler.demandV5=state.DemandV5;scheduler.demandV6=state.DemandV6;scheduler.demandLazy=state.DemandLazy;scheduler.demandCertified=state.DemandCertified;scheduler.demandRectangle=state.DemandRectangle;scheduler.demandTerminalRectangle=state.DemandTerminalRectangle;scheduler.demandTerminalCorner=state.DemandTerminalCorner;scheduler.demandOriginShadow=state.DemandOriginShadow;scheduler.demandTerminalCost=state.DemandTerminalCost;if(state.LazyCatalog!=null){var provider=input.Values.Select(q=>q.Provider).Concat(canonicalProviders??Enumerable.Empty<ILayeredNavigationProvider>()).FirstOrDefault(p=>p.Binding==state.LazyCatalog.ProviderBinding)??throw new ArgumentException("Lazy cache logical restore requires current canonical provider.");var partial=NavLazyPartitionStore.Restore(NavDemandQueryWork.DetachSharedRecipe(state.LazyCatalog.Recipe,provider),state.LazyCatalog);partial.Seal();scheduler.SetLazyCatalog(partial);partial.Release();}if(state.DemandCatalog!=null){var cat=NavDemandQueryCatalog.Restore(state.DemandCatalog);scheduler.SetDemandCatalog(cat);cat.ReleaseOwner();}
            if(state.Version==3){
                if(scheduler.submittedEpoch.Count!=scheduler.subscribers.Count)
                    throw new ArgumentException("Scheduler submitted epoch ownership mismatch.");
                foreach(var pair in scheduler.submittedEpoch)if(!scheduler.subscribers.ContainsKey(pair.Key)||pair.Value<0||pair.Value>Math.Max(0,state.LastEpoch))
                    throw new ArgumentException("Invalid scheduler submitted epoch.");
                long queued=0,results=0;
                foreach(var pair in scheduler.subscribers)queued+=EstimateRequest(scheduler.jobs[pair.Value.Item1].Subscribers[pair.Value.Item2]);
                foreach(var result in scheduler.ready)results+=EstimateCompletion(result);
                if(queued!=state.QueuedBytes||results!=state.ReadyBytes||scheduler.providerBytes!=state.ProviderBytes||scheduler.cacheBytes>MaxCacheBytes||
                    scheduler.RetainedBytes>MaxRetainedBytes-scheduler.externalReservationBytes)
                    throw new ArgumentException("Scheduler ownership charge mismatch or retained overflow.");
            }
            return scheduler;
        }

        internal void AbsorbHistoricalCounters(NavSchedulerCounters c)
        {
            MutableRoot();logicalGraph+=c.LogicalGraphWork;physicalGraph+=c.PhysicalGraphWork;logicalField+=c.LogicalFieldWork;physicalField+=c.PhysicalFieldWork;connectorWork+=c.ConnectorWork;cacheHits+=c.CacheHits;graphBuilds+=c.GraphBuilds;fieldBuilds+=c.FieldBuilds;cancelled+=c.Cancelled;evictions+=c.Evictions;maintenanceWork+=c.MaintenanceWork;
            peakQueue=Math.Max(peakQueue,c.PeakQueue);peakLabels=Math.Max(peakLabels,c.PeakFieldLabels);peakFrontier=Math.Max(peakFrontier,c.PeakFrontier);peakQueueAge=Math.Max(peakQueueAge,c.PeakQueueAge);peakRetainedBytes=Math.Max(peakRetainedBytes,c.PeakRetainedBytes);peakCacheBytes=Math.Max(peakCacheBytes,c.PeakCacheBytes);
        }
        public int PendingCount => subscribers.Count;
        public int ReadyCount => ready.Count;
        // Host and scheduler may own the same immutable provider instance.
        // Expose the scheduler's share so the host can count the physical object once.
        public long ProviderRetainedBytes => providerBytes;
        public bool HasProviderReference(ILayeredNavigationProvider provider) => providerRefs.Contains(provider);
        // Host-owned qualified projection/provider/answer buffers share the
        // same finite retention ceiling; the pure scheduler reserves zero.
        internal sealed class EpochStamp
        {internal long Epoch,External;internal int Graph,Field,Connectors,Bookkeeping,Maintenance;}
        internal EpochStamp CaptureEpoch()=>new EpochStamp{Epoch=lastEpoch,External=externalReservationBytes,Graph=graphQuota,Field=fieldQuota,Connectors=connectorQuota,Bookkeeping=bookkeepingQuota,Maintenance=maintenanceQuota};
        internal void RestoreUnspentEpoch(EpochStamp s)
        {MutableRoot();lastEpoch=s.Epoch;externalReservationBytes=s.External;graphQuota=s.Graph;fieldQuota=s.Field;connectorQuota=s.Connectors;bookkeepingQuota=s.Bookkeeping;maintenanceQuota=s.Maintenance;}
        internal bool RetentionBlocked {get;private set;}
        internal long ExternalReservationBytes=>externalReservationBytes;
        internal bool TryReserveExternalBytes(long bytes)
        {MutableRoot();if(bytes<0||bytes>MaxRetainedBytes)throw new ArgumentOutOfRangeException(nameof(bytes));if(RetainedBytes>MaxRetainedBytes-bytes)return false;externalReservationBytes=bytes;return true;}
        public void ReserveExternalBytes(long bytes)
        {MutableRoot();if(bytes<0||bytes>MaxRetainedBytes)throw new ArgumentOutOfRangeException(nameof(bytes),bytes,"External route retention exceeds the existing bound.");externalReservationBytes=bytes;}
        // A new immutable host provider may be blocked by idle cached artifacts.
        // Release at most the caller's maintenance budget before retrying the FIFO head.
        public bool TryMakeRoomForExternal(long bytes,int maxEvictions)
        {
            MutableRoot();if(bytes<0||maxEvictions<0)throw new ArgumentOutOfRangeException();
            for(int i=0;i<maxEvictions&&!HasRoom(bytes);i++){
                if(!EvictOne())break;
                maintenanceWork++;
            }
            return HasRoom(bytes);
        }
        public long ReadyHeadProjectionReserve => ready.Count==0?0:32L*(ready.First.Route?.Legs.Count??0);
        // A host may start the next internal solver epoch only when the current
        // head has consumed its fixed allotment. Render/retry calls never refill it.
        public bool NeedsNextEpoch
        {
            get
            {
                if (jobs.First == null || ready.Count == MaxQueue) return false;
                var job = jobs.First;
                if (bookkeepingQuota == 0) return true;
                if (job.Failure.HasValue) return false;
                switch (job.Phase) {
                    case JobPhase.Direct: return connectorQuota == 0;
                    case JobPhase.Graph: return graphQuota == 0;
                    case JobPhase.Field: return fieldQuota == 0;
                    case JobPhase.Connectors:return connectorQuota==0||job.Connector?.NeedsField==true&&fieldQuota==0;
                    case JobPhase.Origin: return connectorQuota==0;
                    case JobPhase.PartialFieldProbePaid:return fieldQuota==0;
                    default: return false;
                }
            }
        }
        public NavSchedulerCounters Counters => new NavSchedulerCounters(logicalGraph, physicalGraph, logicalField,
            physicalField, connectorWork, cacheHits, graphBuilds, fieldBuilds, cancelled, evictions,
            maintenanceWork,
            subscribers.Count, ready.Count, cachedFields.Count, cacheBytes, peakCacheBytes, peakQueue,
            peakLabels, peakFrontier, RetainedBytes, peakRetainedBytes, peakQueueAge);
        private static long EstimateRequest(NavScheduledRequest request) => 4096L + 128L * (request.Region?.Candidates.Count ?? 0);
        private static long EstimateCompletion(NavScheduledCompletion result) =>
            1024L + 256L * (result.Route?.Legs.Count ?? 0);
        private void AddProviderRef(NavScheduledRequest request)
        {providerRefs.Add(request.Provider,request.ProviderBytes);providerBytes=providerRefs.DiagnosticBytes;}
        private void ReleaseProviderRef(NavScheduledRequest request)
        {providerRefs.Remove(request.Provider);providerBytes=providerRefs.DiagnosticBytes;}
        private long ActiveBytes()
        {
            var job=preparingJob??jobs.First;if(job==null)return 0;
            long bytes = 2048;
            if(job.Demand!=null)bytes+=job.Demand.EstimatedRetainedBytes;
            if (job.Compiler != null) bytes += job.Compiler.EstimatedRetainedBytes;
            if (job.Graph != null && !ReferenceEquals(job.Graph, cachedGraph)) bytes += RetainedGraphBytes(job.Graph, job.Request);
            if (job.Field != null && (!cachedFields.TryGetValue(job.Request.ArtifactKey, out var cached) ||
                !ReferenceEquals(job.Field, lru[cached].Field))) bytes += FieldBytes(job.Field);
            if (job.Connector != null) bytes += job.Connector.EstimatedRetainedBytes;
            if (job.Singleton != null) bytes += job.Singleton.EstimatedRetainedBytes;
            if (job.DirectRoute != null) bytes += 1024L + 256L * job.DirectRoute.Legs.Count;
            return bytes;
        }
        internal string RetentionBreakdown {
            get { var job=jobs.First; return "retained="+RetainedBytes+" external="+externalReservationBytes+
                " queued="+queuedBytes+" ready="+readyBytes+" provider="+providerBytes+" cache="+cacheBytes+
                " cachedGraph="+cachedGraphBytes+" active="+ActiveBytes()+" phase="+job?.Phase+
                " compiler="+(job?.Compiler?.EstimatedRetainedBytes??0)+" graph="+(job?.Graph==null?0:RetainedGraphBytes(job.Graph,job.Request))+
                " field="+(job?.Field?.EstimatedRetainedBytes??0)+" nodes="+(job?.Graph?.Nodes.Count??0)+
                " labels="+(job?.Field?.LabelCount??0)+" frontier="+(job?.Field?.FrontierCount??0)+
                " query="+job?.Request.Query+" groupQuery="+job?.Request.GroupQuery+
                " singletonStatus="+job?.Singleton?.Status+" singletonFrontier="+(job?.Singleton?.FrontierCount??0)+
                " singletonStates="+(job?.Singleton?.StateCount??0)+" singleton="+job?.Singleton?.DiagnosticProgress+
                " region="+(job?.Request.Region.Candidates.Count??0)+" remainingJobs="+jobs.Count; }
        }
        public long RetainedBytes => (corridorDonor?.EstimatedRetainedBytes??0)+ (lazyCatalog?.EstimatedRetainedBytes??0)+ (demandCatalog?.EstimatedRetainedBytes??0)+queuedBytes + readyBytes + (SessionOwnsProviders?0:providerBytes) + cacheBytes + ActiveBytes();
        private bool HasRoom(long bytes) => RetainedBytes <= MaxRetainedBytes - externalReservationBytes - bytes;
        private void UpdatePeak() => peakRetainedBytes = Math.Max(peakRetainedBytes, RetainedBytes);
        private bool EvictOne()
        {
            var oldest = lru.First;
            if (oldest != null) {
                cachedFields.Remove(oldest.Key); cacheBytes -= oldest.Bytes;
                lru.RemoveFirst(); evictions++; return true;
            }
            if (cachedGraph != null) { ClearCache(); return true; }
            return false;
        }
        private bool TryMaintenanceEviction()
        {
            if (maintenanceQuota == 0 || !EvictOne()) return false;
            maintenanceQuota--; maintenanceWork++; return true;
        }

        public bool Submit(NavScheduledRequest request)
        {
            MutableRoot();if(request?.CertifiedGroupCorridor==true)boundedMode=true;if(IsRegistered)RequireEdge(request?.StorageRoot);if(request?.DemandQueryV2==true)demandV2=true;if(request?.DemandQueryV3==true)demandV3=true;if(request?.DemandQueryV4==true)demandV4=true;if(request?.DemandQueryV5==true)demandV5=true;if(request?.DemandQueryV6==true)demandV6=true;if(request?.LazyPartition==true)demandLazy=true;if(request?.CertifiedLazyPartition==true)demandCertified=true;if(request?.RectangularOrigins==true)demandRectangle=true;if(request?.TerminalRectangleProof==true)demandTerminalRectangle=true;if(request?.TerminalCornerProof==true)demandTerminalCorner=true;if(request?.OriginShadowProof==true)demandOriginShadow=true;if(request?.TerminalCostProof==true)demandTerminalCost=true;
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (subscribers.ContainsKey(request.Subscription.StableKey)) return false;
            if (subscribers.Count + ready.Count >= MaxQueue) return false;
            long providerIncrement = providerRefs.Contains(request.Provider) ? 0 : request.ProviderBytes;
            if (!HasRoom(EstimateRequest(request) + providerIncrement + (jobs.Count == 0 ? 2048 : 0))) return false;
            bool existing=jobsByKey.TryGetValue(request.ArtifactKey,out long jobId);
            Job job=null;try{job=existing?jobs[jobId].Fork(RootAdmission):new Job(NavStorageLayout.Before(rootReservation,512),true,true){Request=request,Phase=request.Singleton||request.Query?JobPhase.Direct:JobPhase.Graph,QueueNode=jobs.NextId};
                var node=job.Subscribers.AddLast(request);job.Seal();
                if(existing)jobs.Replace(jobId,job);else{jobId=jobs.AddLast(job);jobsByKey.Add(NavNameKey.Artifact(request),jobId,request.StorageRoot);}
                subscribers.Add(NavNameKey.Subscription(request.Subscription),new NavSubscriberLink(jobId,node),request.StorageRoot);
                submittedEpoch.Add(NavNameKey.Subscription(request.Subscription),Math.Max(0,lastEpoch),request.StorageRoot);
                queuedBytes+=EstimateRequest(request);AddProviderRef(request);peakQueue=Math.Max(peakQueue,subscribers.Count+ready.Count);UpdatePeak();return true;
            }catch{rootFaulted=true;throw;}finally{job?.Release();}
        }
        private void PublishJob(Job job)
        {if(jobsByKey.TryGetValue(job.Request.ArtifactKey,out var id)&&id==job.QueueNode){job.Seal();jobs.Replace(id,job);}}
        private void CancelPrepared(Job job,string subscriptionKey,long subscriberId)
        {
            var removed=job.Subscribers[subscriberId];job.Subscribers.Remove(subscriberId);subscribers.Remove(subscriptionKey);
            submittedEpoch.Remove(subscriptionKey);queuedBytes-=EstimateRequest(removed);ReleaseProviderRef(removed);cancelled++;
            if(job.Subscribers.Count==0)RemoveJob(job);
            else if(job.ActiveConnectorKey==subscriptionKey){job.Connector=null;job.SetActiveSubscription(null);if(job.Phase==JobPhase.PartialFieldProbePaid)job.Phase=JobPhase.Connectors;}
        }
        public bool Cancel(string subscriptionKey)
        {
            MutableRoot();
            if(!subscribers.TryGetValue(subscriptionKey,out var found))return false;
            var job=jobs[found.Item1].Fork(RootAdmission);
            try{CancelPrepared(job,subscriptionKey,found.Item2);PublishJob(job);return true;}catch{rootFaulted=true;throw;}finally{job.Release();}
        }
        // A physical host row owns either an active subscriber or its ready
        // completion until transfer. Cancellation must retire that exact owner.
        internal bool CancelRetained(NavRouteSubscription subscription)
        {
            MutableRoot();if(subscription==null)throw new ArgumentNullException(nameof(subscription));
            if(subscribers.TryGetValue(subscription.StableKey,out var active)){
                if(!ReferenceEquals(jobs[active.Item1].Subscribers[active.Item2].Subscription,subscription))
                    throw new ArgumentException("Active cancellation subscription owner changed.");
                return Cancel(subscription.StableKey);
            }
            foreach(var pair in ready.Entries)if(pair.Value.Subscription.StableKey==subscription.StableKey){
                if(!ReferenceEquals(pair.Value.Subscription,subscription))throw new ArgumentException("Ready cancellation subscription owner changed.");
                long bytes=EstimateCompletion(pair.Value);
                try{ready.Remove(pair.Key);readyBytes-=bytes;cancelled++;return true;}
                catch{rootFaulted=true;throw;}
            }
            return false;
        }
        private void RemoveJob(Job job)
        {
            jobsByKey.Remove(job.Request.ArtifactKey);
            if(job.GraphProviderOwner!=null){ReleaseProviderRef(job.GraphProviderOwner);job.GraphProviderOwner=null;}
            jobs.Remove(job.QueueNode);
        }
        private static long GraphBytes(LayeredNavigationGraph graph, NavScheduledRequest request) =>
            graph.EstimatedRetainedBytes > 0 ? graph.EstimatedRetainedBytes :
            4096L + 64L * request.Limits.MaxNodes + 512L * graph.Nodes.Count +
            256L * graph.Arcs.Count + 256L * graph.PortalQualifications.Count + request.ProviderBytes;
        private static long FieldBytes(NavSharedTerminalField field) => field.EstimatedRetainedBytes;
        private void ClearCache()
        {
            SetCorridorDonor(null);SetDemandCatalog(null);SetLazyCatalog(null);
            if(artifactOwner!=null){ReleaseProviderRef(artifactOwner);artifactOwner=null;}
            if (cachedGraph != null) evictions += cachedFields.Count + 1;
            cachedFields.Clear();
            var replacement=NavPersistentQueue<CacheField>.Create(rootReservation);var previous=lru;lru=replacement;previous.Release(); cachedGraphRecipe=null; cachedGraph = null; cachedGraphCompiler = null; SetCachedGraphKey(null);
            cachedGraphWork = 0; cachedGraphBytes = 0; cacheBytes = 0;
        }
        private void CacheGraph(Job job)
        {
            if (job.Graph == null) return;
            if(attemptKey!=null){
                if(artifactOwner!=null&&artifactOwner.ArtifactKey!=job.Request.ArtifactKey)throw new InvalidOperationException("Incompatible live attempt owner.");
                if(artifactOwner==null){artifactOwner=job.Request;AddProviderRef(artifactOwner);cacheBytes+=OwnerMetadataBytes;}
                cacheBytes-=cachedGraphBytes;cachedGraph=job.Graph;cachedGraphCompiler=job.CompletedCompiler;SetCachedGraphKey(job.Request);
                cachedGraphWork=job.GraphWork;cachedGraphBytes=RetainedGraphBytes(job.Graph,job.Request);cacheBytes+=cachedGraphBytes;
                peakCacheBytes=Math.Max(peakCacheBytes,cacheBytes);return;
            }
            if (cachedGraphKey != job.Request.CompileKey) {
                if (cachedGraph != null && maintenanceQuota == 0) return;
                if (cachedGraph != null) { maintenanceQuota--; maintenanceWork++; }
                ClearCache();
            }
            long size = RetainedGraphBytes(job.Graph, job.Request);
            if (size > MaxCacheBytes || !HasRoom(size)) return;
            cacheBytes -= cachedGraphBytes;
            cachedGraph = job.Graph; cachedGraphRecipe=job.GraphProviderOwner??job.Request; SetCachedGraphKey(job.Request);
            cachedGraphCompiler = job.CompletedCompiler;
            cachedGraphWork = job.GraphWork; cachedGraphBytes = size;
            cacheBytes += size;
            peakCacheBytes = Math.Max(peakCacheBytes, cacheBytes);
        }
        private void StoreField(Job job)
        {
            if(job.Field==null||(job.Field.Status!=NavSolveStatus.Ready&&job.Field.Status!=NavSolveStatus.Pending)||cachedGraphKey!=job.Request.CompileKey)return;
            bool replacing=cachedFields.TryGetValue(job.Request.ArtifactKey,out var existing);
            if(replacing&&ReferenceEquals(lru[existing].Field,job.Field))return;
            if(attemptKey!=null&&cachedFields.Count!=0&&!replacing)return;
            long size=FieldBytes(job.Field),previous=replacing?lru[existing].Bytes:0;
            if(size+cachedGraphBytes>MaxCacheBytes||!HasRoom(Math.Max(0,size-previous)))return;
            bool evicted=!replacing&&(lru.Count>=MaxCachedFields||cacheBytes+size>MaxCacheBytes);
            var victim=evicted?lru.First:null;long removed=victim?.Bytes??0;
            if(evicted&&(maintenanceQuota==0||victim==null)||cacheBytes-previous-removed+size>MaxCacheBytes)return;
            // Prepare the entry and both lookup/ordering roots before publishing any replacement.
            // A failed owned candidate leaves the committed scheduler, old entry and job intact.
            CacheField entry=null;NavPersistentQueue<CacheField> nextLru=null;NavPersistentIndex<NavNameKey,long> nextIndex=null;
            try{
                // The cached artifact keeps its canonical recipe owner across progress updates.
                // Equal artifact keys do not imply equal owned selector identities.
                var recipe=replacing?lru[existing].Recipe:job.Request;
                entry=new CacheField(recipe,job.Field,job.FieldWork,size,NavStorageLayout.Before(rootReservation,128),true,incremental:true);
                lru.Seal();cachedFields.Seal();nextLru=lru.ForkPrepared(RootAdmission);nextIndex=cachedFields.ForkPrepared(RootAdmission);
                if(evicted){nextIndex.Remove(victim.Key);nextLru.RemoveFirst();}
                if(replacing){nextLru.Replace(existing,entry);nextLru.MoveLast(existing);}
                else nextIndex.Add(NavNameKey.Artifact(entry.Recipe),nextLru.AddLast(entry),entry.Recipe.StorageRoot);
                var oldLru=lru;var oldIndex=cachedFields;lru=nextLru;cachedFields=nextIndex;nextLru=null;nextIndex=null;oldLru.Release();oldIndex.Release();
                cacheBytes=cacheBytes-previous-removed+size;
                if(evicted){maintenanceQuota--;maintenanceWork++;evictions++;}
                peakCacheBytes=Math.Max(peakCacheBytes,cacheBytes);UpdatePeak();
            }finally{entry?.Release();nextLru?.Release();nextIndex?.Release();}
        }
        // The retained cache keeps its real compiler/provider recipe owner. A
        // later independent query borrows that exact graph; equal bindings never
        // replace/coalesce either subscriber's distinct provider facade.
        private bool CanBorrowQueryGraph(Job job)=>job.Request.GroupQuery&&artifactOwner!=null&&artifactOwner.GroupQuery&&cachedFields.Count==0&&
            cachedGraph!=null&&cachedGraphCompiler!=null&&ReferenceEquals(cachedGraphCompiler.Graph,cachedGraph)&&
            ReferenceEquals(cachedGraph.PhysicalProvider,artifactOwner.Provider)&&cachedGraphKey==job.Request.CompileKey&&artifactOwner.CompileKey==job.Request.CompileKey&&
            checked((long)cachedGraph.Nodes.Count*job.Request.Region.Candidates.Count)<=job.Request.MaxLabels;
        private static bool QueryEligible(Job job)=>job.Request.Query&&job.Graph!=null&&checked((long)job.Graph.Nodes.Count*job.Request.Region.Candidates.Count)<=job.Request.MaxLabels;
        private static bool UsesSingleton(Job job)=>job.Request.Singleton||QueryEligible(job);
        private static bool ValidPartialProbeJob(Job job)
        {
            var request=job.Subscribers.First;
            return request!=null&&!request.Singleton&&!job.Failure.HasValue&&job.GraphDebt==0&&job.FieldDebt==0&&job.Compiler==null&&job.Singleton==null&&job.DirectRoute==null&&
                job.Field!=null&&job.Field.Status==NavSolveStatus.Pending&&job.Field.PartialCapacitySafe&&job.Field.SeedComplete&&job.Connector!=null&&job.Connector.Status==NavSolveStatus.Pending&&job.Connector.NeedsField&&
                job.ActiveConnectorKey==request.Subscription.StableKey&&job.Connector.MatchesPreparedJob(job.Field,request.Subscription);
        }
        private bool CompleteCurrent(Job job, NavSolveStatus status, NavTypedRoute route, ref int used, int maxSteps)
        {
            var node = job.Subscribers.First;
            if (node == null) { RemoveJob(job); return true; }
            if(status==NavSolveStatus.Ready&&job.Field?.Status==NavSolveStatus.Pending)StoreField(job);
            long admitted = submittedEpoch[node.Subscription.StableKey];
            var completion = new NavScheduledCompletion(node,status,route,admitted,lastEpoch,NavStorageLayout.Before(rootReservation,128),true);
            try{long required = Math.Max(0, EstimateCompletion(completion) - EstimateRequest(node));
            while (!HasRoom(required) && TryMaintenanceEviction()) { }
            if (!HasRoom(required))return false;
            job.Subscribers.RemoveFirst(); subscribers.Remove(node.Subscription.StableKey);
            peakQueueAge = Math.Max(peakQueueAge, completion.QueueAge);
            submittedEpoch.Remove(node.Subscription.StableKey);
            queuedBytes -= EstimateRequest(node); ReleaseProviderRef(node);
            if (ready.Count >= MaxQueue) throw new InvalidOperationException("Ready result capacity exceeded.");
            ready.AddLast(completion); readyBytes += EstimateCompletion(completion);
            if(job.Demand!=null)CertifiedQueryReadyObserver?.Invoke(node,job.Demand);
            if(attemptKey!=null&&job.Request.LazyPartition&&job.Demand!=null){var partial=job.Demand.ExportLazyStore();if(partial!=null){SetLazyCatalog(partial);partial.Release();}}
            if(attemptKey!=null&&job.Request.DemandQueryV2&&job.Demand!=null){var cat=job.Demand.ExportCatalog(RootAdmission);if(cat!=null){SetDemandCatalog(cat);cat.ReleaseOwner();}}

            job.Demand=null;job.Connector = null; job.Singleton = null; job.DirectRoute = null; job.SetActiveSubscription(null);job.SetDirectSubscription(null);
            if (job.Subscribers.Count == 0) RemoveJob(job);
            UpdatePeak();
            return true;}finally{completion.Release();}
        }
        public int Advance(long logicalEpoch, NavEpochQuota quota, Func<NavScheduledIdentity, bool> current)
            => Advance(logicalEpoch, quota, current, int.MaxValue);
        public int Advance(long logicalEpoch, NavEpochQuota quota, Func<NavScheduledIdentity, bool> current, int maxSteps)
        {
            MutableRoot();
            if (logicalEpoch < 0 || logicalEpoch < lastEpoch || quota == null || current == null || maxSteps < 0)
                throw new ArgumentException("Invalid navigation epoch or identity predicate.");
            SetEpoch(logicalEpoch,quota);
            RetentionBlocked=false;int used=0;Job job=null,sourceJob=null;bool failed=false;
            try{
            while(used<maxSteps&&jobs.First!=null&&ready.Count<MaxQueue){
                if(job==null||jobs.First.QueueNode!=job.QueueNode){
                    if(job!=null){PublishJob(job);job.Release();job=null;preparingJob=null;}
                    sourceJob=jobs.First;job=sourceJob.Fork(RootAdmission);preparingJob=job;
                }
                if(job.Subscribers.First!=null&&!current(job.Subscribers.First.Identity)){
                    if(bookkeepingQuota==0)break;SpendBookkeeping();used++;
                    var key=job.Subscribers.First.Subscription.StableKey;CancelPrepared(job,key,subscribers[key].Item2);continue;
                }
                if (job.Failure.HasValue) {
                    if (bookkeepingQuota == 0) break;
                    SpendBookkeeping(); used++;
                    int before = used;
                    if (!CompleteCurrent(job, job.Failure.Value, null, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if(job.Phase==JobPhase.PartialFieldProbePaid){
                    if(!ValidPartialProbeJob(job)||connectorWork==0||lastEpoch<0)throw new InvalidOperationException("Paid partial-field probe lost its exact job/connector.");
                    if(fieldQuota==0)break;
                    if(!HasRoom(job.Field.NextGrowthBytes)){if(TryMaintenanceEviction())continue;RetentionBlocked=true;break;}
                    if(job.Field.IsSealed)job.PrepareField(RootAdmission);
                    if(ReferenceEquals(job.Connector,sourceJob.Connector))job.PrepareConnector(RootAdmission);
                    else if(!ReferenceEquals(job.Connector.PreparedField,job.Field))job.Connector.RebindPreparedField(job.Field);
                    job.Field.AdvanceOne(job.Connector);job.FieldWork++;physicalField++;logicalField++;fieldQuota--;used++;job.Phase=JobPhase.Connectors;
                    peakLabels=Math.Max(peakLabels,job.Field.LabelCount);peakFrontier=Math.Max(peakFrontier,job.Field.FrontierCount);
                    if(job.Field.Status!=NavSolveStatus.Pending&&job.FieldWork>0){fieldBuilds++;StoreField(job);}
                    UpdatePeak();continue;
                }
                if (job.Phase == JobPhase.Direct) {
                    if (connectorQuota == 0) break;
                    if (!HasRoom(4096)) {
                        if (TryMaintenanceEviction()) continue;
                        RetentionBlocked=true;break;
                    }
                    var request = job.Request; var origin = request.Subscription.Origin;
                    var endpoint = request.Subscription.AssignedEndpoint;
                    connectorQuota--; connectorWork++; used++;
                    if (!request.Provider.IsValid(origin, request.Clearance) ||
                        !request.Provider.IsValid(endpoint, request.Clearance)) {
                        job.Failure = NavSolveStatus.InvalidEndpoint; continue;
                    }
                    var route = DirectPreparedRoute(request);
                    try{if (route != null) {
                        int before = used;
                        if (!CompleteCurrent(job, NavSolveStatus.Ready, route, ref used, maxSteps)) {
                            // The route is immutable; retain it as a completed
                            // singleton until publication memory is available.
                            job.DirectRoute = route; job.Phase = JobPhase.Connectors;
                            if (used == before) break;
                        }
                    } else job.Phase = JobPhase.Graph;
                    }finally{route?.ReleaseOwner();}UpdatePeak(); continue;
                }
                if(job.Request.DemandQuery&&job.Phase!=JobPhase.Direct&&job.Demand?.CertificateInsufficient!=true){
                    bool indexStep=job.Demand?.CanBookkeeping==true&&bookkeepingQuota>0;
                    bool graphStep=job.Demand==null?graphQuota>0:job.Demand.CanGraph&&graphQuota>0&&!(job.Demand.CanConnector&&connectorQuota>0);
                    if(job.Demand==null&&(job.Request.CertifiedLazyPartition?bookkeepingQuota==0:graphQuota==0)||job.Demand!=null&&job.Demand.Status==NavSolveStatus.Pending&&!indexStep&&!graphStep&&(!job.Demand.CanConnector||connectorQuota==0))break;
                    if(!HasRoom(job.Demand?.NextGrowthBytes??65536L)){if(TryMaintenanceEviction())continue;RetentionBlocked=true;break;}
                    if(job.Demand==null){var shared=attemptKey!=null&&demandCatalog!=null&&demandCatalog.Compatible(job.Request)?demandCatalog:null;var lazySharedStore=attemptKey!=null&&lazyCatalog!=null&&lazyCatalog.Compatible(job.Request)?lazyCatalog:null;job.AdoptDemand(new NavDemandQueryWork(job.Request,RootAdmission,shared:shared,lazyShared:lazySharedStore));if(shared!=null||lazySharedStore!=null)cacheHits++;if(job.Request.CertifiedGroupCorridor&&corridorDonor!=null){job.Demand.AttachCorridor(corridorDonor);SpendBookkeeping();used++;UpdatePeak();continue;}}
                    else if(ReferenceEquals(job.Demand,sourceJob.Demand))job.AdoptDemand(job.Demand.Fork(RootAdmission));
                    if(job.Demand.Status==NavSolveStatus.Pending){
                        // Shared-catalog birth starts beyond sampling. Choose
                        // a paid lane from the actual prepared work, not null.
                        indexStep=job.Demand.CanBookkeeping&&bookkeepingQuota>0;
                        graphStep=job.Demand.CanGraph&&graphQuota>0&&!(job.Demand.CanConnector&&connectorQuota>0);
                        if(!indexStep&&!graphStep&&(!job.Demand.CanConnector||connectorQuota==0))break;
                        if(indexStep){using(PerformanceProfile.Sample(ProfileStage.DemandBookkeeping))job.Demand.StepBookkeeping();SpendBookkeeping();}else if(graphStep){using(PerformanceProfile.Sample(ProfileStage.DemandGraph))job.Demand.StepGraph();graphQuota--;logicalGraph++;physicalGraph++;job.GraphWork++;}else{using(PerformanceProfile.Sample(ProfileStage.DemandConnector))job.Demand.StepConnector();connectorQuota--;connectorWork++;}used++;
                    }else if(!job.Demand.CanDonateCorridor||corridorDonor!=null){
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;
                    }
                    // A pending donor publication pays its own unit below. Charging
                    // another Ready unit first would starve it forever at maxSteps=1.
                    peakFrontier=Math.Max(peakFrontier,job.Demand.FrontierCount);
                    if(job.Demand.Status!=NavSolveStatus.Pending){if(job.Demand.CanDonateCorridor&&corridorDonor==null){if(bookkeepingQuota==0||used==maxSteps)break;SetCorridorDonor(job.Demand);SpendBookkeeping();used++;if(used==maxSteps)break;}int before=used;if(!CompleteCurrent(job,job.Demand.Status,job.Demand.Route,ref used,maxSteps)&&before==used)break;}
                    UpdatePeak();continue;
                }
                if (job.Phase == JobPhase.Graph) {
                    if(attemptKey!=null&&artifactOwner!=null&&artifactOwner.ArtifactKey!=job.Request.ArtifactKey&&!CanBorrowQueryGraph(job)){
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;ClearCache();continue;
                    }
                    if(attemptKey!=null&&job.Graph==null&&cachedGraphKey==job.Request.CompileKey){
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;
                        job.Graph=cachedGraph;job.CompletedCompiler=cachedGraphCompiler;job.GraphProviderOwner=artifactOwner;AddProviderRef(job.GraphProviderOwner);cacheHits++;if(UsesSingleton(job))job.Phase=JobPhase.Connectors;else BeginOrigin(job);UpdatePeak();continue;
                    }
                    if (graphQuota == 0) break;
                    long graphReserve = job.Compiler != null
                        ? job.Compiler.NextGrowthBytes
                        : job.Graph == null && cachedGraphKey == job.Request.CompileKey
                            ? RetainedGraphBytes(cachedGraph, job.Request) : job.Request.Provider is PartitionNavigationProvider
                                ? 4096L + 40L * job.Request.Limits.MaxNodes : LayeredNavigationCompileWork.FlatInitialStorageBytes+256L;
                    if(attemptKey!=null){
                        if(artifactOwner==null)graphReserve+=OwnerMetadataBytes;
                        // Flat publication changes the conservative compiler estimate
                        // to the complete graph estimate; reserve the whole delta.
                        if(job.Compiler!=null&&!(job.Request.Provider is PartitionNavigationProvider)&&job.Compiler.Phase==LayeredNavigationCompileWork.Stage.WrapAdjacency)
                            graphReserve=Math.Max(graphReserve,512L*job.Compiler.SampleCount+256L*job.Request.Provider.Portals.Count+OwnerMetadataBytes);
                    }
                    if (!HasRoom(graphReserve)) {
                        if (TryMaintenanceEviction()) continue;
                        RetentionBlocked=true;break;
                    }
                    if (job.Graph == null && job.Compiler == null && job.GraphDebt == 0) {
                        if (cachedGraphKey == job.Request.CompileKey) {
                            job.Graph = cachedGraph; job.GraphDebt = cachedGraphWork;
                            job.CompletedCompiler = cachedGraphCompiler;
                            cacheHits++;
                        } else job.AdoptCompiler(rootReservation==null?new LayeredNavigationCompileWork(job.Request.Provider,
                            job.Request.Clearance, job.Request.Profile, job.Request.Limits):LayeredNavigationCompileWork.CreateOwned(job.Request.Provider,job.Request.Clearance,job.Request.Profile,job.Request.Limits,OwnedRootReservation));
                    }
                    graphQuota--; logicalGraph++; used++;
                    if (job.GraphDebt > 0) job.GraphDebt--;
                    else {
                        try { if(ReferenceEquals(job.Compiler,sourceJob.Compiler))job.PrepareCompiler(RootAdmission);job.Compiler.Advance(1); job.GraphWork++; physicalGraph++; }
                        catch (ArgumentException) { job.Failure = NavSolveStatus.CapacityExceeded; job.Compiler = null; continue; }
                        if (job.Compiler.Phase == LayeredNavigationCompileWork.Stage.Ready) {
                            job.Graph = job.Compiler.Graph; job.CompletedCompiler = job.Compiler;
                            job.Compiler = null; graphBuilds++;
                            if(attemptKey!=null){job.GraphProviderOwner=job.Request;AddProviderRef(job.GraphProviderOwner);}
                            CacheGraph(job);
                        }
                    }
                    if (job.Graph != null && job.GraphDebt == 0){
                        if(attemptKey!=null&&!UsesSingleton(job))BeginOrigin(job);
                        else job.Phase = UsesSingleton(job) ? JobPhase.Connectors : JobPhase.Field;
                    }
                    UpdatePeak();
                    continue;
                }
                if(job.Phase==JobPhase.Origin){
                    if(connectorQuota==0)break;
                    var request=job.Subscribers.First;
                    if(!job.OriginValidated){connectorQuota--;connectorWork++;used++;job.OriginValidated=true;
                        if(!job.Graph.IsValid(request.Subscription.Origin)){job.Failure=NavSolveStatus.InvalidEndpoint;continue;}
                    }else if(job.OriginCursor<job.Graph.Nodes.Count){
                        connectorQuota--;connectorWork++;used++;
                        if(job.Graph.TryAttach(request.Subscription.Origin,job.OriginCursor++,out _)){job.HasIngress=true;job.OriginComplete=true;job.Phase=job.Field?.Status==NavSolveStatus.Ready?JobPhase.Connectors:JobPhase.Field;}
                    }else{
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;job.OriginComplete=true;job.Phase=JobPhase.Connectors;
                    }
                    UpdatePeak();continue;
                }
                if (job.Phase == JobPhase.Field) {
                    if(job.FieldDebt==0&&job.Field?.Status==NavSolveStatus.Pending&&job.Field.PartialCapacitySafe&&job.Field.SeedComplete){job.Phase=JobPhase.Connectors;continue;}
                    if(job.Field==null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var incremental)&&lru[incremental].Incremental){
                        if(bookkeepingQuota==0)break;
                        SpendBookkeeping();used++;job.Field=lru[incremental].Field;lru.MoveLast(incremental);cacheHits++;
                        if(job.Field.Status==NavSolveStatus.Ready||job.Field.PartialCapacitySafe&&job.Field.SeedComplete)job.Phase=JobPhase.Connectors;
                        UpdatePeak();continue;
                    }
                    if(attemptKey!=null&&job.Field==null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var owned)){
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;job.Field=lru[owned].Field;cacheHits++;job.Phase=JobPhase.Connectors;UpdatePeak();continue;
                    }

                    if (fieldQuota == 0) break;
                    long fieldReserve = job.Field != null ? job.Field.NextGrowthBytes : cachedFields.TryGetValue(job.Request.ArtifactKey, out var candidate)
                        ? lru[candidate].Bytes : NavSharedTerminalField.InitialStorageBytes(job.Graph,job.Request.Region,job.Request.MaxLabels,rootReservation!=null)+NavSharedTerminalField.FirstStepGrowthBytes(job.Graph,job.Request.Region);
                    if (job.Graph.SupportsPackedField &&
                        fieldReserve + RetainedGraphBytes(job.Graph, job.Request) + job.Request.ProviderBytes > MaxRetainedBytes) {
                        job.Failure = NavSolveStatus.CapacityExceeded; continue;
                    }
                    if (!HasRoom(fieldReserve)) {
                        if (TryMaintenanceEviction()) continue;
                        RetentionBlocked=true;break;
                    }
                    if (job.Field == null && job.FieldDebt == 0) {
                        if (cachedFields.TryGetValue(job.Request.ArtifactKey, out var cached)) {
                            job.Field = lru[cached].Field; job.FieldDebt = lru[cached].Work;
                            lru.MoveLast(cached); cacheHits++;
                        } else job.AdoptField(rootReservation==null?new NavSharedTerminalField(job.Graph, job.Request.Region, job.Request.MaxLabels):NavSharedTerminalField.CreateOwned(job.Graph,job.Request.Region,job.Request.MaxLabels,OwnedRootReservation));
                    }
                    if (job.FieldDebt == 0 && job.Field.Status != NavSolveStatus.Pending) {
                        fieldQuota--; logicalField++; physicalField++; used++;
                        job.Phase = JobPhase.Connectors; continue;
                    }
                    fieldQuota--; logicalField++; used++;
                    if (job.FieldDebt > 0) job.FieldDebt--;
                    else if (job.Field.Status == NavSolveStatus.Pending) {
                        if(job.Field.IsSealed)job.PrepareField(RootAdmission);job.Field.Advance(1); job.FieldWork++; physicalField++;
                        peakLabels = Math.Max(peakLabels, job.Field.LabelCount);
                        peakFrontier = Math.Max(peakFrontier, job.Field.FrontierCount);
                    }
                    if (job.FieldDebt == 0 && job.Field.Status != NavSolveStatus.Pending) {
                        if (job.FieldWork > 0) { fieldBuilds++; StoreField(job); }
                        job.Phase = JobPhase.Connectors;
                    }
                    UpdatePeak();
                    continue;
                }
                if (job.DirectRoute != null) {
                    if (bookkeepingQuota == 0) break;
                    SpendBookkeeping(); used++;
                    int before = used;
                    if (!CompleteCurrent(job, NavSolveStatus.Ready, job.DirectRoute, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if (UsesSingleton(job)) {
                    if (connectorQuota == 0) break;
                    if (!HasRoom(job.Singleton==null?NavIncrementalSingletonRoute.InitialStorageBytes+512L:job.Singleton.NextGrowthBytes)) {
                        if (TryMaintenanceEviction()) continue;
                        RetentionBlocked=true;break;
                    }
                    bool created = job.Singleton == null;
                    bool stepped = false;
                    if (created) job.AdoptSingleton(rootReservation==null?new NavIncrementalSingletonRoute(job.Graph,
                        job.Subscribers.First.Subscription, job.Request.MaxLabels,job.Request.Query?job.Request.Region:null):NavIncrementalSingletonRoute.CreateOwned(job.Graph,job.Subscribers.First.Subscription,job.Request.MaxLabels,OwnedRootReservation,job.Request.Query?job.Request.Region:null));
                    if (job.Singleton.Status == NavSolveStatus.Pending) {
                        if(ReferenceEquals(job.Singleton,sourceJob.Singleton))job.PrepareSingleton(RootAdmission);var singletonStep = job.Singleton.Advance(1);
                        if (singletonStep.Consumed != 1) throw new InvalidOperationException("Singleton did not consume its route step.");
                        connectorQuota--; connectorWork++; used++;
                        stepped = true;
                    }
                    if (job.Singleton.Status != NavSolveStatus.Pending) {
                        if (created && !stepped) { connectorQuota--; connectorWork++; used++; }
                        else if (!stepped) {
                            if (bookkeepingQuota == 0) break;
                            SpendBookkeeping(); used++;
                        }
                        int before = used;
                        if (!CompleteCurrent(job, job.Singleton.Status, job.Singleton.Route, ref used, maxSteps) && used == before) break;
                    }
                    UpdatePeak(); continue;
                }
                if(attemptKey!=null&&!job.Request.Singleton){
                    var request=job.Subscribers.First;
                    if(CertifiedFlat(request)&&!MatchesOrigin(job,request)){BeginOrigin(job);continue;}
                    if(job.DirectSubscription!=request.Subscription.StableKey){
                        if(connectorQuota==0)break;
                        if(!HasRoom(4096)){if(TryMaintenanceEviction())continue;RetentionBlocked=true;break;}
                        connectorQuota--;connectorWork++;used++;job.SetDirectSubscription(request.Subscription);
                        var direct=DirectPreparedRoute(request);job.DirectRoute=direct;direct?.ReleaseOwner();UpdatePeak();continue;
                    }
                    if(job.OriginComplete&&!job.HasIngress){
                        if(bookkeepingQuota==0)break;SpendBookkeeping();used++;
                        int before=used;if(!CompleteCurrent(job,NavSolveStatus.BlockedConnector,null,ref used,maxSteps)&&used==before)break;
                        UpdatePeak();continue;
                    }
                }
                bool partial=job.Field.Status==NavSolveStatus.Pending&&job.Field.PartialCapacitySafe&&job.Field.SeedComplete;
                if(job.Field.Status!=NavSolveStatus.Ready&&!partial){
                    if (bookkeepingQuota == 0) break;
                    SpendBookkeeping(); used++;
                    int before = used;
                    if (!CompleteCurrent(job, job.Field.Status, null, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if (connectorQuota == 0) break;
                if (!HasRoom(job.Connector==null?NavIncrementalSharedRoute.InitialStorageBytes+1088L+1024L:job.Connector.NextGrowthBytes)) {
                    if (TryMaintenanceEviction()) continue;
                    RetentionBlocked=true;break;
                }
                bool createdConnector = job.Connector == null;
                if (createdConnector) {
                    job.AdoptConnector(rootReservation==null?new NavIncrementalSharedRoute(job.Field, job.Subscribers.First.Subscription):NavIncrementalSharedRoute.CreateOwned(job.Field,job.Subscribers.First.Subscription,OwnedRootReservation));
                    job.SetActiveSubscription(job.Subscribers.First.Subscription);
                }
                if(partial&&job.Connector.NeedsField){
                    // A durable phase is the receipt for exactly one paid observation.
                    // It preserves maxSteps=1 and is consumed with the atomic field+prefix update.
                    if(fieldQuota==0||connectorQuota==0)break;
                    if(!ValidPartialProbeJob(job))throw new InvalidOperationException("Partial-field probe cannot bind its first subscriber.");
                    connectorQuota--;connectorWork++;used++;job.Phase=JobPhase.PartialFieldProbePaid;UpdatePeak();continue;
                }
                if (job.Connector.Status != NavSolveStatus.Pending) {
                    if (createdConnector) {
                        connectorQuota--; connectorWork++; used++;
                    } else {
                        if (bookkeepingQuota == 0) break;
                        SpendBookkeeping(); used++;
                    }
                    int before = used;
                    if (!CompleteCurrent(job, job.Connector.Status, job.Connector.Route, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if(ReferenceEquals(job.Connector,sourceJob.Connector))job.PrepareConnector(RootAdmission);var step=partial?job.Connector.AdvancePartial(1):job.Connector.Advance(1);
                if (step.Consumed == 0) throw new InvalidOperationException("Ready field did not advance connector.");
                connectorQuota--; connectorWork++; used++;
                if (step.Status != NavSolveStatus.Pending) {
                    int before = used;
                    if (!CompleteCurrent(job, step.Status, job.Connector.Route, ref used, maxSteps) && used == before) break;
                }
                UpdatePeak();
            }
            return used;
            }catch{failed=true;rootFaulted=true;throw;}finally{preparingJob=null;try{if(job!=null&&!failed)PublishJob(job);}finally{job?.Release();}}
        }
        public bool TryTake(Func<NavScheduledIdentity, bool> current, out NavScheduledCompletion completion)
        {
            MutableRoot();
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (ready.Count > 0) {
                var value = ready.First;value.Retain();
                try{ready.RemoveFirst();readyBytes -= EstimateCompletion(value);
                    if (!current(value.Identity)) { cancelled++;value.Release(); completion = null; return false; }
                    completion = value; return true;
                }catch{value.Release();rootFaulted=true;throw;}
            }
            completion = null; return false;
        }
        public void ClearWorld()
        {
            MutableRoot();
            try{jobs.Clear(); jobsByKey.Clear(); subscribers.Clear(); submittedEpoch.Clear(); ready.Clear();
                ClearCache();providerRefs.Clear(); queuedBytes = 0; readyBytes = 0; providerBytes = 0;externalReservationBytes=0;
            }catch{rootFaulted=true;throw;}
        }
    }
}
