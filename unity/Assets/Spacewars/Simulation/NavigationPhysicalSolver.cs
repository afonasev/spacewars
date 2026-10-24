using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    internal static class NavPhysicalSolveValidation
    {
        internal static bool Cost(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0;
        internal static bool Attachment(LayeredNavigationGraph graph,NavLocation from,int node,INavStorageReservation reservation,out double cost)
        {
            cost=0;var to=graph.Nodes[node].Location;if(!Sweep(graph,from,to,reservation))return false;cost=NavContract.Number(LayeredNavigationProvider.Distance(from.Position,to.Position)*graph.SurfaceCost(from.SurfaceId));return true;
        }
        internal static bool Sweep(LayeredNavigationGraph graph,NavLocation from,NavLocation to,INavStorageReservation reservation)
        {
            if(graph.PhysicalProvider is PartitionNavigationProvider partition){
                if(from.SurfaceId!=to.SurfaceId)return false;
                // This is a non-emitting semantic probe. It preserves the stored
                // selectors; canonical temporary structs only select the exact
                // authored support needed by the existing trace verifier.
                try{partition.VerifyPhysicalTrace(partition.OwnedCheckpointLocation(from),partition.OwnedCheckpointLocation(to),graph.Binding.Clearance,null,reservation);return true;}catch(ArgumentException){return false;}
            }return graph.CanSweep(from,to);
        }
        internal static void Arc(LayeredNavigationGraph graph,NavGraphArc value)
        {
            if(value.From<0||value.From>=graph.Nodes.Count||value.To<0||value.To>=graph.Nodes.Count||!Cost(value.Cost))throw new ArgumentException("Physical work arc dimensions/cost contradiction.");
            foreach(var arc in graph.Outgoing(value.From))if(NavPhysicalMacro.SameArc(value,arc))return;throw new ArgumentException("Physical work arc is not the exact graph arc.");
        }
        internal static void Legs(IReadOnlyList<NavTypedLeg> legs,out double cost)
        {
            cost=0;NavTypedLeg previous=null;foreach(var leg in legs){if(leg==null||!Cost(leg.Cost)||previous!=null&&!previous.To.Equals(leg.From))throw new ArgumentException("Physical work leg continuity/cost contradiction.");cost+=leg.Cost;previous=leg;}if(!Cost(cost))throw new ArgumentException("Physical work aggregate cost contradiction.");
        }
        internal static void SurfaceLeg(LayeredNavigationGraph graph,NavTypedLeg leg,NavLocation from,NavLocation to,INavStorageReservation reservation)
        {
            if(leg==null||leg.Kind!=NavTypedLegKind.Surface||leg.PortalId!=null||!leg.From.Equals(from)||!leg.To.Equals(to)||!leg.HasCanonicalProvider((graph.PhysicalProvider as INavStorageFacade)?.StorageRoot)||!Sweep(graph,from,to,reservation)||leg.Cost!=LayeredNavigationProvider.Distance(from.Position,to.Position)*graph.SurfaceCost(from.SurfaceId))throw new ArgumentException("Physical work surface boundary leg contradiction.");
        }
    }
    public sealed partial class NavTypedRoute : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&ownedSubscription!=null&&Legs is NavOwnedStorage storage&&storage.IsImmutableCaptureRoot;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.TypedRoute);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(128)||NavStorageLayout.ObjectBytesBound(GetType())>128||!ReferenceEquals(Subscription.MetadataOwner,ownedSubscription))throw new ArgumentException("Physical route owner/layout contradiction.");
            writer.Output.Write(Cost);NavPhysicalMacro.WriteMetadata(writer,this,Subscription);NavPhysicalMacro.WriteEdge(writer,this,(NavOwnedStorage)Legs);
        }
        private NavTypedRoute(NavRouteSubscription subscription,IReadOnlyList<NavTypedLeg> legs,double cost,bool physicalBirth):base(128)
        {Subscription=subscription;Legs=legs;Cost=cost;}
        internal static NavTypedRoute ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            double cost=reader.Input.ReadDouble();if(!NavPhysicalSolveValidation.Cost(cost)||NavStorageLayout.ObjectBytesBound(typeof(NavTypedRoute))>128)throw new ArgumentException("Invalid physical route cost/layout.");
            var subscription=NavPhysicalMacro.ReadMetadata<NavRouteSubscription>(reader,NavMetadataRole.Subscription);var storage=NavPhysicalMacro.ReadEdge<NavOwnedStorage>(reader);
            if(!(storage is NavPagedList<NavTypedLeg>)&&!(storage is NavRetainedArray<NavTypedLeg>))throw new ArgumentException("Wrong physical route leg representation.");var legs=(IReadOnlyList<NavTypedLeg>)storage;
            using(NavStorageScratch.Reserve(reader.Reservation,8192)){
                NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=cost||legs.Count>0&&(!legs[0].From.Equals(subscription.Origin)||!legs[legs.Count-1].To.Equals(subscription.AssignedEndpoint)))throw new ArgumentException("Physical route endpoint/cost contradiction.");
            }
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(128);var route=allocation.Register(new NavTypedRoute(subscription,legs,cost,true));route.ownedSubscription=NavPhysicalMacro.Acquire(route,subscription.MetadataOwner);NavPhysicalMacro.Acquire(route,storage);return route;
            }
        }
    }
    public sealed partial class NavIncrementalSingletonRoute : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen&&reservation==null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(queryRegion==null?NavPhysicalStorageKind.Singleton:NavPhysicalStorageKind.QuerySingletonV1);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(2048)||NavStorageLayout.ObjectBytesBound(GetType())>2048||!ReferenceEquals(ownedSubscription,subscription.MetadataOwner))throw new ArgumentException("Physical singleton owner/layout/callback contradiction.");
            if(queryRegion!=null){NavPhysicalMacro.WriteEdge(writer,this,queryRegionOwner);writer.Output.Write(queryCandidate);writer.Output.Write(queryChosenCandidate);}
            var w=writer.Output;w.Write(maxStates);w.Write((int)Status);w.Write((int)phase);w.Write(cursor);w.Write(arcCursor);w.Write(current);w.Write(chosen);w.Write(reconstructCursor);w.Write(reconstructLeg);w.Write(traceNode);w.Write(best);w.Write(routeCost);w.Write(startSeeded);
            NavPhysicalMacro.WriteEdge(writer,this,graph);NavPhysicalMacro.WriteMetadata(writer,this,subscription);NavPhysicalMacro.WriteEdge(writer,this,starts);NavPhysicalMacro.WriteEdge(writer,this,ends);NavPhysicalMacro.WriteEdge(writer,this,distance);NavPhysicalMacro.WriteEdge(writer,this,previous);NavPhysicalMacro.WriteEdge(writer,this,frontier);NavPhysicalMacro.WriteEdge(writer,this,legs);NavPhysicalMacro.WriteEdge(writer,this,chain);NavPhysicalMacro.WriteEdge(writer,this,Route,true);
        }
        internal static NavIncrementalSingletonRoute ReadPhysicalBody(NavPhysicalBodyReader reader,bool query=false)
        {
            NavTerminalRegion region=null;int queryCursor=0,queryChosen=-1;
            if(query){var root=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);region=root.Facade as NavTerminalRegion??throw new ArgumentException("Invalid query terminal owner.");queryCursor=reader.Input.ReadInt32();queryChosen=reader.Input.ReadInt32();}
            var r=reader.Input;int maximum=r.ReadInt32();var status=(NavSolveStatus)r.ReadInt32();var phase=(Phase)r.ReadInt32();int cursor=r.ReadInt32(),arcCursor=r.ReadInt32(),current=r.ReadInt32(),chosen=r.ReadInt32(),reconstructCursor=r.ReadInt32(),reconstructLeg=r.ReadInt32(),traceNode=r.ReadInt32();double best=r.ReadDouble(),routeCost=r.ReadDouble();bool seeded=NavPhysicalMacro.ReadBoolean(reader);
            if(maximum<1||!Enum.IsDefined(typeof(NavSolveStatus),status)||!Enum.IsDefined(typeof(Phase),phase)||!query&&phase>Phase.Done||double.IsNaN(best)||best<0||!NavPhysicalSolveValidation.Cost(routeCost)||NavStorageLayout.ObjectBytesBound(typeof(NavIncrementalSingletonRoute))>2048)throw new ArgumentException("Invalid physical singleton phase/cost/layout.");
            var graph=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader);var subscription=NavPhysicalMacro.ReadMetadata<NavRouteSubscription>(reader,NavMetadataRole.Subscription);
            var starts=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var ends=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var distance=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var previous=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavGraphArc>>(reader);var frontier=NavPhysicalMacro.ReadEdge<NavCowOrderedSet<NavQueueEntry>>(reader);var legs=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader);var chain=NavPhysicalMacro.ReadEdge<NavPagedList<NavGraphArc>>(reader);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);
            var work=new NavIncrementalSingletonRoute(graph,subscription,maximum,reader.Reservation.TryReserve,false,false,true,region){queryCandidate=queryCursor,queryChosenCandidate=queryChosen,Status=status,phase=phase,cursor=cursor,arcCursor=arcCursor,current=current,chosen=chosen,reconstructCursor=reconstructCursor,reconstructLeg=reconstructLeg,traceNode=traceNode,best=best,routeCost=routeCost,startSeeded=seeded,frozen=true,reservation=null,borrowedRoute=true};
            work.starts=NavPhysicalMacro.Acquire(work,starts);work.ends=NavPhysicalMacro.Acquire(work,ends);work.distance=NavPhysicalMacro.Acquire(work,distance);work.previous=NavPhysicalMacro.Acquire(work,previous);work.frontier=NavPhysicalMacro.Acquire(work,frontier);work.legs=NavPhysicalMacro.Acquire(work,legs);work.chain=NavPhysicalMacro.Acquire(work,chain);work.Route=NavPhysicalMacro.Acquire(work,route);
            using(NavStorageScratch.Reserve(reader.Reservation,checked(16384L+4L*graph.Nodes.Count)))work.ValidatePhysicalSingleton(reader.Reservation);return work;
        }
        private void ValidatePhysicalSingleton(INavStorageReservation allocation)
        {
            if(queryRegion!=null){ValidateQueryState(allocation);return;}
            int count=graph.Nodes.Count;if(cursor<-1||cursor>count||current<-1||current>=count||chosen<-1||chosen>=count||traceNode<0||traceNode>=Math.Max(1,count)||arcCursor<0||current>=0&&arcCursor>graph.Outgoing(current).Count||reconstructCursor<0||reconstructCursor>chain.Count+2||reconstructLeg<0||starts.Count>count||ends.Count>count||distance.Count>maxStates||previous.Count>distance.Count||frontier.Count>maxStates||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||(Route!=null)!=(Status==NavSolveStatus.Ready))throw new ArgumentException("Physical singleton dimensions/status contradiction.");
            void Attachments(NavCowDictionary<int,double> map,NavLocation location,int visited){foreach(var pair in map){if(pair.Key<0||pair.Key>=visited||!NavPhysicalSolveValidation.Cost(pair.Value)||!NavPhysicalSolveValidation.Attachment(graph,location,pair.Key,allocation,out double expected)||pair.Value!=expected)throw new ArgumentException("Physical singleton attachment prefix contradiction.");}}
            Attachments(starts,subscription.Origin,phase==Phase.Direct?0:phase==Phase.Origins?cursor:count);Attachments(ends,subscription.AssignedEndpoint,phase<Phase.Endpoints?0:phase==Phase.Endpoints?cursor:count);
            foreach(var pair in distance)if(pair.Key<0||pair.Key>=count||!NavPhysicalSolveValidation.Cost(pair.Value)||!previous.ContainsKey(pair.Key)&&(!starts.TryGetValue(pair.Key,out double seedCost)||seedCost!=pair.Value))throw new ArgumentException("Physical singleton label contradiction.");
            foreach(var pair in previous){NavPhysicalSolveValidation.Arc(graph,pair.Value);if(pair.Key!=pair.Value.To||!distance.TryGetValue(pair.Key,out double cost)||!distance.TryGetValue(pair.Value.From,out double parent)||cost<parent+pair.Value.Cost)throw new ArgumentException("Physical singleton predecessor contradiction.");}
            foreach(var entry in frontier)if(entry.Node<0||entry.Node>=count||entry.Candidate!=0||!distance.TryGetValue(entry.Node,out double cost)||cost!=entry.Cost)throw new ArgumentException("Physical singleton frontier cost contradiction.");
            var seen=new int[count];int epoch=0;foreach(var pair in previous){int walkNode=pair.Key;epoch++;while(previous.TryGetValue(walkNode,out var upstream)&&seen[walkNode]!=-1){if(seen[walkNode]==epoch)throw new ArgumentException("Physical singleton predecessor cycle.");seen[walkNode]=epoch;walkNode=upstream.From;}walkNode=pair.Key;while(previous.TryGetValue(walkNode,out var prior)&&seen[walkNode]==epoch){seen[walkNode]=-1;walkNode=prior.From;}}
            for(int n=0;n<count;n++)seen[n]=-1;int walk=chosen;
            foreach(var arc in chain){NavPhysicalSolveValidation.Arc(graph,arc);if(walk<0||arc.To!=walk||!previous.TryGetValue(walk,out var predecessor)||!NavPhysicalMacro.SameArc(predecessor,arc)||seen[walk]!=-1)throw new ArgumentException("Physical singleton reverse chain contradiction.");seen[walk]=1;walk=arc.From;}
            if(chain.Count>0&&walk!=traceNode||phase==Phase.Trace&&walk!=traceNode)throw new ArgumentException("Physical singleton trace cursor contradiction.");
            NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost)throw new ArgumentException("Physical singleton emitted cost contradiction.");
            if(phase<Phase.Search&&(distance.Count!=0||previous.Count!=0||frontier.Count!=0||chain.Count!=0||legs.Count!=0||startSeeded)||phase<Phase.Trace&&chain.Count!=0||phase<Phase.Reconstruct&&legs.Count!=0)throw new ArgumentException("Physical singleton future phase payload.");
            if(phase==Phase.Search&&!startSeeded){if(current!=-1||previous.Count!=0||chain.Count!=0||legs.Count!=0)throw new ArgumentException("Physical singleton seeding state contradiction.");foreach(var pair in distance)if(pair.Key>cursor||!starts.TryGetValue(pair.Key,out double start)||start!=pair.Value)throw new ArgumentException("Physical singleton seed prefix contradiction.");}
            if(phase==Phase.Reconstruct||Status==NavSolveStatus.Ready&&chosen>=0)ValidatePhysicalReconstruction(allocation);
            if(Route!=null){if(!ReferenceEquals(Route.Subscription,subscription))throw new ArgumentException("Physical singleton result subscription alias contradiction.");if(chosen<0){if(legs.Count!=0||routeCost!=0||Route.Legs.Count!=1)throw new ArgumentException("Physical singleton direct Ready contradiction.");NavPhysicalSolveValidation.SurfaceLeg(graph,Route.Legs[0],subscription.Origin,subscription.AssignedEndpoint,allocation);}else{if(Route.Cost!=routeCost||Route.Legs.Count!=legs.Count)throw new ArgumentException("Physical singleton result cost contradiction.");for(int n=0;n<legs.Count;n++)if(!ReferenceEquals(Route.Legs[n],legs[n]))throw new ArgumentException("Physical singleton result leg alias contradiction.");}}
        }
        private void ValidatePhysicalReconstruction(INavStorageReservation allocation)
        {
            int extra=queryRegion==null?0:1;
            if(chosen<0||reconstructCursor>chain.Count+1+extra+(Status==NavSolveStatus.Ready?1:0)||reconstructCursor==0&&reconstructLeg!=0)throw new ArgumentException("Physical singleton reconstruction cursor contradiction.");
            int emitted=reconstructCursor==0?0:1;for(int c=1;c<reconstructCursor&&c<=chain.Count;c++)emitted+=chain[chain.Count-c].Trace?.Count??1;
            if(reconstructCursor>chain.Count+1)emitted++;if(queryRegion!=null&&reconstructCursor>chain.Count+2)emitted++;emitted+=reconstructLeg;if(legs.Count!=emitted)throw new ArgumentException("Physical singleton reconstruction prefix length contradiction.");
            if(emitted>0){int first=chain.Count==0?chosen:chain[chain.Count-1].From;NavPhysicalSolveValidation.SurfaceLeg(graph,legs[0],subscription.Origin,graph.Nodes[first].Location,allocation);}
            if(queryRegion!=null&&reconstructCursor>chain.Count+1){
                int terminal=legs.Count-1-(reconstructCursor>chain.Count+2?1:0);
                NavPhysicalSolveValidation.SurfaceLeg(graph,legs[terminal],graph.Nodes[chosen].Location,queryRegion.Candidates[queryChosenCandidate],allocation);
            }
            if(Status==NavSolveStatus.Ready)NavPhysicalSolveValidation.SurfaceLeg(graph,legs[legs.Count-1],queryRegion==null?graph.Nodes[chosen].Location:queryRegion.Candidates[queryChosenCandidate],subscription.AssignedEndpoint,allocation);
            int at=1;for(int c=1;c<=chain.Count;c++){var arc=chain[chain.Count-c];var trace=arc.Trace;int available=c<reconstructCursor?trace?.Count??1:c==reconstructCursor?reconstructLeg:0;if(trace!=null){if(available>trace.Count)throw new ArgumentException("Physical singleton trace leg cursor contradiction.");for(int n=0;n<available;n++)if(!ReferenceEquals(legs[at++],trace[n]))throw new ArgumentException("Physical singleton stored trace leg alias contradiction.");}else if(available!=0){var leg=legs[at++];if(!leg.From.Equals(graph.Nodes[arc.From].Location)||!leg.To.Equals(graph.Nodes[arc.To].Location)||leg.Cost!=arc.Cost||leg.Kind!=(arc.Kind==NavGraphArcKind.Portal?NavTypedLegKind.Portal:NavTypedLegKind.Surface)||leg.PortalId!=arc.PortalId)throw new ArgumentException("Physical singleton expanded arc leg contradiction.");}}
        }
    }
}
