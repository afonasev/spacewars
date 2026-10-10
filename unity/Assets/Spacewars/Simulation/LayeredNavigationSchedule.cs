using System;
using System.Collections.Generic;

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
    public sealed class NavScheduledRequest
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
    public sealed class NavScheduledCompletion
    {
        internal NavScheduledCompletion(NavScheduledRequest request, NavSolveStatus status, NavTypedRoute route,
            long submittedEpoch, long completedEpoch)
        {
            Identity = request.Identity; Subscription = request.Subscription; Status = status; Route = route;
            SubmittedEpoch = submittedEpoch; CompletedEpoch = completedEpoch;
        }
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
    public sealed class LayeredNavigationScheduler
    {
        public const int MaxQueue = 4096, MaxCachedFields = 256;
        public const long MaxCacheBytes = 64L * 1024 * 1024, MaxRetainedBytes = 64L * 1024 * 1024;
        private enum JobPhase { Direct, Graph, Field, Connectors }
        private sealed class Job
        {
            internal NavScheduledRequest Request;
            internal readonly LinkedList<NavScheduledRequest> Subscribers = new LinkedList<NavScheduledRequest>();
            internal LinkedListNode<Job> QueueNode;
            internal JobPhase Phase;
            internal LayeredNavigationCompileWork Compiler;
            internal LayeredNavigationCompileWork CompletedCompiler;
            internal LayeredNavigationGraph Graph;
            internal NavSharedTerminalField Field;
            internal NavIncrementalSharedRoute Connector;
            internal NavIncrementalSingletonRoute Singleton;
            internal NavTypedRoute DirectRoute;
            internal long GraphDebt, FieldDebt;
            internal int GraphWork, FieldWork;
            internal string ActiveConnectorKey;
            internal NavSolveStatus? Failure;
        }
        private sealed class CacheField
        {
            internal string Key;
            internal NavSharedTerminalField Field;
            internal int Work;
            internal long Bytes;
        }
        private readonly LinkedList<Job> jobs = new LinkedList<Job>();
        private readonly Dictionary<string, Job> jobsByKey = new Dictionary<string, Job>(StringComparer.Ordinal);
        private readonly Dictionary<string, Tuple<Job, LinkedListNode<NavScheduledRequest>>> subscribers =
            new Dictionary<string, Tuple<Job, LinkedListNode<NavScheduledRequest>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> submittedEpoch = new Dictionary<string, long>(StringComparer.Ordinal);
        // Reference identity matters: equal authored bindings can be held by
        // separately allocated immutable provider instances in queued requests.
        private readonly Dictionary<ILayeredNavigationProvider, Tuple<int, long>> providerRefs =
            new Dictionary<ILayeredNavigationProvider, Tuple<int, long>>();
        private readonly Queue<NavScheduledCompletion> ready = new Queue<NavScheduledCompletion>();
        private Dictionary<string, LinkedListNode<CacheField>> cachedFields =
            new Dictionary<string, LinkedListNode<CacheField>>(StringComparer.Ordinal);
        private LinkedList<CacheField> lru = new LinkedList<CacheField>();
        private string cachedGraphKey;
        private LayeredNavigationGraph cachedGraph;
        private LayeredNavigationCompileWork cachedGraphCompiler;
        private int cachedGraphWork;
        private long cachedGraphBytes, cacheBytes, peakCacheBytes;
        private long lastEpoch = -1, logicalGraph, physicalGraph, logicalField, physicalField;
        private long connectorWork, cacheHits, graphBuilds, fieldBuilds, cancelled, evictions, maintenanceWork;
        private long queuedBytes, readyBytes, providerBytes, peakRetainedBytes, peakQueueAge;
        private long externalReservationBytes;
        private int graphQuota, fieldQuota, connectorQuota, bookkeepingQuota, maintenanceQuota;
        private int peakQueue, peakLabels, peakFrontier;

        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            internal long LastEpoch, LogicalGraph, PhysicalGraph, LogicalField, PhysicalField;
            internal long ConnectorWork, CacheHits, GraphBuilds, FieldBuilds, Cancelled, Evictions, MaintenanceWork;
            internal long QueuedBytes, ReadyBytes, ProviderBytes, PeakRetainedBytes, PeakQueueAge;
            internal KeyValuePair<string, long>[] SubmittedEpoch;
            internal int GraphQuota, FieldQuota, ConnectorQuota, BookkeepingQuota, MaintenanceQuota;
            internal int PeakQueue, PeakLabels, PeakFrontier;
            internal JobState[] Jobs;
            internal NavScheduledCompletion[] Ready;
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
        }
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
                    Compiler = job.Compiler?.Save(), Graph = job.CompletedCompiler?.Save(),
                    Field = job.Field?.Save(), Connector = job.Connector?.Save(), Singleton = job.Singleton?.Save(),
                    DirectRoute = job.DirectRoute
                });
            }
            return new Checkpoint {
                Version = 1, LastEpoch = lastEpoch, LogicalGraph = logicalGraph,
                PhysicalGraph = physicalGraph, LogicalField = logicalField, PhysicalField = physicalField,
                ConnectorWork = connectorWork, CacheHits = cacheHits, GraphBuilds = graphBuilds,
                FieldBuilds = fieldBuilds, Cancelled = cancelled, Evictions = evictions,
                MaintenanceWork = maintenanceWork,
                QueuedBytes = queuedBytes, ReadyBytes = readyBytes, ProviderBytes = providerBytes,
                PeakRetainedBytes = peakRetainedBytes, PeakQueueAge = peakQueueAge,
                SubmittedEpoch = new List<KeyValuePair<string, long>>(submittedEpoch).ToArray(),
                GraphQuota = graphQuota, FieldQuota = fieldQuota, ConnectorQuota = connectorQuota,
                BookkeepingQuota = bookkeepingQuota, MaintenanceQuota = maintenanceQuota,
                PeakQueue = peakQueue, PeakLabels = peakLabels, PeakFrontier = peakFrontier,
                Jobs = states.ToArray(), Ready = ready.ToArray()
            };
        }
        public static LayeredNavigationScheduler Restore(Checkpoint state, IEnumerable<NavScheduledRequest> exactPendingInputs)
        {
            if (state == null || state.Version != 1 || exactPendingInputs == null)
                throw new ArgumentException("Invalid scheduler checkpoint.");
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
                providerBytes = state.ProviderBytes, peakRetainedBytes = state.PeakRetainedBytes,
                peakQueueAge = state.PeakQueueAge,
                graphQuota = state.GraphQuota, fieldQuota = state.FieldQuota,
                connectorQuota = state.ConnectorQuota, bookkeepingQuota = state.BookkeepingQuota,
                maintenanceQuota = state.MaintenanceQuota,
                peakQueue = state.PeakQueue,
                peakLabels = state.PeakLabels, peakFrontier = state.PeakFrontier
            };
            if (state.Ready.Length > MaxQueue) throw new ArgumentException("Invalid ready result capacity.");
            foreach (var pair in state.SubmittedEpoch) scheduler.submittedEpoch.Add(pair.Key, pair.Value);
            foreach (var result in state.Ready) scheduler.ready.Enqueue(result);
            foreach (var saved in state.Jobs) {
                if (saved.SubscriptionKeys.Length == 0 || saved.Phase < 0 || saved.Phase > (int)JobPhase.Connectors)
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
                    DirectRoute = saved.DirectRoute };
                if (saved.Compiler != null)
                    job.Compiler = LayeredNavigationCompileWork.Restore(first.Provider, first.Clearance,
                        first.Profile, first.Limits, saved.Compiler);
                if (saved.Graph != null) {
                    var restored = LayeredNavigationCompileWork.Restore(first.Provider, first.Clearance,
                        first.Profile, first.Limits, saved.Graph);
                    if (restored.Phase != LayeredNavigationCompileWork.Stage.Ready)
                        throw new ArgumentException("Incomplete published graph checkpoint.");
                    job.Graph = restored.Graph; job.CompletedCompiler = restored;
                }
                if (saved.Field != null) job.Field = NavSharedTerminalField.Restore(job.Graph, first.Region,
                    first.MaxLabels, saved.Field);
                foreach (var key in saved.SubscriptionKeys) {
                    var node = job.Subscribers.AddLast(input[key]);
                    scheduler.subscribers.Add(key, Tuple.Create(job, node));
                    scheduler.AddProviderRef(input[key]);
                }
                if (saved.Connector != null) {
                    if (saved.ActiveConnectorKey != job.Subscribers.First.Value.Subscription.StableKey)
                        throw new ArgumentException("Stale active connector checkpoint.");
                    job.Connector = NavIncrementalSharedRoute.Restore(job.Field, job.Subscribers.First.Value.Subscription,
                        saved.Connector);
                }
                if (saved.Singleton != null)
                    job.Singleton = NavIncrementalSingletonRoute.Restore(job.Graph,
                        job.Subscribers.First.Value.Subscription, saved.Singleton, first.MaxLabels);
                job.QueueNode = scheduler.jobs.AddLast(job); scheduler.jobsByKey.Add(saved.ArtifactKey, job);
            }
            if (scheduler.subscribers.Count + scheduler.ready.Count > MaxQueue)
                throw new ArgumentException("Invalid scheduler queue capacity.");
            scheduler.providerBytes = state.ProviderBytes;
            return scheduler;
        }

        public int PendingCount => subscribers.Count;
        public int ReadyCount => ready.Count;
        // Host and scheduler may own the same immutable provider instance.
        // Expose the scheduler's share so the host can count the physical object once.
        public long ProviderRetainedBytes => providerBytes;
        public bool HasProviderReference(ILayeredNavigationProvider provider) => providerRefs.ContainsKey(provider);
        // Host-owned qualified projection/provider/answer buffers share the
        // same finite retention ceiling; the pure scheduler reserves zero.
        public void ReserveExternalBytes(long bytes)
        {if(bytes<0||bytes>MaxRetainedBytes)throw new ArgumentOutOfRangeException(nameof(bytes));externalReservationBytes=bytes;}
        // A new immutable host provider may be blocked by idle cached artifacts.
        // Release at most the caller's maintenance budget before retrying the FIFO head.
        public bool TryMakeRoomForExternal(long bytes,int maxEvictions)
        {
            if(bytes<0||maxEvictions<0)throw new ArgumentOutOfRangeException();
            for(int i=0;i<maxEvictions&&!HasRoom(bytes);i++){
                if(!EvictOne())break;
                maintenanceWork++;
            }
            return HasRoom(bytes);
        }
        public long ReadyHeadProjectionReserve => ready.Count==0?0:32L*(ready.Peek().Route?.Legs.Count??0);
        // A host may start the next internal solver epoch only when the current
        // head has consumed its fixed allotment. Render/retry calls never refill it.
        public bool NeedsNextEpoch
        {
            get
            {
                if (jobs.First == null || ready.Count == MaxQueue) return false;
                var job = jobs.First.Value;
                if (bookkeepingQuota == 0) return true;
                if (job.Failure.HasValue) return false;
                switch (job.Phase) {
                    case JobPhase.Direct: return connectorQuota == 0;
                    case JobPhase.Graph: return graphQuota == 0;
                    case JobPhase.Field: return fieldQuota == 0;
                    case JobPhase.Connectors: return connectorQuota == 0;
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
        {
            if (providerRefs.TryGetValue(request.Provider, out var entry))
                providerRefs[request.Provider] = Tuple.Create(entry.Item1 + 1, entry.Item2);
            else {
                long bytes = request.ProviderBytes;
                providerRefs.Add(request.Provider, Tuple.Create(1, bytes)); providerBytes += bytes;
            }
        }
        private void ReleaseProviderRef(NavScheduledRequest request)
        {
            var entry = providerRefs[request.Provider];
            if (entry.Item1 == 1) { providerRefs.Remove(request.Provider); providerBytes -= entry.Item2; }
            else providerRefs[request.Provider] = Tuple.Create(entry.Item1 - 1, entry.Item2);
        }
        private long ActiveBytes()
        {
            if (jobs.First == null) return 0;
            var job = jobs.First.Value;
            long bytes = 2048;
            if (job.Compiler != null) bytes += job.Compiler.EstimatedRetainedBytes;
            if (job.Graph != null && !ReferenceEquals(job.Graph, cachedGraph)) bytes += GraphBytes(job.Graph, job.Request);
            if (job.Field != null && (!cachedFields.TryGetValue(job.Request.ArtifactKey, out var cached) ||
                !ReferenceEquals(job.Field, cached.Value.Field))) bytes += FieldBytes(job.Field);
            if (job.Connector != null) bytes += 2048L + 512L * job.Connector.StateCount;
            if (job.Singleton != null) bytes += 2048L + 512L *
                (job.Singleton.FrontierCount + job.Singleton.StateCount);
            if (job.DirectRoute != null) bytes += 1024L + 256L * job.DirectRoute.Legs.Count;
            return bytes;
        }
        public long RetainedBytes => queuedBytes + readyBytes + providerBytes + cacheBytes + ActiveBytes();
        private bool HasRoom(long bytes) => RetainedBytes <= MaxRetainedBytes - externalReservationBytes - bytes;
        private void UpdatePeak() => peakRetainedBytes = Math.Max(peakRetainedBytes, RetainedBytes);
        private bool EvictOne()
        {
            var oldest = lru.First;
            if (oldest != null) {
                cachedFields.Remove(oldest.Value.Key); cacheBytes -= oldest.Value.Bytes;
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
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (subscribers.ContainsKey(request.Subscription.StableKey)) return false;
            if (subscribers.Count + ready.Count >= MaxQueue) return false;
            long providerIncrement = providerRefs.ContainsKey(request.Provider) ? 0 : request.ProviderBytes;
            if (!HasRoom(EstimateRequest(request) + providerIncrement + (jobs.Count == 0 ? 2048 : 0))) return false;
            if (!jobsByKey.TryGetValue(request.ArtifactKey, out var job)) {
                job = new Job { Request = request, Phase = request.Singleton ? JobPhase.Direct : JobPhase.Graph };
                job.QueueNode = jobs.AddLast(job); jobsByKey.Add(request.ArtifactKey, job);
            }
            // A shared artifact can span authority groups; each subscriber still
            // carries its own world/root/member/request/HOLD/exact endpoints.
            var node = job.Subscribers.AddLast(request);
            subscribers.Add(request.Subscription.StableKey, Tuple.Create(job, node));
            submittedEpoch.Add(request.Subscription.StableKey, Math.Max(0, lastEpoch));
            queuedBytes += EstimateRequest(request); AddProviderRef(request);
            peakQueue = Math.Max(peakQueue, subscribers.Count + ready.Count);
            UpdatePeak();
            return true;
        }
        public bool Cancel(string subscriptionKey)
        {
            if (!subscribers.TryGetValue(subscriptionKey, out var found)) return false;
            var job = found.Item1; job.Subscribers.Remove(found.Item2); subscribers.Remove(subscriptionKey);
            submittedEpoch.Remove(subscriptionKey); queuedBytes -= EstimateRequest(found.Item2.Value);
            ReleaseProviderRef(found.Item2.Value);
            cancelled++;
            if (job.Subscribers.Count == 0) RemoveJob(job);
            else if (job.ActiveConnectorKey == subscriptionKey) { job.Connector = null; job.ActiveConnectorKey = null; }
            return true;
        }
        private void RemoveJob(Job job)
        {
            jobs.Remove(job.QueueNode); jobsByKey.Remove(job.Request.ArtifactKey);
            job.Compiler = null; job.Field = null; job.Connector = null; job.Singleton = null; job.DirectRoute = null;
        }
        private static long GraphBytes(LayeredNavigationGraph graph, NavScheduledRequest request) =>
            graph.EstimatedRetainedBytes > 0 ? graph.EstimatedRetainedBytes :
            4096L + 64L * request.Limits.MaxNodes + 512L * graph.Nodes.Count +
            256L * graph.Arcs.Count + 256L * graph.PortalQualifications.Count + request.ProviderBytes;
        private static long FieldBytes(NavSharedTerminalField field) => field.EstimatedRetainedBytes;
        private void ClearCache()
        {
            if (cachedGraph != null) evictions += cachedFields.Count + 1;
            cachedFields = new Dictionary<string, LinkedListNode<CacheField>>(StringComparer.Ordinal);
            lru = new LinkedList<CacheField>(); cachedGraph = null; cachedGraphCompiler = null; cachedGraphKey = null;
            cachedGraphWork = 0; cachedGraphBytes = 0; cacheBytes = 0;
        }
        private void CacheGraph(Job job)
        {
            if (job.Graph == null) return;
            if (cachedGraphKey != job.Request.CompileKey) {
                if (cachedGraph != null && maintenanceQuota == 0) return;
                if (cachedGraph != null) { maintenanceQuota--; maintenanceWork++; }
                ClearCache();
            }
            long size = GraphBytes(job.Graph, job.Request);
            if (size > MaxCacheBytes || !HasRoom(size)) return;
            cacheBytes -= cachedGraphBytes;
            cachedGraph = job.Graph; cachedGraphKey = job.Request.CompileKey;
            cachedGraphCompiler = job.CompletedCompiler;
            cachedGraphWork = job.GraphWork; cachedGraphBytes = size;
            cacheBytes += size;
            peakCacheBytes = Math.Max(peakCacheBytes, cacheBytes);
        }
        private void StoreField(Job job)
        {
            if (job.Field == null || job.Field.Status != NavSolveStatus.Ready || cachedGraphKey != job.Request.CompileKey) return;
            long size = FieldBytes(job.Field);
            if (size + cachedGraphBytes > MaxCacheBytes || !HasRoom(size)) return;
            if (cachedFields.Count >= MaxCachedFields || cacheBytes + size > MaxCacheBytes) {
                if (!TryMaintenanceEviction()) return;
            }
            if (cachedFields.Count >= MaxCachedFields || cacheBytes + size > MaxCacheBytes) return;
            if (cachedFields.TryGetValue(job.Request.ArtifactKey, out var existing)) {
                cacheBytes -= existing.Value.Bytes; lru.Remove(existing); cachedFields.Remove(job.Request.ArtifactKey);
            }
            var entry = new CacheField { Key = job.Request.ArtifactKey, Field = job.Field, Work = job.FieldWork, Bytes = size };
            cachedFields.Add(entry.Key, lru.AddLast(entry)); cacheBytes += size;
            peakCacheBytes = Math.Max(peakCacheBytes, cacheBytes);
            UpdatePeak();
        }
        private bool CompleteCurrent(Job job, NavSolveStatus status, NavTypedRoute route, ref int used, int maxSteps)
        {
            var node = job.Subscribers.First;
            if (node == null) { RemoveJob(job); return true; }
            long admitted = submittedEpoch[node.Value.Subscription.StableKey];
            var completion = new NavScheduledCompletion(node.Value, status, route, admitted, lastEpoch);
            long required = Math.Max(0, EstimateCompletion(completion) - EstimateRequest(node.Value));
            while (!HasRoom(required) && TryMaintenanceEviction()) { }
            if (!HasRoom(required)) return false;
            job.Subscribers.RemoveFirst(); subscribers.Remove(node.Value.Subscription.StableKey);
            peakQueueAge = Math.Max(peakQueueAge, completion.QueueAge);
            submittedEpoch.Remove(node.Value.Subscription.StableKey);
            queuedBytes -= EstimateRequest(node.Value); ReleaseProviderRef(node.Value);
            if (ready.Count >= MaxQueue) throw new InvalidOperationException("Ready result capacity exceeded.");
            ready.Enqueue(completion); readyBytes += EstimateCompletion(completion);
            job.Connector = null; job.Singleton = null; job.DirectRoute = null; job.ActiveConnectorKey = null;
            if (job.Subscribers.Count == 0) RemoveJob(job);
            UpdatePeak();
            return true;
        }
        public int Advance(long logicalEpoch, NavEpochQuota quota, Func<NavScheduledIdentity, bool> current)
            => Advance(logicalEpoch, quota, current, int.MaxValue);
        public int Advance(long logicalEpoch, NavEpochQuota quota, Func<NavScheduledIdentity, bool> current, int maxSteps)
        {
            if (logicalEpoch < 0 || logicalEpoch < lastEpoch || quota == null || current == null || maxSteps < 0)
                throw new ArgumentException("Invalid navigation epoch or identity predicate.");
            if (logicalEpoch != lastEpoch) {
                lastEpoch = logicalEpoch; graphQuota = quota.Graph; fieldQuota = quota.Field;
                connectorQuota = quota.Connectors; bookkeepingQuota = quota.Bookkeeping;
                maintenanceQuota = quota.Maintenance;
            }
            int used = 0;
            while (used < maxSteps && jobs.First != null && ready.Count < MaxQueue) {
                var job = jobs.First.Value;
                if (job.Subscribers.First != null && !current(job.Subscribers.First.Value.Identity)) {
                    if (bookkeepingQuota == 0) break;
                    bookkeepingQuota--; used++;
                    Cancel(job.Subscribers.First.Value.Subscription.StableKey);
                    continue;
                }
                if (jobs.First == null || jobs.First.Value != job) continue;
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
                        break;
                    }
                    var request = job.Request; var origin = request.Subscription.Origin;
                    var endpoint = request.Subscription.AssignedEndpoint;
                    connectorQuota--; connectorWork++; used++;
                    if (!request.Provider.IsValid(origin, request.Clearance) ||
                        !request.Provider.IsValid(endpoint, request.Clearance)) {
                        job.Failure = NavSolveStatus.InvalidEndpoint; continue;
                    }
                    IReadOnlyList<NavTypedLeg> directTrace = null;
                    if (request.Provider is PartitionNavigationProvider partition) {
                        if (partition.TryTrace(origin, endpoint.Position, request.Clearance, out var reached, out var trace) &&
                            reached.Equals(endpoint)) directTrace = trace;
                    } else if (origin.SurfaceId == endpoint.SurfaceId &&
                        request.Provider.CanSweep(origin, endpoint, request.Clearance)) {
                        double cost = NavContract.Number(LayeredNavigationProvider.Distance(origin.Position, endpoint.Position) *
                            request.Provider.SurfaceCost(origin.SurfaceId));
                        directTrace = new[] { new NavTypedLeg(origin, endpoint, cost, NavTypedLegKind.Surface, null) };
                    }
                    if (directTrace != null) {
                        double cost = 0;
                        foreach (var leg in directTrace) cost += leg.Cost;
                        var route = new NavTypedRoute(request.Subscription, new List<NavTypedLeg>(directTrace), cost);
                        int before = used;
                        if (!CompleteCurrent(job, NavSolveStatus.Ready, route, ref used, maxSteps)) {
                            // The route is immutable; retain it as a completed
                            // singleton until publication memory is available.
                            job.DirectRoute = route; job.Phase = JobPhase.Connectors;
                            if (used == before) break;
                        }
                    } else job.Phase = JobPhase.Graph;
                    UpdatePeak(); continue;
                }
                if (job.Phase == JobPhase.Graph) {
                    if (graphQuota == 0) break;
                    long graphReserve = job.Compiler != null
                        ? (job.Request.Provider is PartitionNavigationProvider ? 4096L + 256L :
                            4096L + (job.Compiler.Phase == LayeredNavigationCompileWork.Stage.WrapAdjacency
                            ? 256L * job.Compiler.SampleCount + job.Request.ProviderBytes : 0))
                        : job.Graph == null && cachedGraphKey == job.Request.CompileKey
                            ? GraphBytes(cachedGraph, job.Request) : job.Request.Provider is PartitionNavigationProvider
                                ? 4096L + 40L * job.Request.Limits.MaxNodes : 4096L + 64L * job.Request.Limits.MaxNodes;
                    if (!HasRoom(graphReserve)) {
                        if (TryMaintenanceEviction()) continue;
                        break;
                    }
                    if (job.Graph == null && job.Compiler == null && job.GraphDebt == 0) {
                        if (cachedGraphKey == job.Request.CompileKey) {
                            job.Graph = cachedGraph; job.GraphDebt = cachedGraphWork;
                            job.CompletedCompiler = cachedGraphCompiler;
                            cacheHits++;
                        } else job.Compiler = new LayeredNavigationCompileWork(job.Request.Provider,
                            job.Request.Clearance, job.Request.Profile, job.Request.Limits);
                    }
                    graphQuota--; logicalGraph++; used++;
                    if (job.GraphDebt > 0) job.GraphDebt--;
                    else {
                        try { job.Compiler.Advance(1); job.GraphWork++; physicalGraph++; }
                        catch (ArgumentException) { job.Failure = NavSolveStatus.CapacityExceeded; job.Compiler = null; continue; }
                        if (job.Compiler.Phase == LayeredNavigationCompileWork.Stage.Ready) {
                            job.Graph = job.Compiler.Graph; job.CompletedCompiler = job.Compiler;
                            job.Compiler = null; graphBuilds++;
                            CacheGraph(job);
                        }
                    }
                    if (job.Graph != null && job.GraphDebt == 0)
                        job.Phase = job.Request.Singleton ? JobPhase.Connectors : JobPhase.Field;
                    UpdatePeak();
                    continue;
                }
                if (job.Phase == JobPhase.Field) {
                    if (fieldQuota == 0) break;
                    long fieldReserve = job.Field != null ? 4096 : cachedFields.TryGetValue(job.Request.ArtifactKey, out var candidate)
                        ? candidate.Value.Bytes : job.Request.Provider is PartitionNavigationProvider
                            ? 4096L + 64L * job.Graph.Nodes.Count * job.Request.Region.Candidates.Count : 4096;
                    if (job.Request.Provider is PartitionNavigationProvider &&
                        fieldReserve + GraphBytes(job.Graph, job.Request) + job.Request.ProviderBytes > MaxRetainedBytes) {
                        job.Failure = NavSolveStatus.CapacityExceeded; continue;
                    }
                    if (!HasRoom(fieldReserve)) {
                        if (TryMaintenanceEviction()) continue;
                        break;
                    }
                    if (job.Field == null && job.FieldDebt == 0) {
                        if (cachedFields.TryGetValue(job.Request.ArtifactKey, out var cached)) {
                            job.Field = cached.Value.Field; job.FieldDebt = cached.Value.Work;
                            lru.Remove(cached); lru.AddLast(cached); cacheHits++;
                        } else job.Field = new NavSharedTerminalField(job.Graph, job.Request.Region, job.Request.MaxLabels);
                    }
                    if (job.FieldDebt == 0 && job.Field.Status != NavSolveStatus.Pending) {
                        fieldQuota--; logicalField++; physicalField++; used++;
                        job.Phase = JobPhase.Connectors; continue;
                    }
                    fieldQuota--; logicalField++; used++;
                    if (job.FieldDebt > 0) job.FieldDebt--;
                    else if (job.Field.Status == NavSolveStatus.Pending) {
                        job.Field.Advance(1); job.FieldWork++; physicalField++;
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
                    if (!HasRoom(4096)) {
                        if (TryMaintenanceEviction()) continue;
                        break;
                    }
                    bool created = job.Singleton == null;
                    bool stepped = false;
                    if (created) job.Singleton = new NavIncrementalSingletonRoute(job.Graph,
                        job.Subscribers.First.Value.Subscription, job.Request.MaxLabels);
                    if (job.Singleton.Status == NavSolveStatus.Pending) {
                        var singletonStep = job.Singleton.Advance(1);
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
                if (job.Field.Status != NavSolveStatus.Ready) {
                    if (bookkeepingQuota == 0) break;
                    bookkeepingQuota--; used++;
                    int before = used;
                    if (!CompleteCurrent(job, job.Field.Status, null, ref used, maxSteps) && used == before) break;
                    continue;
                }
                if (connectorQuota == 0) break;
                if (!HasRoom(4096)) {
                    if (TryMaintenanceEviction()) continue;
                    break;
                }
                bool createdConnector = job.Connector == null;
                if (createdConnector) {
                    job.Connector = new NavIncrementalSharedRoute(job.Field, job.Subscribers.First.Value.Subscription);
                    job.ActiveConnectorKey = job.Subscribers.First.Value.Subscription.StableKey;
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
                var step = job.Connector.Advance(1);
                if (step.Consumed == 0) throw new InvalidOperationException("Ready field did not advance connector.");
                connectorQuota--; connectorWork++; used++;
                if (step.Status != NavSolveStatus.Pending) {
                    int before = used;
                    if (!CompleteCurrent(job, step.Status, job.Connector.Route, ref used, maxSteps) && used == before) break;
                }
                UpdatePeak();
            }
            return used;
        }
        public bool TryTake(Func<NavScheduledIdentity, bool> current, out NavScheduledCompletion completion)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (ready.Count > 0) {
                var value = ready.Dequeue();
                readyBytes -= EstimateCompletion(value);
                if (!current(value.Identity)) { cancelled++; completion = null; return false; }
                completion = value; return true;
            }
            completion = null; return false;
        }
        public void ClearWorld()
        {
            jobs.Clear(); jobsByKey.Clear(); subscribers.Clear(); submittedEpoch.Clear(); ready.Clear();
            providerRefs.Clear(); queuedBytes = 0; readyBytes = 0; providerBytes = 0;
            externalReservationBytes=0;
            ClearCache();
        }
    }
}
