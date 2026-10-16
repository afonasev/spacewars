using System;

namespace Spacewars.Simulation
{
    internal sealed partial class NavFieldFrontier
    {
        internal void RequirePhysicalDimensions(int expectedNodes,int expectedCandidates,int expectedMaximum,bool packed)
        {if(nodes!=expectedNodes||candidates!=expectedCandidates||maxLabels!=expectedMaximum||(legacy==null)!=packed)throw new ArgumentException("Physical field frontier configuration contradiction.");}
    }
    internal sealed partial class NavPackedFieldStorage
    {
        internal void VisitPhysicalLabels(Action<int,NavPackedFieldCell> visit)
        {for(int p=0;p<pages.Count;p++){var page=pages.Get(p);if(page==null)continue;for(int n=0;n<LabelsPerPage;n++)if((page.Values[n].Flags&1)!=0)visit(checked(p*LabelsPerPage+n),page.Values[n]);}}
    }
    public sealed partial class NavSharedTerminalField : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.Field);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(512)||NavStorageLayout.ObjectBytesBound(GetType())>512||!ReferenceEquals(ownedRegion,region.StorageRoot)||ownedKey==null||!ReferenceEquals(StableKey,ownedKey.Value))throw new ArgumentException("Physical field owner/layout contradiction.");
            var w=writer.Output;w.Write(maxLabels);w.Write((int)Status);w.Write((int)phase);w.Write(candidate);w.Write(node);w.Write(current);w.Write(arcCursor);w.Write(currentCandidate);w.Write(packed!=null);
            NavPhysicalMacro.WriteEdge(writer,this,graph);NavPhysicalMacro.WriteEdge(writer,this,ownedRegion);NavPhysicalMacro.WriteEdge(writer,this,ownedKey);NavPhysicalMacro.WriteEdge(writer,this,frontier);
            if(packed!=null){if(distance!=null||next!=null)throw new ArgumentException("Mixed physical field modes.");NavPhysicalMacro.WriteEdge(writer,this,packed);}
            else{NavPhysicalMacro.WriteEdge(writer,this,distance);NavPhysicalMacro.WriteEdge(writer,this,next);}
        }
        private NavSharedTerminalField(LayeredNavigationGraph graph,NavTerminalRegion region,int maximum,NavOwnedText key):base(512)
        {this.graph=graph;this.region=region;maxLabels=maximum;StableKey=key.Value;frozen=true;}
        internal static NavSharedTerminalField ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;int maximum=r.ReadInt32();var status=(NavSolveStatus)r.ReadInt32();var phase=(Phase)r.ReadInt32();int candidate=r.ReadInt32(),node=r.ReadInt32(),current=r.ReadInt32(),arcCursor=r.ReadInt32(),currentCandidate=r.ReadInt32();bool compact=NavPhysicalMacro.ReadBoolean(reader);
            if(maximum<1||maximum>1048576||!Enum.IsDefined(typeof(NavSolveStatus),status)||!Enum.IsDefined(typeof(Phase),phase)||NavStorageLayout.ObjectBytesBound(typeof(NavSharedTerminalField))>512)throw new ArgumentException("Invalid physical field dimensions/phase/layout.");
            var graph=NavPhysicalMacro.ReadEdge<LayeredNavigationGraph>(reader);var regionRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);if(!(regionRoot.Facade is NavTerminalRegion region))throw new ArgumentException("Wrong physical field terminal owner.");var key=NavPhysicalMacro.ReadEdge<NavOwnedText>(reader);var frontier=NavPhysicalMacro.ReadEdge<NavFieldFrontier>(reader);
            NavPackedFieldStorage packed=null;NavCowDictionary<long,double> distance=null;NavCowDictionary<long,NavGraphArc> next=null;
            if(compact)packed=NavPhysicalMacro.ReadEdge<NavPackedFieldStorage>(reader);else{distance=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,double>>(reader);next=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavGraphArc>>(reader);}
            frontier.RequirePhysicalDimensions(graph.Nodes.Count,region.Candidates.Count,maximum,compact);if(compact&&(!graph.SupportsPackedField||packed.Capacity!=(long)graph.Nodes.Count*region.Candidates.Count))throw new ArgumentException("Physical field packed address dimensions contradiction.");
            NavContract.Sha256(key.Value);using(NavStorageScratch.Reserve(reader.Reservation,checked(8192+NavContract.DigestScratchBytes(130))))if(key.Value!=NavContract.Digest(w=>{w.Write(graph.Binding.StableKey);w.Write(region.StableKey);}))throw new ArgumentException("Physical field recomputed key mismatch.");
            using(var allocation=new NavStorageAllocation(reader.Reservation.TryReserve)){
                allocation.Reserve(512);var field=allocation.Register(new NavSharedTerminalField(graph,region,maximum,key){Status=status,phase=phase,candidate=candidate,node=node,current=current,arcCursor=arcCursor,currentCandidate=currentCandidate});
                NavPhysicalMacro.Acquire(field,graph);field.graphOwned=true;field.ownedRegion=NavPhysicalMacro.Acquire(field,regionRoot);field.ownedKey=NavPhysicalMacro.Acquire(field,key);field.frontier=NavPhysicalMacro.Acquire(field,frontier);field.packed=NavPhysicalMacro.Acquire(field,packed);field.distance=NavPhysicalMacro.Acquire(field,distance);field.next=NavPhysicalMacro.Acquire(field,next);
                using(NavStorageScratch.Reserve(reader.Reservation,checked(16384L+4L*graph.Nodes.Count)))field.ValidatePhysicalField(reader.Reservation);return field;
            }
        }
        private void ValidatePhysicalField(INavStorageReservation allocation)
        {
            int nodes=graph.Nodes.Count,candidates=region.Candidates.Count;if(candidate<0||candidate>candidates||node<0||node>nodes||current<-1||current>=nodes||arcCursor<0||current>=0&&arcCursor>graph.Incoming(current).Count||currentCandidate<0||currentCandidate>=candidates||LabelCount>maxLabels||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||Status==NavSolveStatus.Ready&&(frontier.Count!=0||LabelCount==0)||Status==NavSolveStatus.BlockedConnector&&LabelCount!=0)throw new ArgumentException("Physical field cursor/status contradiction.");
            void Label(int n,int c,double cost,bool hasNext,NavGraphArc successor){
                if(n<0||n>=nodes||c<0||c>=candidates||!NavPhysicalSolveValidation.Cost(cost))throw new ArgumentException("Physical field label dimensions/cost contradiction.");
                if(hasNext){NavPhysicalSolveValidation.Arc(graph,successor);if(successor.From!=n||!TryGet(successor.To,c,out double tail)||cost<tail+successor.Cost||Status==NavSolveStatus.Ready&&cost!=tail+successor.Cost)throw new ArgumentException("Physical field successor association contradiction.");}
                if(!hasNext&&(!NavPhysicalSolveValidation.Attachment(graph,region.Candidates[c],n,allocation,out double seedCost)||cost!=seedCost))throw new ArgumentException("Physical field root label contradiction.");
                if(phase==Phase.Seed){if(hasNext||c>candidate||c==candidate&&n>=node||!NavPhysicalSolveValidation.Attachment(graph,region.Candidates[c],n,allocation,out double expected)||cost!=expected)throw new ArgumentException("Physical field seed prefix contradiction.");}
            }
            int labels=0;if(packed!=null)packed.VisitPhysicalLabels((index,cell)=>{Label(index%nodes,index/nodes,cell.Distance,(cell.Flags&2)!=0,cell.Next);labels++;});
            else {foreach(var pair in distance){int n=(int)pair.Key,c=(int)(pair.Key>>32);bool has=next.TryGetValue(pair.Key,out var successor);Label(n,c,pair.Value,has,successor);labels++;}foreach(var pair in next)if(!distance.ContainsKey(pair.Key))throw new ArgumentException("Physical field orphan successor.");}
            if(labels!=LabelCount)throw new ArgumentException("Physical field label cardinality contradiction.");
            var seen=new int[nodes];
            for(int c=0;c<candidates;c++){
                Array.Clear(seen,0,seen.Length);int epoch=0;
                for(int n=0;n<nodes;n++)if(TryNext(n,c,out _)&&seen[n]!=-1){
                    int walk=n;epoch++;while(TryNext(walk,c,out var successor)&&seen[walk]!=-1){if(seen[walk]==epoch)throw new ArgumentException("Physical field successor cycle.");seen[walk]=epoch;walk=successor.To;}
                    walk=n;while(TryNext(walk,c,out var prior)&&seen[walk]==epoch){seen[walk]=-1;walk=prior.To;}
                }
            }
            foreach(var entry in frontier)if(entry.Node<0||entry.Node>=nodes||entry.Candidate<0||entry.Candidate>=candidates||!TryGet(entry.Node,entry.Candidate,out double cost)||cost!=entry.Cost)throw new ArgumentException("Physical field frontier association contradiction.");
            if(phase==Phase.Seed&&current!=-1||phase==Phase.Search&&(candidate!=candidates||node!=0))throw new ArgumentException("Physical field seed/search transition contradiction.");
        }
    }
    public sealed partial class NavIncrementalSharedRoute : INavPhysicalBody
    {
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen&&reservation==null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(NavPhysicalStorageKind.SharedConnector);
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(2048)||NavStorageLayout.ObjectBytesBound(GetType())>2048||!ReferenceEquals(ownedSubscription,subscription.MetadataOwner))throw new ArgumentException("Physical shared connector owner/layout/callback contradiction.");
            var w=writer.Output;w.Write((int)Status);w.Write((int)phase);w.Write(cursor);w.Write(candidate);w.Write(chosenNode);w.Write(chosenCandidate);w.Write(walkNode);w.Write(traversed);w.Write(arcLeg);w.Write(best);w.Write(routeCost);
            NavPhysicalMacro.WriteEdge(writer,this,field);NavPhysicalMacro.WriteMetadata(writer,this,subscription);NavPhysicalMacro.WriteEdge(writer,this,egress);NavPhysicalMacro.WriteEdge(writer,this,ingress);NavPhysicalMacro.WriteEdge(writer,this,legs);NavPhysicalMacro.WriteEdge(writer,this,Route,true);
        }
        internal static NavIncrementalSharedRoute ReadPhysicalBody(NavPhysicalBodyReader reader)
        {
            var r=reader.Input;var status=(NavSolveStatus)r.ReadInt32();var phase=(Phase)r.ReadInt32();int cursor=r.ReadInt32(),candidate=r.ReadInt32(),chosenNode=r.ReadInt32(),chosenCandidate=r.ReadInt32(),walkNode=r.ReadInt32(),traversed=r.ReadInt32(),arcLeg=r.ReadInt32();double best=r.ReadDouble(),routeCost=r.ReadDouble();
            if(!Enum.IsDefined(typeof(NavSolveStatus),status)||!Enum.IsDefined(typeof(Phase),phase)||double.IsNaN(best)||best<0||!NavPhysicalSolveValidation.Cost(routeCost)||NavStorageLayout.ObjectBytesBound(typeof(NavIncrementalSharedRoute))>2048)throw new ArgumentException("Invalid physical shared connector phase/cost/layout.");
            var field=NavPhysicalMacro.ReadEdge<NavSharedTerminalField>(reader);var subscription=NavPhysicalMacro.ReadMetadata<NavRouteSubscription>(reader,NavMetadataRole.Subscription);var egress=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var ingress=NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader);var legs=NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader);var route=NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true);
            var work=new NavIncrementalSharedRoute(field,subscription,reader.Reservation.TryReserve,false,false,true){Status=status,phase=phase,cursor=cursor,candidate=candidate,chosenNode=chosenNode,chosenCandidate=chosenCandidate,walkNode=walkNode,traversed=traversed,arcLeg=arcLeg,best=best,routeCost=routeCost,frozen=true,reservation=null,borrowedRoute=true};
            work.egress=NavPhysicalMacro.Acquire(work,egress);work.ingress=NavPhysicalMacro.Acquire(work,ingress);work.legs=NavPhysicalMacro.Acquire(work,legs);work.Route=NavPhysicalMacro.Acquire(work,route);
            using(NavStorageScratch.Reserve(reader.Reservation,16384))work.ValidatePhysicalShared(reader.Reservation);return work;
        }
        private void ValidatePhysicalShared(INavStorageReservation allocation)
        {
            var graph=field.Graph;int nodes=graph.Nodes.Count,candidates=field.Region.Candidates.Count;
            if(cursor<0||cursor>Math.Max(nodes,candidates)||candidate<0||candidate>candidates||chosenNode<-1||chosenNode>=nodes||chosenCandidate<-1||chosenCandidate>=candidates||walkNode<0||walkNode>=Math.Max(1,nodes)||traversed<0||traversed>(long)nodes*candidates||arcLeg<0||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||(Route!=null)!=(Status==NavSolveStatus.Ready))throw new ArgumentException("Physical shared connector cursor/status contradiction.");
            foreach(var pair in egress){if(pair.Key<0||pair.Key>=candidates||phase==Phase.Egress&&pair.Key>=cursor||!NavPhysicalSolveValidation.Sweep(graph,field.Region.Candidates[pair.Key],subscription.AssignedEndpoint,allocation)||pair.Value!=LayeredNavigationProvider.Distance(field.Region.Candidates[pair.Key].Position,subscription.AssignedEndpoint.Position)*graph.SurfaceCost(subscription.AssignedEndpoint.SurfaceId))throw new ArgumentException("Physical shared egress prefix contradiction.");}
            foreach(var pair in ingress){if(pair.Key<0||pair.Key>=nodes||phase<Phase.Ingress||phase==Phase.Ingress&&pair.Key>=cursor||!NavPhysicalSolveValidation.Attachment(graph,subscription.Origin,pair.Key,allocation,out double cost)||pair.Value!=cost)throw new ArgumentException("Physical shared ingress prefix contradiction.");}
            if(phase==Phase.Select){double minimum=double.PositiveInfinity;int selectedNode=-1,selectedCandidate=-1;for(int n=0;n<=Math.Min(cursor,nodes-1);n++){int limit=n<cursor?candidates:candidate;for(int c=0;c<limit;c++)if(ingress.TryGetValue(n,out double start)&&egress.TryGetValue(c,out double end)&&field.TryGet(n,c,out double tail)){double value=start+tail+end;if(value<minimum||value==minimum&&(n<selectedNode||n==selectedNode&&c<selectedCandidate)){minimum=value;selectedNode=n;selectedCandidate=c;}}}if(best!=minimum||chosenNode!=selectedNode||chosenCandidate!=selectedCandidate)throw new ArgumentException("Physical shared selected prefix contradiction.");}
            NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost||phase<=Phase.Select&&legs.Count!=0)throw new ArgumentException("Physical shared emitted prefix cost contradiction.");
            if(phase>=Phase.Origin&&phase<Phase.Done||Status==NavSolveStatus.Ready){if(chosenNode<0||chosenCandidate<0||!ingress.ContainsKey(chosenNode)||!egress.ContainsKey(chosenCandidate)||!field.TryGet(chosenNode,chosenCandidate,out _))throw new ArgumentException("Physical shared chosen pair contradiction.");ValidatePhysicalSharedLegs(allocation);}
            if(Route!=null){if(!ReferenceEquals(Route.Subscription,subscription)||Route.Cost!=routeCost||Route.Legs.Count!=legs.Count)throw new ArgumentException("Physical shared result association contradiction.");for(int n=0;n<legs.Count;n++)if(!ReferenceEquals(Route.Legs[n],legs[n]))throw new ArgumentException("Physical shared result leg alias contradiction.");}
        }
        private void ValidatePhysicalSharedLegs(INavStorageReservation allocation)
        {
            int emitted=phase==Phase.Origin?0:1,at=1,walk=chosenNode,completed=traversed-(arcLeg>0?1:0);if(completed<0)throw new ArgumentException("Physical shared partial arc counter contradiction.");
            for(int step=0;step<completed;step++){if(!field.TryNext(walk,chosenCandidate,out var arc))throw new ArgumentException("Physical shared traversal prefix contradiction.");int count=arc.Trace?.Count??1;for(int n=0;n<count;n++){if(at>=legs.Count)throw new ArgumentException("Physical shared missing arc leg.");if(arc.Trace!=null){if(!ReferenceEquals(legs[at++],arc.Trace[n]))throw new ArgumentException("Physical shared trace leg alias contradiction.");}else{var leg=legs[at++];if(!leg.From.Equals(field.Graph.Nodes[arc.From].Location)||!leg.To.Equals(field.Graph.Nodes[arc.To].Location)||leg.Cost!=arc.Cost||leg.Kind!=(arc.Kind==NavGraphArcKind.Portal?NavTypedLegKind.Portal:NavTypedLegKind.Surface)||leg.PortalId!=arc.PortalId)throw new ArgumentException("Physical shared arc leg contradiction.");}}emitted+=count;walk=arc.To;}
            if(walk!=walkNode)throw new ArgumentException("Physical shared walk cursor contradiction.");
            if(arcLeg>0){if(!field.TryNext(walk,chosenCandidate,out var arc)||arc.Trace==null||arcLeg>=arc.Trace.Count)throw new ArgumentException("Physical shared partial trace cursor contradiction.");for(int n=0;n<arcLeg;n++)if(at>=legs.Count||!ReferenceEquals(legs[at++],arc.Trace[n]))throw new ArgumentException("Physical shared partial trace identity contradiction.");emitted+=arcLeg;}
            if(emitted>0)NavPhysicalSolveValidation.SurfaceLeg(field.Graph,legs[0],subscription.Origin,field.Graph.Nodes[chosenNode].Location,allocation);
            if(phase==Phase.Endpoint||Status==NavSolveStatus.Ready){if(field.TryNext(walkNode,chosenCandidate,out _))throw new ArgumentException("Physical shared premature terminal leg.");NavPhysicalSolveValidation.SurfaceLeg(field.Graph,legs[at++],field.Graph.Nodes[walkNode].Location,field.Region.Candidates[chosenCandidate],allocation);emitted++;}
            if(Status==NavSolveStatus.Ready){NavPhysicalSolveValidation.SurfaceLeg(field.Graph,legs[at],field.Region.Candidates[chosenCandidate],subscription.AssignedEndpoint,allocation);emitted++;}if(legs.Count!=emitted)throw new ArgumentException("Physical shared emitted length contradiction.");
        }
    }
}
