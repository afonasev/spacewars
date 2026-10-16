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
            NavRouteSubscription subscription, int maxLabels, bool singleton = false)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Clearance = clearance ?? throw new ArgumentNullException(nameof(clearance));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            if (!singleton && region == null) throw new ArgumentNullException(nameof(region));
            Region = region; Singleton = singleton;
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            if (maxLabels < 1 || maxLabels > 1048576) throw new ArgumentOutOfRangeException(nameof(maxLabels));
            MaxLabels = maxLabels;
            ProviderBytes = provider.EstimatedRetainedBytes;
            GraphKey = new NavGraphBinding(provider.Binding, clearance, profile).StableKey;
            CompileKey = NavContract.Digest(w => {
                w.Write(GraphKey); w.Write(limits.MaxSamples); w.Write(limits.MaxNodes); w.Write(limits.MaxArcs);
            });
            ArtifactKey = NavContract.Digest(w => {
                w.Write(CompileKey); w.Write(singleton);
                w.Write(singleton ? subscription.StableKey : region.StableKey); w.Write(maxLabels);
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
        public const long MaxCacheBytes = 64L * 1024 * 1024, MaxRetainedBytes = 64L * 1024 * 1024;
        internal enum JobPhase { Direct, Graph, Field, Connectors, Origin }
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
                    fork.Compiler=Compiler;fork.CompletedCompiler=CompletedCompiler;fork.Graph=Graph;fork.Field=Field;fork.Connector=Connector;fork.Singleton=Singleton;fork.DirectRoute=DirectRoute;
                    fork.GraphDebt=GraphDebt;fork.FieldDebt=FieldDebt;fork.GraphWork=GraphWork;fork.FieldWork=FieldWork;fork.SetNamed(ref fork.ActiveConnectorKey,ref fork.activeKeyReference,ActiveConnectorKey,activeKeyReference);fork.Failure=Failure;
                    fork.SetOrigin(OriginBinding,originMetadata);fork.SetNamed(ref fork.DirectSubscription,ref fork.directKeyReference,DirectSubscription,directKeyReference);fork.OriginCursor=OriginCursor;fork.OriginValidated=OriginValidated;fork.OriginComplete=OriginComplete;fork.HasIngress=HasIngress;
                    return fork;
                }catch{fork.Release();throw;}
            }
            internal void Seal()
            {if(frozen)return;Compiler?.SealPrepared();CompletedCompiler?.SealPrepared();Field?.SealPrepared();Connector?.SealPrepared();Singleton?.SealPrepared();Subscribers.Seal();frozen=true;}
            internal void PrepareCompiler(Func<long,bool> reserve){var fork=Compiler.ForkPrepared(reserve);AdoptCompiler(fork);}
            internal void PrepareField(Func<long,bool> reserve){var fork=Field.ForkPrepared(reserve);AdoptField(fork);}
            internal void PrepareConnector(Func<long,bool> reserve){var fork=Connector.ForkPrepared(Field,reserve);AdoptConnector(fork);}
            internal void PrepareSingleton(Func<long,bool> reserve){var fork=Singleton.ForkPrepared(reserve);AdoptSingleton(fork);}
            protected override void ReleaseEdges()
            {Subscribers?.Release();activeKeyReference.Owner?.Release();directKeyReference.Owner?.Release();originMetadata?.Release();requestProvider?.Release();graphProvider?.Release();compiler?.Release();completedCompiler?.Release();graph?.Release();field?.Release();connector?.Release();singleton?.Release();directRoute?.Release();}
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
            internal readonly long Bytes;
            private readonly NavOwnedStorage recipeStorage;
            internal CacheField(NavScheduledRequest recipe,NavSharedTerminalField field,int work,long bytes,Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
            {using(var a=new NavStorageAllocation(reserve)){if(!alreadyReserved)a.Reserve(128);a.Register(this);if(IsRegistered){RequireEdge(field);RequireEdge(recipe.StorageRoot);}Key=recipe.ArtifactKey;Recipe=recipe;Field=field;Work=work;Bytes=bytes;field.SealPrepared();field.Retain();recipeStorage=recipe.StorageRoot??(recipe.Provider as INavStorageFacade)?.StorageRoot;recipeStorage?.Retain();Retain();}}
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
            MutableRoot();SetEpoch(epoch,quota);if(artifactOwner==null)return true;
            if(jobs.Count!=0||ready.Count!=0)throw new InvalidOperationException("Attempt still has subscribers/results.");
            if(bookkeepingQuota==0)return false;
            bookkeepingQuota--;ClearCache();UpdatePeak();return true;
        }
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
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
            internal int OwnerGraphWork,OwnerFieldWork; internal long PeakCacheBytes;
            internal long ExternalReservationBytes;
            internal bool SessionProviders, RetentionBlocked;
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
            internal int Work;
        }
        internal sealed class JobState
        {
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
        // Continuation role keys are SHA256 strings. Count the maximum live
        // job/subscription/ready rows without allocating an iterator just to
        // admit the digest's own grow/copy workspace.
        internal long LogicalContinuationScratchBytes=>checked(NavContract.DigestScratchBytes(1024L*(1+jobs.Count+subscribers.Count+ready.Count))+8192L+256L*(jobs.Count+ready.Count));
        internal string LogicalContinuationBinding => NavContract.Digest(w=>{
            if(attemptKey!=null){w.Write(attemptKey);w.Write(artifactOwner?.ArtifactKey??"");}
            w.Write(lastEpoch);w.Write(graphQuota);w.Write(fieldQuota);w.Write(connectorQuota);w.Write(bookkeepingQuota);w.Write(maintenanceQuota);
            w.Write(logicalGraph);w.Write(physicalGraph);w.Write(logicalField);w.Write(physicalField);w.Write(connectorWork);
            w.Write(cacheHits);w.Write(graphBuilds);w.Write(fieldBuilds);w.Write(cancelled);w.Write(evictions);w.Write(maintenanceWork);
            foreach(var job in jobs){w.Write(job.Request.ArtifactKey);w.Write((int)job.Phase);w.Write(job.GraphWork);w.Write(job.FieldWork);
                if(attemptKey!=null){w.Write(job.OriginBinding??"");w.Write(job.OriginCursor);w.Write(job.OriginValidated);w.Write(job.OriginComplete);w.Write(job.HasIngress);w.Write(job.DirectSubscription??"");}
                w.Write(job.GraphDebt);w.Write(job.FieldDebt);w.Write(job.ActiveConnectorKey??"");foreach(var r in job.Subscribers)w.Write(r.Subscription.StableKey);}
            foreach(var item in ready){w.Write(item.Subscription.StableKey);w.Write((int)item.Status);}
        });
        public Checkpoint Save()
        {
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
                    DirectRoute = job.DirectRoute,
                    OwnedGraph=job.Graph!=null&&ReferenceEquals(job.Graph,cachedGraph),
                    OwnedField=job.Field!=null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var fieldOwner)&&ReferenceEquals(job.Field,lru[fieldOwner].Field),
                    GraphProviderOwner=job.GraphProviderOwner,OriginBinding=job.OriginBinding,OriginCursor=job.OriginCursor,OriginValidated=job.OriginValidated,OriginComplete=job.OriginComplete,HasIngress=job.HasIngress,DirectSubscription=job.DirectSubscription
                });
            }
            return new Checkpoint {
                Version = 3, AttemptKey=attemptKey,ArtifactOwner=artifactOwner,
                ExternalReservationBytes=externalReservationBytes,SessionProviders=SessionOwnsProviders,RetentionBlocked=RetentionBlocked,
                CacheGraphRecipe=cachedGraphRecipe,CacheGraph=attemptKey==null?cachedGraphCompiler?.Save():null,CacheGraphWork=cachedGraphWork,
                CacheFields=attemptKey==null?System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(lru,c=>new CacheFieldState{Recipe=c.Recipe,Field=c.Field.Save(),Work=c.Work})):null,
                OwnerGraph=artifactOwner==null?null:cachedGraphCompiler.Save(),OwnerGraphWork=cachedGraphWork,PeakCacheBytes=peakCacheBytes,
                OwnerField=artifactOwner!=null&&cachedFields.TryGetValue(artifactOwner.ArtifactKey,out var owner)?lru[owner].Field.Save():null,
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
        public static LayeredNavigationScheduler Restore(Checkpoint state, IEnumerable<NavScheduledRequest> exactPendingInputs)
        {
            if (state == null || (state.Version != 1&&state.Version!=2&&state.Version!=3) || exactPendingInputs == null)
                throw new ArgumentException("Invalid scheduler checkpoint.");
            if(state.Version==3&&(state.Jobs==null||state.Ready==null||state.SubmittedEpoch==null||
                state.Jobs.Length>MaxQueue||state.Ready.Length>MaxQueue||state.SubmittedEpoch.Length>MaxQueue||
                state.QueuedBytes<0||state.ReadyBytes<0||state.ProviderBytes<0||state.PeakCacheBytes<0||
                state.PeakQueue<0||state.PeakQueue>MaxQueue||state.PeakLabels<0||state.PeakFrontier<0||
                state.LogicalGraph<0||state.PhysicalGraph<0||state.LogicalField<0||state.PhysicalField<0||
                state.ConnectorWork<0||state.CacheHits<0||state.GraphBuilds<0||state.FieldBuilds<0||state.Cancelled<0||state.Evictions<0||state.MaintenanceWork<0))
                throw new ArgumentException("Invalid scheduler checkpoint cardinality/counters.");
            var input = new Dictionary<string, NavScheduledRequest>(StringComparer.Ordinal);
            foreach (var request in exactPendingInputs) input.Add(request.Subscription.StableKey, request);
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
                        var entry=new CacheField(r,field,state.OwnerFieldWork,FieldBytes(field));
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
                        if(field.Status!=NavSolveStatus.Ready)throw new ArgumentException("Incomplete ordinary cached field.");
                        var entry=new CacheField(recipe,field,saved.Work,FieldBytes(field));
                        scheduler.cachedFields.Add(NavNameKey.Artifact(entry.Recipe),scheduler.lru.AddLast(entry),entry.Recipe.StorageRoot);entry.Release();field.ReleaseOwner();scheduler.cacheBytes+=entry.Bytes;
                    }
                    scheduler.peakCacheBytes=state.PeakCacheBytes;
                }else if(state.CacheGraph!=null||state.CacheFields!=null)throw new ArgumentException("Contradictory cache ownership.");
            }
            if (state.Ready.Length > MaxQueue) throw new ArgumentException("Invalid ready result capacity.");
            foreach (var pair in state.SubmittedEpoch) scheduler.submittedEpoch.Add(NavNameKey.Subscription(input[pair.Key].Subscription), pair.Value,input[pair.Key].StorageRoot);
            foreach (var result in state.Ready) scheduler.ready.AddLast(result);
            foreach (var saved in state.Jobs) {
                if (saved.SubscriptionKeys.Length == 0 || saved.Phase < 0 || saved.Phase > (int)JobPhase.Origin)
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
                if (saved.Singleton != null)
                    job.AdoptSingleton(NavIncrementalSingletonRoute.Restore(job.Graph,
                        job.Subscribers.First.Subscription, saved.Singleton, first.MaxLabels));
                job.QueueNode=scheduler.jobs.NextId;job.Seal();scheduler.jobs.AddLast(job); job.Release(); scheduler.jobsByKey.Add(NavNameKey.Artifact(first), job.QueueNode,first.StorageRoot);
            }
            if (scheduler.subscribers.Count + scheduler.ready.Count > MaxQueue)
                throw new ArgumentException("Invalid scheduler queue capacity.");
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
                    case JobPhase.Connectors: return connectorQuota == 0;
                    case JobPhase.Origin: return connectorQuota==0;
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
                " region="+(job?.Request.Region.Candidates.Count??0)+" remainingJobs="+jobs.Count; }
        }
        public long RetainedBytes => queuedBytes + readyBytes + (SessionOwnsProviders?0:providerBytes) + cacheBytes + ActiveBytes();
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
            MutableRoot();if(IsRegistered)RequireEdge(request?.StorageRoot);
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (subscribers.ContainsKey(request.Subscription.StableKey)) return false;
            if (subscribers.Count + ready.Count >= MaxQueue) return false;
            long providerIncrement = providerRefs.Contains(request.Provider) ? 0 : request.ProviderBytes;
            if (!HasRoom(EstimateRequest(request) + providerIncrement + (jobs.Count == 0 ? 2048 : 0))) return false;
            bool existing=jobsByKey.TryGetValue(request.ArtifactKey,out long jobId);
            Job job=null;try{job=existing?jobs[jobId].Fork(RootAdmission):new Job(NavStorageLayout.Before(rootReservation,512),true,true){Request=request,Phase=request.Singleton?JobPhase.Direct:JobPhase.Graph,QueueNode=jobs.NextId};
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
            else if(job.ActiveConnectorKey==subscriptionKey){job.Connector=null;job.SetActiveSubscription(null);}
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
            if (job.Field == null || job.Field.Status != NavSolveStatus.Ready || cachedGraphKey != job.Request.CompileKey) return;
            long size = FieldBytes(job.Field);
            if(attemptKey!=null){
                if(cachedFields.Count!=0)return;
                var own=new CacheField(job.Request,job.Field,job.FieldWork,size,NavStorageLayout.Before(rootReservation,128),true);
                try{cachedFields.Add(NavNameKey.Artifact(own.Recipe),lru.AddLast(own),own.Recipe.StorageRoot);}finally{own.Release();}cacheBytes+=size;peakCacheBytes=Math.Max(peakCacheBytes,cacheBytes);UpdatePeak();return;
            }
            if (size + cachedGraphBytes > MaxCacheBytes || !HasRoom(size)) return;
            if (cachedFields.Count >= MaxCachedFields || cacheBytes + size > MaxCacheBytes) {
                if (!TryMaintenanceEviction()) return;
            }
            if (cachedFields.Count >= MaxCachedFields || cacheBytes + size > MaxCacheBytes) return;
            if (cachedFields.TryGetValue(job.Request.ArtifactKey, out var existing)) {
                cacheBytes -= lru[existing].Bytes; lru.Remove(existing); cachedFields.Remove(job.Request.ArtifactKey);
            }
            var entry = new CacheField(job.Request,job.Field,job.FieldWork,size,NavStorageLayout.Before(rootReservation,128),true);
            try{cachedFields.Add(NavNameKey.Artifact(entry.Recipe), lru.AddLast(entry),entry.Recipe.StorageRoot);}finally{entry.Release();}cacheBytes += size;
            peakCacheBytes = Math.Max(peakCacheBytes, cacheBytes);
            UpdatePeak();
        }
        private bool CompleteCurrent(Job job, NavSolveStatus status, NavTypedRoute route, ref int used, int maxSteps)
        {
            var node = job.Subscribers.First;
            if (node == null) { RemoveJob(job); return true; }
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
            job.Connector = null; job.Singleton = null; job.DirectRoute = null; job.SetActiveSubscription(null);job.SetDirectSubscription(null);
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
                    if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;
                    var key=job.Subscribers.First.Subscription.StableKey;CancelPrepared(job,key,subscribers[key].Item2);continue;
                }
                if (job.Failure.HasValue) {
                    if (bookkeepingQuota == 0) break;
                    bookkeepingQuota--; used++;
                    int before = used;
                    if (!CompleteCurrent(job, job.Failure.Value, null, ref used, maxSteps) && used == before) break;
                    continue;
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
                if (job.Phase == JobPhase.Graph) {
                    if(attemptKey!=null&&artifactOwner!=null&&artifactOwner.ArtifactKey!=job.Request.ArtifactKey){
                        if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;ClearCache();continue;
                    }
                    if(attemptKey!=null&&job.Graph==null&&cachedGraphKey==job.Request.CompileKey){
                        if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;
                        job.Graph=cachedGraph;job.CompletedCompiler=cachedGraphCompiler;job.GraphProviderOwner=artifactOwner;AddProviderRef(job.GraphProviderOwner);cacheHits++;if(job.Request.Singleton)job.Phase=JobPhase.Connectors;else BeginOrigin(job);UpdatePeak();continue;
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
                        if(attemptKey!=null&&!job.Request.Singleton)BeginOrigin(job);
                        else job.Phase = job.Request.Singleton ? JobPhase.Connectors : JobPhase.Field;
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
                        if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;job.OriginComplete=true;job.Phase=JobPhase.Connectors;
                    }
                    UpdatePeak();continue;
                }
                if (job.Phase == JobPhase.Field) {
                    if(attemptKey!=null&&job.Field==null&&cachedFields.TryGetValue(job.Request.ArtifactKey,out var owned)){
                        if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;job.Field=lru[owned].Field;cacheHits++;job.Phase=JobPhase.Connectors;UpdatePeak();continue;
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
                        if(ReferenceEquals(job.Field,sourceJob.Field))job.PrepareField(RootAdmission);job.Field.Advance(1); job.FieldWork++; physicalField++;
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
                    bookkeepingQuota--; used++;
                    int before = used;
                    if (!CompleteCurrent(job, NavSolveStatus.Ready, job.DirectRoute, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if (job.Request.Singleton) {
                    if (connectorQuota == 0) break;
                    if (!HasRoom(job.Singleton==null?NavIncrementalSingletonRoute.InitialStorageBytes+512L:job.Singleton.NextGrowthBytes)) {
                        if (TryMaintenanceEviction()) continue;
                        RetentionBlocked=true;break;
                    }
                    bool created = job.Singleton == null;
                    bool stepped = false;
                    if (created) job.AdoptSingleton(rootReservation==null?new NavIncrementalSingletonRoute(job.Graph,
                        job.Subscribers.First.Subscription, job.Request.MaxLabels):NavIncrementalSingletonRoute.CreateOwned(job.Graph,job.Subscribers.First.Subscription,job.Request.MaxLabels,OwnedRootReservation));
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
                            bookkeepingQuota--; used++;
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
                        var direct=DirectPreparedRoute(request);job.DirectRoute=direct;direct?.ReleaseOwner();if(job.DirectRoute!=null){UpdatePeak();continue;}
                    }
                    if(job.OriginComplete&&!job.HasIngress){
                        if(bookkeepingQuota==0)break;bookkeepingQuota--;used++;
                        int before=used;if(!CompleteCurrent(job,NavSolveStatus.BlockedConnector,null,ref used,maxSteps)&&used==before)break;
                        UpdatePeak();continue;
                    }
                }
                if (job.Field.Status != NavSolveStatus.Ready) {
                    if (bookkeepingQuota == 0) break;
                    bookkeepingQuota--; used++;
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
                if (job.Connector.Status != NavSolveStatus.Pending) {
                    if (createdConnector) {
                        connectorQuota--; connectorWork++; used++;
                    } else {
                        if (bookkeepingQuota == 0) break;
                        bookkeepingQuota--; used++;
                    }
                    int before = used;
                    if (!CompleteCurrent(job, job.Connector.Status, job.Connector.Route, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if(ReferenceEquals(job.Connector,sourceJob.Connector))job.PrepareConnector(RootAdmission);var step = job.Connector.Advance(1);
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
