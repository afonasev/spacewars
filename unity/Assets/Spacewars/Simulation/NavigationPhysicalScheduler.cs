using System;
namespace Spacewars.Simulation
{
    public sealed partial class LayeredNavigationScheduler : INavPhysicalBody
    {
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Scheduler);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||rootReservation!=null||preparingJob!=null||StorageBytes!=NavStorageLayout.Charge(1024)||NavStorageLayout.ObjectBytesBound(GetType())>1024)throw new ArgumentException("Physical scheduler callback/layout contradiction.");
            writer.Output.Write(lastEpoch);
            writer.Output.Write(logicalGraph);
            writer.Output.Write(physicalGraph);
            writer.Output.Write(logicalField);
            writer.Output.Write(physicalField);
            writer.Output.Write(connectorWork);
            writer.Output.Write(cacheHits);
            writer.Output.Write(graphBuilds);
            writer.Output.Write(fieldBuilds);
            writer.Output.Write(cancelled);
            writer.Output.Write(evictions);
            writer.Output.Write(maintenanceWork);
            writer.Output.Write(queuedBytes);
            writer.Output.Write(readyBytes);
            writer.Output.Write(providerBytes);
            writer.Output.Write(peakRetainedBytes);
            writer.Output.Write(peakQueueAge);
            writer.Output.Write(externalReservationBytes);
            writer.Output.Write(cachedGraphBytes);
            writer.Output.Write(cacheBytes);
            writer.Output.Write(peakCacheBytes);
            writer.Output.Write(cachedGraphWork);
            writer.Output.Write(graphQuota);
            writer.Output.Write(fieldQuota);
            writer.Output.Write(connectorQuota);
            writer.Output.Write(bookkeepingQuota);
            writer.Output.Write(maintenanceQuota);
            writer.Output.Write(peakQueue);
            writer.Output.Write(peakLabels);
            writer.Output.Write(peakFrontier);
            writer.Output.Write(sessionOwnsProviders);
            writer.Output.Write(RetentionBlocked);
            NavPhysicalMacro.WriteEdge(writer,this,jobs,false);
            NavPhysicalMacro.WriteEdge(writer,this,jobsByKey,false);
            NavPhysicalMacro.WriteEdge(writer,this,subscribers,false);
            NavPhysicalMacro.WriteEdge(writer,this,submittedEpoch,false);
            NavPhysicalMacro.WriteEdge(writer,this,providerRefs,false);
            NavPhysicalMacro.WriteEdge(writer,this,ready,false);
            NavPhysicalMacro.WriteEdge(writer,this,cachedFields,false);
            NavPhysicalMacro.WriteEdge(writer,this,lru,false);
            NavPhysicalMacro.WriteEdge(writer,this,cacheGraphRoot,true);
            NavPhysicalMacro.WriteEdge(writer,this,cacheCompilerRoot,true);
            NavPhysicalMacro.WriteEdge(writer,this,graphRecipeStorage,true);
            NavPhysicalMacro.WriteEdge(writer,this,attemptRecipeStorage,true);
            NavPhysicalMacro.WriteEdge(writer,this,attemptBindingStorage,true);
            cachedGraphKeyReference.Validate(cachedGraphKey,this);cachedGraphKeyReference.Write(writer);
            if(!ReferenceEquals(attemptKey,attemptBindingStorage?.Value))throw new ArgumentException("Physical scheduler attempt binding alias contradiction.");
        }
        private LayeredNavigationScheduler(bool physicalBirth):base(1024){rootFrozen=true;}
        internal static LayeredNavigationScheduler ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            long lastEpoch=reader.Input.ReadInt64();
            long logicalGraph=reader.Input.ReadInt64();
            long physicalGraph=reader.Input.ReadInt64();
            long logicalField=reader.Input.ReadInt64();
            long physicalField=reader.Input.ReadInt64();
            long connectorWork=reader.Input.ReadInt64();
            long cacheHits=reader.Input.ReadInt64();
            long graphBuilds=reader.Input.ReadInt64();
            long fieldBuilds=reader.Input.ReadInt64();
            long cancelled=reader.Input.ReadInt64();
            long evictions=reader.Input.ReadInt64();
            long maintenanceWork=reader.Input.ReadInt64();
            long queuedBytes=reader.Input.ReadInt64();
            long readyBytes=reader.Input.ReadInt64();
            long providerBytes=reader.Input.ReadInt64();
            long peakRetainedBytes=reader.Input.ReadInt64();
            long peakQueueAge=reader.Input.ReadInt64();
            long externalReservationBytes=reader.Input.ReadInt64();
            long cachedGraphBytes=reader.Input.ReadInt64();
            long cacheBytes=reader.Input.ReadInt64();
            long peakCacheBytes=reader.Input.ReadInt64();
            int cachedGraphWork=reader.Input.ReadInt32();
            int graphQuota=reader.Input.ReadInt32();
            int fieldQuota=reader.Input.ReadInt32();
            int connectorQuota=reader.Input.ReadInt32();
            int bookkeepingQuota=reader.Input.ReadInt32();
            int maintenanceQuota=reader.Input.ReadInt32();
            int peakQueue=reader.Input.ReadInt32();
            int peakLabels=reader.Input.ReadInt32();
            int peakFrontier=reader.Input.ReadInt32();
            bool sessionOwnsProviders=NavPhysicalMacro.ReadBoolean(reader);
            bool RetentionBlocked=NavPhysicalMacro.ReadBoolean(reader);
            var jobs=NavPhysicalMacro.ReadEdge<NavPersistentQueue<Job>>(reader,false);
            var jobsByKey=NavPhysicalMacro.ReadEdge<NavPersistentIndex<NavNameKey,long>>(reader,false);
            var subscribers=NavPhysicalMacro.ReadEdge<NavPersistentIndex<NavNameKey,NavSubscriberLink>>(reader,false);
            var submittedEpoch=NavPhysicalMacro.ReadEdge<NavPersistentIndex<NavNameKey,long>>(reader,false);
            var providerRefs=NavPhysicalMacro.ReadEdge<NavProviderReferences>(reader,false);
            var ready=NavPhysicalMacro.ReadEdge<NavPersistentQueue<NavScheduledCompletion>>(reader,false);
            var cachedFields=NavPhysicalMacro.ReadEdge<NavPersistentIndex<NavNameKey,long>>(reader,false);
            var lru=NavPhysicalMacro.ReadEdge<NavPersistentQueue<CacheField>>(reader,false);
            var cacheGraphRoot=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader,true);
            var cacheCompilerRoot=NavPhysicalMacro.ReadEdge<LayeredNavigationCompileWork>(reader,true);
            var graphRecipeStorage=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader,true);
            var attemptRecipeStorage=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader,true);
            var attemptBindingStorage=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);
            var key=NavNameReference.Read(reader);
            if(lastEpoch<-1||logicalGraph<0||physicalGraph<0||logicalField<0||physicalField<0||connectorWork<0||cacheHits<0||graphBuilds<0||fieldBuilds<0||cancelled<0||evictions<0||maintenanceWork<0||queuedBytes<0||readyBytes<0||providerBytes<0||peakRetainedBytes<0||peakQueueAge<0||externalReservationBytes<0||cachedGraphBytes<0||cacheBytes<0||peakCacheBytes<0||cachedGraphWork<0||graphQuota<0||fieldQuota<0||connectorQuota<0||bookkeepingQuota<0||maintenanceQuota<0||peakQueue<0||peakLabels<0||peakFrontier<0||externalReservationBytes>MaxRetainedBytes||graphRecipeStorage!=null&&!(graphRecipeStorage.Facade is NavScheduledRequest)||attemptRecipeStorage!=null&&!(attemptRecipeStorage.Facade is NavScheduledRequest)||key.Owner!=null&&key.Role!=NavNameRole.RecipeCompileKey||NavStorageLayout.ObjectBytesBound(typeof(LayeredNavigationScheduler))>1024)throw new ArgumentException("Physical scheduler dimensions/key/layout contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(1024);var value=allocation.Register(new LayeredNavigationScheduler(true){
lastEpoch=lastEpoch,logicalGraph=logicalGraph,physicalGraph=physicalGraph,logicalField=logicalField,physicalField=physicalField,connectorWork=connectorWork,cacheHits=cacheHits,graphBuilds=graphBuilds,fieldBuilds=fieldBuilds,cancelled=cancelled,evictions=evictions,maintenanceWork=maintenanceWork,queuedBytes=queuedBytes,readyBytes=readyBytes,providerBytes=providerBytes,peakRetainedBytes=peakRetainedBytes,peakQueueAge=peakQueueAge,externalReservationBytes=externalReservationBytes,cachedGraphBytes=cachedGraphBytes,cacheBytes=cacheBytes,peakCacheBytes=peakCacheBytes,cachedGraphWork=cachedGraphWork,graphQuota=graphQuota,fieldQuota=fieldQuota,connectorQuota=connectorQuota,bookkeepingQuota=bookkeepingQuota,maintenanceQuota=maintenanceQuota,peakQueue=peakQueue,peakLabels=peakLabels,peakFrontier=peakFrontier,sessionOwnsProviders=sessionOwnsProviders,RetentionBlocked=RetentionBlocked});
                value.jobs=NavPhysicalMacro.Acquire(value,jobs);
                value.jobsByKey=NavPhysicalMacro.Acquire(value,jobsByKey);
                value.subscribers=NavPhysicalMacro.Acquire(value,subscribers);
                value.submittedEpoch=NavPhysicalMacro.Acquire(value,submittedEpoch);
                value.providerRefs=NavPhysicalMacro.Acquire(value,providerRefs);
                value.ready=NavPhysicalMacro.Acquire(value,ready);
                value.cachedFields=NavPhysicalMacro.Acquire(value,cachedFields);
                value.lru=NavPhysicalMacro.Acquire(value,lru);
                value.cacheGraphRoot=NavPhysicalMacro.Acquire(value,cacheGraphRoot);
                value.cacheCompilerRoot=NavPhysicalMacro.Acquire(value,cacheCompilerRoot);
                value.graphRecipeStorage=NavPhysicalMacro.Acquire(value,graphRecipeStorage);
                value.attemptRecipeStorage=NavPhysicalMacro.Acquire(value,attemptRecipeStorage);
                value.attemptBindingStorage=NavPhysicalMacro.Acquire(value,attemptBindingStorage);
                value.graphRecipe=graphRecipeStorage?.Facade as NavScheduledRequest;value.attemptRecipe=attemptRecipeStorage?.Facade as NavScheduledRequest;value.cachedGraphKeyReference=key;value.cachedGraphKey=key.Resolve();NavPhysicalMacro.Acquire(value,key.Owner);value.attemptKey=attemptBindingStorage?.Value;
                using(NavStorageScratch.Reserve(reader.Reservation,16384))value.ValidatePhysicalScheduler();return value;
            }
        }
        private void ValidatePhysicalScheduler()
        {
            if(subscribers.Count+ready.Count>MaxQueue||jobs.Count!=jobsByKey.Count||subscribers.Count!=submittedEpoch.Count||cachedFields.Count!=lru.Count||lru.Count>MaxCachedFields||providerBytes!=providerRefs.DiagnosticBytes)throw new ArgumentException("Physical scheduler queue/index/count contradiction.");
            long queued=0,resultBytes=0;int active=0;
            foreach(var row in jobs.Entries){var job=row.Value;if(job==null||job.QueueNode!=row.Key||job.Subscribers.Count==0||!jobsByKey.TryGetValue(job.Request.ArtifactKey,out long id)||id!=row.Key)throw new ArgumentException("Physical scheduler job queue/key contradiction.");
                foreach(var entry in job.Subscribers.Entries){var request=entry.Value;if(request==null||request.ArtifactKey!=job.Request.ArtifactKey||!subscribers.TryGetValue(request.Subscription.StableKey,out var link)||link.Item1!=row.Key||link.Item2!=entry.Key||!submittedEpoch.TryGetValue(request.Subscription.StableKey,out long epoch)||epoch<0||epoch>Math.Max(0,lastEpoch)||!providerRefs.Contains(request.Provider))throw new ArgumentException("Physical scheduler subscription link/epoch/provider contradiction.");queued=checked(queued+EstimateRequest(request));active++;}
                if(job.GraphProviderOwner!=null&&!providerRefs.Contains(job.GraphProviderOwner.Provider))throw new ArgumentException("Missing physical job provider reference.");
            }
            foreach(var pair in jobsByKey){if(!jobs.TryGetValue(pair.Value,out var job)||!ExactPhysicalName(pair.Key,NavNameKey.Artifact(job.Request)))throw new ArgumentException("Physical scheduler reverse job key selector contradiction.");}
            foreach(var pair in subscribers){if(!jobs.TryGetValue(pair.Value.Item1,out var job)||!job.Subscribers.TryGetValue(pair.Value.Item2,out var request)||!ExactPhysicalName(pair.Key,NavNameKey.Subscription(request.Subscription)))throw new ArgumentException("Physical scheduler reverse subscriber selector contradiction.");}
            foreach(var pair in submittedEpoch){if(!subscribers.TryGetValue(pair.Key,out var link)||!ExactPhysicalName(pair.Key,NavNameKey.Subscription(jobs[link.Item1].Subscribers[link.Item2].Subscription)))throw new ArgumentException("Physical scheduler epoch selector contradiction.");}
            foreach(var result in ready)resultBytes=checked(resultBytes+EstimateCompletion(result));if(active!=subscribers.Count||queued!=queuedBytes||resultBytes!=readyBytes)throw new ArgumentException("Physical scheduler diagnostic queue bytes contradiction.");
            long cache=cachedGraphBytes+(artifactOwner==null?0:OwnerMetadataBytes);
            foreach(var row in lru.Entries){var field=row.Value;if(field==null||!cachedFields.TryGetValue(field.Key,out long id)||id!=row.Key)throw new ArgumentException("Physical scheduler LRU link contradiction.");cache=checked(cache+field.Bytes);}
            foreach(var pair in cachedFields){if(!lru.TryGetValue(pair.Value,out var field)||!ExactPhysicalName(pair.Key,NavNameKey.Artifact(field.Recipe)))throw new ArgumentException("Physical scheduler cache selector contradiction.");}
            if(cache!=cacheBytes||cacheBytes>MaxCacheBytes||peakCacheBytes<cacheBytes||peakQueue<subscribers.Count+ready.Count)throw new ArgumentException("Physical scheduler cache/history diagnostic contradiction.");
            if(cacheGraphRoot==null){if(cacheCompilerRoot!=null||cachedGraphKey!=null||graphRecipeStorage!=null||cachedGraphWork!=0||cachedGraphBytes!=0||lru.Count!=0)throw new ArgumentException("Physical scheduler missing graph cache root.");}
            else {var recipe=graphRecipe??artifactOwner;if(recipe==null||recipe.CompileKey!=cachedGraphKey||cacheCompilerRoot!=null&&!ReferenceEquals(cacheCompilerRoot.Graph,cacheGraphRoot)||RetainedGraphBytes(cacheGraphRoot,recipe)!=cachedGraphBytes)throw new ArgumentException("Physical scheduler cache graph/recipe/charge contradiction.");}
            if(attemptKey==null&&artifactOwner!=null||artifactOwner!=null&&!providerRefs.Contains(artifactOwner.Provider))throw new ArgumentException("Physical scheduler attempt ownership contradiction.");
            foreach(var entry in providerRefs.PhysicalEntries){int references=ReferenceEquals(artifactOwner?.Provider,entry.Provider)?1:0;foreach(var job in jobs){if(ReferenceEquals(job.GraphProviderOwner?.Provider,entry.Provider))references++;foreach(var request in job.Subscribers)if(ReferenceEquals(request.Provider,entry.Provider))references++;}if(references!=entry.Count)throw new ArgumentException("Physical scheduler provider logical multiplicity contradiction.");}
        }
        private static bool ExactPhysicalName(NavNameKey a,NavNameKey b)=>ReferenceEquals(a.Value,b.Value)&&ReferenceEquals(a.Reference.Owner,b.Reference.Owner)&&a.Reference.Role==b.Reference.Role&&a.Reference.Index==b.Reference.Index;
    }
}
