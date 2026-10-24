using System;

namespace Spacewars.Simulation
{
    public sealed partial class LayeredNavigationScheduler
    {
        internal override bool IsImmutableCaptureRoot=>rootFrozen&&!rootFaulted;
        private Func<long,bool> rootReservation;
        private NavOwnedText attemptBindingStorage;
        private bool rootFrozen,rootFaulted,rootReleased;
        private Func<long,bool> RootAdmission=>rootReservation??LocalAdmission;
        private INavStorageReservation OwnedRootReservation=>rootReservation?.Target as INavStorageReservation??throw new InvalidOperationException("Missing scheduler allocation scope.");
        public LayeredNavigationScheduler():this(null,false){}
        private LayeredNavigationScheduler(Func<long,bool> admission,bool fork,bool alreadyReserved=false):base(1024)
        {
            rootReservation=admission;
            using(var allocation=new NavStorageAllocation(admission)){if(!alreadyReserved)allocation.Reserve(1024);allocation.Register(this);Retain();}
            if(fork)return;
            try{
                jobs=NavPersistentQueue<Job>.Create(admission);jobsByKey=NavPersistentIndex<NavNameKey,long>.Create(System.Collections.Generic.Comparer<NavNameKey>.Default,admission);
                subscribers=NavPersistentIndex<NavNameKey,NavSubscriberLink>.Create(System.Collections.Generic.Comparer<NavNameKey>.Default,admission);submittedEpoch=NavPersistentIndex<NavNameKey,long>.Create(System.Collections.Generic.Comparer<NavNameKey>.Default,admission);
                providerRefs=NavProviderReferences.Create(admission);ready=NavPersistentQueue<NavScheduledCompletion>.Create(admission);
                cachedFields=NavPersistentIndex<NavNameKey,long>.Create(System.Collections.Generic.Comparer<NavNameKey>.Default,admission);lru=NavPersistentQueue<CacheField>.Create(admission);
            }catch{ReleaseStorage();throw;}
        }
        internal static LayeredNavigationScheduler CreateOwned(INavStorageReservation reservation)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return new LayeredNavigationScheduler(NavStorageLayout.Before(reservation.TryReserve,1024),false,true);}
        private void MutableRoot(){if(rootFrozen||rootFaulted||rootReleased)throw new InvalidOperationException("Committed or failed scheduler root cannot be mutated.");}
        internal void SealPrepared()
        {
            MutableRoot();if(preparingJob!=null)throw new InvalidOperationException("Cannot seal an active scheduler operation.");
            corridorDonor?.Seal();jobs.Seal();jobsByKey.Seal();subscribers.Seal();submittedEpoch.Seal();providerRefs.Seal();ready.Seal();cachedFields.Seal();lru.Seal();
            rootReservation=null;rootFrozen=true;
        }
        internal LayeredNavigationScheduler ForkOwned(INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);if(!rootFrozen||rootFaulted)throw new InvalidOperationException("Seal scheduler before preparing a fork.");
            var fork=new LayeredNavigationScheduler(NavStorageLayout.Before(reservation.TryReserve,1024),true,true);
            try{
                fork.jobs=jobs.ForkOwned(reservation);fork.jobsByKey=jobsByKey.ForkOwned(reservation);fork.subscribers=subscribers.ForkOwned(reservation);fork.submittedEpoch=submittedEpoch.ForkOwned(reservation);
                fork.providerRefs=providerRefs.ForkOwned(reservation);fork.ready=ready.ForkOwned(reservation);fork.cachedFields=cachedFields.ForkOwned(reservation);fork.lru=lru.ForkOwned(reservation);
                fork.corridorBookkeeping=corridorBookkeeping;fork.boundedMode=boundedMode;fork.corridorDonor=NavPhysicalMacro.Acquire(fork,corridorDonor);fork.demandV2=demandV2;fork.demandV3=demandV3;fork.demandV4=demandV4;fork.demandV5=demandV5;fork.demandV6=demandV6;fork.demandLazy=demandLazy;fork.demandCertified=demandCertified;fork.demandRectangle=demandRectangle;fork.demandTerminalRectangle=demandTerminalRectangle;fork.demandTerminalCorner=demandTerminalCorner;fork.demandOriginShadow=demandOriginShadow;fork.demandTerminalCost=demandTerminalCost;fork.lazyCatalog=NavPhysicalMacro.Acquire(fork,lazyCatalog);fork.demandCatalog=NavPhysicalMacro.Acquire(fork,demandCatalog);
                fork.cachedGraphKey=cachedGraphKey;fork.cachedGraphKeyReference=cachedGraphKeyReference;cachedGraphKeyReference.Owner?.Retain();fork.cachedGraph=cachedGraph;fork.cachedGraphCompiler=cachedGraphCompiler;fork.cachedGraphRecipe=cachedGraphRecipe;
                fork.cachedGraphWork=cachedGraphWork;fork.cachedGraphBytes=cachedGraphBytes;fork.cacheBytes=cacheBytes;fork.peakCacheBytes=peakCacheBytes;
                fork.lastEpoch=lastEpoch;fork.logicalGraph=logicalGraph;fork.physicalGraph=physicalGraph;fork.logicalField=logicalField;fork.physicalField=physicalField;
                fork.connectorWork=connectorWork;fork.cacheHits=cacheHits;fork.graphBuilds=graphBuilds;fork.fieldBuilds=fieldBuilds;fork.cancelled=cancelled;fork.evictions=evictions;fork.maintenanceWork=maintenanceWork;
                fork.queuedBytes=queuedBytes;fork.readyBytes=readyBytes;fork.providerBytes=providerBytes;fork.peakRetainedBytes=peakRetainedBytes;fork.peakQueueAge=peakQueueAge;fork.externalReservationBytes=externalReservationBytes;
                fork.graphQuota=graphQuota;fork.fieldQuota=fieldQuota;fork.connectorQuota=connectorQuota;fork.bookkeepingQuota=bookkeepingQuota;fork.maintenanceQuota=maintenanceQuota;
                fork.peakQueue=peakQueue;fork.peakLabels=peakLabels;fork.peakFrontier=peakFrontier;fork.sessionOwnsProviders=sessionOwnsProviders;fork.attemptKey=attemptKey;attemptBindingStorage?.Retain();fork.attemptBindingStorage=attemptBindingStorage;fork.artifactOwner=artifactOwner;fork.RetentionBlocked=RetentionBlocked;
                return fork;
            }catch{fork.ReleaseStorage();throw;}
        }
        private static long OriginBindingWireBytes(NavScheduledRequest request)=>checked(NavContract.StringWireBytes(request.CompileKey)+NavContract.LocationWireBytes(request.Subscription.Origin)+NavContract.StringWireBytes(request.Subscription.HoldBinding??"")+32L);
        private static void WriteOriginBinding(System.IO.BinaryWriter writer,NavScheduledRequest request)
        {writer.Write(request.CompileKey);NavContract.Location(writer,request.Subscription.Origin);writer.Write(request.Subscription.HoldBinding??"");writer.Write(request.Subscription.EntityId);writer.Write(request.Subscription.Incarnation);writer.Write(request.Subscription.MobilityRevision);}
        private bool MatchesOrigin(Job job,NavScheduledRequest request)
        {
            if(rootReservation==null)return job.OriginBinding==OriginBinding(request);
            using(var scratch=NavStorageScratch.Reserve(OwnedRootReservation,NavContract.DigestScratchBytes(OriginBindingWireBytes(request))))return job.OriginBinding==NavContract.Digest(w=>WriteOriginBinding(w,request));
        }
        internal NavTypedRoute DirectPreparedRoute(NavScheduledRequest request)
        {
            if(rootReservation==null)return DirectRoute(request);
            var reservation=OwnedRootReservation;NavStorageAdmission.RequireOwned(reservation,request.StorageRoot);
            var from=request.Subscription.Origin;var to=request.Subscription.AssignedEndpoint;
            if(!request.Provider.IsValid(from,request.Clearance)||!request.Provider.IsValid(to,request.Clearance))return null;
            NavPagedList<NavTypedLeg> owned=null;
            try{
                if(request.Provider is PartitionNavigationProvider partition){
                    if(!partition.TryTraceOwned(from,to.Position,request.Clearance,out var reached,out var trace,reservation))return null;
                    owned=trace as NavPagedList<NavTypedLeg>??throw new InvalidOperationException("Owned trace must preserve its physical list.");if(!reached.Equals(to))return null;
                }else{
                    if(from.SurfaceId!=to.SurfaceId||!request.Provider.CanSweep(from,to,request.Clearance))return null;
                    owned=NavPagedList<NavTypedLeg>.Create(rootReservation);var leg=NavTypedLeg.CreateOwned(from,to,NavContract.Number(LayeredNavigationProvider.Distance(from.Position,to.Position)*request.Provider.SurfaceCost(from.SurfaceId)),NavTypedLegKind.Surface,null,reservation,request.Provider);
                    try{owned.Add(leg);}finally{leg.ReleaseOwner();}owned.Seal();
                }
                double cost=0;foreach(var leg in owned)cost+=leg.Cost;return new NavTypedRoute(request.Subscription,owned,cost,NavStorageLayout.Before(rootReservation,128),true);
            }finally{owned?.ReleaseStorage();}
        }
        internal void ReleaseStorage(){if(rootReleased)return;rootReleased=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()
        {
            corridorDonor?.Release();corridorDonor=null;demandCatalog?.Release();demandCatalog=null;lazyCatalog?.Release();lazyCatalog=null;
            jobs?.Release();jobsByKey?.Release();subscribers?.Release();submittedEpoch?.Release();providerRefs?.Release();ready?.Release();cachedFields?.Release();lru?.Release();
            cachedGraphKeyReference.Owner?.Release();cachedGraphKeyReference=default;attemptBindingStorage?.Release();attemptBindingStorage=null;cacheGraphRoot?.Release();cacheCompilerRoot?.Release();graphRecipeStorage?.Release();attemptRecipeStorage?.Release();
            cacheGraphRoot=null;cacheCompilerRoot=null;graphRecipeStorage=null;attemptRecipeStorage=null;rootReservation=null;
        }
    }
}
