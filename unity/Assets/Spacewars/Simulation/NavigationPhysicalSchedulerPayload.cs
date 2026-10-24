using System;

namespace Spacewars.Simulation
{
    public sealed partial class NavScheduledCompletion : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&recipeStorage!=null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Completion);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>128)throw new ArgumentException("Physical completion ownership/layout contradiction.");
            writer.Output.Write((int)Status);writer.Output.Write(SubmittedEpoch);writer.Output.Write(CompletedEpoch);NavPhysicalMacro.WriteEdge(writer,this,recipeStorage);NavPhysicalMacro.WriteEdge(writer,this,Route,true);
        }
        private NavScheduledCompletion(NavScheduledRequest recipe,NavSolveStatus status,NavTypedRoute route,long submitted,long completed,bool physicalBirth):base(128)
        {Identity=recipe.Identity;Subscription=recipe.Subscription;Status=status;Route=route;SubmittedEpoch=submitted;CompletedEpoch=completed;recipeStorage=recipe.StorageRoot;}
        internal static NavScheduledCompletion ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var status=(NavSolveStatus)reader.Input.ReadInt32();long submitted=reader.Input.ReadInt64(),completed=reader.Input.ReadInt64();var storage=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);
            if(!(storage.Facade is NavScheduledRequest recipe)||!Enum.IsDefined(typeof(NavSolveStatus),status)||status==NavSolveStatus.Pending||submitted<0||completed<submitted||(status==NavSolveStatus.Ready)!=(route!=null)||route!=null&&!ReferenceEquals(route.Subscription,recipe.Subscription)||NavStorageLayout.ObjectBytesBound(typeof(NavScheduledCompletion))>128)throw new ArgumentException("Physical completion recipe/status/epochs contradiction.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var value=allocation.Register(new NavScheduledCompletion(recipe,status,route,submitted,completed,true));NavPhysicalMacro.Acquire(value,storage);NavPhysicalMacro.Acquire(value,route);return value;}
        }
    }
    public sealed partial class LayeredNavigationScheduler
    {
        internal sealed partial class CacheField : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered&&recipeStorage!=null;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Field,value:Incremental?NavPhysicalValueKind.IncrementalCacheField:NavPhysicalValueKind.CacheField);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {
                if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>128||!ReferenceEquals(Key,Recipe.ArtifactKey))throw new ArgumentException("Physical cache entry owner/layout contradiction.");
                writer.Output.Write(Work);writer.Output.Write(Bytes);NavPhysicalMacro.WriteEdge(writer,this,recipeStorage);NavPhysicalMacro.WriteEdge(writer,this,Field);
            }
            private CacheField(NavScheduledRequest recipe,NavSharedTerminalField field,int work,long bytes,bool physicalBirth,bool incremental):base(128)
            {Incremental=incremental;Recipe=recipe;recipeStorage=recipe.StorageRoot;Key=recipe.ArtifactKey;Field=field;Work=work;Bytes=bytes;}
            internal static CacheField ReadPhysicalBody(NavPhysicalBodyReader reader,bool incremental=false)
            {
                int work=reader.Input.ReadInt32();long bytes=reader.Input.ReadInt64();var storage=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var field=NavPhysicalMacro.ReadEdge<NavSharedTerminalField>(reader);
                if(!(storage.Facade is NavScheduledRequest recipe)||recipe.Singleton||field.Graph.Binding.StableKey!=recipe.GraphKey||field.Region.StableKey!=recipe.Region.StableKey||work<0||bytes!=FieldBytes(field)||(field.Status!=NavSolveStatus.Ready&&!(incremental&&field.Status==NavSolveStatus.Pending))||NavStorageLayout.ObjectBytesBound(typeof(CacheField))>128)throw new ArgumentException("Physical cache field recipe/work/bytes contradiction.");
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){allocation.Reserve(128);var value=allocation.Register(new CacheField(recipe,field,work,bytes,true,incremental));NavPhysicalMacro.Acquire(value,storage);NavPhysicalMacro.Acquire(value,field);return value;}
            }
        }
        internal sealed partial class Job : INavPhysicalBody
        {
            internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
            public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(Request.CertifiedLazyPartition?NavPhysicalStorageKind.CertifiedDemandQueryJobV2:Request.DemandQuery?NavPhysicalStorageKind.DemandQueryJobV1:NavPhysicalStorageKind.Job);
            public void WritePhysicalBody(NavPhysicalBodyWriter writer)
            {
                if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(512)||NavStorageLayout.ObjectBytesBound(GetType())>512||!ReferenceEquals(requestProvider,Request?.StorageRoot)||!ReferenceEquals(graphProvider,GraphProviderOwner?.StorageRoot))throw new ArgumentException("Physical job owner/layout contradiction.");
                var w=writer.Output;w.Write(QueueNode);w.Write((int)Phase);w.Write(GraphDebt);w.Write(FieldDebt);w.Write(GraphWork);w.Write(FieldWork);w.Write(Failure.HasValue);if(Failure.HasValue)w.Write((int)Failure.Value);w.Write(OriginCursor);w.Write(OriginValidated);w.Write(OriginComplete);w.Write(HasIngress);
                NavPhysicalMacro.WriteEdge(writer,this,requestProvider);NavPhysicalMacro.WriteEdge(writer,this,graphProvider,true);NavPhysicalMacro.WriteEdge(writer,this,Subscribers);
                NavPhysicalMacro.WriteEdge(writer,this,Compiler,true);NavPhysicalMacro.WriteEdge(writer,this,CompletedCompiler,true);NavPhysicalMacro.WriteEdge(writer,this,Graph,true);NavPhysicalMacro.WriteEdge(writer,this,Field,true);NavPhysicalMacro.WriteEdge(writer,this,Connector,true);NavPhysicalMacro.WriteEdge(writer,this,Singleton,true);NavPhysicalMacro.WriteEdge(writer,this,DirectRoute,true);
                activeKeyReference.Validate(ActiveConnectorKey,this);activeKeyReference.Write(writer);directKeyReference.Validate(DirectSubscription,this);directKeyReference.Write(writer);if(!ReferenceEquals(OriginBinding,originMetadata?.Value))throw new ArgumentException("Physical job origin text alias contradiction.");NavPhysicalMacro.WriteEdge(writer,this,originMetadata,true);if(Request.DemandQuery)NavPhysicalMacro.WriteEdge(writer,this,Demand,true);
            }
            private Job(bool physicalBirth):base(512){frozen=true;}
            internal static Job ReadPhysicalBody(NavPhysicalBodyReader reader,bool demandPolicy=false,bool certified=false)
            {
                var r=reader.Input;long queue=r.ReadInt64();var phase=(JobPhase)r.ReadInt32();long graphDebt=r.ReadInt64(),fieldDebt=r.ReadInt64();int graphWork=r.ReadInt32(),fieldWork=r.ReadInt32();NavSolveStatus? failure=NavPhysicalMacro.ReadBoolean(reader)?(NavSolveStatus?)r.ReadInt32():null;int cursor=r.ReadInt32();bool validated=NavPhysicalMacro.ReadBoolean(reader),complete=NavPhysicalMacro.ReadBoolean(reader),ingress=NavPhysicalMacro.ReadBoolean(reader);
                var requestRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);var providerRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader,true);var subscribers=NavPhysicalMacro.ReadEdge<NavPersistentQueue<NavScheduledRequest>>(reader);
                var compiler=NavPhysicalMacro.ReadEdge<LayeredNavigationCompileWork>(reader,true);var completed=NavPhysicalMacro.ReadEdge<LayeredNavigationCompileWork>(reader,true);var graph=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader,true);var field=NavPhysicalMacro.ReadEdge<NavSharedTerminalField>(reader,true);var connector=NavPhysicalMacro.ReadEdge<NavIncrementalSharedRoute>(reader,true);var singleton=NavPhysicalMacro.ReadEdge<NavIncrementalSingletonRoute>(reader,true);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);
                var active=NavNameReference.Read(reader);var direct=NavNameReference.Read(reader);var origin=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader,true);var demand=demandPolicy?NavPhysicalMacro.ReadEdge<NavDemandQueryWork>(reader,true):null;
                if(!(requestRoot.Facade is NavScheduledRequest request)||providerRoot!=null&&!(providerRoot.Facade is NavScheduledRequest)||queue<1||!Enum.IsDefined(typeof(JobPhase),phase)||graphDebt<0||fieldDebt<0||graphWork<0||fieldWork<0||failure.HasValue&&(!Enum.IsDefined(typeof(NavSolveStatus),failure.Value)||failure==NavSolveStatus.Pending||failure==NavSolveStatus.Ready)||cursor<0||active.Owner!=null&&active.Role!=NavNameRole.SubscriptionStableKey||direct.Owner!=null&&direct.Role!=NavNameRole.SubscriptionStableKey||NavStorageLayout.ObjectBytesBound(typeof(Job))>512)throw new ArgumentException("Physical job dimensions/selector/status contradiction.");
                if(request.CertifiedLazyPartition!=certified||request.DemandQuery!=demandPolicy||demand!=null&&!demand.Matches(request)||demandPolicy&&!(certified&&demand?.CertificateInsufficient==true)&&(graph!=null||compiler!=null||completed!=null||field!=null||connector!=null||singleton!=null)||phase==JobPhase.Direct&&demand!=null)throw new ArgumentException("Demand job association contradiction.");
                if(graph!=null&&graph.Binding.StableKey!=request.GraphKey||completed!=null&&!ReferenceEquals(completed.Graph,graph)||field!=null&&(graph==null||!ReferenceEquals(field.Graph,graph)||request.Singleton||field.Region.StableKey!=request.Region.StableKey)||connector!=null&&(field==null||!connector.MatchesPhysicalJob(field,active))||singleton!=null&&(graph==null||!(request.Singleton||request.Query&&checked((long)graph.Nodes.Count*request.Region.Candidates.Count)<=request.MaxLabels)||!singleton.MatchesPhysicalJob(graph,request.Subscription,request.Query?request.Region:null))||route!=null&&!ReferenceEquals(route.Subscription.MetadataOwner,request.Singleton||request.Query?request.Subscription.MetadataOwner:direct.Owner)||phase==JobPhase.Direct&&(graph!=null||compiler!=null||completed!=null||field!=null||connector!=null||singleton!=null)||phase==JobPhase.Origin&&origin==null||!failure.HasValue&&(phase==JobPhase.Field||phase==JobPhase.Origin)&&graph==null||origin==null&&(cursor!=0||validated||complete||ingress)||origin!=null&&(origin.Value.Length!=64||graph==null||cursor>graph.Nodes.Count||!validated&&(cursor!=0||complete||ingress)||ingress&&!complete))throw new ArgumentException("Physical job work/graph/field/selector/origin association contradiction.");
                if(phase==JobPhase.PartialFieldProbePaid&&(failure.HasValue||graphDebt!=0||fieldDebt!=0||compiler!=null||singleton!=null||route!=null||field==null||field.Status!=NavSolveStatus.Pending||!field.PartialCapacitySafe||!field.SeedComplete||connector==null||connector.Status!=NavSolveStatus.Pending||!connector.NeedsField||subscribers.First==null||!connector.MatchesPreparedJob(field,subscribers.First.Subscription)))throw new ArgumentException("Physical paid partial-field probe contradiction.");
                active.Require(reader.Reservation,active.Resolve());direct.Require(reader.Reservation,direct.Resolve());
                using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                    allocation.Reserve(512);var value=allocation.Register(new Job(true){request=request,graphProviderOwner=providerRoot?.Facade as NavScheduledRequest,QueueNode=queue,Phase=phase,GraphDebt=graphDebt,FieldDebt=fieldDebt,GraphWork=graphWork,FieldWork=fieldWork,Failure=failure,OriginCursor=cursor,OriginValidated=validated,OriginComplete=complete,HasIngress=ingress,activeKeyReference=active,directKeyReference=direct,ActiveConnectorKey=active.Resolve(),DirectSubscription=direct.Resolve(),OriginBinding=origin?.Value});
                    value.demand=NavPhysicalMacro.Acquire(value,demand);value.requestProvider=NavPhysicalMacro.Acquire(value,requestRoot);value.graphProvider=NavPhysicalMacro.Acquire(value,providerRoot);value.Subscribers=NavPhysicalMacro.Acquire(value,subscribers);value.compiler=NavPhysicalMacro.Acquire(value,compiler);value.completedCompiler=NavPhysicalMacro.Acquire(value,completed);value.graph=NavPhysicalMacro.Acquire(value,graph);value.field=NavPhysicalMacro.Acquire(value,field);value.connector=NavPhysicalMacro.Acquire(value,connector);value.singleton=NavPhysicalMacro.Acquire(value,singleton);value.directRoute=NavPhysicalMacro.Acquire(value,route);value.originMetadata=NavPhysicalMacro.Acquire(value,origin);NavPhysicalMacro.Acquire(value,active.Owner);NavPhysicalMacro.Acquire(value,direct.Owner);return value;
                }
            }
        }
    }
    internal static partial class NavPhysicalBodyFactory
    {
        private static bool TryReadSchedulerPayload(NavPhysicalTag tag,NavPhysicalBodyReader reader,out NavOwnedStorage value)
        {
            value=null;
            if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.CertifiedDemandQueryJobV2)))value=LayeredNavigationScheduler.Job.ReadPhysicalBody(reader,true,true);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.DemandQueryJobV1)))value=LayeredNavigationScheduler.Job.ReadPhysicalBody(reader,true);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Job)))value=LayeredNavigationScheduler.Job.ReadPhysicalBody(reader);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Completion)))value=NavScheduledCompletion.ReadPhysicalBody(reader);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Field,value:NavPhysicalValueKind.CacheField)))value=LayeredNavigationScheduler.CacheField.ReadPhysicalBody(reader);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.Field,value:NavPhysicalValueKind.IncrementalCacheField)))value=LayeredNavigationScheduler.CacheField.ReadPhysicalBody(reader,true);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ProviderReferences)))value=NavProviderReferences.ReadPhysicalBody(reader);
            else if(tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ProviderReference)))value=NavProviderReferences.Entry.ReadPhysicalBody(reader);
            return value!=null;
        }
    }

    public sealed partial class NavIncrementalSharedRoute
    {
        internal bool MatchesPhysicalJob(NavSharedTerminalField expected,NavNameReference key)=>ReferenceEquals(field,expected)&&ReferenceEquals(subscription.MetadataOwner,key.Owner)&&ReferenceEquals(subscription.StableKey,key.Resolve());
    }
    public sealed partial class NavIncrementalSingletonRoute
    {
        internal bool MatchesPhysicalJob(LayeredNavigationGraph expected,NavRouteSubscription key,NavTerminalRegion region=null)=>ReferenceEquals(graph,expected)&&ReferenceEquals(subscription,key)&&ReferenceEquals(queryRegion,region);
    }

}
